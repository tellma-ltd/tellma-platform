# Tellma.Connector.AzureBlobs.Adapter.IntegrationTests

The Azure blob store against the Azurite emulator: the shared blob-store conformance suite (the same
cases the runtime's file-system and in-memory stores pass), plus what only this store does, namely
the per-tenant container and its name, a private container ensured once, the service's own
create-only refusal of a name another writer created, the length guard on a content that cannot
seek, and the shared store instruments.

Each case composes the store as a host does, under a container prefix of its own, and deletes its
containers when it ends, so cases run side by side on one emulator.

## Running

Marked `Category=Integration`, so the pull-request integration job runs it on Linux, where Docker is
always present.

```bash
dotnet test test/connector/azure-blobs/Tellma.Connector.AzureBlobs.Adapter.IntegrationTests
```

| Variable | What it is |
|---|---|
| `TELLMA_TEST_AZURITE` | A running emulator's connection string, used instead of starting a container. |
| `TELLMA_TEST_AZURITE_IMAGE` | The emulator image to start; defaults to a pinned `mcr.microsoft.com/azure-storage/azurite` release. |

Without either, the suite starts the emulator with Testcontainers. On Windows without Docker the
suite is skipped and says why; anywhere else a missing emulator fails the run, so CI never passes by
skipping. The emulator lags the SDK's newest service version, so it runs with its API-version check
off: the suite pins the store's behaviour, not the emulator's.
