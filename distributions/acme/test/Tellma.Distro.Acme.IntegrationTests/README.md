# Tellma.Distro.Acme.IntegrationTests

SQL Server tests (`Category=Integration`) of the Acme distribution as a developer's checkout runs
it: the migrator and the web host in process, over Acme's own `appsettings.json` and
`appsettings.Development.json`, with only the catalog, the credential profile and the tenant
database prefix replaced by ones unique to each test (`AcmeMigrator`). The server is the shared
fixture's: a Testcontainers SQL Server by default, or the one `TELLMA_TEST_SQL` names (LocalDB on a
Windows runner).

| File | Covers |
|---|---|
| `MigratorTests` | `migrate` from an empty server creating the catalog, the identity engine's schema at its last migration and the two seed tenants, active; every database in the platform collation; a second `migrate` applying no identity migration; `provision` drawing 1000 from the sequence and refusing a requested id in the sequence's band, taken or free, as `tenant-registration-refused`; `provision` into an existing database in another collation failing without migrating it; the provisioning record holding a step's version and a bumped step running again on every tenant. |
| `WebHostTests` | A sign-in through the BFF with the engine in process (`/bff/login`, the engine's email-code sign-in, the callback, `/bff/user`); a global sign-out at the engine ending the host's session over the back channel; a refresh the engine rejects ending the session; a host whose identity store lacks the engine's last migration failing startup with the `migrate` command; a host started before its catalog exists turning ready once the catalog is migrated (standalone identity mode, the identity server's metadata standing in as already read). |

`AcmeHost` builds the web host call for call as `Program.cs` does, on a test server that also
answers what the host reaches at its own public origin: the OpenID Connect handler's back channel
and the engine's back-channel logout client. Its warnings and errors are kept for a failed
assertion to report. The browser is the identity suite's `OidcFlowClient`
(`test/apps/Tellma.Identity.TestSupport`).

Assertions that need the users stack (a tenant request binding the signed-in user, a suspension
refusing a member on the next request and on a second host) join when tenants carry users.

```bash
dotnet test distributions/acme/test/Tellma.Distro.Acme.IntegrationTests --filter "Category=Integration&Category!=Nightly&Live!=true"
```
