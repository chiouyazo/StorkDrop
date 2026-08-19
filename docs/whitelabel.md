# White-label editions

StorkDrop can be shipped as a re-branded, fully isolated edition (own name, logo, window title,
install location, executable name, config/data locations and uninstall entry) while staying
recognizable as StorkDrop. This lets a company distribute StorkDrop under its own name, and lets
multiple such editions coexist on one machine without interfering with each other or with vanilla
StorkDrop.

The single StorkDrop build stays the source of truth. Branding is applied two ways that work
together:

- **At runtime** the app reads an optional `whitelabel.json` next to its executable.
- **At install time** the installer reads the same config's `prefix` and lays the edition down under a prefixed name automatically.

Updates are unchanged: they still come from the public GitHub releases. A branded install re-applies
its brand during self-update (see below), so it never reverts to plain StorkDrop.

## `whitelabel.json`

Placed next to the executable in the install directory. All fields are optional; a missing or
unreadable file means plain, unbranded StorkDrop.

```json
{
  "prefix": "acme",
  "displayName": "Acme GmbH Edition",
  "logo": "brand-logo.png",
  "forbidNewFeeds": true,
  "feed": {
    "name": "Acme Feed",
    "url": "https://nexus.example/repository",
    "lockPasswordHash": "<PasswordHasher base64 hash>"
  }
}
```

| Field | Type | Effect |
|-------|------|--------|
| `prefix` | string | Short code that isolates the edition. Drives the folder name `<prefix>-StorkDrop` for install dir, `%APPDATA%`, `%LOCALAPPDATA%`, temp and the single-instance lock. |
| `displayName` | string | Shown in the window title (`StorkDrop - <displayName>`) and the sidebar header. |
| `logo` | string | File name (relative to the install dir) of the logo shown in the sidebar. The window/taskbar icon stays the StorkDrop icon by design. |
| `forbidNewFeeds` | bool | When true, the "Add feed" button in Settings is disabled so the user cannot add feeds. |
| `feed.name` / `feed.url` | string | Pre-configure the primary feed. Both are fixed by the vendor and read-only in the setup wizard and Settings; the user only supplies username and password. They are re-applied on load and save, so editing the config file by hand cannot change them. |
| `feed.lockPasswordHash` | string | Optional. A `PasswordHasher` hash (Base64 of a 16-byte salt + 32-byte PBKDF2-SHA256 key) that locks install/update/uninstall behind a password. Shipping the one-way hash (never the plaintext) matches the existing soft-lock model. When set, the lock is enforced and its controls are disabled in the UI. |
| `feed.provider` | string | `Nexus` (default) or `S3`. Selects the storage backend for the pre-configured feed. |
| `feed.s3` | object | S3 coordinates when `provider` is `S3`: `bucket`, `region`, `serviceUrl` (omit for AWS), `usePathStyle`, `prefix`, `channels`. These are vendor-fixed and read-only; the customer only supplies the access key and secret. **No secret is ever baked into the edition.** |
| `visibleChannels` | string[] | Channels the edition exposes (e.g. `["prod"]`). Locks the app-wide visible-channel set for the edition. |

### Branded S3 edition example

```jsonc
{
  "prefix": "acme",
  "displayName": "Acme GmbH Edition",
  "logo": "brand-logo.png",
  "forbidNewFeeds": true,
  "visibleChannels": ["prod"],
  "feed": {
    "name": "Acme Products",
    "provider": "S3",
    "lockPasswordHash": "<PasswordHasher base64 hash>",
    "s3": {
      "bucket": "acme-storkdrop",
      "region": "eu-central-1",
      "serviceUrl": "https://minio.acme.com",   // omit for AWS S3
      "usePathStyle": true,
      "channels": ["prod"]
    }
  }
}
```

The customer runs the branded `Setup.exe`, then enters only their S3 access key and secret; the bucket, endpoint and channel are vendor-fixed and locked, and the edition can only ever see `prod`. See [S3 storage](s3-storage.md) for the access model.

The sidebar shows the brand logo with a "powered by StorkDrop" line underneath when branded.

To generate `feed.lockPasswordHash`, run the vendor's chosen password through `PasswordHasher.Hash(...)` once and paste the result.

The `prefix` is the isolation key: with `prefix: "acme"` the edition uses `%APPDATA%\acme-StorkDrop\...`,
`C:\Program Files\acme-StorkDrop\...`, its own single-instance mutex and its own uninstall entry, all
independent of any other edition.

## Packaging and installing a branded edition

You never type the prefix; the installer reads it from `whitelabel.json`. Package a branded installer
by shipping the generic `Setup.exe` together with a `whitelabel` folder next to it:

```
acme-edition/
  StorkDrop-<version>-Setup.exe
  whitelabel/
    whitelabel.json        ( "prefix": "acme", "displayName": ..., "logo": "brand-logo.png", ... )
    brand-logo.png
```

The customer just runs `Setup.exe`. At startup it reads `prefix` from `whitelabel\whitelabel.json`
and installs automatically as `acme-StorkDrop`: install dir `C:\Program Files\acme-StorkDrop`,
executable `acme-StorkDrop.exe`, its own Start-menu group and uninstall entry. The `whitelabel`
folder's contents are copied into the install directory so the app is branded at runtime. No prefix
to type, no folder to pick.

Options:

| How | Result |
|-----|--------|
| `Setup.exe` with a `whitelabel\` folder next to it | Auto-detected; installs the branded edition. Primary distribution model. |
| `Setup.exe` alone (no `whitelabel\` folder) | Plain StorkDrop in `C:\Program Files\StorkDrop`. |
| `Setup.exe /WHITELABELDIR="<folder>"` | Use a white-label folder at another location. |
| `Setup.exe /PREFIX=acme` | Force the prefix directly, overriding the config (used by self-update). |

Because the prefix is resolved from the file at startup (before the wizard runs), the uninstall
`AppId`, install dir, executable name and shortcuts are all correct with no user input, and multiple
editions get distinct uninstall entries and coexist cleanly.

## Self-update keeps the brand

Updates are downloaded from the same public GitHub release (one generic `Setup.exe`). Before running
the downloaded installer, a branded install re-passes its own `/PREFIX`. Because the `AppId` and
install dir derive from the prefix, the installer upgrades the edition in place; the existing
`whitelabel.json` and logo already in the install directory are preserved, so it stays branded
rather than reverting to plain StorkDrop.

## What is not branded

The name "StorkDrop" stays visible (window title prefix, version line, publisher/URLs) by design:
the goal is a recognizable white-label, not an unrecognizable fork.
