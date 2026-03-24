using FluentAssertions;
using StorkDrop.Core.Models;
using StorkDrop.Core.Services;
using Xunit;

namespace StorkDrop.Tests.Core;

public sealed class ManifestValidatorTests
{
    private readonly ManifestValidator _validator = new();

    private static ProductManifest CreateValidManifest() =>
        new(
            ProductId: "test-product",
            Title: "Test Product",
            Version: "1.0.0",
            ReleaseDate: new DateOnly(2025, 1, 15),
            InstallType: InstallType.Plugin,
            Description: "A test product"
        );

    [Fact]
    public void Validate_ValidManifest_ShouldReturnValid()
    {
        ProductManifest manifest = CreateValidManifest();

        ManifestValidationResult result = _validator.Validate(manifest);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_NullManifest_ShouldReturnInvalid()
    {
        ManifestValidationResult result = _validator.Validate(null);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("null"));
    }

    [Fact]
    public void Validate_EmptyProductId_ShouldReturnError()
    {
        ProductManifest manifest = CreateValidManifest() with { ProductId = "" };

        ManifestValidationResult result = _validator.Validate(manifest);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ProductId"));
    }

    [Fact]
    public void Validate_EmptyTitle_ShouldReturnError()
    {
        ProductManifest manifest = CreateValidManifest() with { Title = "" };

        ManifestValidationResult result = _validator.Validate(manifest);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Title"));
    }

    [Fact]
    public void Validate_InvalidVersion_ShouldReturnError()
    {
        ProductManifest manifest = CreateValidManifest() with { Version = "not-a-version" };

        ManifestValidationResult result = _validator.Validate(manifest);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Version"));
    }

    [Fact]
    public void Validate_EmptyVersion_ShouldReturnError()
    {
        ProductManifest manifest = CreateValidManifest() with { Version = "" };

        ManifestValidationResult result = _validator.Validate(manifest);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Version"));
    }

    [Fact]
    public void Validate_DefaultReleaseDate_ShouldReturnError()
    {
        ProductManifest manifest = CreateValidManifest() with { ReleaseDate = default };

        ManifestValidationResult result = _validator.Validate(manifest);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ReleaseDate"));
    }

    [Fact]
    public void Validate_BundleWithoutProducts_ShouldReturnError()
    {
        ProductManifest manifest = CreateValidManifest() with { InstallType = InstallType.Bundle };

        ManifestValidationResult result = _validator.Validate(manifest);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("BundledProductIds"));
    }

    [Fact]
    public void Validate_BundleWithProducts_ShouldBeValid()
    {
        ProductManifest manifest = CreateValidManifest() with
        {
            InstallType = InstallType.Bundle,
            BundledProductIds = ["product-a", "product-b"],
        };

        ManifestValidationResult result = _validator.Validate(manifest);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_BundledProductIdsWithEmptyEntry_ShouldReturnError()
    {
        ProductManifest manifest = CreateValidManifest() with
        {
            InstallType = InstallType.Plugin,
            BundledProductIds = ["product-a", ""],
        };

        ManifestValidationResult result = _validator.Validate(manifest);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("empty entry"));
    }

    [Fact]
    public void Validate_MultipleErrors_ShouldReturnAllErrors()
    {
        ProductManifest manifest = new(
            ProductId: "",
            Title: "",
            Version: "",
            ReleaseDate: default,
            InstallType: InstallType.Plugin
        );

        ManifestValidationResult result = _validator.Validate(manifest);

        result.IsValid.Should().BeFalse();
        result.Errors.Count.Should().BeGreaterThanOrEqualTo(4);
    }
}
