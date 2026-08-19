using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Registry.S3;

namespace StorkDrop.Publisher;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        string command = args[0].ToLowerInvariant();
        Dictionary<string, string> options = ParseOptions(args.Skip(1));

        try
        {
            return command switch
            {
                "publish" => await PublishAsync(options),
                "iam-policy" => WritePolicy(options),
                _ => Unknown(command),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> PublishAsync(Dictionary<string, string> options)
    {
        string bucket = Required(options, "bucket");
        string channel = options.GetValueOrDefault("channel", "prod");
        string manifestPath = Required(options, "manifest");
        string packagePath = Required(options, "package");

        S3FeedSettings settings = new S3FeedSettings(
            Bucket: bucket,
            Region: options.GetValueOrDefault("region"),
            ServiceUrl: options.GetValueOrDefault("service-url"),
            UsePathStyle: options.ContainsKey("path-style"),
            AccessKeyId: options.GetValueOrDefault("access-key"),
            Prefix: options.GetValueOrDefault("prefix")
        );
        DecryptedFeedSecrets secrets = new DecryptedFeedSecrets(
            S3SecretKey: options.GetValueOrDefault("secret-key")
        );

        AWSCredentials credentials = new StaticKeysCredentialProvider().GetCredentials(
            settings,
            secrets
        );
        using IAmazonS3 s3 = S3ClientBuilder.Build(settings, credentials);

        string manifestJson = await File.ReadAllTextAsync(manifestPath);
        ProductManifest? manifest = JsonSerializer.Deserialize<ProductManifest>(
            manifestJson,
            S3Json.Options
        );
        if (manifest is null)
        {
            Console.Error.WriteLine($"Could not read manifest at {manifestPath}");
            return 1;
        }

        S3Publisher publisher = new S3Publisher(s3, bucket, settings.Prefix);
        await using FileStream package = File.OpenRead(packagePath);
        ProductManifest stored = await publisher.PublishAsync(channel, manifest, package);

        Console.WriteLine(
            $"Published {stored.ProductId} {stored.Version} to channel '{channel}' (sha256 {stored.ContentSha256})."
        );
        return 0;
    }

    private static int WritePolicy(Dictionary<string, string> options)
    {
        string bucket = Required(options, "bucket");
        string channel = options.GetValueOrDefault("channel", "prod");
        string? prefix = options.GetValueOrDefault("prefix");

        Console.WriteLine(IamPolicyGenerator.ForCustomer(bucket, prefix, channel));
        return 0;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintUsage();
        return 1;
    }

    private static string Required(Dictionary<string, string> options, string key)
    {
        if (options.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
            return value;
        throw new ArgumentException($"Missing required option --{key}.");
    }

    private static Dictionary<string, string> ParseOptions(IEnumerable<string> args)
    {
        Dictionary<string, string> options = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase
        );
        string[] list = args.ToArray();
        for (int i = 0; i < list.Length; i++)
        {
            string arg = list[i];
            if (!arg.StartsWith("--"))
                continue;
            string key = arg[2..];
            if (i + 1 < list.Length && !list[i + 1].StartsWith("--"))
            {
                options[key] = list[i + 1];
                i++;
            }
            else
            {
                options[key] = "true"; // flag
            }
        }
        return options;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            storkdrop-publish - publish products to a StorkDrop S3 bucket

            Commands:
              publish     --bucket <b> --manifest <manifest.json> --package <pkg.zip>
                          [--channel prod] [--region <r>] [--service-url <url>] [--path-style]
                          [--access-key <k>] [--secret-key <s>] [--prefix <p>]

              iam-policy  --bucket <b> [--channel prod] [--prefix <p>]
            """
        );
    }
}
