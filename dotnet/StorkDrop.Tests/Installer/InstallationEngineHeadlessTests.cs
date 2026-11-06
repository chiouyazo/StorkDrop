using FluentAssertions;
using StorkDrop.Contracts.Models;
using Xunit;

namespace StorkDrop.Tests.Installer;

public sealed class InstallationEngineHeadlessTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void ElevatedLongRunning_WithPrompt_FollowsTheUsersChoice(int chosen, bool keepWaiting)
    {
        EngineFixture fixture = new EngineFixture();
        fixture.Engine.OnPrompt = _ => new PluginPromptResult { ChosenIndex = chosen };

        fixture.Engine.HandleElevatedLongRunning("step").Should().Be(keepWaiting);
    }

    [Fact]
    public void ElevatedLongRunning_PromptDismissed_StopsWaiting()
    {
        EngineFixture fixture = new EngineFixture();
        fixture.Engine.OnPrompt = _ => new PluginPromptResult();

        fixture.Engine.HandleElevatedLongRunning("step").Should().BeFalse();
    }
}
