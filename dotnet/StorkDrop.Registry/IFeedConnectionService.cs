namespace StorkDrop.Registry;

public interface IFeedConnectionService
{
    HttpClient CreateAuthenticatedClient(string baseUrl, string? username, string? password);

    Task<FeedConnectionResult> TestConnectionAsync(
        string url,
        string? username,
        string? password,
        CancellationToken cancellationToken = default
    );
}
