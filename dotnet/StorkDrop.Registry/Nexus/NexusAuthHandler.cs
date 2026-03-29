using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StorkDrop.Contracts.Models;

namespace StorkDrop.Registry.Nexus;

public sealed class NexusAuthHandler(
    IOptions<NexusOptions> options,
    ILogger<NexusAuthHandler> logger
) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        NexusOptions opts = options.Value;
        if (!string.IsNullOrEmpty(opts.Username) && !string.IsNullOrEmpty(opts.Password))
        {
            try
            {
                string credentials = Convert.ToBase64String(
                    Encoding.ASCII.GetBytes($"{opts.Username}:{opts.Password}")
                );
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Fehler beim Kodieren der Authentifizierungsdaten");
            }
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
