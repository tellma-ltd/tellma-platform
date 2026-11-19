# PC-F59: CI flake: TenantScopeTests missing-database open answers 233 instead of 4060

## Implementer notes

### Failure

CI run 44571660410 (integration-windows, LocalDB) on c54b24e6, a plans-only commit:
`TenantScopeTests.A_tenant_whose_database_is_missing_fails_its_open_at_once_rather_than_after_a_connect_retry_wait`
failed at `Assert.Equal(4060, refused.Number)` with an actual value of 233. Ubuntu integration was
green. The same assertion failed once under local parallel load during PC-078 (noted in
reviews/PC-078.md as a "load flake").

### Root cause

This is the transport effect PC-F36 found for the catalog login, now seen on the tenant open.
LocalDB speaks only named pipes and shared memory. Over those transports, a login the server
refuses for a missing database sometimes ends the session before the client reads the server's
4060/18456 tokens. The client then raises its own 233 alone ("no process is on the other end of
the pipe"). Over TCP (the ubuntu container) the refusal always arrives whole.

Measured on local LocalDB with Microsoft.Data.SqlClient 6.1.6, using raw opens of absent databases
with `Connect Retry Count=0`, 16 in parallel, in a temporary test that was not committed:

| Run | Opens | `[4060, 18456]` | `[233]` alone |
|-----|------:|----------------:|--------------:|
| 1   | 1500  | 1492            | 8             |
| 2   | 1500  | 1487            | 13            |
| 3   | 1500  | 1489            | 11            |
| 4   | 1500  | 1493            | 7             |
| 5   | 1500  | 1488            | 12            |

No other error form appeared. `master` reported every one of those databases as absent.

The test pinned one of the two forms, so about 0.5-1% of Windows runs went red. There is no defect
in the code under test.

### Is the production classification right? Yes, so no production change

- **The data executor.** An open that fails never sends its command.
  `DataExecutor.Classify(sent: false, …)` returns `Reported` for any error `DataErrors.Classify`
  does not call final. 4060 is in `ReportedTransientNumbers` and 233 is in `AmbiguousNumbers`, so
  both forms are re-run on the same budget and surface the same way: `DataAccessRetryExhausted`,
  then 503. Spec 0012 §6.3-§6.5 agree.
- **Other tenant-side code.** Nothing else reads 4060 from a tenant open (`git grep 4060`). The
  catalog's own login already classifies a lone 233 by asking `master` (PC-F36). Nothing on the
  tenant side needs that question, because both forms lead to the same outcome.
- **SqlClient's own connect retry**, the property this test pins. Reflection on 6.1.6 shows the set
  it retries an open on: `SqlInternalConnectionTds.s_transientErrors` = {4060, 10928, 10929, 40197,
  40501, 40613, 42108, 42109}. That set excludes 233.
  - Measured with `Connect Retry Count=1;Connect Retry Interval=4`: every one of 1198 `[4060, 18456]`
    opens took ≥ 4028 ms. The two `[233]` opens took 172 and 365 ms.
  - So a 233 is never subject to the 10 s wait the test guards against. On a run that sees 233 the
    timing assertion holds without exercising the pin. The pin itself is guarded deterministically by
    the unit theory
    `ConnectionCompositionTests.The_client_library_s_own_connect_retry_is_off_whatever_the_profile_says`,
    and on every TCP run, including all ubuntu CI runs.

### What changed

Only the test and its suite README changed:
`test/core/Tellma.Core.IntegrationTests/Tenancy/TenantScopeTests.cs` and
`test/core/Tellma.Core.IntegrationTests/README.md`.

1. **The refusal accepts both of its forms.** It passes when the error list contains
   `CatalogStatements.CannotOpenDatabase` (4060), or is exactly `[CatalogStatements.LoginInterrupted]`
   (233 alone). It reuses the constants PC-F36 introduced. Any other number, or a 233 alongside other
   errors, still fails, and the failure message lists every error number.
2. **The database is confirmed absent through `master`** (`DB_ID`), as the catalog does. A cut-off
   login proves nothing by itself. This also shows the server is still up, so the 233 was not the
   server going away.
3. **The time bound is SqlClient's default `ConnectRetryInterval` (10 s), no longer a fixed 5 s.**
   - A retried open sleeps that full interval, so it can never finish under the bound. A login that
     is merely slow under load can.
   - The old 5 s bound was itself load-sensitive. In the same tally, `[4060, 18456]` opens with the
     retry off reached 4986 ms at 16-way parallelism.
   - Mutation check: with the `ConnectRetryCount = 0` pin removed from
     `TenantConnectionStrings.Compose`, the fixed test fails with "The open failed after
     00:00:10.0648048, at least SqlClient's connect retry interval of 00:00:10". The pin was restored
     afterwards.
4. **The suite README row for `Tenancy/TenantScopeTests`** now lists this test and the pooled
   reconnect test. Neither was listed before.

There are no retries and no quarantine (D-011). The test is deterministic over both transports.

### Deviations

None. The specs do not name the transport-level form of a refused login. Spec 0012's table already
lists 233 as a lost connection, which before the send is re-run like 4060.

### Open risks

- **A single login can still stall past 10 s on a badly loaded host.**
  - In one burst of the first 16-way tally run (just after a 6-minute tally had finished), some
    opens took about 13.8 s with the retry off. None did in the next four runs (6000 opens).
  - That is synthetic 16-way contention in one process. The test makes a single open.
  - The new bound has twice the margin of the old one, and it cannot be raised further without
    letting a retried open pass. An alternative that counts login attempts (server error log or
    SqlClient EventSource) would remove the timing entirely, but it is far more fragile. I did not
    take it.
- **A Windows run that sees 233 does not exercise the connect-retry pin end to end.** The unit
  theory and every TCP run still do.

### What reviewers should examine hardest

- Whether accepting a lone 233 can mask a real regression. The only other way to get 233 here is
  the server ending the session for another reason, which the `master` check would catch as a
  failed open rather than a pass.
- Whether the interval-based bound is the right discriminator, given that the interval is read from
  `new SqlConnectionStringBuilder()`, the default the composed string inherits.
- That the executor's pre-send handling (`Classify(sent: false, …)`) really treats both forms alike
  (`DataExecutor.cs:649-668`, `DataErrors.cs:95-99`).

### Tests

- build: `dotnet build Tellma.slnx`, 0 warnings.
- format: `dotnet format Tellma.slnx --verify-no-changes`, clean.
- unit filter: 20 assemblies, 11422 passed.
- integration, Tellma.Core.IntegrationTests (the only touched suite):
  - Testcontainers: 540 of 540 passed.
  - LocalDB: 523 passed and 14 failed, with 3 skipped, on a host loaded by other slots. All 14
    failures were login "Connection Timeout Expired" errors: MigratorTests, TenantCatalogTests,
    VersionTagSeedTests and the AccessEvaluationTests `ConnectDatabase` fixture. This is the known
    local load flake. Those four classes rerun alone: 63 of 63 passed.
- the TenantScopeTests class repeatedly:
  - LocalDB: 10 runs, 70 of 70 passed.
  - Testcontainers: 3 runs, 21 of 21 passed.
- the mutation check above.
- nightly-tier: none.
