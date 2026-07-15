# Tellma.Identity.Web

The **standalone host** for the Tellma Identity Server: a thin ASP.NET Core app that composes the
[`Tellma.Identity`](../Tellma.Identity/README.md) engine with hosting concerns — configuration,
Serilog logging, and the OpenTelemetry pipeline (Azure Monitor export is config-gated). All identity
behavior lives in the engine; this project must stay composition-only (an architecture test asserts
it references no OpenIddict types directly).

Run locally:

```bash
dotnet run --project src/apps/Tellma.Identity.Web
```

In the Development environment the host seeds a dev admin (`admin@localhost`), generates persisted
self-signed signing/encryption certificates, and writes invitation/recovery emails to the log sink
instead of sending them. Configuration schema: see the `TellmaIdentity` section in
`appsettings.json` and the options documentation in the engine README.
