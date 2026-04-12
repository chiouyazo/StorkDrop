using System.Runtime.InteropServices;

namespace StorkDrop.Installer;

internal static class NativeMethods
{
    internal const int RmRebootReasonNone = 0;
    internal const int CchRmMaxAppName = 255;
    internal const int CchRmMaxSvcName = 63;

    [Flags]
    internal enum MoveFileFlags : uint
    {
        ReplaceExisting = 0x00000001,
        CopyAllowed = 0x00000002,
        DelayUntilReboot = 0x00000004,
        WriteThrough = 0x00000008,
    }

    internal enum RmAppType
    {
        RmUnknownApp = 0,
        RmMainWindow = 1,
        RmOtherWindow = 2,
        RmService = 3,
        RmExplorer = 4,
        RmConsole = 5,
        RmCritical = 1000,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RmUniqueProcess
    {
        internal int DwProcessId;
        internal System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct RmProcessInfo
    {
        internal RmUniqueProcess Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxAppName + 1)]
        internal string StrAppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxSvcName + 1)]
        internal string StrServiceShortName;

        internal RmAppType ApplicationType;
        internal uint AppStatus;
        internal uint TsSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        internal bool BRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    internal static extern int RmStartSession(
        out uint pSessionHandle,
        int dwSessionFlags,
        string strSessionKey
    );

    [DllImport("rstrtmgr.dll")]
    internal static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    internal static extern int RmRegisterResources(
        uint pSessionHandle,
        uint nFiles,
        string[] rgsFileNames,
        uint nApplications,
        RmUniqueProcess[] rgApplications,
        uint nServices,
        string[] rgsServiceNames
    );

    [DllImport("rstrtmgr.dll")]
    internal static extern int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        [In, Out] RmProcessInfo[] rgAffectedApps,
        ref uint lpdwRebootReasons
    );

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MoveFileExW(
        string lpExistingFileName,
        string? lpNewFileName,
        MoveFileFlags dwFlags
    );

    internal const uint PROCESS_QUERY_INFORMATION = 0x0400;
    internal const uint TOKEN_QUERY = 0x0008;

    internal enum TOKEN_INFORMATION_CLASS
    {
        TokenUser = 1,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SidAndAttributes
    {
        internal IntPtr Sid;
        internal uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TokenUser
    {
        internal SidAndAttributes User;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(
        uint dwDesiredAccess,
        bool bInheritHandle,
        uint dwProcessId
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(
        IntPtr processHandle,
        uint desiredAccess,
        out IntPtr tokenHandle
    );

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        TOKEN_INFORMATION_CLASS tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength
    );

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool LookupAccountSidW(
        string? lpSystemName,
        IntPtr sid,
        char[] lpName,
        ref int cchName,
        char[] lpReferencedDomainName,
        ref int cchReferencedDomainName,
        out int peUse
    );
}
