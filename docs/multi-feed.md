# Multi-feed support

StorkDrop connects to multiple feeds simultaneously. Products from all feeds appear in a unified marketplace. Each feed chooses a storage backend with the `provider` field: `Nexus` (default, HTTP raw repositories), `S3` (object storage), or `Local` (a folder on disk, for developer sideloading). See [S3 storage](s3-storage.md) for the S3 backend.

```json
{
  "feeds": [
    {
      "id": "internal",
      "name": "Internal Feed",
      "url": "https://nexus.company.com",
      "repository": "releases"
    },
    {
      "id": "vendor",
      "name": "Vendor Feed",
      "url": "https://feed.vendor.com:8443",
      "repository": "tools"
    }
  ]
}
```

- Each feed gets its own HTTP client with independent credentials
- Products are tagged with their source feed throughout the entire lifecycle
- The feed filter dropdown appears when 2+ feeds are configured
- Installed products remember their source feed, so updates check the right repository
- Elevated processes receive the feed ID as a command-line argument

## Backends

All feed interactions go through the `IRegistryClient` interface. Client creation is delegated to per-backend `IRegistryClientFactory` implementations, selected by `FeedConfiguration.Provider`:

- `NexusRegistryClientFactory` — pinned repository or discovery mode (one client per raw repo).
- `S3RegistryClientFactory` — one client per visible channel (see [S3 storage](s3-storage.md)).
- `LocalRegistryClientFactory` — a local folder (`FeedConfiguration.Url`); one product per subfolder, each holding a `manifest.json` and one package `.zip`. For developer sideloading; the full install pipeline (requirements, plugins, elevation) runs unchanged. Add it with the CLI (`--provider local`); the desktop UI does not offer adding one but displays and can remove it.

To add another backend (GitHub Releases, Azure Artifacts, ...):

1. Implement `IRegistryClient` for your backend.
2. Implement `IRegistryClientFactory` (`Provider` + `CreateAsync`) and register it in DI. `FeedRegistry` picks the factory whose `Provider` matches the feed.
3. The marketplace, engine, updates, and all UI features work automatically.

## Layout & permissions per backend

Adding the S3 backend did **not** change the Nexus layout or Nexus permissions — a Nexus repo looks and is secured exactly as before. The two backends differ only in how a product store is laid out and how access is scoped:

| | Nexus (unchanged) | S3 (new) |
|---|---|---|
| Store | One raw repository per feed | One bucket (shared), channel = top-level prefix, optional base `prefix` |
| Product layout | `{productId}/manifest.json`, `{productId}/versions/{version}/manifest.json`, `{productId}/versions/{version}/{productId}-{version}.zip` | `{channel}/{productId}/manifest.json`, `{channel}/{productId}/versions/{version}/manifest.json`, `{channel}/{productId}/versions/{version}/{productId}-{version}.zip` (same shape, channel-prefixed) |
| Channels (prod/dev/feature) | **Separate repositories** (same `productId` in each), badge from the manifest | **Top-level prefix** in one bucket: `{channel}/...` |
| Listing / discovery | Nexus components REST API (`/service/rest/v1/components`) | `ListObjectsV2` with a delimiter — returns only what the credentials may list. **No catalog/index files.** |
| Latest | Root `manifest.json` copied manually | Root `manifest.json` copy, refreshed automatically by the publisher |
| Integrity | none | SHA-256 in the manifest (`contentSha256`), verified on every download — no extra file |
| Auth | HTTP Basic (username + password) | Access key + secret (v1); STS-ready |
| Access scoping | **Per repository** — grant a user read on the repos they may see | **Per channel prefix** — grant read on `{channel}/*`; the client only sees what it may list. Per-product subsets are an STS concern, not key prefixes. |
| Publishing | upload the zip, then the version manifest, then the root manifest | `storkdrop-publish` (uploads manifest+zip, refreshes the root/latest manifest) |

### Nexus permissions (unchanged)

The StorkDrop client authenticates to Nexus with HTTP Basic auth. The account only needs **read/browse** on the raw repository (Nexus privilege `nx-repository-view-raw-<repo>-read` / `-browse`, or a role bundling them). Because each release channel is a **separate repository**, access is scoped per repository: give a customer read on the prod repo only and they never see the dev/feature repos. This is the model the S3 backend replaces with a single bucket + prefix-scoped IAM policies — see [S3 storage](s3-storage.md#iam-policies).

The manifest itself is identical across backends. `contentSha256` is optional and only produced/verified by the S3 publisher; Nexus repos need no changes and can omit it.
