namespace StorkDrop.Installer;

internal interface IEnvironmentVariableStore
{
    string? Get(string name, EnvironmentVariableTarget target);

    void Set(string name, string? value, EnvironmentVariableTarget target);
}
