# PC-F57 reviews

## Review 1

Commit reviewed: 562077d8 (branch pc/PC-F57, cut from 7a6c5502).

Verified, no finding:
- Every hint stamp comes from `SYSUTCDATETIME()` assigned to the `datetimeoffset(3)` column: the writers' upsert, the reconciliation's own update and insert, and the test's restamp. The migrator and other writers take the same path. On LocalDB (SQL Server 2019, 15.0.4382), the column assignment and `CAST(TODATETIMEOFFSET(...) AS datetimeoffset(3))` both round half up (.0004999 to .000, .0005 to .001). Because the two roundings are the same monotone function, the argument holds on a clock that does not step back. A stamp at s < t0 rounds to at most round(t0), which is below `@readAt`. A stamp at s' after the loop exits rounds to at least `@readAt`.
- Isolation: the read of the tenant's users runs on the primary through `OpenAsync(tenantId)`. A post-commit hint whose stamp is below `@readAt` started after its tenant commit, so the later user read sees that commit. Overwriting that hint is therefore consistent. An upsert that blocks on the reconciliation's row locks stamps after `@readAt`.
- The loop ends on its own: each iteration re-reads the clock. A backward step during the wait delays the call by at most the size of the step, capped by the command timeout. The next call computes its own marker from the stepped-back clock.
- The new test tells the fixed statement apart from both broken ones. I simulated 20 iterations on LocalDB. The old statement put the before-stamp at or above the instant 20 times out of 20. The marker without its wait put the after-stamp below the instant 19 times out of 20. The fixed statement is deterministic, so the test is not flaky. On Linux the clock is finer, but a stamp and a read a few microseconds apart still share a millisecond almost every time.
- The only callers of `CatalogTime` are `MembershipReconcileService` (through the store), the integration tests, and `FakeReconcileStore`, which has its own clock. No other statement compares a stamp with an instant read in an earlier round trip. `ConnectPrologue`'s `@tm_Now` and the job and notification predicates compare within one statement.
- Build of Tellma.Core.IntegrationTests: Build succeeded, 0 warnings. `dotnet test ... --filter "Category=Integration&Category!=Nightly&Live!=true&FullyQualifiedName~Tellma.Core.IntegrationTests.Catalog"`: 44 passed, 0 failed (Windows LocalDB).
- No PC ids, `plans/` paths, spec references or `docs/` references appear in XML docs, test names or assertion messages.

### Findings

**R1-1 (major) PLAN §7.1 item 4: the commit message does not list the suites that were run.**
- The `Tests:` block of 562077d8 names the new test and three tests run alone 20 times. It does not list the build, the format check, the PR-tier unit filter, or the touched integration suites by name. PROCESS.md's commit template asks for `Tests: <suites run: unit filter; integration suites by name; ...>`, and reviewers check that list.
- The commit changes `src/core/Tellma.Core`, so every suite whose ProjectReference closure includes Tellma.Core counts as touched. That includes all of Tellma.Core.IntegrationTests, not only the Catalog tests, and the other integration suites that reference Tellma.Core, such as Tellma.Core.AspNetCore's and the distribution suites.
- Scenario: CatalogTime now holds a session for up to one clock tick. A suite that times CatalogTime, or runs the reconcile service against a real catalog, could regress without anyone noticing, and the reviewers cannot confirm the gate.
- Fix: run `dotnet build Tellma.slnx`, `dotnet format --verify-no-changes`, the PR-tier unit filter, and each touched integration suite with the §7.1 filter. Then amend the commit's `Tests:` block to list them.

**R1-2 (minor) The cost and the guarantee in the docs are overstated.**
- Locations: `src/core/Tellma.Core/Tenancy/TenantMembershipDirectory.cs:175-179` ("so it may take a millisecond or two") and `src/core/Tellma.Core/Tenancy/CatalogStatements.cs:180-183`. The commit message says the same ("a WAITFOR of at most a millisecond or two").
- On Windows SQL Server, `SYSUTCDATETIME()` advances in steps of the system timer, about 15.6 ms. On LocalDB 15.0.4382 I measured 14 distinct values over 200,000 reads, with a maximum gap of 16.1 ms. The loop therefore waits for the next tick: 8.9 ms on average per call over 50 calls, and up to about 16 ms.
- Both docs also state the split unconditionally ("every hint stamped after it returns is at or above it"). That holds only while the catalog's clock does not step back, for example through an NTP step or an Azure SQL failover to a node whose clock is behind. When it does step back, a fresh hint can fall below `@readAt` and be overwritten by the stale read. The hint is navigation only and the next reconciliation repairs it, so the impact is small. That is the cost of the deviation from a rowversion.
- Fix: say the call waits until the clock passes the current millisecond, which is up to one clock tick (about 16 ms on Windows). Say the split assumes a catalog clock that does not step back.

### Responses
