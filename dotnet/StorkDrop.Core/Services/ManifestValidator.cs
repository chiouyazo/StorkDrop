using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.Core.Services;

public sealed class ManifestValidator
{
    public ManifestValidationResult Validate(ProductManifest? manifest)
    {
        List<string> errors = [];

        if (manifest is null)
        {
            errors.Add("Manifest is null.");
            return new ManifestValidationResult(false, errors);
        }

        if (string.IsNullOrWhiteSpace(manifest.ProductId))
            errors.Add("ProductId is required.");

        if (string.IsNullOrWhiteSpace(manifest.Title))
            errors.Add("Title is required.");

        if (string.IsNullOrWhiteSpace(manifest.Version))
            errors.Add("Version is required.");
        else if (!VersionComparer.IsValid(manifest.Version))
            errors.Add($"Version '{manifest.Version}' is not a valid semantic version.");

        if (manifest.ReleaseDate == default)
            errors.Add("ReleaseDate is required.");

        if (!Enum.IsDefined(manifest.InstallType))
            errors.Add($"InstallType '{manifest.InstallType}' is not a valid install type.");

        if (
            manifest.InstallType == InstallType.Bundle
            && (manifest.BundledProductIds is null || manifest.BundledProductIds.Length == 0)
        )
            errors.Add("Bundle products must specify BundledProductIds.");

        if (manifest.BundledProductIds is not null)
        {
            foreach (string id in manifest.BundledProductIds)
            {
                if (string.IsNullOrWhiteSpace(id))
                    errors.Add("BundledProductIds contains an empty entry.");
            }
        }

        if (manifest.EnvironmentVariables is not null)
        {
            for (int i = 0; i < manifest.EnvironmentVariables.Length; i++)
            {
                EnvironmentVariableInfo envVar = manifest.EnvironmentVariables[i];
                string prefix = $"EnvironmentVariables[{i}]";

                if (string.IsNullOrWhiteSpace(envVar.Name))
                    errors.Add($"{prefix}: Name is required.");

                if (string.IsNullOrWhiteSpace(envVar.Value))
                    errors.Add($"{prefix}: Value is required.");

                if (
                    !envVar.Action.Equals("set", StringComparison.OrdinalIgnoreCase)
                    && !envVar.Action.Equals("append", StringComparison.OrdinalIgnoreCase)
                )
                    errors.Add($"{prefix}: Action must be 'set' or 'append'.");

                if (
                    !envVar.Target.Equals("machine", StringComparison.OrdinalIgnoreCase)
                    && !envVar.Target.Equals("user", StringComparison.OrdinalIgnoreCase)
                )
                    errors.Add($"{prefix}: Target must be 'machine' or 'user'.");
            }
        }

        return new ManifestValidationResult(errors.Count == 0, errors);
    }
}

public sealed record ManifestValidationResult(bool IsValid, IReadOnlyList<string> Errors);
