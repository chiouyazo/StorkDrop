# UAC elevation internals

When an installation target is a protected path (like `C:\Program Files`), StorkDrop needs admin privileges. Instead of running the entire app as admin, StorkDrop re-launches itself as a separate elevated process that performs only the privileged operation.

## How it works

1. The install dialog detects a protected path and shows an admin hint
2. StorkDrop spawns a new instance of itself with the Windows `runas` verb (triggers UAC prompt)
3. The elevated process receives the operation as command-line arguments
4. It boots a headless DI container (same services, no WPF UI), executes the operation, and exits
5. The main process reloads state from disk to see the changes

## Command-line arguments

The elevated process is launched with these argument patterns:

```
StorkDrop.exe --install "<productId>" "<targetPath>" "<feedId>" [--plugin-dir <path>] [--config-file <path>]
StorkDrop.exe --uninstall "<productId>" [--plugin-dir <path>]
StorkDrop.exe --update "<productId>" "<targetPath>" "<feedId>" [--plugin-dir <path>] [--config-file <path>]
```

## Plugin config through elevation

Plugin configuration values (from the config dialog in the main process) are serialized to a temporary JSON file:

1. Main process writes config values to `%TEMP%\StorkDrop\elevation-config-{guid}.json`
2. Path is passed via `--config-file` argument
3. Elevated process reads the file and deletes it immediately
4. Values are passed to the installation engine as `PluginConfigValues`

## Plugin directory forwarding

If StorkDrop was launched with `--plugin-dir` (for development), those arguments are forwarded to the elevated process so plugins load from the same directory.

## Headless DI container

The elevated process uses the same `AppHostBuilder.Build()` as the normal app. The difference is what happens after startup:

- No WPF windows, no sync context manipulation, no setup wizard
- Loads the feed registry (`feedRegistry.ReloadAsync()`)
- Executes the single operation (install/update/uninstall)
- Sets exit code 0 (success) or 1 (failure)
- Stops the host and exits

## Timeout

The main process waits up to 10 minutes for the elevated process to complete (configurable in `ElevationHelper`). If the elevated process doesn't exit in time, it's considered failed.
