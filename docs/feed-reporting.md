# Feed Status Reporting

StorkDrop can report, per feed, which of that feed's products are installed on a machine.
Reporting is **opt-in per feed**: it happens only when a feed has a report URL configured.

## When a report is sent

A report is sent whenever a product **from that feed** is installed, updated, or removed on the
machine. Each report is a **full snapshot** of the current state of that feed's products — not a
delta. A missed report is therefore corrected by the next one (self-healing).

Delivery uses an on-disk spool with retry, so reports survive offline periods and restarts.

## Transport

- **Method:** HTTP `POST`
- **URL:** the feed's configured report endpoint (`FeedConfiguration.ReportUrl`)
- **Content-Type:** `application/json`
- **Signature header:** `X-Signature: sha256=<hex>` (see [Authentication](#authentication))

## Format — CloudEvents 1.0

The body is a [CloudEvents 1.0](https://github.com/cloudevents/spec) event in **structured JSON
mode**. The envelope carries standard metadata; the report itself is the `data` field.

| Envelope field    | Value                                                        |
|-------------------|--------------------------------------------------------------|
| `specversion`     | `1.0`                                                        |
| `type`            | `com.storkdrop.inventory.report`                             |
| `source`          | `storkdrop://<machineId>`                                    |
| `subject`         | the feed id                                                  |
| `id`              | unique event id (GUID) — usable for idempotency              |
| `time`            | RFC 3339 timestamp                                           |
| `datacontenttype` | `application/json`                                           |
| `data`            | the report object (see below)                                |

### `data` — report payload

All property names are camelCase.

| Field              | Type                | Description                                             |
|--------------------|---------------------|---------------------------------------------------------|
| `machineId`        | string              | Stable per-machine GUID                                 |
| `hostname`         | string              | Machine host name                                       |
| `operatingSystem`  | string              | OS description                                          |
| `storkDropVersion` | string              | StorkDrop version that sent the report                  |
| `sentAt`           | string (RFC 3339)   | When the report was generated                           |
| `feedId`           | string              | The reporting feed's id                                 |
| `feedName`         | string              | The reporting feed's display name                       |
| `customerId`       | string \| null      | Optional deployment/customer label configured on the feed |
| `products`         | array               | The feed's currently installed products (see below)     |

Each `products[]` entry:

| Field           | Type              | Description                              |
|-----------------|-------------------|------------------------------------------|
| `productId`     | string            | Product identifier                       |
| `title`         | string            | Product title                            |
| `version`       | string            | Installed version                        |
| `channel`       | string \| null    | Source channel (the runtime feed id, e.g. `feed:repo`) |
| `instanceId`    | string            | Instance identifier                      |
| `installedDate` | string (RFC 3339) | When the product instance was installed  |

### Example

```json
{
  "specversion": "1.0",
  "id": "4515e466-042e-481c-9ce4-dee43889f7c7",
  "type": "com.storkdrop.inventory.report",
  "source": "storkdrop://demo-machine-01",
  "subject": "nexus",
  "time": "2026-07-03T11:30:21Z",
  "datacontenttype": "application/json",
  "data": {
    "machineId": "demo-machine-01",
    "hostname": "DEMO-PC",
    "operatingSystem": "Windows 11 (10.0.26200)",
    "storkDropVersion": "1.4.2",
    "sentAt": "2026-07-03T11:30:21Z",
    "feedId": "nexus",
    "feedName": "Nexus",
    "customerId": "demo-customer",
    "products": [
      {
        "productId": "example-app",
        "title": "Example App",
        "version": "2026.1.3",
        "channel": "nexus:raw-hosted",
        "instanceId": "default",
        "installedDate": "2026-06-01T09:00:00Z"
      }
    ]
  }
}
```

## Authentication

Each request is signed with **HMAC-SHA256** over the exact raw request body, keyed with the feed's
report secret. The signature is sent as:

```
X-Signature: sha256=<lowercase-hex-digest>
```

A receiver verifies by recomputing the HMAC with the shared secret and comparing (constant-time):

```
expected = "sha256=" + hex(hmac_sha256(key = reportSecret, message = rawBody))
valid    = constantTimeEquals(expected, headerValue)
```

If no report secret is configured for the feed, the header is omitted.

> The body format is standard CloudEvents and the auth is a standard HMAC webhook signature, so any
> receiver that speaks HTTP + JSON can consume reports — StorkDrop is not tied to a specific backend.
