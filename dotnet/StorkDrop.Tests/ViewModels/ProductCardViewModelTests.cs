using FluentAssertions;
using StorkDrop.App.ViewModels;
using Xunit;

namespace StorkDrop.Tests.ViewModels;

public sealed class ProductCardViewModelTests
{
    [Fact]
    public void HasBadge_NoBadgeText_IsFalse()
    {
        ProductCardViewModel card = new ProductCardViewModel();

        card.HasBadge.Should().BeFalse();
    }

    [Fact]
    public void HasBadge_BadgeTextSet_IsTrueAndNotifies()
    {
        ProductCardViewModel card = new ProductCardViewModel();
        List<string?> raised = [];
        card.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        card.BadgeText = "FEATURE CRYSYNC-2468";

        card.HasBadge.Should().BeTrue();
        raised.Should().Contain(nameof(ProductCardViewModel.HasBadge));
    }
}
