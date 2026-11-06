# CLI

StorkDrop includes a built-in CLI for headless installations. This is useful for scripted deployments, CI/CD pipelines, or remote management via PowerShell/SSH.

The CLI runs independently of the desktop app - both can run at the same time without interfering.

## Commands

### install

Install a product from any configured feed.

```
storkdrop --cli install <productId> [options]
```

| Option | Description |
|--------|-------------|
| `--version <version>` | Install a specific version (default: latest) |
| `--path <path>` | Install path (default: manifest's recommendedInstallPath) |
| `--instance <id>` | Instance name for multi-instance products (default: "default") |
| `--config-file <path>` | JSON file with plugin config values |
| `--config key=value` | Set a plugin config value (repeatable) |
| `--feed <id\|name>` | Install from this feed only |
| `--unlock <password>` | Feed lock password, see [Passing secrets](#passing-secrets-without-command-line-arguments) |

Examples:

```bash
storkdrop --cli install my-product
storkdrop --cli install my-product --version 1.2.3
storkdrop --cli install my-product --path "C:\Program Files\MyProduct"
storkdrop --cli install my-product --config target-database=Production --config smtp-port=587

# Multi-instance: install a second instance
storkdrop --cli install my-product --instance test --path "C:\MyProduct-Test"
storkdrop --cli install my-product --config-file install-config.json
```

### uninstall

Uninstall an installed product.

```
storkdrop --cli uninstall <productId> [--instance <id>]
```

### update

Update an installed product to the latest (or specific) version.

```
storkdrop --cli update <productId> [--instance <id>] [options]
```

| Option | Description |
|--------|-------------|
| `--version <version>` | Update to a specific version (default: latest) |
| `--config-file <path>` | JSON file with plugin config values |
| `--config key=value` | Set a plugin config value (repeatable) |

### list

List all available products across all configured feeds.

```
storkdrop --cli list [--feed <id|name>] [--json]
```

The version column shows the version offered by the feed. Exits with code 4 if a feed could not be queried; the products of the other feeds are still printed.

### versions

List available versions for a product.

```
storkdrop --cli versions <productId> [--feed <id|name>] [--json]
```

### installed

List installed products with instance, version, feed and badge.

```
storkdrop --cli installed [--feed <id|name>] [--json]
```

`--feed` restricts the list to products that were installed from that feed.

### outdated

List installed products for which the feed they were installed from offers a newer version. The command only reads: nothing is downloaded or installed.

```
storkdrop --cli outdated [--feed <id|name>] [--json]
```

Each installed instance is checked against its own feed only. Instances installed without a feed id, from a feed that is no longer configured, or from a feed that cannot be queried are reported as errors and the command exits with code 4. Whether a version counts as newer follows the same version comparison the desktop app uses.

## Global options

| Option | Description |
|--------|-------------|
| `--feed <id\|name>` | Restrict the command to one feed. Without it, `install`, `update` and `apply` use the first feed that has the product. |
| `--json` | Print JSON instead of a table (`list`, `versions`, `installed`, `outdated`) |
| `--instance <id>` | Target an instance by name (default: "default") |
| `--id <uniqueId>` | Target an instance by its unique id (`uninstall`, `update`, `re-execute`) |

## JSON output

With `--json`, stdout contains exactly one JSON document and nothing else. Log lines are always written to stderr, in every command, so they never mix into stdout.

`list`:

```json
{
  "products": [
    {
      "productId": "demo",
      "title": "Demo",
      "version": "2.0.0",
      "installType": "executable",
      "feedId": "prod",
      "feedName": "Production",
      "badgeText": null,
      "badgeColor": null,
      "installed": [
        { "instanceId": "default", "instanceUniqueId": null, "version": "1.0.0" }
      ]
    }
  ],
  "errors": []
}
```

`versions`: `{ "productId": "...", "feeds": [ { "feedId", "feedName", "versions": [...] } ], "errors": [] }`

`installed`: `{ "products": [ { "productId", "instanceId", "instanceUniqueId", "title", "version", "installedPath", "installedDate", "feedId", "feedName", "badgeText", "badgeColor" } ] }`

`outdated`: `{ "products": [ { "productId", "instanceId", "instanceUniqueId", "title", "installedVersion", "availableVersion", "feedId", "feedName", "badgeText", "badgeColor" } ], "errors": [] }`

Every entry in `errors` has `feedId`, `productId`, `instanceId` (each may be `null`) and `message`. Property names are camelCase and keys are always present; missing values are `null`.

Example in PowerShell:

```powershell
$outdated = storkdrop --cli outdated --json | ConvertFrom-Json
$outdated.products | Where-Object feedId -eq 'prod'
```

### re-execute

Re-run plugin actions (PreInstall + PostInstall) on an installed product without re-downloading or re-copying files. Previous configuration values are pre-filled in the dialog.

```
storkdrop --cli re-execute <productId> [--instance <id>] [options]
```

| Option | Description |
|--------|-------------|
| `--config-file <path>` | JSON file with plugin config values |
| `--config key=value` | Set a plugin config value (repeatable) |
| `--skip-pre` | Skip the PreInstall phase |
| `--skip-post` | Skip the PostInstall phase |
| `--run-files` | Also run file handlers (requires files stored in .stork/files/) |

### help

Show usage information.

```
storkdrop --cli help
storkdrop --cli help install
```

## Plugin configuration

Products with plugins may require configuration values (database name, server, etc.). In the desktop app, these are shown as a form dialog. In CLI mode, you provide them via `--config` or `--config-file`.

### Config file format

A JSON object mapping field keys to values:

```json
{
  "target-database": "Production",
  "smtp-server": "mail.example.com",
  "smtp-port": "587"
}
```

### Inline config

```
--config target-database=Production --config smtp-port=587
```

When both `--config-file` and `--config` are used, inline values take precedence.

### Missing required fields

If a plugin requires fields that are not provided, the CLI prints what is missing and exits with code 6:

```
Missing required plugin configuration:
  --config target-database=<value>  (Target Database)
  --config smtp-server=<value>  (SMTP Server)
```

## Unattended provisioning commands

These exist for automated/headless setup (e.g. provisioning a fresh test VM).

### add-feed / remove-feed

Register a feed without the desktop UI. The password is encrypted locally (DPAPI on
Windows), so it works correctly under the account that runs the install.

```
storkdrop --cli add-feed --url https://feed.example.com --repo Dev_Ephemeral \
  --user ci --password secret --id dev --name "Dev Ephemeral"
storkdrop --cli remove-feed dev
```

`add-feed` replaces an existing feed with the same `--id` (or same url+repo) instead of
duplicating, then reloads the feed registry and runs a connection test.

S3 feeds use `--provider s3` with these options:

| Option | Description |
|--------|-------------|
| `--bucket <name>` | Bucket name (required) |
| `--region <region>` | AWS region |
| `--service-url <url>` | Custom endpoint (MinIO, other S3-compatible services) |
| `--path-style` | Use path-style addressing |
| `--access-key <id>` | Access key id |
| `--secret-key <key>` | Secret key (stored encrypted) |
| `--prefix <prefix>` | Key prefix inside the bucket |
| `--channels <a,b,c>` | Comma-separated channel list |

#### Passing secrets without command-line arguments

Arguments are visible in process listings and shell history. Each secret can instead come from a file or an environment variable. An explicit argument wins over the file, the file wins over the environment variable.

| Secret | Argument | File | Environment variable |
|--------|----------|------|----------------------|
| Feed password (`add-feed`) | `--password` | `--password-file <path>` | `STORKDROP_FEED_PASSWORD` |
| S3 secret key (`add-feed`) | `--secret-key` | `--secret-key-file <path>` | `STORKDROP_S3_SECRET_KEY` |
| Feed lock password (`install`, `uninstall`, `update`, `re-execute`) | `--unlock` | `--unlock-file <path>` | `STORKDROP_UNLOCK_PASSWORD` |

A file holds the secret as its content; a trailing line break is removed. A path given with a `-file` option that does not exist is an error (exit code 2).

A **local feed** points at a folder instead of a server, for installing local builds without
Nexus/S3. The folder holds one product per subfolder, each with a `manifest.json` and one
package `.zip`; the product then appears in the marketplace and installs through the normal
pipeline (requirements, plugins, elevation).

```
storkdrop --cli add-feed --provider local --url C:\path\to\localfeed --id local --name "Local Dev"
```

The desktop UI does not offer adding a local feed, but shows it and lets you remove it.

### apply

Install an ordered set of products described by an **environment manifest**. Products
listed in each manifest's `requiredProductIds` are resolved and installed first.

```
storkdrop --cli apply env-manifest.json
storkdrop --cli apply env-manifest.json --report C:\temp\result.json --continue-on-error
```

Manifest format:

```json
{
  "products": [
    { "id": "my-stork-plugin" },
    { "id": "my-product", "version": "1.0.6-pr123.5",
      "config": { "target-database": "Test" } },
    { "id": "my-product-test-scenarios", "config": { "scenario": "order-pos" } }
  ]
}
```

`apply` writes a machine-readable JSON report (default
`%TEMP%/storkdrop-apply-result.json`) with a per-step `{ id, version, ok, error, durationMs }`,
and exits non-zero if any product failed (unless `--continue-on-error`).

If `apply` stops before any product is processed (manifest file missing, not valid JSON, or without products), the report is still written, with `success: false` and a single step with id `manifest` that carries the error. A report from an earlier run is overwritten.

## Exit codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Operation failed (install, uninstall, update, re-execute, apply); details on stderr |
| 2 | Invalid usage: unknown command, missing argument, unreadable or invalid `--config-file`, `--config` without `key=value`, missing `-file` argument target |
| 3 | Not found: product, version, installed product, feed |
| 4 | Feed error: a feed could not be queried (network, authentication). Results from the other feeds are still printed |
| 5 | The feed is locked and the unlock password is missing or wrong |
| 6 | Required plugin configuration is missing |

Every failure exits with a non-zero code; 0 is returned only if the command did what was asked.

## Notes

- Without `--feed`, the CLI searches all configured feeds for the product and the first feed that has the product is used. A feed that fails during this search is reported as a warning on stderr. If the product is not found and a feed failed, the exit code is 4 instead of 3.
- Command output (tables, JSON, progress lines) is written to stdout. Log lines and errors are written to stderr.
- `storkdrop.exe` is a Windows GUI executable. PowerShell and cmd do not wait for such a program, so `$LASTEXITCODE` is empty and the output can be lost when it is called on its own. To get output and exit code, make the shell wait: pipe the call (`storkdrop --cli outdated --json | Out-String`), or use `Start-Process -Wait -PassThru` with `-RedirectStandardOutput` and read `ExitCode` from the returned process object.
- The CLI does not show any WPF windows or dialogs.
- The CLI and the desktop app can run simultaneously without interfering.
