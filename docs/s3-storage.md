# S3 storage backend

StorkDrop supports two storage backends behind the same `IRegistryClient` seam:

- **Nexus** — the original HTTP raw-repository backend (unchanged).
- **S3** — object storage on AWS S3 **or any S3-compatible service** (MinIO, Cloudflare R2, Wasabi, Backblaze B2).

A feed picks its backend with the `provider` field (`Nexus` or `S3`). The S3 backend deliberately mirrors the Nexus raw-repository model: **discovery is done by listing, and there are no extra index/catalog/pointer files.** What a client can see is exactly what its credentials are allowed to list and get — so access is governed purely by prefix rights, the same way a Nexus account only sees the repositories it may read.

## Why S3

One **bucket** is the source of truth. Release channels (`prod`, `dev`, `feature`, ...) are the **top-level prefix**, so a channel is the access-scoping boundary — you grant a customer read on `prod/` and they can never list or get anything under `dev/`/`feature/`. This replaces the Nexus "one repository per channel" model with one bucket + prefix-scoped IAM, with no data duplication.

## Bucket layout

Identical in spirit to the Nexus raw layout, with the channel as the leading prefix. All keys are relative to an optional base `prefix` (so several logical stores can share one bucket):

```
{prefix}{channel}/{productId}/manifest.json                                  latest (copy of the newest version)
{prefix}{channel}/{productId}/versions/{version}/manifest.json
{prefix}{channel}/{productId}/versions/{version}/{productId}-{version}.zip
```

- **No `catalog.json`, `index.json`, or `latest.json`.** Products are discovered by listing `{channel}/` (with a `/` delimiter → the product ids), versions by listing `{productId}/versions/`. This is the S3 equivalent of how the Nexus client uses the components API.
- The root `{productId}/manifest.json` is a copy of the newest version's manifest — the same convention the Nexus layout uses — and the publisher keeps it current automatically. `GetProductManifestAsync(productId)` reads it in one GET.
- The ZIP filename is `{productId}-{version}.zip`.

## Integrity

The publisher computes the package SHA-256 and stores it **inside the manifest** as `contentSha256` (a field that already exists on the manifest — not a separate file). On download the S3 client streams the package to a temp file, hashes it, and compares against `manifest.contentSha256`; a mismatch aborts before the installer sees the bytes.

Verification is **fail-closed**: if a manifest has no `contentSha256`, the download is rejected. The publisher always writes it, so this only bites hand-uploaded manifests. Set `allowUnverified: true` on the S3 feed settings to explicitly opt out (not recommended).

Key path segments (channel, productId, version) are validated before use: a name containing `/ \ : * ? " < > | `, `..`, control characters, or surrounding whitespace is rejected, so a free-form channel/product name can never break the key layout, escape a prefix, or widen an IAM policy.

## Visibility & access model — read this

Access is **prefix-based**, exactly like Nexus repository permissions, so understand what that means:

- **Channel-level tenancy (this is what is implemented).** Granting a customer read on `{channel}/` lets them list and get **every product in that channel** — exactly like a Nexus account with read on a repo sees every product in it. Access is scoped per channel: give a customer `prod` and they can never list or get `dev`/`feature`. This is the model the client and IAM generator support today.
- **Per-customer product subsets (NOT via key prefixes).** Because a single `ListObjectsV2` call takes one prefix and IAM does not filter a listing per key, a customer scoped to a subset of products cannot enumerate them by listing the channel — so subsets are deliberately **not** modelled by carving product prefixes into the layout. The intended mechanism is **STS scoped-session credentials**: a broker (the future management console) issues short-lived credentials whose session policy reflects the customer's entitlement. The seam exists (`IS3CredentialProvider`, `S3FeedSettings.RoleArn`); it is not built yet. Until then, per-product entitlement is out of scope for the S3 backend.

A single linear key path cannot cleanly express a two-axis (channel × product) permission matrix, so the layout commits to the channel axis (the actual "customer = prod" requirement) and leaves per-product entitlement to STS. There is intentionally no global catalogue object, so listing cannot leak products a customer has no rights to.

## Credentials

- **v1: per-customer access key + secret**, scoped by IAM policy. Long-lived keys, provisioned per install (never a shared secret baked into a distributed installer), encrypted locally at rest with the same DPAPI-based service that protects feed passwords.
- **Future: STS token-vending.** The credential provider is an interface (`IS3CredentialProvider`); the static-keys implementation can be swapped for a short-lived STS provider (the `roleArn` field is already in the schema) without touching the registry client.

## Configuring an S3 feed

In Settings → Feeds, set **Storage backend** to `S3`:

```jsonc
{
  "feeds": [
    {
      "id": "acme",
      "name": "Acme Products",
      "url": "s3://acme",              // informational for S3 feeds
      "provider": "S3",
      "s3": {
        "bucket": "acme-storkdrop",
        "region": "eu-central-1",
        "serviceUrl": "https://minio.acme.com",  // omit for AWS S3
        "usePathStyle": true,                      // required for MinIO / most S3-compatible services
        "accessKeyId": "AKIA...",
        "encryptedSecretKey": "<encrypted>",
        "prefix": null,
        "channels": null                           // null = use the app-wide visibleChannels
      }
    }
  ],
  "visibleChannels": ["prod"]
}
```

`visibleChannels` controls which channel prefixes the feed expands into (one client per channel). Customer editions keep this at `["prod"]`; operators set `["prod", "dev", "feature"]`.

## IAM policies

`storkdrop-publish iam-policy` generates a least-privilege, read-only policy scoped to a channel prefix:

```bash
storkdrop-publish iam-policy --bucket acme-storkdrop --channel prod
```

The policy grants only `s3:GetObject` on `{channel}/*` + prefix-scoped `s3:ListBucket`. There is no catalogue object to grant, and dev/feature are never in scope. Per-product subsets are intentionally not expressed here (see the access model above) — that is an STS concern.

## Publishing

```bash
storkdrop-publish publish \
  --bucket acme-storkdrop \
  --channel prod \
  --manifest ./manifest.json \
  --package ./acme.app-1.2.3.zip \
  --service-url https://minio.acme.com --path-style \
  --access-key AKIA... --secret-key ...
```

This uploads the version manifest (with `contentSha256` embedded) and the package, then sets the product's root `manifest.json` to the version you just published. As in a Nexus raw repo, **"latest" is simply the version you publish last** — there is no auto-highest-version logic. Publish the version you want as latest, last. It is idempotent.

## Testing

`StorkDrop.Registry.S3.IntegrationTests` runs against a real S3 server. By default it starts MinIO via Testcontainers (needs a Docker daemon Testcontainers can drive). To point at an already-running MinIO/S3 instead, set:

```
STORKDROP_TEST_S3_ENDPOINT=http://127.0.0.1:9000
STORKDROP_TEST_S3_ACCESSKEY=...
STORKDROP_TEST_S3_SECRETKEY=...
```

The suite covers publish round-trip, checksum verification (including a tampered-package failure), version discovery / latest / specific-version lookup, channel and prefix isolation, `FeedRegistry` channel expansion, connection tests, the branded-edition end-to-end path, and IAM policy generation.
