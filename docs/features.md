# Features

## Marketplace

The marketplace shows all available products from all configured feeds. Users can:

- **Search** products by name, ID, or description
- **Filter** by feed, publisher, or product type (Suite, Plugin, Bundle)
- **Browse** product details with version history and release notes
- **Install** any version - not just the latest

When a product has a plugin connected to it, the install dialog shows a hint:

> "A plugin is connected to this product and may prompt for additional configuration during installation."

## Installation tracking

Installations run in the background. A status indicator in the bottom bar shows active installations - like a browser's download bar:

- Click it to see all active and completed installations
- Click any installation to open a **live log viewer** with a dark terminal-style UI
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

## Data protection

- Feed passwords are encrypted with DPAPI (`ProtectedData` with `CurrentUser` scope) - they're tied to the current Windows user and machine
- Configuration files use atomic writes (temp file + move) to prevent corruption
- The product registry validates for duplicate entries on load
