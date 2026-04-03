# Multi-feed support

StorkDrop connects to multiple Nexus repositories simultaneously. Products from all feeds appear in a unified marketplace.

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

## Adding a new feed type

All feed interactions go through the `IRegistryClient` interface. To add a non-Nexus backend (GitHub Releases, S3, Azure Artifacts):

1. Implement `IRegistryClient` for your backend
2. Extend `FeedRegistry` to create your client type based on a field in `FeedConfiguration`
3. The marketplace, engine, updates, and all UI features work automatically
