namespace StorkDrop.Installer;

internal sealed class SystemEnvironmentVariableStore : IEnvironmentVariableStore
{
    public string? Get(string name, EnvironmentVariableTarget target) =>
        Environment.GetEnvironmentVariable(name, target);

    public void Set(string name, string? value, EnvironmentVariableTarget target) =>
        Environment.SetEnvironmentVariable(name, value, target);
}
