# Tellma.Connector.AzureBlobs.Adapter.Tests

The Azure blob store's logic that needs no storage service: nothing here reaches Azure or an
emulator.

| File | Covers |
|---|---|
| `AzureBlobStoreOptionsValidatorTests.cs` | Exactly one of endpoint and connection string, an absolute HTTPS endpoint, a container prefix the largest tenant id still extends to a valid container name, and positive limits. |
| `LengthGuardStreamTests.cs` | The guard a content that cannot seek is uploaded through: its declared length read whole, an early end and an overrun failing and recorded, and the rewinds a retried request seeks with. |
| `AzureBlobsCompositionTests.cs` | One store however often it is added, the credential an endpoint is reached with (passed, else the host's), and the startup check refusing settings that name no usable account. |

## Running

```bash
dotnet test test/connector/azure-blobs/Tellma.Connector.AzureBlobs.Adapter.Tests
```
