# Tellma.Distro.Acme.IntegrationTests

SQL Server tests (`Category=Integration`) of the Acme distribution as a developer's checkout runs
it: the migrator and the web host in process, over Acme's own `appsettings.json` and
`appsettings.Development.json`, with only the catalog, the credential profile and the tenant
database prefix replaced by ones unique to each test (`AcmeMigrator`). The server is the shared
fixture's: a Testcontainers SQL Server by default, or the one `TELLMA_TEST_SQL` names (LocalDB on a
Windows runner).

| File | Covers |
|---|---|
| `MigratorTests` | `migrate` from an empty server creating the catalog, the identity engine's schema at its last migration and the two seed tenants, active; every database in the platform collation; a second `migrate` applying no identity migration; `provision` drawing 1000 from the sequence and refusing a requested id in the sequence's band, taken or free, as `tenant-registration-refused`; `provision` into an existing database in another collation failing without migrating it; the provisioning record holding a step's version and a bumped step running again on every tenant, a `ReadOnly` one included, which stays `ReadOnly`. |
| `WebHostTests` | A sign-in through the BFF with the engine in process (`/bff/login`, the engine's email-code sign-in, the callback, `/bff/user`); a global sign-out at the engine ending the host's session over the back channel; a refresh the engine rejects ending the session; a host whose identity store lacks the engine's last migration failing startup with the `migrate` command; a host started before its catalog exists turning ready once the catalog is migrated (standalone identity mode, the identity server's metadata standing in as already read). |
| `TenantStateTests` | The state verdicts on tenant data for a signed-in session: a `Frozen` tenant whose database is set `READ_ONLY` refusing a mutation as `403 tenant-frozen` before the endpoint runs; a `ReadOnly` tenant refusing one as `403 tenant-read-only`; a suspension refusing the next request on the host that received it and the same session on a second host (one key ring, configured client secrets, a one-second catalog refresh) within one refresh. |

`AcmeHost` builds the web host call for call as `Program.cs` does, on a test server that also
answers what the host reaches at its own public origin: the OpenID Connect handler's back channel
and the engine's back-channel logout client. Its warnings and errors are kept for a failed
assertion to report. `AcmeHostOptions` adds what a test needs beyond that: another environment, a
further composition beside Acme's (one that changes no model), services, and the tenant probes
(`TenantProbes`), a read that runs one batch against the tenant database and a mutation that
changes nothing and records that it ran, mapped onto the web group as a distribution maps its own
endpoints. Two hosts of one test can share a browser's cookie jar (`CreateFlowClient(cookies)`,
`CreateBrowser(cookies)`), as a load balancer sends one browser to every instance. The browser is
the identity suite's `OidcFlowClient` (`test/apps/Tellma.Identity.TestSupport`), signed in by
`AcmeSignIn`.

Some tests are skipped, each naming the capability it waits for, and join as those capabilities are
composed: in `TenantStateTests`, the first tenant request binding the signed-in user, a `Frozen`
tenant serving a read against its `READ_ONLY` database, a read on a `ReadOnly` tenant stamping the
user's activity, an action a `ReadOnly` tenant allows (setting one's own locale) succeeding there,
and a job whose handler runs when read-only (`ProbeJobHandler`) succeeding on a `ReadOnly` tenant;
in `MigratorTests`, the provisioning record holding the platform's own steps.

```bash
dotnet test distributions/acme/test/Tellma.Distro.Acme.IntegrationTests --filter "Category=Integration&Category!=Nightly&Live!=true"
```
