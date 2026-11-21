# Tellma.Connector.AzureBlobs.Adapter

The Azure Blob Storage blob store: an `IBlobStore` on the first-party Azure SDK, one storage account
per distribution and one private container per tenant. It is the hosted store of Azure-deployed
distributions; `Tellma.Defaults.Azure` activates it from configuration.

```csharp
services.AddAzureBlobStore(options => configuration.GetSection(AzureBlobStoreOptions.SectionName).Bind(options), credential: null);
```

## Configuration

```jsonc
"Tellma": {
  "Blobs": {
    "Azure": {
      // The account's blob endpoint, reached with the host's managed identity. Exactly one of
      // ServiceUri and ConnectionString is set.
      "ServiceUri": "https://tellmaacme.blob.core.windows.net",
      // For Azurite and local development only, instead of ServiceUri.
      // "ConnectionString": "UseDevelopmentStorage=true",
      "ContainerPrefix": "tellma-",   // containers are {ContainerPrefix}t{tenantId}
      "MaxParallelism": 8,            // requests one write or delete call runs at once
      "ReadBufferSize": 1048576       // bytes a download fetches per ranged request
    }
  }
}
```

**There is deliberately no account key.** An endpoint is reached with a `TokenCredential`: the one
passed to `AddAzureBlobStore`, else the host's registered one (the App Service's managed identity in
Azure, the developer's own sign-in locally). The identity needs *Storage Blob Data Contributor* on the
account, which covers creating the tenants' containers. The adapter never constructs a credential of
its own. A credential that cannot produce a token is a storage failure like any other: the operation
throws `BlobStoreException`, is metered as failed, and a write or delete of several objects still
attempts each.

The settings are validated when the store is built, and the `blobs.azure` startup check reports every
broken rule, a connection string that does not parse, and an endpoint without a credential before the
host serves traffic. The prefix is checked with the largest and the smallest tenant ids appended, so no tenant can be
handed an invalid container name later (an empty prefix is refused: `t1` is under the
three-character minimum). The endpoint is the account's HTTPS address alone: one with
a path, a query (a pasted shared access signature, say), a fragment or user information is refused,
and no message prints more of it than its scheme and host.

## Behaviour

- **Create-only uploads.** Every upload carries `If-None-Match: *`; of two writers racing to one name
  exactly one wins, and the other (a 409 or 412 from the service) sees `BlobAlreadyExistsException`.
- **Atomic uploads.** A seekable content up to 256 MiB goes in one request, which the service applies
  whole or not at all. A larger content, or one that cannot seek, may instead be staged in blocks of
  8 MiB that are committed at once, so a reader or a listing never sees a partial object. A content that cannot seek
  is read through a length guard, which fails the upload on an early end or an overrun.
- **Containers are ensured lazily.** A tenant's first write creates its container (private, no
  anonymous access) and the process remembers it. A write that finds the container gone forgets it,
  creates it again and retries once.
- **Ranged reads.** A download stream knows the blob's length, can seek, and fetches only the ranges
  read, pinned to the version it opened, so a range request reads only what it serves.
- **Nothing to purge.** Uncommitted blocks are invisible to reads and listings and expire on the
  service, so `PurgeIncompleteAsync` is a no-op.

The store measures every call on the same `Tellma.Blobs` instruments as the runtime's own stores, so
dashboards do not care which store a distribution runs.
