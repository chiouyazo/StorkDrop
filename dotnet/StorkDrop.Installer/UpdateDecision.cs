namespace StorkDrop.Installer;

public enum UpdateDecision
{
    Proceed,
    ManifestNotFound,
    AlreadyUpToDate,
    DowngradeBlocked,
}
