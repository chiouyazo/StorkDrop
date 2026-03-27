using StorkDrop.Contracts.Interfaces;

namespace StorkDrop.Installer;

public sealed class FileLockDetector : IFileLockDetector
{
    public IReadOnlyList<string> GetLockingProcesses(string filePath)
    {
        if (!OperatingSystem.IsWindows())
            return [];

        List<string> processes = [];
        int result = NativeMethods.RmStartSession(
            out uint sessionHandle,
            0,
            Guid.NewGuid().ToString()
        );
        if (result != 0)
            return processes;

        try
        {
            string[] resources = [filePath];
            result = NativeMethods.RmRegisterResources(sessionHandle, 1, resources, 0, [], 0, []);
            if (result != 0)
                return processes;

            uint pnProcInfo = 0;
            uint lpdwRebootReasons = (uint)NativeMethods.RmRebootReasonNone;

            result = NativeMethods.RmGetList(
                sessionHandle,
                out uint pnProcInfoNeeded,
                ref pnProcInfo,
                [],
                ref lpdwRebootReasons
            );

            if (result == 234 && pnProcInfoNeeded > 0) // ERROR_MORE_DATA
            {
                NativeMethods.RmProcessInfo[] processInfo = new NativeMethods.RmProcessInfo[
                    pnProcInfoNeeded
                ];
                pnProcInfo = pnProcInfoNeeded;

                result = NativeMethods.RmGetList(
                    sessionHandle,
                    out pnProcInfoNeeded,
                    ref pnProcInfo,
                    processInfo,
                    ref lpdwRebootReasons
                );

                if (result == 0)
                {
                    for (int i = 0; i < pnProcInfo; i++)
                    {
                        string appName = processInfo[i].StrAppName;
                        if (!string.IsNullOrWhiteSpace(appName))
                        {
                            processes.Add(appName);
                        }
                    }
                }
            }
        }
        catch
        {
            // P/Invoke call failed; return whatever we have so far
        }
        finally
        {
            try
            {
                NativeMethods.RmEndSession(sessionHandle);
            }
            catch
            {
                // Ensure RmEndSession failure does not propagate
            }
        }

        return processes;
    }

    public bool IsFileLocked(string filePath)
    {
        if (!File.Exists(filePath))
            return false;

        // Try to open with Delete share. This is what matters for uninstall.
        // Using Read access with Delete share is much less sensitive than
        // ReadWrite with no sharing, avoiding false positives from antivirus,
        // Windows indexer, or other transient handles.
        FileStream? stream = null;
        try
        {
            stream = File.Open(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        finally
        {
            stream?.Dispose();
        }
    }
}
