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
| `--config-file <path>` | JSON file with plugin config values |
| `--config key=value` | Set a plugin config value (repeatable) |

Examples:

```bash
storkdrop --cli install my-product
storkdrop --cli install my-product --version 1.2.3
storkdrop --cli install my-product --path "C:\Program Files\MyProduct"
storkdrop --cli install my-product --config target-database=Production --config smtp-port=587
storkdrop --cli install my-product --config-file install-config.json
```

### uninstall

Uninstall an installed product.

```
storkdrop --cli uninstall <productId>
```

### update

Update an installed product to the latest (or specific) version.

```
storkdrop --cli update <productId> [options]
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
