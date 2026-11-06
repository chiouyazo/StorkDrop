using System.Runtime.InteropServices;
using FluentAssertions;
using StorkDrop.Contracts.Services;
using Xunit;

namespace StorkDrop.Tests.Core;

public sealed class ElevationArgumentsTests
{
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(
        [MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine,
        out int pNumArgs
    );

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private static string[] Parse(string commandLine)
    {
        IntPtr argv = CommandLineToArgvW("program.exe " + commandLine, out int count);
        try
        {
            string[] result = new string[count - 1];
            for (int i = 1; i < count; i++)
                result[i - 1] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!;
            return result;
        }
        finally
        {
            LocalFree(argv);
        }
    }

    public static TheoryData<string> TrickyValues =>
        new TheoryData<string>
        {
            @"C:\Program Files\Acme App\",
            @"C:\Program Files\Acme App",
            @"C:\path with ""quotes""\x",
            @"D:\trailing\\",
            "plain",
            "",
            "with\ttab",
            @"\\server\share\dir with space\",
            "ünïcödé pfad\\",
            @"a\""b",
        };

    [Theory]
    [MemberData(nameof(TrickyValues))]
    public void Quote_RoundTripsThroughWindowsCommandLineParsing(string value)
    {
        string[] parsed = Parse(ElevationArguments.Quote(value));

        parsed.Should().Equal(value);
    }

    [Fact]
    public void Quote_LeavesSimpleTokensUntouched()
    {
        ElevationArguments.Quote("--install").Should().Be("--install");
        ElevationArguments.Quote("acme.app").Should().Be("acme.app");
    }

    [Fact]
    public void ForInstall_RoundTripsEveryValue_IncludingTrailingBackslashPath()
    {
        string commandLine = ElevationArguments.ForInstall(
            productId: "acme.app",
            version: "2.36.1-CRYSYNC-2468-20260915-12",
            targetPath: @"C:\Program Files (x86)\Acme App\",
            feedId: "nexus:raw",
            instanceId: "prod 1",
            configFilePath: @"C:\Users\Max Muster\AppData\Local\Temp\StorkDrop\elevation-config-1.json",
            commandLineArgs: ["x.exe", "--plugin-dir", @"C:\plug ins\"],
            lockOwnerProcessId: 4242,
            progressLogPath: @"C:\Temp\progress log.log"
        );

        Parse(commandLine)
            .Should()
            .Equal(
                "--install",
                "acme.app",
                @"C:\Program Files (x86)\Acme App\",
                "nexus:raw",
                "--instance",
                "prod 1",
                "--version",
                "2.36.1-CRYSYNC-2468-20260915-12",
                "--plugin-dir",
                @"C:\plug ins\",
                "--config-file",
                @"C:\Users\Max Muster\AppData\Local\Temp\StorkDrop\elevation-config-1.json",
                "--lock-owner",
                "4242",
                "--progress-log",
                @"C:\Temp\progress log.log"
            );
    }

    [Fact]
    public void ForInstall_KeepsPositionalLayoutTheChildParses()
    {
        string[] parsed = Parse(
            ElevationArguments.ForInstall("p", "1.0", @"C:\t", "f", "default", null, [], 1, null)
        );

        parsed[0].Should().Be("--install");
        parsed[1].Should().Be("p");
        parsed[2].Should().Be(@"C:\t");
        parsed[3].Should().Be("f");
        parsed.Should().NotContain("--config-file").And.NotContain("--progress-log");
    }

    [Fact]
    public void ForUpdate_UsesUpdateVerbWithSamePositionalLayout()
    {
        string[] parsed = Parse(
            ElevationArguments.ForUpdate("p", "2.0", @"C:\t", "f", "i", @"C:\c.json", [], 7, null)
        );

        parsed
            .Take(8)
            .Should()
            .Equal("--update", "p", @"C:\t", "f", "--instance", "i", "--version", "2.0");
        parsed.Should().Contain(["--config-file", @"C:\c.json", "--lock-owner", "7"]);
    }

    [Fact]
    public void ForUninstall_CarriesInstanceAndLockOwner()
    {
        Parse(ElevationArguments.ForUninstall("p", "inst", [], 99, null))
            .Should()
            .Equal("--uninstall", "p", "--instance", "inst", "--lock-owner", "99");
    }

    [Fact]
    public void ForReExecute_AddsSkipFlagsOnlyWhenPhasesAreDisabled()
    {
        Parse(ElevationArguments.ForReExecute("p", "i", true, true, null, [], 1, null))
            .Should()
            .NotContain(["--skip-pre", "--skip-post"]);
        Parse(ElevationArguments.ForReExecute("p", "i", false, true, null, [], 1, null))
            .Should()
            .Contain("--skip-pre")
            .And.NotContain("--skip-post");
        Parse(ElevationArguments.ForReExecute("p", "i", true, false, null, [], 1, null))
            .Should()
            .Contain("--skip-post")
            .And.NotContain("--skip-pre");
    }

    [Fact]
    public void PluginDirTokens_ForwardsEveryPluginDirPair()
    {
        string[] commandLine =
        [
            "x.exe",
            "--cli",
            "--plugin-dir",
            @"C:\a b",
            "--plugin-dir",
            @"C:\c",
        ];

        ElevationArguments
            .PluginDirTokens(commandLine)
            .Should()
            .Equal("--plugin-dir", @"C:\a b", "--plugin-dir", @"C:\c");
    }

    [Fact]
    public void PluginDirTokens_IgnoresTrailingFlagWithoutValue()
    {
        ElevationArguments.PluginDirTokens(["x.exe", "--plugin-dir"]).Should().BeEmpty();
    }

    [Fact]
    public void Flags_MatchWhatTheChildProcessReads()
    {
        ElevationArguments.LockOwnerFlag.Should().Be("--lock-owner");
        ElevationArguments.ProgressLogFlag.Should().Be("--progress-log");
    }
}
