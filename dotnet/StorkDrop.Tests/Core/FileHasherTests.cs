using FluentAssertions;
using StorkDrop.Contracts.Services;
using Xunit;

namespace StorkDrop.Tests.Core;

public sealed class FileHasherTests
{
    [Fact]
    public async Task ComputeSha256Async_EmptyFile_MatchesKnownVector()
    {
        string temp = Path.Combine(
            Path.GetTempPath(),
            "sd-hash-" + Guid.NewGuid().ToString("N") + ".bin"
        );
        try
        {
            await File.WriteAllBytesAsync(temp, []);

            string hash = await FileHasher.ComputeSha256Async(temp);

            hash.Should().Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    [Fact]
    public async Task ComputeSha256Async_DifferentContent_ProducesDifferentHash()
    {
        string a = Path.Combine(Path.GetTempPath(), "sd-a-" + Guid.NewGuid().ToString("N"));
        string b = Path.Combine(Path.GetTempPath(), "sd-b-" + Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllTextAsync(a, "hello");
            await File.WriteAllTextAsync(b, "world");

            string ha = await FileHasher.ComputeSha256Async(a);
            string hb = await FileHasher.ComputeSha256Async(b);

            ha.Should().NotBe(hb);
        }
        finally
        {
            if (File.Exists(a))
                File.Delete(a);
            if (File.Exists(b))
                File.Delete(b);
        }
    }
}
