namespace StorkDrop.Installer;

public sealed record ProductLockHolder(
    int ProcessId,
    string ProcessName,
    DateTimeOffset Since,
    string Operation
);
