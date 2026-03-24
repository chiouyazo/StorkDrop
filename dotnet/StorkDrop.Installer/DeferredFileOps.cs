namespace StorkDrop.Installer;

public sealed class DeferredFileOps
{
    public bool ScheduleMoveOnReboot(string sourcePath, string destinationPath)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        return NativeMethods.MoveFileExW(
            sourcePath,
            destinationPath,
            NativeMethods.MoveFileFlags.DelayUntilReboot
                | NativeMethods.MoveFileFlags.ReplaceExisting
        );
    }

    public bool ScheduleDeleteOnReboot(string filePath)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        return NativeMethods.MoveFileExW(
            filePath,
            null,
            NativeMethods.MoveFileFlags.DelayUntilReboot
        );
    }
}
