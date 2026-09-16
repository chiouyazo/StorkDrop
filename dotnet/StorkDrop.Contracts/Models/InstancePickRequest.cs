namespace StorkDrop.Contracts.Models;

/// <summary>
/// A plugin's request to let the operator pick one installed instance of a product through StorkDrop's
/// instance picker (e.g. to resolve the install path of another product to integrate with).
/// </summary>
/// <param name="ProductId">Product whose installed instances are offered.</param>
/// <param name="Message">Optional prompt text; the host shows a default when null.</param>
public sealed record InstancePickRequest(string ProductId, string? Message = null);
