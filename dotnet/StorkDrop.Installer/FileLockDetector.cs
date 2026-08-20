using System.Diagnostics;
using System.Runtime.InteropServices;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

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

            if (result == 234 && pnProcInfoNeeded > 0)
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
        catch { }
        finally
        {
            try
            {
                NativeMethods.RmEndSession(sessionHandle);
            }
            catch { }
        }

        return processes;
    }

    public void ThrowIfAnyLocked(string directory)
    {
        if (!Directory.Exists(directory))
            return;

        string[] files;
        try
        {
            files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        foreach (string file in files)
        {
            string ext = Path.GetExtension(file);
            if (
                !ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                && !ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)
            )
                continue;

            if (IsFileLocked(file))
            {
                IReadOnlyList<string> processes = GetLockingProcesses(file);
                string processNames =
                    processes.Count > 0 ? string.Join(", ", processes) : string.Empty;
                throw new FileLockedException(Path.GetFileName(file), processNames);
            }
        }
    }

    public IReadOnlyList<LockedFileInfo> GetLockedFiles(string directory)
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(directory))
            return [];

        string[] files;
        try
        {
            files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }

        List<string> candidates = [];
        foreach (string file in files)
        {
            string ext = Path.GetExtension(file);
            if (
                ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)
            )
                candidates.Add(file);
        }

        return GetLockedFiles(candidates, CancellationToken.None);
    }

    public IReadOnlyList<LockedFileInfo> GetLockedFiles(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default
    )
    {
        List<LockedFileInfo> lockedFiles = [];
        if (!OperatingSystem.IsWindows())
            return lockedFiles;

        List<string> locked = [];
        foreach (string file in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsFileLocked(file))
                locked.Add(file);
        }

        if (locked.Count == 0)
            return lockedFiles;

        // One Restart Manager session for all locked files, not one per file.
        List<LockingProcessInfo> processes = GetDetailedLockingProcesses(locked);

        foreach (string file in locked)
        {
            lockedFiles.Add(
                new LockedFileInfo
                {
                    FilePath = file,
                    FileName = Path.GetFileName(file),
                    Processes = processes,
                }
            );
        }

        return lockedFiles;
    }

    public bool TryKillProcess(int processId)
    {
        // Force-kill the whole tree via taskkill and do not wait on teardown: waiting per process is
        // too slow with many runners. The caller polls the file locks to confirm they were released.
        try
        {
            using Process? taskKill = Process.Start(
                new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = $"/F /T /PID {processId}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            );
            return taskKill is not null;
        }
        catch
        {
            return false;
        }
    }

    public bool IsFileLocked(string filePath)
    {
        if (!File.Exists(filePath))
            return false;

        FileStream? stream = null;
        try
        {
            // Probe for write access, not just read: a process that has the file loaded as a module
            // (a running .exe, the STEPS runner processes) still allows shared reads but blocks any
            // replace/delete. A read-only open would miss exactly the locks that make a copy fail.
            stream = File.Open(
                filePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete
            );
            return false;
        }
        catch (IOException)
        {
            // Sharing violation: another process holds it in a way that prevents replacement.
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // ACL / read-only, not an in-use lock. Elevation and attribute handling deal with that
            // elsewhere; reporting it here would false-positive when the probe runs unelevated.
            return false;
        }
        finally
        {
            stream?.Dispose();
        }
    }

    private List<LockingProcessInfo> GetDetailedLockingProcesses(IReadOnlyList<string> filePaths)
    {
        List<LockingProcessInfo> processes = [];
        if (filePaths.Count == 0)
            return processes;

        int result = NativeMethods.RmStartSession(
            out uint sessionHandle,
            0,
            Guid.NewGuid().ToString()
        );
        if (result != 0)
            return processes;

        try
        {
            string[] resources = filePaths as string[] ?? filePaths.ToArray();
            result = NativeMethods.RmRegisterResources(
                sessionHandle,
                (uint)resources.Length,
                resources,
                0,
                [],
                0,
                []
            );
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

            if (result == 234 && pnProcInfoNeeded > 0)
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
                        NativeMethods.RmProcessInfo rmInfo = processInfo[i];
                        int pid = rmInfo.Process.DwProcessId;
                        string appName = rmInfo.StrAppName ?? string.Empty;
                        DateTime? startTime = FileTimeToDateTime(rmInfo.Process.ProcessStartTime);
                        string userName = string.Empty;

                        try
                        {
                            Process proc = Process.GetProcessById(pid);
                            if (string.IsNullOrWhiteSpace(appName))
                                appName = proc.ProcessName;
                            userName = GetProcessOwner(pid);
                        }
                        catch { }

                        if (string.IsNullOrWhiteSpace(appName))
                            continue;

                        processes.Add(
                            new LockingProcessInfo
                            {
                                ProcessId = pid,
                                ProcessName = appName,
                                UserName = userName,
                                StartTime = startTime,
                                SessionId = rmInfo.TsSessionId,
                            }
                        );
                    }
                }
            }
        }
        catch { }
        finally
        {
            try
            {
                NativeMethods.RmEndSession(sessionHandle);
            }
            catch { }
        }

        return processes;
    }

    private static DateTime? FileTimeToDateTime(System.Runtime.InteropServices.ComTypes.FILETIME ft)
    {
        long high = (long)ft.dwHighDateTime << 32;
        long fileTime = high | (uint)ft.dwLowDateTime;
        if (fileTime <= 0)
            return null;

        try
        {
            return DateTime.FromFileTime(fileTime);
        }
        catch
        {
            return null;
        }
    }

    private static string GetProcessOwner(int processId)
    {
        if (!OperatingSystem.IsWindows())
            return string.Empty;

        IntPtr processHandle = IntPtr.Zero;
        IntPtr tokenHandle = IntPtr.Zero;

        try
        {
            processHandle = NativeMethods.OpenProcess(
                NativeMethods.PROCESS_QUERY_INFORMATION,
                false,
                (uint)processId
            );
            if (processHandle == IntPtr.Zero)
                return string.Empty;

            if (
                !NativeMethods.OpenProcessToken(
                    processHandle,
                    NativeMethods.TOKEN_QUERY,
                    out tokenHandle
                )
            )
                return string.Empty;

            NativeMethods.GetTokenInformation(
                tokenHandle,
                NativeMethods.TOKEN_INFORMATION_CLASS.TokenUser,
                IntPtr.Zero,
                0,
                out int tokenInfoLength
            );

            if (tokenInfoLength <= 0)
                return string.Empty;

            IntPtr tokenInfo = Marshal.AllocHGlobal(tokenInfoLength);
            try
            {
                if (
                    !NativeMethods.GetTokenInformation(
                        tokenHandle,
                        NativeMethods.TOKEN_INFORMATION_CLASS.TokenUser,
                        tokenInfo,
                        tokenInfoLength,
                        out _
                    )
                )
                    return string.Empty;

                NativeMethods.TokenUser tokenUser = Marshal.PtrToStructure<NativeMethods.TokenUser>(
                    tokenInfo
                );

                int nameSize = 256;
                int domainSize = 256;
                char[] name = new char[nameSize];
                char[] domain = new char[domainSize];

                if (
                    !NativeMethods.LookupAccountSidW(
                        null,
                        tokenUser.User.Sid,
                        name,
                        ref nameSize,
                        domain,
                        ref domainSize,
                        out _
                    )
                )
                    return string.Empty;

                string domainStr = new string(domain, 0, domainSize);
                string nameStr = new string(name, 0, nameSize);

                if (string.IsNullOrEmpty(domainStr))
                    return nameStr;

                return $"{domainStr}\\{nameStr}";
            }
            finally
            {
                Marshal.FreeHGlobal(tokenInfo);
            }
        }
        catch
        {
            return string.Empty;
        }
        finally
        {
            if (tokenHandle != IntPtr.Zero)
                NativeMethods.CloseHandle(tokenHandle);
            if (processHandle != IntPtr.Zero)
                NativeMethods.CloseHandle(processHandle);
        }
    }
}
