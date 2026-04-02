namespace StorkDrop.Contracts.Models;

/// <summary>
/// Declares a companion product that should be offered for installation after the main product installs.
/// </summary>
/// <param name="Id">The product ID of the companion product.</param>
/// <param name="HideNoAccess">
/// When true, silently skip this product if not accessible in any feed.
/// When false, show a non-blocking warning that the product is recommended but not available.
/// </param>
public sealed record OptionalPostProduct(string Id, bool HideNoAccess = true);
