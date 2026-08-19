using System.Text.Json;
using System.Text.Json.Serialization;

namespace StorkDrop.Registry.S3;

/// <summary>Shared JSON settings for reading and writing S3 product manifests.</summary>
public static class S3Json
{
    public static readonly JsonSerializerOptions Options = new JsonSerializerOptions(
        JsonSerializerDefaults.Web
    )
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
}
