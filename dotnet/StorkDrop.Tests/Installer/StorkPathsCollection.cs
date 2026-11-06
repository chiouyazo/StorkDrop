using Xunit;

namespace StorkDrop.Tests.Installer;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StorkPathsCollection
{
    public const string Name = "StorkPaths";
}
