using System.Text.Json;
using System.Text.Json.Nodes;

namespace StorkDrop.Publisher;

/// <summary>
/// Generates a least-privilege, read-only S3/IAM policy for a customer, scoped to one channel prefix
/// (default <c>prod</c>). Access is enforced purely by that prefix: the customer can only list and get
/// objects under it, so they never see anything outside their channel. There is no catalog object to
/// grant — discovery is done by listing, exactly like the Nexus backend.
///
/// This grants the WHOLE channel (channel-level tenancy: every prod customer sees every prod product,
/// matching a Nexus repo where read = browse-all). Per-customer product subsets are deliberately NOT
/// modelled by carving product prefixes into the key path here, because a single S3 <c>ListObjectsV2</c>
/// call takes one prefix and IAM does not filter a listing per key — the client could not enumerate a
/// product subset. Per-product/per-customer entitlement is intended to be issued as scoped STS session
/// credentials (see <c>IS3CredentialProvider</c> / <c>S3FeedSettings.RoleArn</c>), not baked into the
/// bucket layout.
/// </summary>
public static class IamPolicyGenerator
{
    public static string ForCustomer(string bucket, string? prefix = null, string channel = "prod")
    {
        string normalizedPrefix = NormalizePrefix(prefix);
        string bucketArn = $"arn:aws:s3:::{bucket}";
        string channelPrefix = $"{normalizedPrefix}{channel}/";

        JsonObject policy = new JsonObject
        {
            ["Version"] = "2012-10-17",
            ["Statement"] = new JsonArray
            {
                new JsonObject
                {
                    ["Sid"] = "ReadChannel",
                    ["Effect"] = "Allow",
                    ["Action"] = new JsonArray { "s3:GetObject" },
                    ["Resource"] = new JsonArray { $"{bucketArn}/{channelPrefix}*" },
                },
                new JsonObject
                {
                    ["Sid"] = "ListChannel",
                    ["Effect"] = "Allow",
                    ["Action"] = new JsonArray { "s3:ListBucket" },
                    ["Resource"] = new JsonArray { bucketArn },
                    ["Condition"] = new JsonObject
                    {
                        ["StringLike"] = new JsonObject
                        {
                            ["s3:prefix"] = new JsonArray { $"{channelPrefix}*" },
                        },
                    },
                },
            },
        };

        return policy.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string NormalizePrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return string.Empty;
        string trimmed = prefix.Trim().Trim('/');
        return trimmed.Length == 0 ? string.Empty : trimmed + "/";
    }
}
