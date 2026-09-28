# Product manifest

## Repository structure

This section describes the **Nexus** raw-repository layout. For the **S3** backend the manifest is identical, but the storage layout and publishing differ — see [S3 storage](s3-storage.md).

Products are stored in Nexus raw repositories with this exact layout:

```
my-product/
  manifest.json                         Latest version manifest (copy of newest version)
  versions/
    1.0.0/
      manifest.json                     Version-specific manifest
      my-product-1.0.0.zip              Product artifact (ZIP)
    1.1.0/
      manifest.json
      my-product-1.1.0.zip
```

**Important rules:**

- The ZIP filename must be `{productId}-{version}.zip` (e.g., `my-product-1.0.0.zip`)
- The root `manifest.json` should always be a copy of the latest version's manifest
- Each version gets its own subfolder under `versions/`
- **Upload the ZIP before the manifest.** StorkDrop discovers products by scanning for `manifest.json`. If the manifest is uploaded first, users can attempt to download the product before the ZIP is available. Always upload the artifact ZIP first, then the version manifest, then the root manifest.

## How the ZIP is processed

StorkDrop supports **two-layer packaging**. The outer ZIP (downloaded from Nexus) can contain:

1. An inner ZIP with the actual product files to install
2. Loose files like `.sql` that are handled by plugins before installation

**Single-layer packaging** (simple products):

```
my-product-1.0.0.zip
  MyProduct.exe                    -> copied to install dir
  MyProduct.dll                    -> copied to install dir
  config/
    default.json                   -> copied to install dir/config/
```

**Two-layer packaging** (products with plugin-handled files):

```
my-product-1.0.0.zip              <- outer ZIP (downloaded by StorkDrop)
  contents.zip                     <- inner ZIP (extracted to install dir)
    MyProduct.exe
    MyProduct.dll
    config/
      default.json
  update-1.0.0.sql                 <- loose file, handled by plugin (NOT copied)
  migration-combined.sql           <- loose file, handled by plugin (NOT copied)
```

**How extraction works:**

1. StorkDrop downloads and extracts the outer ZIP to a temporary directory
2. If the extracted contents contain **exactly one `.zip` file**, StorkDrop recognizes this as two-layer packaging:
   - The inner ZIP is extracted in-place (its contents replace the ZIP file)
   - Loose files alongside the inner ZIP (like `.sql` files) remain in the temp directory
3. File type handler plugins claim their extensions (e.g., `.sql`) and process them
4. Remaining files (from the inner ZIP extraction) are copied to the install directory

This means you can ship database migration files, scripts, or any plugin-handled files alongside your product binaries in a single package, without them ending up in the install directory.

**Rules:**

- Every copied file is tracked in `{productId}.files.json` for precise uninstall
- Files claimed by plugins are NOT copied to the install directory
- The inner ZIP filename can be anything (e.g., `contents.zip`, `binaries.zip`)
- If there is no inner ZIP (or multiple ZIPs), all files are treated as single-layer packaging

## Uninstall behavior

When uninstalling, StorkDrop only deletes the files it originally installed (listed in the file manifest). Files that were added to the install directory after installation (logs, user configs, etc.) are preserved. Empty directories are cleaned up after file deletion. If no file manifest exists (legacy install), the entire directory is deleted as a fallback.

## Manifest reference

```jsonc
{
  "productId": "my-product",
  "title": "My Product",
  "version": "1.0.0",
  "releaseDate": "2026-03-24",
  "installType": "Suite", // Plugin | Suite | Bundle | Executable
  "description": "Short description for the marketplace card",
  "releaseNotes": "# What's new\n- Feature A\n- Bug fix B",
  "recommendedInstallPath": "C:\\Program Files\\MyCompany\\MyProduct",
  "publisher": "My Company",
  "imageUrl": "https://example.com/icon.png",
  "downloadSizeBytes": 52428800,
  "contentSha256": "<hex sha-256 of the package zip; set by the S3 publisher, verified on download>",
  "requirements": ["Windows 10+", ".NET 8 Runtime"],
  "shortcuts": [
    { "exeName": "MyProduct.exe", "displayName": "My Product" },
    {
      "exeName": "MyAdmin.exe",
      "displayName": "My Product Admin",
      "iconPath": "admin.ico",
    },
  ],
  "shortcutFolder": "My Company",
  "environmentVariables": [
    { "name": "MY_PRODUCT_HOME", "value": "{InstallPath}", "action": "set" },
    {
      "name": "PATH",
      "value": "{InstallPath}\\bin",
      "action": "append",
      "mustExist": true,
    },
  ],
  "plugins": [
    { "assembly": "MyProduct.dll", "typeName": "MyProduct.Installer" },
  ],
  "bundledProductIds": ["my-other-product"],
  "requiredProductIds": ["dependency-product"],
  "optionalPostProducts": [
    { "id": "my-example-data", "hideNoAccess": true },
  ],
  "badgeText": "STABLE",
  "badgeColor": "#2E7D32",
  "allowMultipleInstances": false,
  "preserveOnSwitch": ["config.json", "secret.key"],
  "versionSchema": {
    "separator": "-",
    "parts": [
      { "label": "Release" },
      { "label": "Build" },
      { "label": "Date", "format": "date:yyyyMMdd" },
      { "label": "Revision" },
    ],
  },
  "cleanup": {
    "registryKeys": [],
    "dataLocations": ["%APPDATA%\\MyProduct"],
  },
}
```

## Install path templates

`recommendedInstallPath` may contain a `{ProductPath:<productId>}` token to anchor this product inside an already-installed product's instance directory. This is meant for add-ons that must live next to a host product (e.g. dropping a plugin into a host's plugin folder) without making the user hunt for the path.

| Token | Resolves to |
|-------|-------------|
| `{ProductPath:<productId>}` | The install path of a user-selected installed instance of `<productId>` |

When StorkDrop sees this token it asks, before anything else, which installed instance of `<productId>` to install into (a dropdown of all installed instances), then substitutes that instance's install path.

```jsonc
{ "recommendedInstallPath": "{ProductPath:acme.suite}/addons/plugins" }
```

If no instance of `<productId>` is installed, the install is aborted with a message asking to install that product first.

## Channels (badges)

Products can be published to multiple Nexus repositories with different badges. StorkDrop merges them into a single card in the marketplace:

| Field | Type | Description |
|-------|------|-------------|
| `badgeText` | string | Badge label displayed in the channel dropdown (e.g. "STABLE", "DEV", "FEATURE") |
| `badgeColor` | string | Hex color for the badge pill (e.g. "#2E7D32" for green, "#E53935" for red) |

Same `productId` across different repos = one product with multiple channels. The channel dropdown in the product detail view lets users pick which channel to install from.

On the **S3** backend, channels are the top-level prefix within a single bucket (`{channel}/{productId}/...`) rather than separate repositories, and access to a channel is enforced by IAM policy on that prefix. `badgeText`/`badgeColor` still drive the badge. See [S3 storage](s3-storage.md).

Pipeline example:

```yaml
# Stable channel (v* tags without pre-release suffix)
stork-manifest-update --badge-text "STABLE" --badge-color "#2E7D32"
stork-publish --repository Prod_MyProduct

# Dev channel (v*-dev* tags)
stork-manifest-update --badge-text "DEV" --badge-color "#E53935"
stork-publish --repository Prod_MyProduct_Develop
```

## Multi-instance

Products that support multiple simultaneous installations (e.g. a production instance and a test instance) set `allowMultipleInstances: true`. This enables the manage button (gear icon) on the marketplace card.

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `allowMultipleInstances` | bool | `false` | Enables multi-instance support |

Each instance has a unique `InstanceId` (e.g. "production", "test"). Plugins receive `context.InstanceId` to differentiate service names, configs, etc.

## Channel switching

When switching between channels, files matching `preserveOnSwitch` patterns are kept across the switch:

| Field | Type | Description |
|-------|------|-------------|
| `preserveOnSwitch` | string[] | Glob patterns for files to preserve (e.g. `["config.json", "data/**"]`) |

Without this field, all files are replaced during a channel switch.

## Version grouping

The "Change Version" picker can show a channel's versions either as a flat list or as a dependent
**cascade** (pick the release, then the build, then the date, then the revision, etc.). This is fully
automatic and needs no manifest changes — `versionSchema` is optional and only adds labels and formatting.

**Automatic grouping (no manifest needed).** StorkDrop splits each version of a channel on `-` (the SemVer
core `2.34.7` stays the first segment) and, when *every* version of that channel splits into the same
number of segments (at least two), presents them as a cascade. Segments that never vary are skipped as a
choice. If the versions are irregular (different segment counts) or there is only a single segment, the
picker falls back to the flat list. StorkDrop never has to know what the parts mean.

**Optional `versionSchema`.** A product may describe its version layout so the cascade levels get proper
names and parts can be formatted (e.g. a date). It is purely additive and backward compatible: omit it and
grouping still works automatically; include it and it is applied only when it matches.

| Field | Type | Description |
|-------|------|-------------|
| `versionSchema.separator` | string | Delimiter between parts. Default `-`. The first part keeps its dots (the SemVer core). |
| `versionSchema.parts` | array | One entry per version part, in order. |
| `versionSchema.parts[].label` | string | Column/level name shown in the picker (e.g. "Build", "Date"). |
| `versionSchema.parts[].format` | string? | Optional display format. Supported: `date:<pattern>` — parses the raw segment with the .NET date pattern (invariant culture) and shows it as an ISO date (`2026-09-15`). The stored/installed version keeps the raw value. |

```jsonc
{
  "versionSchema": {
    "separator": "-",
    "parts": [
      { "label": "Release" }, // 2.34.7
      { "label": "Build" }, // 2386
      { "label": "Date", "format": "date:yyyyMMdd" }, // 20260915 -> shown as 2026-09-15
      { "label": "Revision" }, // 2438
    ],
  },
}
```

For a version like `2.34.7-2386-20260915-2438` this yields levels **Release → Build → Date → Revision**,
with the date segment displayed as `2026-09-15`.

**Graceful fallback.** The schema is applied only when it fits. If the part count does not match a
channel's versions, or a `date:` part fails to parse, StorkDrop drops the schema and falls back to generic
grouping; if the versions are not groupable at all, it falls back to the flat list. A malformed or
unexpected `versionSchema` therefore never breaks the picker — it just degrades to the plain list.

## Custom metadata

Products can attach free-form key/value metadata that StorkDrop passes through to plugins verbatim
(as `PluginContext.ProductMetadata`) and **never interprets itself**. Use it to declare product-specific
requirements a plugin then acts on — e.g. a minimum host version that a plugin checks and warns
about during install.

| Field | Type | Description |
|-------|------|-------------|
| `metadata` | object (string→string) | Arbitrary product-declared key/value pairs surfaced to plugins |

```json
{
  "metadata": {
    "minWindowsVersion": "Win11"
  }
}
```

## Environment variables

Products declare environment variables in the manifest. Changes are tracked per-product and precisely reversed on uninstall.

| Action   | On install                         | On uninstall                      |
| -------- | ---------------------------------- | --------------------------------- |
| `set`    | Creates or overwrites the variable | Deletes it entirely               |
| `append` | Appends a value with separator     | Removes only the appended portion |

The `mustExist` flag (for `append`) controls what happens when the target variable doesn't exist: if `true`, the append is silently skipped; if `false` (default), the variable is created.

Concurrent environment variable modifications from parallel installations are serialized to prevent race conditions where two products both append to `PATH` and one overwrites the other.
