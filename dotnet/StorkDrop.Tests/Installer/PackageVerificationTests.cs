using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RichardSzalay.MockHttp;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class PackageVerificationTests
{
    private const string DownloadUrl =
        "https://github.com/acme/StorkDrop/releases/download/v1.2.3/StorkDrop-1.2.3-Setup.exe";
    private const string SumsUrl =
        "https://github.com/acme/StorkDrop/releases/download/v1.2.3/SHA256SUMS.txt";

    private static string HashOf(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static async Task<string> WriteInstallerAsync(TestDirectory dir, string content)
    {
        string path = dir.File("StorkDrop-1.2.3-Setup.exe");
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    private static Task<bool> Verify(MockHttpMessageHandler handler, string file) =>
        PublishedChecksumVerifier.VerifyAsync(
            handler.ToHttpClient(),
            new Uri(DownloadUrl),
            file,
            NullLogger.Instance,
            CancellationToken.None
        );

    [Fact]
    public void Normalize_TrimsAndLowerCases_AndTreatsBlankAsMissing()
    {
        PackageHashVerifier.Normalize("  ABCDEF ").Should().Be("abcdef");
        PackageHashVerifier.Normalize("   ").Should().BeNull();
        PackageHashVerifier.Normalize(null).Should().BeNull();
    }

    [Fact]
    public void Matches_IsCaseAndWhitespaceInsensitive()
    {
        PackageHashVerifier.Matches(" ABC ", "abc").Should().BeTrue();
        PackageHashVerifier.Matches("abc", "abd").Should().BeFalse();
        PackageHashVerifier.Matches(null, "abc").Should().BeFalse();
    }

    [Fact]
    public async Task VerifyFileAsync_ReturnsHash_WhenMatching_AndThrowsOnMismatch()
    {
        using TestDirectory dir = new TestDirectory();
        string file = await WriteInstallerAsync(dir, "payload");

        (await PackageHashVerifier.VerifyFileAsync(file, HashOf("payload"), "x", default))
            .Should()
            .Be(HashOf("payload"));

        Func<Task> act = () =>
            PackageHashVerifier.VerifyFileAsync(file, HashOf("other"), "pkg 1.0", default);
        await act.Should()
            .ThrowAsync<PackageIntegrityException>()
            .WithMessage("*Checksum mismatch for pkg 1.0*");
    }

    [Fact]
    public void FindChecksum_ParsesGeneratedFormat_AndBinaryMarker_AndBom()
    {
        string hashA = new string('a', 64);
        string hashB = new string('B', 64);
        string content =
            $"﻿{hashA}  StorkDrop.App.exe\r\n{hashB} *StorkDrop-1.2.3-Setup.exe\r\n\r\nnot a line\r\n";

        PublishedChecksumVerifier
            .FindChecksum(content, "StorkDrop-1.2.3-Setup.exe")
            .Should()
            .Be(new string('b', 64));
        PublishedChecksumVerifier.FindChecksum(content, "StorkDrop.App.exe").Should().Be(hashA);
        PublishedChecksumVerifier.FindChecksum(content, "missing.exe").Should().BeNull();
    }

    [Fact]
    public void FindChecksum_IgnoresMalformedHashes()
    {
        PublishedChecksumVerifier.FindChecksum("zzz  a.exe\nabc  a.exe", "a.exe").Should().BeNull();
    }

    [Fact]
    public void ChecksumUrlFor_PointsAtTheFileNextToTheDownload()
    {
        PublishedChecksumVerifier
            .ChecksumUrlFor(new Uri(DownloadUrl))
            .ToString()
            .Should()
            .Be(SumsUrl);
    }

    [Fact]
    public async Task VerifyAsync_Succeeds_WhenPublishedChecksumMatches()
    {
        using TestDirectory dir = new TestDirectory();
        string file = await WriteInstallerAsync(dir, "installer-bytes");
        MockHttpMessageHandler handler = new MockHttpMessageHandler();
        handler
            .When(SumsUrl)
            .Respond(
                "text/plain",
                $"{new string('0', 64)}  StorkDrop.App.exe\n{HashOf("installer-bytes")}  StorkDrop-1.2.3-Setup.exe\n"
            );

        (await Verify(handler, file)).Should().BeTrue();
    }

    [Fact]
    public async Task VerifyAsync_ThrowsIntegrityException_WhenChecksumDiffers()
    {
        using TestDirectory dir = new TestDirectory();
        string file = await WriteInstallerAsync(dir, "tampered");
        MockHttpMessageHandler handler = new MockHttpMessageHandler();
        handler
            .When(SumsUrl)
            .Respond("text/plain", $"{HashOf("original")}  StorkDrop-1.2.3-Setup.exe\n");

        Func<Task> act = () => Verify(handler, file);

        await act.Should().ThrowAsync<PackageIntegrityException>();
    }

    [Fact]
    public async Task VerifyAsync_ReturnsFalse_WhenChecksumFileIsNotPublished()
    {
        using TestDirectory dir = new TestDirectory();
        string file = await WriteInstallerAsync(dir, "x");
        MockHttpMessageHandler handler = new MockHttpMessageHandler();
        handler.When(SumsUrl).Respond(HttpStatusCode.NotFound);

        (await Verify(handler, file)).Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAsync_ReturnsFalse_WhenFileIsNotListed()
    {
        using TestDirectory dir = new TestDirectory();
        string file = await WriteInstallerAsync(dir, "x");
        MockHttpMessageHandler handler = new MockHttpMessageHandler();
        handler.When(SumsUrl).Respond("text/plain", $"{HashOf("y")}  Other-Setup.exe\n");

        (await Verify(handler, file)).Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAsync_ReturnsFalse_WhenTheServerFails()
    {
        using TestDirectory dir = new TestDirectory();
        string file = await WriteInstallerAsync(dir, "x");
        MockHttpMessageHandler handler = new MockHttpMessageHandler();
        handler.When(SumsUrl).Respond(HttpStatusCode.InternalServerError);

        (await Verify(handler, file)).Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAsync_ReturnsFalse_WhenTheNetworkFails()
    {
        using TestDirectory dir = new TestDirectory();
        string file = await WriteInstallerAsync(dir, "x");
        MockHttpMessageHandler handler = new MockHttpMessageHandler();
        handler.When(SumsUrl).Throw(new HttpRequestException("offline"));

        (await Verify(handler, file)).Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAsync_ComparesAgainstTheAssetNameNotTheLocalFileName()
    {
        using TestDirectory dir = new TestDirectory();
        string file = dir.File("renamed-locally.exe");
        await File.WriteAllTextAsync(file, "bytes");
        MockHttpMessageHandler handler = new MockHttpMessageHandler();
        handler
            .When(SumsUrl)
            .Respond("text/plain", $"{HashOf("bytes")}  StorkDrop-1.2.3-Setup.exe\n");

        (await Verify(handler, file)).Should().BeTrue();
    }
}
