# Product manifest

## Repository structure

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
  "installType": "Suite", // Plugin | Suite | Bundle
  "description": "Short description for the marketplace card",
  "releaseNotes": "# What's new\n- Feature A\n- Bug fix B",
  "recommendedInstallPath": "C:\\Program Files\\MyCompany\\MyProduct",
  "publisher": "My Company",
  "imageUrl": "https://example.com/icon.png",
  "downloadSizeBytes": 52428800,
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
  "cleanup": {
    "registryKeys": [],
    "dataLocations": ["%APPDATA%\\MyProduct"],
  },
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
