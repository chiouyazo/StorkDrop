using FluentAssertions;
using StorkDrop.App.ViewModels;
using Xunit;

namespace StorkDrop.Tests.ViewModels;

public sealed class UpdateItemViewModelTests
{
    [Theory]
    [InlineData("STEPS", "STEPS", false)]
    [InlineData("STEPS", "steps", false)]
    [InlineData(null, null, false)]
    [InlineData("", null, false)]
    [InlineData("STEPS", "FEATURE CRYSYNC-2468", true)]
    [InlineData("FEATURE CRYSYNC-2468", "STEPS", true)]
    [InlineData("FEATURE CRYSYNC-2468", "FEATURE CRYSYNC-2500", true)]
    [InlineData(null, "FEATURE CRYSYNC-2468", true)]
    [InlineData("EPIC", null, true)]
    public void HasBadgeChange_ComparesInstalledAndAvailableBadge(
        string? installed,
        string? available,
        bool expected
    )
    {
        UpdateItemViewModel item = new UpdateItemViewModel
        {
            CurrentBadgeText = installed,
            BadgeText = available,
        };

        item.HasBadgeChange.Should().Be(expected);
    }

    [Fact]
    public void BadgeDisplay_EmptyBadge_ShowsDash()
    {
        UpdateItemViewModel item = new UpdateItemViewModel { BadgeText = "FEATURE X-1" };

        item.BadgeDisplay.Should().Be("FEATURE X-1");
        item.CurrentBadgeDisplay.Should().Be("-");
    }

    [Fact]
    public void HasBadge_FollowsBadgeText()
    {
        UpdateItemViewModel item = new UpdateItemViewModel();
        item.HasBadge.Should().BeFalse();

        item.BadgeText = "STEPS";

        item.HasBadge.Should().BeTrue();
    }

    [Fact]
    public void HasFeedName_FollowsFeedName()
    {
        UpdateItemViewModel item = new UpdateItemViewModel();
        item.HasFeedName.Should().BeFalse();

        item.FeedName = "Intern / EPIC";

        item.HasFeedName.Should().BeTrue();
    }

    [Fact]
    public void BadgeChanges_RaisePropertyChangedForDerivedProperties()
    {
        UpdateItemViewModel item = new UpdateItemViewModel();
        List<string?> raised = [];
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        item.BadgeText = "EPIC";
        item.CurrentBadgeText = "STEPS";

        raised
            .Should()
            .Contain([
                nameof(UpdateItemViewModel.HasBadge),
                nameof(UpdateItemViewModel.HasBadgeChange),
                nameof(UpdateItemViewModel.BadgeDisplay),
                nameof(UpdateItemViewModel.CurrentBadgeDisplay),
            ]);
    }
}
