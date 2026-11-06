using System.IO;
using FluentAssertions;
using StorkDrop.App.Services.Cli;
using Xunit;

namespace StorkDrop.Tests.Cli;

public sealed class CliSecretResolverTests : IDisposable
{
    private const string Flag = "--password";
    private const string FileFlag = "--password-file";
    private const string EnvName = "STORKDROP_FEED_PASSWORD";

    private readonly string _directory = Directory.CreateTempSubdirectory("stork-cli-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static string? Resolve(string[] args, string? environmentValue = null) =>
        CliSecretResolver.Resolve(args, Flag, FileFlag, EnvName, _ => environmentValue);

    private string WriteFile(string content)
    {
        string path = Path.Combine(_directory, Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ArgumentStillWorks()
    {
        Resolve(["--cli", "add-feed", Flag, "secret"]).Should().Be("secret");
    }

    [Fact]
    public void ArgumentWinsOverFileAndEnvironment()
    {
        string file = WriteFile("from-file");

        Resolve([Flag, "arg", FileFlag, file], "env").Should().Be("arg");
    }

    [Fact]
    public void FileWinsOverEnvironment()
    {
        string file = WriteFile("from-file");

        Resolve([FileFlag, file], "env").Should().Be("from-file");
    }

    [Fact]
    public void FileContent_TrailingNewlineIsTrimmed_OtherWhitespaceKept()
    {
        string file = WriteFile(" pa ss \r\n");

        Resolve([FileFlag, file]).Should().Be(" pa ss ");
    }

    [Fact]
    public void EnvironmentVariable_IsUsedWhenNothingElseGiven()
    {
        Resolve([], "env").Should().Be("env");
    }

    [Fact]
    public void NothingGiven_ReturnsNull()
    {
        Resolve([]).Should().BeNull();
        Resolve([], "").Should().BeNull();
    }

    [Fact]
    public void MissingFile_ThrowsUsageError()
    {
        Action act = () => Resolve([FileFlag, Path.Combine(_directory, "missing")]);

        act.Should().Throw<CliException>().Which.ExitCode.Should().Be(CliExitCodes.Usage);
    }
}
