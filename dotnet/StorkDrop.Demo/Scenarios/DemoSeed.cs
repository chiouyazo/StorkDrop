namespace StorkDrop.Demo.Scenarios;

internal sealed record DemoSeed(
    string ProductId,
    string FeedId,
    string Version,
    string InstanceId = "default"
);
