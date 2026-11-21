# Tellma.Defaults.Azure.AspNetCore.IntegrationTests

The live suite of the Azure web defaults' realtime line: the platform's hub, composed by
`UseAzureWebDefaults()` into the shared web test host (`test/shared/Tellma.Testing.WebHost`),
running on a real Azure SignalR Service instance. The service has no emulator for a server SDK's
traffic, so nothing short of an instance checks that the hub-only token's claims reach the hub's
connect, that events reach their groups and no other, that a close tells `session.ended` before the
service closes the connection, and that two deployments on one instance never hear each other.

Every test is `Category=Integration` and `Live=true`, and skips unless
`TELLMA_SIGNALR_TEST_CONNECTIONSTRING` holds the test instance's connection string. Each test runs
under an environment name of its own, so its application name on the shared instance is its own
too. The suite runs nightly in `.github/workflows/nightly.yml` (`signalr-live`), once the
`live-realtime-tests` environment, its `SIGNALR_TEST_CONNECTIONSTRING` secret and the repository
variable `SIGNALR_LIVE_ENABLED` exist.

`AzureSignalRTestHost` is linked into `Tellma.Defaults.Azure.Tests`, whose offline test composes
the same line with no service to reach.

```bash
TELLMA_SIGNALR_TEST_CONNECTIONSTRING="Endpoint=https://...;AccessKey=...;Version=1.0;" \
  dotnet test test/defaults/Tellma.Defaults.Azure.AspNetCore.IntegrationTests
```
