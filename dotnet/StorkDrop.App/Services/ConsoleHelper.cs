using System.IO;
using System.Runtime.InteropServices;

namespace StorkDrop.App.Services;

internal static class ConsoleHelper
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    private const int ATTACH_PARENT_PROCESS = -1;

    public static void AttachToParentConsole()
    {
        if (!AttachConsole(ATTACH_PARENT_PROCESS))
            AllocConsole();

        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
    }
}
