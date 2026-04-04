using StorkDrop.Contracts.Interfaces;

namespace StorkDrop.Demo.Services;

internal sealed class DemoPluginSettingsStore : IPluginSettingsStore
{
    private readonly Dictionary<string, Dictionary<string, string>> _store = new Dictionary<
        string,
        Dictionary<string, string>
    >
    {
        ["demo-sql-tools"] = new Dictionary<string, string>
        {
            ["databases"] =
                """[{"name":"Production","server":"db-prod-01","database":"NovaDB","password":"secret"},{"name":"Staging","server":"db-staging-01","database":"NovaDB_Staging","password":"secret"}]""",
        },
    };

    public Task<Dictionary<string, string>> LoadAsync(
        string pluginId,
        CancellationToken ct = default
    ) => Task.FromResult(_store.GetValueOrDefault(pluginId) ?? new Dictionary<string, string>());

    public Task SaveAsync(
        string pluginId,
        Dictionary<string, string> values,
        CancellationToken ct = default
    )
    {
        _store[pluginId] = values;
        return Task.CompletedTask;
    }
}
