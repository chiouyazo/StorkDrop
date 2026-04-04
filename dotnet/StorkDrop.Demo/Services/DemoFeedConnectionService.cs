using System.Net.Http;
using StorkDrop.Registry;

namespace StorkDrop.Demo.Services;

internal sealed class DemoFeedConnectionService : IFeedConnectionService
{
    public HttpClient CreateAuthenticatedClient(
        string baseUrl,
        string? username,
        string? password
    ) => new HttpClient();

    public Task<FeedConnectionResult> TestConnectionAsync(
        string url,
        string? username,
        string? password,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new FeedConnectionResult(Success: true, RepositoryCount: 2));
}
