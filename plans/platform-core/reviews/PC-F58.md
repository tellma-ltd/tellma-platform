# PC-F58: CI fix, Acme test host awaits the tenant snapshot (after PC-080)

## Review 1

Reviewed commit d430d6ae on its own (`git diff d430d6ae^ d430d6ae`). Inputs were the red run 44526593455 (integration-windows job log), spec 0011 §3.4 "Startup" and "Staleness bound" and §5 `/health/ready`, PLAN.md §6–§7, the WATCHLIST entries for 0011, and the live DECISIONS.md. Nothing in WATCHLIST or DECISIONS bears on this item.

**Root cause.** Confirmed: this is a readiness race in the test host. PC-080 did not cause it.
- The red run is on fc1ad812. Its failing test logged `TenantRegistry: The tenant snapshot could not be loaded at startup … TaskCanceledException`. The next request, `POST /1/api/web/probe`, answered 503 in 2.8 ms, which is the registry's `catalog-unavailable` refusal (`TenantRegistry.Available`).
- Run 44526834171 on e80d6323 was green. Its only change from fc1ad812 is LEDGER.md.
- PC-080 adds `TellmaContractMiddleware`, which checks projected endpoints only, so the probe passes straight through. Its startup audit runs before the registry's bounded first load, so it does not count against the 10 s bound.
- Spec 0011 §3.4 says a host whose first load fails or overruns the bound starts not ready, and the refresh loop retries every 15 s by default. Before this commit, nothing in `AcmeHost.StartAsync` waited for that, so on a loaded Windows LocalDB runner a test's first tenant request could land before the snapshot existed.

**Determinism.** The fix is deterministic for every host started through `AcmeHost.StartAsync`:
- `AwaitCatalogAsync` returns at once when `IsAvailable` is true.
- Otherwise it calls `RefreshAsync(force: false)`. That joins the run still in flight: the bounded first load cancels only its wait (`task.WaitAsync`), so its forced run stays the registry's `_running`. If no run is in flight, which happens when the history read itself was cancelled, it starts one.
- A successful refresh publishes a snapshot confirmed now. Every test stays well inside `MaxStaleness` (5 min by default, 1 h for the suspension test's first host).
- Every Acme host that serves a tenant request is started through this path: all ten `AcmeHost.StartAsync` call sites in TenantStateTests and WebHostTests.
- The only `AwaitsCatalog = false` is `A_host_started_before_its_catalog_exists_turns_ready_once_the_catalog_is_migrated`, which must start not ready (Production, missing catalog database). That is correct: waiting there would fail on `VerifyCatalogAsync`'s "could not be opened".

**Hang and masking.**
- `AwaitCatalogAsync` can wait no longer than the refresh itself. Each catalog read is bounded by SqlClient's connect and command timeouts, and behind those by the xUnit token and the job's `timeout-minutes`. It cannot hang without bound.
- A refresh that fails fails the test with the refresh's own exception, which is a better diagnosis than the old 503.
- A first load that failed for a real reason, other than slowness, fails again on the refresh and is not masked. The refresh path re-runs the catalog verification too, because `_catalogVerified` stays false.
- The one thing the Acme suite no longer asserts implicitly is that a healthy host is ready at the end of `StartAsync`. Two other places still cover it: `Tellma.Core.AspNetCore.Tests` starts `TellmaTestHost` with a fake reader on a fake clock and sends tenant requests straight away, and `Tellma.Core.Tests` pins the first-load bound. So I raise no finding for it.
- The new `catch { DisposeAsync; throw; }` path is safe. Disposing a host whose start failed is already exercised by `A_host_whose_identity_store_lacks_the_engine_s_last_migration_fails_startup_naming_the_migrate_command` (`await using` after a failed `StartAsync`).

**Other hosts.**
- Identity integration (`InProcFactory`, `StandaloneFactory`, `Tellma.Identity.TestInProcHost`) composes no tenant registry, so it has no such race.
- `Tellma.Core.AspNetCore.Tests` (`TellmaTestHost`) runs the first-load bound on a `FakeTimeProvider` over `FakeCatalogReader`, so the bound never fires on its own and there is no race.
- `Tellma.Core.Webhooks.Tests` composes no registry.
- `Tellma.Core.IntegrationTests` (`CatalogHost`) calls `InitializeAsync` directly with the bound raised to 1 minute (CatalogHost.cs:130). The same race is still there in principle, past 60 s, but it is far outside anything observed: the whole Core suite took 8.3 min on the red run. No finding against this commit.

**Gates re-run by Reviewer 1.**
- `dotnet build` of the Acme integration project: **Build succeeded**, 0 warnings.
- `dotnet format … --verify-no-changes` on the project: clean.
- The four changed files have no BOM and use LF.
- Acme integration suite, PR filter, one run (Debug, `--no-build` after the build above): 23 total, 21 passed, 2 skipped, 0 failed, in 5.2 min. The two skips are the `NeedsProjectedActions` and `NeedsJobWorker` placeholders. This matches the commit's claim of 21 passed and 2 skipped. The local host was not loaded enough to overrun the first-load bound, so this run confirms the change does no harm rather than proving it closes the race. The proof is the reasoning under "Determinism" above.
- PLAN.md §7.1 item 4: the only changed project is the Acme integration test project, which nothing references. So the Acme integration suite is the only touched suite, and the commit message lists it.
- AGENTS.md and §7.1 item 5: XML docs on the new members, an inline comment on the wait, the README updated, and no spec, PC-id or `docs/` references in code.

### Findings

**R1-1 (minor): `AcmeHost.StartAsync()`, the instance method, still starts without waiting for the snapshot.**
- Where: distributions/acme/test/Tellma.Distro.Acme.IntegrationTests/Infrastructure/AcmeHost.cs:185-188.
- The static `StartAsync(values, options)` now waits for the catalog by default, and the README says starting a host waits. The `Build(...)` plus `host.StartAsync()` pair still returns as soon as the host has started.
- Today its only caller expects startup to fail (WebHostTests.cs:103-106), so nothing is red.
- Failure scenario: a later test builds a host first, for example to set `BackchannelLogoutSink` or inspect services before start, then starts it with `host.StartAsync()` and sends a tenant request. It reintroduces exactly the race this item fixed, on the same loaded runner, with nothing to point at it. The README sentence "Starting it waits until its tenant snapshot is loaded" is then wrong for that path.
- Suggested fix, either of:
  - Keep `AwaitsCatalog` from `Build`'s options on the instance and have `StartAsync()` honour it. The identity-behind test fails inside `_app.StartAsync` before any wait, so it is unaffected.
  - At minimum, say in the instance method's XML doc and in the README that it does not wait, and that a caller expecting a ready host calls `AwaitCatalogAsync` after it.

### Responses

All fixes are in the "PC-F58: review 1 fixes" commit (child of d430d6ae).

- **R1-1: fixed.** The host keeps `AcmeHostOptions.AwaitsCatalog` from `Build`, and the instance
  `StartAsync()` honours it: it starts the application, then waits for the snapshot unless the
  option was off. The static `StartAsync(values, options)` now builds and calls the instance
  method, disposing the host if either step fails, so both paths behave the same and the README's
  "Starting it waits until its tenant snapshot is loaded" holds for both. The one caller of
  `Build` plus `StartAsync()` (the identity-store-behind test) fails inside the application's own
  start, before any wait, so it is unaffected.
