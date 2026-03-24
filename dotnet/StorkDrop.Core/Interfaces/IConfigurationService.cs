using StorkDrop.Core.Models;

namespace StorkDrop.Core.Interfaces;

public interface IConfigurationService
{
    Task<AppConfiguration?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken = default);
    Task ExportAsync(string filePath, CancellationToken cancellationToken = default);
    Task ImportAsync(string filePath, CancellationToken cancellationToken = default);
    bool ConfigurationExists();
}
