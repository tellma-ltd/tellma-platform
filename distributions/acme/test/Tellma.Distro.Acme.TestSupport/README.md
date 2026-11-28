# Tellma.Distro.Acme.TestSupport

Test plumbing for the suites that run the Acme reference distribution as a deployment runs it. It
holds no tests and is not a test project (no `Microsoft.NET.Test.Sdk`), so `dotnet test Tellma.slnx`
never runs it.

| Type | What it does |
|---|---|
| `AcmeMigrator` | Runs Acme's migrator in process over its own `appsettings.json` and `appsettings.Development.json`, with the catalog, the credential profile and the tenant database prefix replaced by ones unique to the caller, against a SQL Server the caller names; sets a tenant's state and answers its connection string. |
| `AcmeHost` | Builds the web host call for call as `Program.cs` does, on a test server that also answers what the host reaches at its own public origin, or on Kestrel at a loopback port (`UsesKestrel`) for a test that needs a real connection; waits until its tenant snapshot is loaded; keeps its warnings and errors. |
| `AcmeHostOptions` | What a test needs beyond that: another environment, the composed blob store and processor, a further composition, services, the tenant probes, a test's own endpoints, and Kestrel. |
| `AcmeSignIn` | Signs a browser in through the BFF and the identity engine in process. |
| `TenantProbes` | Endpoints mapped onto the web group as a distribution maps its own: a read of one batch, a read whose batch waits a minute, and a mutation that changes nothing. |
| `WarningLog` | The host's warnings and errors, for a failed assertion to report. |

Its types are internal, visible to `Tellma.Distro.Acme.IntegrationTests` and to the platform's web
integration suite, `test/core/Tellma.Core.AspNetCore.IntegrationTests`.
