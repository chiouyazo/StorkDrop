using Microsoft.Extensions.DependencyInjection;
using StorkDrop.Core.Interfaces;

namespace StorkDrop.Installer;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInstaller(this IServiceCollection services)
    {
        services.AddSingleton<IProductRepository, ProductRepository>();
        services.AddSingleton<IActivityLog, ActivityLogStore>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        services.AddSingleton<IEncryptionService, EncryptionService>();
        services.AddSingleton<IFileLockDetector, FileLockDetector>();
        services.AddSingleton<IInstallationEngine, InstallationEngine>();
        services.AddSingleton<FileOperations>();
        services.AddSingleton<DeferredFileOps>();
        services.AddSingleton<UninstallService>();

        return services;
    }
}
