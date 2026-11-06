using System.Diagnostics;

namespace StorkDrop.Contracts.Services;

internal sealed class ProcessElevatedChild(Process process) : IElevatedChild
{
    public int ExitCode => process.ExitCode;

    public bool WaitForExit(TimeSpan timeout) => process.WaitForExit(timeout);

    public void Kill()
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
            when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The child may have exited in the meantime.
        }
    }
}
