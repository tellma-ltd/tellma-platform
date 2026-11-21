# Tellma.Distro.Acme.IntegrationTests

SQL Server tests (`Category=Integration`) of the Acme distribution as a developer's checkout runs
it: the migrator and the web host in process, over Acme's own `appsettings.json` and
`appsettings.Development.json`, with only the catalog, the credential profile and the tenant
database prefix replaced by ones unique to each test (`AcmeMigrator`). The server is the shared
fixture's: a Testcontainers SQL Server by default, or the one `TELLMA_TEST_SQL` names (LocalDB on a
Windows runner).

| File | Covers |
|---|---|
| `MigratorTests` | `migrate` from an empty server creating the catalog, the identity engine's schema at its last migration and the two seed tenants, active, each with its settings completed from the seed (English then Arabic, Gregorian then Umm al-Qura); every database in the platform collation; a second `migrate` applying no identity migration; `provision` drawing 1000 from the sequence and refusing a requested id in the sequence's band, taken or free, as `tenant-registration-refused`; `provision` into an existing database in another collation failing without migrating it; the provisioning record holding a step's version and a bumped step running again on every tenant, a `ReadOnly` one included, which stays `ReadOnly`. |
| `WebHostTests` | A sign-in through the BFF with the engine in process (`/bff/login`, the engine's email-code sign-in, the callback, `/bff/user`); a global sign-out at the engine ending the host's session over the back channel; a refresh the engine rejects ending the session; a host whose identity store lacks the engine's last migration failing startup with the `migrate` command; a host started before its catalog exists turning ready once the catalog is migrated (standalone identity mode, the identity server's metadata standing in as already read). |
| `LimitsTests` | The limits lowered through configuration: a signed-in user past `Tellma:Api:RequestsPerMinutePerUser` refused as `429 rate-limited` with `Retry-After`; a read whose batch waits a minute on the server answering `504 timeout` at `Tellma:Api:RequestTimeout`, its batch cancelled long before its minute; the Development host serving `/openapi/web.json` (OpenAPI 3.1, the web surface's paths alone) over Acme's composition, a second request answered from the cached copy. |
| `TenantStateTests` | The state verdicts on tenant data for a signed-in session: a `Frozen` tenant whose database is set `READ_ONLY` refusing a mutation as `403 tenant-frozen` before the endpoint runs; a `ReadOnly` tenant refusing one as `403 tenant-read-only` and storing a locale change through `users/me/locale`, an action it allows; a suspension refusing the next request on the host that received it, by the write's own refresh (that host's interval is the option's one-hour maximum), and the same session on a second host (one key ring, configured client secrets, a one-second catalog refresh, asserted as resolved and as the cadence its snapshot is confirmed on) within five seconds of the write, a third of the default interval. |
| `UserIdentityTests` | The users' identity actions against the in-proc engine, run as the system principal in a tenant scope: an invite answering `Invited` for an unknown address, `Active` for an active account and an error for a disabled one, a second invite answering `Reinvited`, and the invitation status reading the email the engine sent through the capturing sender; a sandbox invite linking an existing account and creating and emailing nobody; and a service account whose issued credentials obtain a `tellma_api` client-credentials token that the public API group's test-mapped bearer probe resolves to the service account, until a rotation retires them. |
| `SelfServiceTests` | The client shell's boot calls as the signed-in administrator makes them: `users/me` answering the profile, the access summary and the tags the `Tellma-Version-Tags` header agrees with; `users/me/locale` storing a zone and digits and moving the caller's tag, the header carrying the moved one; `users/me/preferences/set` bound from `{ items }` answering the bag and the moved tag; a bad key answering `422` at `items[0].key`; and the envelope read again holding what the writes stored. |

`AcmeHost` builds the web host call for call as `Program.cs` does, on a test server that also
answers what the host reaches at its own public origin: the OpenID Connect handler's and the bearer scheme's back channels,
the engine's back-channel logout client and the platform's identity-server client. Starting it waits until its tenant snapshot is loaded,
as a load balancer sends an instance nothing before it is ready, so a first catalog load that
outlasts the host's startup bound on a loaded machine never races a test's first request; a test
that starts a host not ready turns that off (`AwaitsCatalog`). Its warnings and errors are kept
for a failed assertion to report. `AcmeHostOptions` adds what a test needs beyond that: another
environment, a further composition beside Acme's (one that changes no model), services, and the
tenant probes (`TenantProbes`), a read that runs one batch against the tenant database, a read whose
batch waits a minute on the server, and a mutation that changes nothing and records that it ran,
mapped onto the web group as a distribution maps its own endpoints, and a test's own endpoints
(`Map`), such as a bearer probe on the public API group. Two hosts of one test can share a browser's cookie jar (`CreateFlowClient(cookies)`,
`CreateBrowser(cookies)`), as a load balancer sends one browser to every instance. The browser is
the identity suite's `OidcFlowClient` (`test/apps/Tellma.Identity.TestSupport`), signed in by
`AcmeSignIn`.

Some tests are gated, each naming the capability it waits for: skipped while Acme's composition
lacks it, they run as soon as it holds it: `PlatformSeams` builds the web host once per run over placeholder settings,
never starting it, and reads off it whether an initializer binds tenant requests to their users,
which provisioning steps are composed, and whether the jobs worker is hosted. A capability it cannot read off the composition counts as
present, so its tests run and say for themselves. The gated tests are, in `TenantStateTests`, the first tenant request binding the signed-in user, a `Frozen`
tenant serving a read against its `READ_ONLY` database, a read on a `ReadOnly` tenant stamping the
user's activity, and a job whose handler runs when read-only (`ProbeJobHandler`) succeeding on a `ReadOnly` tenant;
in `MigratorTests`, the provisioning record holding the platform's own steps.

```bash
dotnet test distributions/acme/test/Tellma.Distro.Acme.IntegrationTests --filter "Category=Integration&Category!=Nightly&Live!=true"
```
