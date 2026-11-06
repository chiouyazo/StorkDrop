using StorkDrop.Installer;

namespace StorkDrop.Tests.Installer;

internal sealed class FakeEnvironmentVariableStore : IEnvironmentVariableStore
{
    private readonly Dictionary<(string Name, EnvironmentVariableTarget Target), string> _values =
    [];

    public string? Get(string name, EnvironmentVariableTarget target) =>
        _values.TryGetValue((name, target), out string? value) ? value : null;

    public void Set(string name, string? value, EnvironmentVariableTarget target)
    {
        if (string.IsNullOrEmpty(name) || name.Contains('='))
            throw new ArgumentException("Invalid variable name.", nameof(name));

        if (value is null)
            _values.Remove((name, target));
        else
            _values[(name, target)] = value;
    }
}
