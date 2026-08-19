using Microsoft.Extensions.DependencyInjection;
using StorkDrop.Contracts.Interfaces;

namespace StorkDrop.Registry.S3;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the S3 backend: the static-keys credential provider and the S3 registry-client
    /// factory. Call alongside <c>AddFeedRegistry()</c> so <c>FeedRegistry</c> can serve S3 feeds.
    /// </summary>
    public static IServiceCollection AddS3Registry(this IServiceCollection services)
    {
        services.AddSingleton<IS3CredentialProvider, StaticKeysCredentialProvider>();
        services.AddSingleton<IRegistryClientFactory, S3RegistryClientFactory>();
        return services;
    }
}
