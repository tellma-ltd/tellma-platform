# PC-F36: a missing catalog reached as SQL error 233 alone

## Review 1

Commit reviewed: `5fd775b`, parent `8b39d28`.

Verdict: the diagnosis is plausible and the fix is correct for the case it targets. It fails safe
whenever the question to `master` cannot be asked. No blocking or major findings. Two minor
findings.

What I checked and found sound:

- **The diagnosis fits the evidence.** Error 233 is the client-side "no process is on the other
  end of the pipe". It shows up when the server closes the session during login, before the client
  has read the 4060/18456 tokens. LocalDB is reached only over its named pipe, while the ubuntu job
  uses TCP to a container, where the tokens arrive intact. That matches a Windows-only, one-off red
  run. The commit's numbers support it: 71 of 1500 before, 1500 of 1500 after, and master saying
  every 233 database was absent.
- **The spec rule holds.** Spec 0011 §3.4 "Startup" says a missing catalog database fails startup
  in Development, and a reachability failure starts the host not ready while the loop retries.
  `TenantRegistry.InitializeAsync` (`TenantRegistry.cs:207-225`) maps `MissingDatabase` to a
  `TellmaCompositionException` in Development. Outside Development it maps it to
  `CatalogDatabaseUnavailable` and starts not ready. The refresh loop's `VerifyCatalogAsync`
  (`:422-442`) throws on `MissingDatabase` and keeps retrying. So outside Development a
  misclassification changes only the log line, never readiness or recovery. The fix cannot cause a
  production not-ready that never recovers.
- **A 233 with an existing database stays a reachability failure** whenever the credential can see
  that database in `master`. `DB_ID` is non-null, so the reader rethrows the original exception
  (`throw;` in an async catch keeps the stack trace). The exceptions are the visibility cases in
  R1-1.
- **When master is unreachable or refuses the credential, the original failure stands.**
  `SqlCatalogReader.DatabaseExistsAsync` (`SqlCatalogReader.cs:113-124`) turns any `SqlException`
  into "exists", which leads to the rethrow. That covers master being unreachable, a contained user
  that cannot log in to `master` (SaaS on Azure SQL Database: managed identity as a contained user,
  spec 0011 §3.5), and a login refused by master. A cancellation by the 10-second first-load bound
  surfaces as `OperationCanceledException`, which `InitializeAsync` already logs as a failed first
  load. So a transient outage cannot become a Development crash through this path.
- **Azure SQL.** Production SaaS reaches the server over TCP, where a refused login does not
  arrive as 233, so this branch should rarely run there. When it does, the contained user's
  `master` login fails, and the outcome is unchanged from before.
- **Security.** The query is parameterized (`@database`, `nvarchar(128)`, the sysname length), so
  nothing is injected. `master` is set through `SqlConnectionStringBuilder.InitialCatalog` over the
  already-composed catalog string, so the credential, encryption and `Application Name` carry over
  and no keyword can be added. The credential gains no privilege it did not already have. Nothing
  is logged: the master exception is swallowed, and the rethrown original carries no connection
  string. The master string has a pool of its own, and it is only ever opened after a 233.
- **The new test is deterministic.** It asks for one created database and one never-created name,
  through the fixture, on the same server and credential on both OSes. It fails if
  `DatabaseExistsAsync` inverts its answer, asks the wrong database, or stops working.
- **AGENTS.md.** All new members have XML docs. There are no PC ids, `plans/` paths or spec
  references in docs, test names or messages. The README row was updated.
- **The gates pass on `5fd775b`.**
  - `dotnet build Tellma.slnx`: Build succeeded, 0 warnings (`.tmp/pcf36-r1-build.log`).
  - `Tellma.Core.Tests`, PR-tier unit filter: 3735 passed, 1 skipped (`.tmp/pcf36-r1-unit.log`).
  - `Tellma.Core.IntegrationTests` with `Category=Integration&Category!=Nightly&Live!=true`: 261
    of 261 on LocalDB (`.tmp/pcf36-r1-integration.log`).
  - Solution-wide PR-tier unit filter: 19 assemblies, 8886 passed, 0 failed
    (`.tmp/pcf36-r1-unit-all.log`).

### Findings

- **R1-1 (minor): `DB_ID` returns NULL for a database the credential exists in but may not see,
  and the reader then calls an existing catalog missing.**
  - Location: `src/core/Tellma.Core/Tenancy/CatalogStatements.cs:53`,
    `src/core/Tellma.Core/Tenancy/CatalogConnection.cs:76-87`, consumed at
    `src/core/Tellma.Core/Tenancy/SqlCatalogReader.cs:33`.
  - Scenario 1, metadata visibility: `DB_ID` returns NULL unless the caller owns the database or
    holds `VIEW ANY DATABASE` (or `ALTER ANY DATABASE`). That permission is granted to `public` by
    default, but revoking it is a common hardening step. On such a server, a catalog user that does
    not own the catalog gets a transient 233 (connection limit, a busy pipe, a login trigger). The
    reader then returns `MissingDatabase`.
  - Scenario 2, no `Initial Catalog`: the catalog string omits `Initial Catalog` and relies on the
    login's default database. Nothing requires the keyword: `TellmaCatalogOptions.Parses` only
    checks that the string parses. `Database` is then `""`, `DB_ID(N'')` is NULL, and any 233
    becomes `MissingDatabase`.
  - Impact: in Development, a transient fault crashes startup with "The catalog database does not
    exist", naming the migrate command. That is the opposite of the §3.4 reachability rule.
    Outside Development, the host still starts not ready and recovers, but the Error log sends the
    operator to run `migrate` against a catalog that exists.
  - The two helpers disagree. `CatalogConnection.DatabaseExistsAsync` documents "false also when
    the login may not see it". The reader's wrapper documents "false only when the server says the
    database is missing", and treats every false as missing.
  - Suggested fix: make the question three-valued, and let only a definite "absent" settle it:
    - `SELECT CASE WHEN DB_ID(@database) IS NOT NULL THEN 1 WHEN HAS_PERMS_BY_NAME(NULL, NULL, N'VIEW ANY DATABASE') = 1 THEN 0 END;`
    - NULL means "cannot tell", and the original failure stands.
    - Do not ask at all when `Database` is empty.
    - Extend the new integration test with the empty-database case.
- **R1-2 (minor): the classification rule itself has no deterministic test.**
  - Location: `src/core/Tellma.Core/Tenancy/SqlCatalogReader.cs:29-39` and `:113-124`;
    `test/core/Tellma.Core.IntegrationTests/Catalog/CatalogMigrationTests.cs:130-138`.
  - Scenario: the new test pins only `CatalogConnection.DatabaseExistsAsync`. Any of these
    regressions passes every suite on Linux:
    - deleting the 233 catch;
    - dropping the `!` at `:33`;
    - turning the wrapper's `return true` into `return false`, which would make an unreachable
      server over named pipes a Development startup crash.
  - On Windows, such a regression surfaces only as the original ~5% flake of
    `RegistryCatalogTests`, which is the failure this item exists to remove.
  - Suggested fix: extract the decision as a pure function over the exception's error numbers and
    the existence question. For example, an internal static
    `ClassifyLoginFailureAsync(IEnumerable<int> numbers, Func<CancellationToken, Task<bool?>> exists, CancellationToken)`
    returning `CatalogHistoryState?`, where null means rethrow. Unit-test it in `Tellma.Core.Tests`
    for:
    - 4060 → missing;
    - 233 with absent → missing;
    - 233 with present → rethrow;
    - 233 with the question throwing → rethrow;
    - any other number → rethrow.
    No `SqlException` needs to be constructed.

### Responses
- R1-1: fixed in the "PC-F36: review 1 fixes" commit. The existence check is three-valued.
  `CatalogStatements.DatabaseExists` returns 1 when `DB_ID` sees the database. It returns 0 when
  the credential holds `VIEW ANY DATABASE` (`HAS_PERMS_BY_NAME`, which counts implied permissions)
  and the database is not there. Otherwise it returns NULL. `CatalogConnection.DatabaseExistsAsync`
  returns `bool?` and answers `null` without a query when the string names no database. Only
  `false` becomes `MissingDatabase`; `null` keeps the original failure. The XML docs of the
  statement, the connection method and the classifier now say the same thing. The integration test
  adds the unnamed-database case (`null`). The hardened-permission case has no integration test,
  because LocalDB with integrated security cannot create a login that lacks `VIEW ANY DATABASE`.
  The classifier's unit tests pin the `null` outcome instead.
- R1-2: fixed in the "PC-F36: review 1 fixes" commit. The decision is now the pure
  `SqlCatalogReader.ClassifyLoginFailureAsync(IEnumerable<int>, Func<CancellationToken, Task<bool?>>, CancellationToken)`,
  which returns `CatalogHistoryState?` (null means rethrow). `ReadHistoryAsync` passes every
  `SqlException` through it. The new `LoginFailureClassificationTests` in Tellma.Core.Tests cover
  these cases:
  - 4060 in either order beside 18456: missing, and the server is never asked;
  - 233 when the database is absent: missing;
  - 233 when the database is present: rethrow;
  - 233 when the server cannot tell: rethrow;
  - 233 when the question throws: rethrow;
  - a cancelled question: cancellation propagates;
  - 18456, -2, 53 and 10054: rethrow, and the server is never asked.
  The question now catches any failure except cancellation, so these tests construct no `SqlException`.
