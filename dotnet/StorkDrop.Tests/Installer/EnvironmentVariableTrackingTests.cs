using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;
using StorkDrop.Installer;
using Xunit;

namespace StorkDrop.Tests.Installer;

[Collection(StorkPathsCollection.Name)]
public sealed class EnvironmentVariableTrackingTests : IDisposable
{
    private const string UniqueId = "ab12cd34";
    private const string InvalidName = "STORKTEST=INVALID";

    private readonly string _productId = "envtest-" + Guid.NewGuid().ToString("N")[..8];
    private readonly string _variableName = "STORKTEST_" + Guid.NewGuid().ToString("N")[..8];
    private readonly FakeEnvironmentVariableStore _store = new FakeEnvironmentVariableStore();
    private readonly IsolatedStorkEnvironment _isolation = new IsolatedStorkEnvironment();
    private readonly EnvironmentVariableService _service;

    public EnvironmentVariableTrackingTests()
    {
        _service = new EnvironmentVariableService(
            NullLogger<EnvironmentVariableService>.Instance,
            _store
        );
    }

    private string TrackingPath => StorkPaths.EnvVarsPath(_productId, UniqueId);

    public void Dispose()
    {
        _store.Set(_variableName, null, EnvironmentVariableTarget.User);
        if (Directory.Exists(TrackingPath))
            Directory.Delete(TrackingPath, recursive: true);
        _service.DeleteTracking(_productId, UniqueId);
        _isolation.Dispose();
    }

    private async Task<List<AppliedEnvironmentVariable>> ReadTrackingAsync()
    {
        string json = await File.ReadAllTextAsync(TrackingPath);
        return JsonSerializer.Deserialize<List<AppliedEnvironmentVariable>>(json)!;
    }

    [Fact]
    public async Task RemoveTracked_WhenAllRemoved_DeletesTrackingFile()
    {
        _store.Set(_variableName, "value", EnvironmentVariableTarget.User);
        await _service.SaveAppliedAsync(
            _productId,
            UniqueId,
            [new AppliedEnvironmentVariable(_variableName, "set", "value", ";", "user")],
            CancellationToken.None
        );

        List<AppliedEnvironmentVariable> failed = await _service.RemoveTrackedAsync(
            _productId,
            UniqueId,
            CancellationToken.None
        );

        failed.Should().BeEmpty();
        File.Exists(TrackingPath).Should().BeFalse();
        _store.Get(_variableName, EnvironmentVariableTarget.User).Should().BeNull();
    }

    [Fact]
    public async Task RemoveTracked_WhenOneRemovalFails_KeepsOnlyTheFailedEntryTracked()
    {
        _store.Set(_variableName, "value", EnvironmentVariableTarget.User);
        AppliedEnvironmentVariable bad = new AppliedEnvironmentVariable(
            InvalidName,
            "set",
            "x",
            ";",
            "user"
        );
        await _service.SaveAppliedAsync(
            _productId,
            UniqueId,
            [new AppliedEnvironmentVariable(_variableName, "set", "value", ";", "user"), bad],
            CancellationToken.None
        );

        List<AppliedEnvironmentVariable> failed = await _service.RemoveTrackedAsync(
            _productId,
            UniqueId,
            CancellationToken.None
        );

        failed.Should().ContainSingle().Which.Should().Be(bad);
        File.Exists(TrackingPath).Should().BeTrue();
        (await ReadTrackingAsync()).Should().ContainSingle().Which.Name.Should().Be(InvalidName);
        _store.Get(_variableName, EnvironmentVariableTarget.User).Should().BeNull();
    }

    [Fact]
    public async Task ApplyTracked_WhenTrackingCannotBeWritten_DoesNotSetTheVariable()
    {
        Directory.CreateDirectory(TrackingPath);

        Func<Task> apply = () =>
            _service.ApplyTrackedAsync(
                _productId,
                UniqueId,
                [new EnvironmentVariableInfo(_variableName, "value", "set", false, ";", "user")],
                @"C:\Acme",
                CancellationToken.None
            );

        await apply.Should().ThrowAsync<Exception>();
        _store.Get(_variableName, EnvironmentVariableTarget.User).Should().BeNull();
    }

    [Fact]
    public async Task ApplyTracked_TracksTheAppliedVariable_AndRemoveUndoesIt()
    {
        List<AppliedEnvironmentVariable> applied = await _service.ApplyTrackedAsync(
            _productId,
            UniqueId,
            [
                new EnvironmentVariableInfo(
                    _variableName,
                    @"{InstallPath}\bin",
                    "set",
                    false,
                    ";",
                    "user"
                ),
            ],
            @"C:\Acme",
            CancellationToken.None
        );

        applied.Should().ContainSingle();
        (await ReadTrackingAsync())
            .Should()
            .ContainSingle()
            .Which.AppliedValue.Should()
            .Be(@"C:\Acme\bin");
        _store.Get(_variableName, EnvironmentVariableTarget.User).Should().Be(@"C:\Acme\bin");

        await _service.RemoveTrackedAsync(_productId, UniqueId, CancellationToken.None);

        _store.Get(_variableName, EnvironmentVariableTarget.User).Should().BeNull();
        File.Exists(TrackingPath).Should().BeFalse();
    }

    [Fact]
    public async Task ApplyTracked_AppendOfAnEntryThatWasAlreadyThere_IsNotTracked()
    {
        _store.Set(_variableName, @"C:\Windows;C:\Acme\bin", EnvironmentVariableTarget.User);

        List<AppliedEnvironmentVariable> applied = await _service.ApplyTrackedAsync(
            _productId,
            UniqueId,
            [
                new EnvironmentVariableInfo(
                    _variableName,
                    @"{InstallPath}\bin",
                    "append",
                    false,
                    ";",
                    "user"
                ),
            ],
            @"C:\Acme",
            CancellationToken.None
        );

        applied.Should().BeEmpty();
        File.Exists(TrackingPath).Should().BeFalse();
        _store
            .Get(_variableName, EnvironmentVariableTarget.User)
            .Should()
            .Be(@"C:\Windows;C:\Acme\bin");
    }

    [Fact]
    public async Task ApplyTracked_AppendOfANewEntry_IsTrackedAndRemovedAgain()
    {
        _store.Set(_variableName, @"C:\Windows", EnvironmentVariableTarget.User);

        List<AppliedEnvironmentVariable> applied = await _service.ApplyTrackedAsync(
            _productId,
            UniqueId,
            [
                new EnvironmentVariableInfo(
                    _variableName,
                    @"{InstallPath}\bin",
                    "append",
                    false,
                    ";",
                    "user"
                ),
            ],
            @"C:\Acme",
            CancellationToken.None
        );

        applied.Should().ContainSingle();
        await _service.RemoveTrackedAsync(_productId, UniqueId, CancellationToken.None);
        _store.Get(_variableName, EnvironmentVariableTarget.User).Should().Be(@"C:\Windows");
    }

    [Fact]
    public async Task ApplyTracked_WhenNothingWasApplied_LeavesNoTrackingFile()
    {
        List<AppliedEnvironmentVariable> applied = await _service.ApplyTrackedAsync(
            _productId,
            UniqueId,
            [new EnvironmentVariableInfo(InvalidName, "value", "set", false, ";", "user")],
            @"C:\Acme",
            CancellationToken.None
        );

        applied.Should().BeEmpty();
        File.Exists(TrackingPath).Should().BeFalse();
    }
}
