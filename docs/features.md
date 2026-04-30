# Features

## Marketplace

The marketplace shows all available products from all configured feeds. Products with the same `productId` across different feeds are merged into a single card. Users can:

- **Search** products by name, ID, or description
- **Filter** by feed, publisher, or product type (Suite, Plugin, Bundle)
- **Browse** product details with version history and release notes
- **Install** any version from any channel - not just the latest
- **Manage** multi-instance products via the gear icon

Each card shows the product title, feed name, latest version, and description. Products that support multiple installations (`allowMultipleInstances`) show a gear icon to manage existing instances.

## Channels

Products published to multiple Nexus repositories with different badges (STABLE, DEV, FEATURE) appear as a single card in the marketplace. The product detail view shows a **channel dropdown** where users pick which channel to install from. Each channel shows its badge as a colored pill.

- Updates only check within the currently installed channel
- Switching channels preserves files defined in `preserveOnSwitch`
- The installed products view shows the current channel badge next to the product title

## Multi-instance

Products that set `allowMultipleInstances: true` can be installed multiple times. Each installation has a unique instance name (e.g. "production", "test"). Instances are fully isolated - own install path, own config, own service registration. The installed view shows all instances, and the search box filters by product name, ID, or instance name.

When a product has a plugin connected to it, the install dialog shows a hint:

> "A plugin is connected to this product and may prompt for additional configuration during installation."

## Installation tracking

Installations, updates, and uninstalls run in the background. A status indicator in the bottom bar shows active operations - like a browser's download bar:

- Click it to see all active and completed operations
- Click any operation to open a **live log viewer** with a dark terminal-style UI
- Logs show every step: download, extract, plugin processing, file copy, shortcut creation
- Completed installations stay in the list with their full log history
- Supports native text selection and Ctrl+C for copying log output

**Example log output:**

```
[14:23:01] Installing Acme Dashboard v2.1.0 to C:\Program Files\Acme\Dashboard
[14:23:01] Downloading product acme-dashboard v2.1.0
[14:23:05] Download complete, extracting...
[14:23:06] Plugin found 2 file(s): update-original.sql, update-combined.sql
[14:23:06] Waiting for user configuration...
[14:23:12] Processing files with plugin...
[14:23:12] update-original.sql: Deployed SQL to Database_xyz via sqlcmd
[14:23:12] update-combined.sql: Skipped (not selected)
[14:23:12] Plugin processing completed successfully
[14:23:13] Copying files to C:\Program Files\Acme\Dashboard
[14:23:14] Creating shortcuts...
[14:23:14] Registering product...
[14:23:14] Installation of Acme Dashboard v2.1.0 completed successfully
```

Toast notifications appear when installations complete or fail.

## Installation isolation

Every installation is isolated from every other installation and from the main application:

- **Per-product locking** - you cannot accidentally install the same product twice at the same time. If you try, StorkDrop immediately tells you "Another installation of X is already in progress" instead of silently corrupting files.
- **Independent cancellation** - each installation has its own cancellation token. Cancelling one install does not affect others.
- **Exception containment** - if an installation fails with an unexpected error, it is caught, logged, and reported as a failed result. Other running installations continue unaffected. The main application never crashes due to an install failure.
- **Environment variable serialization** - if two products both need to modify the `PATH` variable, the operations are serialized so one doesn't overwrite the other's changes.
- **Elevated process isolation** - when admin privileges are needed, a separate process handles the privileged operation. It has its own DI container, its own service instances, and its own error handling. The main application is never affected by what happens in the elevated process.

**What this means in practice:**

You can install Product A and Product B at the same time. If Product A's database migration fails halfway through, Product A's backup is restored and it reports failure - but Product B continues installing normally, completely unaware that anything went wrong with Product A.

## Reliability and safety

StorkDrop is designed for production servers where failed updates are not acceptable.

**Backup and rollback:**
Before updating a product, StorkDrop creates a ZIP backup of the entire installation directory. If anything goes wrong during the update - download failure, extraction error, plugin crash, file copy issue - the backup is automatically restored and the product is left in its previous working state. The user sees a clear error message; the product continues working.

**File manifest tracking:**
Every file that StorkDrop installs is recorded in a manifest (`{productId}.files.json`). When you uninstall, only those exact files are removed - nothing more, nothing less. If you manually added files to the installation directory, they are left untouched. If the manifest is missing (e.g., from a very old install), StorkDrop falls back to removing the entire directory, but warns first.

**Plugin failure handling:**
If a plugin's file handler (e.g., a SQL file deployer) reports failure, the entire installation is aborted immediately. StorkDrop does not continue copying files or creating shortcuts for a product whose custom deployment step failed. The error is logged, a toast notification is shown, and the user can review the full log.

**Atomic configuration writes:**
All configuration files (product registry, settings, activity log) are written to a temporary file first, then atomically moved into place. If StorkDrop crashes mid-write, the previous valid file is still intact.

**Retry with backoff:**
File deletions during uninstall and update retry up to 3 times with 500ms delays. This handles transient locks from antivirus scanners, Windows Search indexer, and other processes that briefly hold file handles.

**Environment variable rollback:**
When a product sets `ACME_HOME` or appends to `PATH`, the exact change is recorded in a tracking file. On uninstall, only that specific change is reversed. For `PATH`, only the appended segment is removed - all other entries are preserved.

## File-in-use handling

When an update needs to replace a running executable:

1. The locked file is renamed to `DEL_{guid}_{filename}` in the same directory
2. The new version is copied into place with the original filename
3. The renamed old file is scheduled for deletion on next reboot (via Windows `MoveFileEx` API)
4. The application runs the new version immediately; the old file is cleaned up on restart

## UAC elevation

StorkDrop runs as a normal user. When the install path requires admin privileges (Program Files, Windows directory):

1. The install dialog detects the protected path and shows a warning
2. A separate elevated process is spawned via the Windows `runas` verb (UAC prompt)
3. The elevated process performs only the install/update/uninstall operation, then exits
4. The main process reloads state from disk to see the changes
5. The feed ID is passed to the elevated process so it downloads from the correct repository

For uninstall, if the product was installed to a protected directory, the confirmation dialog includes a note that admin privileges will be required.

## File lock detection

Before uninstalling or updating, StorkDrop checks `.exe` and `.dll` files for locks:

- Uses the Windows Restart Manager API (`rstrtmgr.dll`) to identify which process holds the lock
- Only checks executable files to avoid false positives from the Windows indexer or antivirus
- Uses `FileShare.ReadWrite | FileShare.Delete` - the minimum access needed to determine if deletion is possible
- Shows a clear error: "Cannot uninstall: file 'MyApp.exe' is locked by: MyApp"

## Executable products

Products with `installType: "Executable"` are action-only products that run plugin steps (database migrations, data insertion, configuration) without installing persistent files. They show "Downloaded" instead of "Installed" in the marketplace and have a prominent "Run" button in the installed tab. They can be re-executed multiple times with different configuration.

## Re-executing plugin actions

Installed products with plugins can re-run their PreInstall and PostInstall steps without re-downloading or re-copying files. The plugin DLLs are stored in `.stork/plugins/` and previous configuration values are pre-filled in the dialog. Users can selectively enable or disable individual phases (PreInstall, PostInstall) via action groups in the configuration dialog.

Plugins can implement `IDescribableStorkPlugin` to provide descriptions of what each phase does. These descriptions are shown in the configuration dialog as bullet points under each phase header.

## Required product installation

When a product declares `requiredProductIds`, StorkDrop resolves each required product across all configured feeds and shows a dialog with checkboxes. Users can install missing required products directly from the dialog before proceeding with the main installation.

## Data protection

- Feed passwords are encrypted with DPAPI (`ProtectedData` with `CurrentUser` scope) - they're tied to the current Windows user and machine
- Configuration files use atomic writes (temp file + move) to prevent corruption
- The product registry validates for duplicate entries on load
