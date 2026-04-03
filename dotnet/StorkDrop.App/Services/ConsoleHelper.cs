using System.Runtime.InteropServices;

namespace StorkDrop.App.Services;

internal static class ConsoleHelper
{
    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    public static void DetachConsole()
    {
        FreeConsole();
    }
}
