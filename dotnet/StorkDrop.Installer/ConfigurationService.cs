using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;

namespace StorkDrop.Installer;

public sealed class ConfigurationService : IConfigurationService, IDisposable
{
    private readonly string _configDir;
    private readonly string _configFilePath;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly ILogger<ConfigurationService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public ConfigurationService(ILogger<ConfigurationService> logger)
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "StorkDrop",
                "Config"
            ),
            logger
        ) { }

    public ConfigurationService(string configDir, ILogger<ConfigurationService> logger)
    {
        _configDir = configDir;
        _configFilePath = Path.Combine(_configDir, "config.json");
        _logger = logger;
        Directory.CreateDirectory(_configDir);
    }

    public async Task<AppConfiguration?> LoadAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Loading configuration from {Path}", _configFilePath);
        if (!File.Exists(_configFilePath))
        {
            _logger.LogDebug("Configuration file not found at {Path}", _configFilePath);
            return null;
        }

        string json = await File.ReadAllTextAsync(_configFilePath, cancellationToken)
            .ConfigureAwait(false);
        AppConfiguration? config = JsonSerializer.Deserialize<AppConfiguration>(json, JsonOptions);

        if (config is not null)
        {
            ValidateConfiguration(config);
        }

        _logger.LogInformation("Configuration loaded successfully from {Path}", _configFilePath);
        return config;
    }

    public async Task SaveAsync(
        AppConfiguration configuration,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogInformation("Saving configuration to {Path}", _configFilePath);
        await _saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        string tempPath = _configFilePath + ".tmp";
        try
        {
            string json = JsonSerializer.Serialize(configuration, JsonOptions);
            string backupPath = _configFilePath + ".bak";

            await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);

            if (File.Exists(_configFilePath))
            {
                File.Copy(_configFilePath, backupPath, overwrite: true);
                File.Move(tempPath, _configFilePath, overwrite: true);
            }
            else
            {
                File.Move(tempPath, _configFilePath);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Best effort cleanup of temp file
            }
            _saveLock.Release();
        }
    }

    public async Task ExportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Exporting configuration to {Path}", filePath);
        if (!File.Exists(_configFilePath))
            throw new InvalidOperationException("No configuration to export.");

        string json = await File.ReadAllTextAsync(_configFilePath, cancellationToken)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Configuration exported successfully to {Path}", filePath);
    }

    public async Task ImportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Importing configuration from {Path}", filePath);
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Import file not found.", filePath);

        string json = await File.ReadAllTextAsync(filePath, cancellationToken)
            .ConfigureAwait(false);

        AppConfiguration? config = JsonSerializer.Deserialize<AppConfiguration>(json, JsonOptions);
        if (config is null)
            throw new InvalidOperationException("Invalid configuration file.");

        ValidateConfiguration(config);

        await _saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        string tempPath = _configFilePath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);

            if (File.Exists(_configFilePath))
            {
                string backupPath = _configFilePath + ".bak";
                File.Copy(_configFilePath, backupPath, overwrite: true);
            }

            File.Move(tempPath, _configFilePath, overwrite: true);
            _logger.LogInformation("Configuration imported successfully from {Path}", filePath);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Best effort cleanup of temp file
            }
            _saveLock.Release();
        }
    }

    public bool ConfigurationExists() => File.Exists(_configFilePath);

    private static void ValidateConfiguration(AppConfiguration config)
    {
        if (config.Feeds is null || config.Feeds.Length == 0)
            throw new InvalidOperationException(
                "Configuration invalid: at least one feed is required."
            );
    }

    public void Dispose()
    {
        _saveLock.Dispose();
    }
}
