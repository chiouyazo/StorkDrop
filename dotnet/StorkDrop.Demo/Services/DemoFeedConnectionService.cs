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

    public async Task<FeedConnectionResult> TestConnectionAsync(
        string url,
        string? username,
        string? password,
        CancellationToken cancellationToken = default
    )
    {
        if (url.Contains("down", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Delay(700, cancellationToken);
            throw new HttpRequestException(
                "No connection could be made because the target machine actively refused it."
            );
        }

        if (url.Contains("unauth", StringComparison.OrdinalIgnoreCase))
            return new FeedConnectionResult(
                Success: false,
                RepositoryCount: 0,
                HttpStatusCode: 401
            );

        return new FeedConnectionResult(Success: true, RepositoryCount: 2);
    }
}
