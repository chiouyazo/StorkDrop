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
storkdrop --cli list
```

### versions

List available versions for a product.

```
storkdrop --cli versions <productId>
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

If a plugin requires fields that are not provided, the CLI prints what is missing and exits with code 1:

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

## Exit codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Error (details printed to stderr) |

## Notes

- The CLI searches all configured feeds for the product. The first feed that has the product is used.
- Progress messages and logs are written to stdout.
- Errors are written to stderr.
- The CLI does not show any WPF windows or dialogs.
- The CLI and the desktop app can run simultaneously without interfering.
