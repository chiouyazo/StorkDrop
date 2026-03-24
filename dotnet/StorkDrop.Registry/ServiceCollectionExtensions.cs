using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.Registry;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNexusRegistry(
        this IServiceCollection services,
        Action<NexusOptions> configure
    )
    {
        services.Configure(configure);
        services.AddTransient<NexusAuthHandler>();
        services
            .AddHttpClient<IRegistryClient, NexusRegistryClient>(
                (serviceProvider, client) =>
                {
                    NexusOptions opts = serviceProvider
                        .GetRequiredService<IOptions<NexusOptions>>()
                        .Value;
                    client.BaseAddress = new Uri(opts.BaseUrl);
                    client.Timeout = opts.Timeout;
                }
            )
            .ConfigurePrimaryHttpMessageHandler(() =>
                new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                }
            )
            .AddHttpMessageHandler<NexusAuthHandler>();

        return services;
    }
}
