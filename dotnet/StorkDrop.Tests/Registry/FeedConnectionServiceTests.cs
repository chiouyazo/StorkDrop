using FluentAssertions;
using StorkDrop.Registry;
using Xunit;

namespace StorkDrop.Tests.Registry;

public sealed class FeedConnectionServiceTests
{
    private readonly FeedConnectionService _service = new();

    [Fact]
    public void CreateAuthenticatedClient_SetsBaseAddress()
    {
        using HttpClient client = _service.CreateAuthenticatedClient(
            "https://nexus.example.com",
            null,
            null
        );

        client.BaseAddress.Should().Be(new Uri("https://nexus.example.com"));
    }

    [Fact]
    public void CreateAuthenticatedClient_AddsAuthorizationHeader_WhenCredentialsProvided()
    {
        using HttpClient client = _service.CreateAuthenticatedClient(
            "https://nexus.example.com",
            "admin",
            "secret"
        );

        client.DefaultRequestHeaders.Authorization.Should().NotBeNull();
        client.DefaultRequestHeaders.Authorization!.Scheme.Should().Be("Basic");
        client.DefaultRequestHeaders.Authorization.Parameter.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CreateAuthenticatedClient_SkipsAuth_WhenNoCredentials()
    {
        using HttpClient client = _service.CreateAuthenticatedClient(
            "https://nexus.example.com",
            null,
            null
        );

        client.DefaultRequestHeaders.Authorization.Should().BeNull();
    }

    [Fact]
    public void CreateAuthenticatedClient_SkipsAuth_WhenEmptyCredentials()
    {
        using HttpClient client = _service.CreateAuthenticatedClient(
            "https://nexus.example.com",
            "",
            ""
        );

        client.DefaultRequestHeaders.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task TestConnectionAsync_ThrowsForUnreachableServer()
    {
        // Use localhost on a port that is almost certainly not listening.
        // The service does not catch connection-level exceptions, so it propagates.
        Func<Task> act = () => _service.TestConnectionAsync("http://127.0.0.1:1", null, null);

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
