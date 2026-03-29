using System.Net.Http.Headers;
using System.Text;
using StorkDrop.Registry.Nexus;

namespace StorkDrop.Registry;

/// <summary>
/// Centralizes feed connection logic: HttpClient creation, authentication, connection testing, and repository discovery.
/// </summary>
public sealed class FeedConnectionService : IFeedConnectionService
{
    public HttpClient CreateAuthenticatedClient(string baseUrl, string? username, string? password)
    {
        HttpClientHandler handler = new()
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };

        HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(30),
        };

        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
        {
            string creds = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{username}:{password}")
            );
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic",
                creds
            );
        }

        return httpClient;
    }

    public async Task<FeedConnectionResult> TestConnectionAsync(
        string url,
        string? username,
        string? password,
        CancellationToken cancellationToken = default
    )
    {
        using HttpClient client = CreateAuthenticatedClient(url, username, password);
        string baseUrl = url.TrimEnd('/');

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        HttpResponseMessage response = await client.GetAsync(
            $"{baseUrl}/service/rest/v1/repositories",
            cts.Token
        );

        if (!response.IsSuccessStatusCode)
        {
            return new FeedConnectionResult(
                Success: false,
                RepositoryCount: 0,
                HttpStatusCode: (int)response.StatusCode
            );
        }

        int repoCount = 0;
        try
        {
            IReadOnlyList<NexusRepositoryInfo> repos =
                await NexusRegistryClient.ListRawHostedRepositoriesAsync(
                    client,
                    baseUrl,
                    cts.Token
                );
            repoCount = repos.Count;
        }
        catch
        {
            // Connection works but repo listing failed — still a success
        }

        return new FeedConnectionResult(Success: true, RepositoryCount: repoCount);
    }
}

public sealed record FeedConnectionResult(
    bool Success,
    int RepositoryCount,
    int? HttpStatusCode = null
);
