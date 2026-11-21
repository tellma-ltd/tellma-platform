# Tellma.Connector.AzureBlobs.Adapter.Tests

The Azure blob store's logic that needs no storage service: nothing here reaches Azure or an
emulator.

| File | Covers |
|---|---|
| `AzureBlobStoreOptionsValidatorTests.cs` | Exactly one of endpoint and connection string, an absolute HTTPS endpoint that is the account's address alone (no path, query, fragment or user information, and no message printing more than its scheme and host), a container prefix the largest and the smallest tenant ids still extend to a valid container name, and positive limits. |
| `LengthGuardStreamTests.cs` | The guard a content that cannot seek is uploaded through: its declared length read whole, an early end and an overrun failing and recorded, a zero-length read that is no end, and the rewinds a retried request seeks with. |
| `AzureBlobStoreCredentialFailureTests.cs` | A host identity that cannot produce a token (refused or unavailable): a read, a write of two objects, a delete of two names, an ensure and a listing each fail as a storage failure metered as failed, and a write or delete of several still attempts each. |
| `AzureBlobsCompositionTests.cs` | One store however often it is added, the credential an endpoint is reached with (passed, else the host's), and the startup check refusing settings that name no usable account. |

## Running

```bash
dotnet test test/connector/azure-blobs/Tellma.Connector.AzureBlobs.Adapter.Tests
```
