# PC-F68: UserSelfServiceTests LastAdministrator failure on preferences/set

## Implementer notes

### Root cause

The failure is an order dependence between the tests of one class. It is not date-dependent and
not a platform bug.

- `UserSelfServiceTests` shares one `ConnectDatabase` (two tenant databases) across its 18 tests
  (`IClassFixture`). The fixture seeds no administrator: each test seeds its own users.
- `preferences/set` and `preferences/delete` are entity actions of the users' stack. An action's
  persist joins the security guards whenever its declared writes, which include the stack's own
  tables, meet a security table (spec 0015 §6.7 step 4; `ActionRun.Persist` passes `StackTables`,
  which hold `core.Users`, to `PersistGuards.Contribute`). So the persist asserts L1, and L1 needs
  an active, invited-or-joined individual with a started, permanent membership of role 1
  (spec 0014 §8.3).
- `Another_user_s_bag_is_read_set_and_deleted_under_the_preferences_filter_within_its_budgets`
  seeds three plain callers and a role, and no administrator. Every other test of the class that
  reaches a guarded write seeds its own ("A write to the users' table needs the tenant to keep a
  human administrator"). This one passed only when an earlier test of the class had already left
  an administrator in the shared database.
- xUnit v3 runs a class's tests in `DefaultTestCaseOrderer` order: an ordinal sort of each test
  case's unique id. That id hashes the test assembly's **absolute path** (decompiled from
  xunit.v3.common 3.2.2: `UniqueIDGenerator.ForAssembly` hashes `assemblyPath` and the config file
  path, unless a `<assembly>.uniqueid` file beside the DLL overrides it). The order is therefore
  stable per checkout and configuration, but differs between slots, configurations and CI runners.

A model of the generator, validated against `-list full` ids and against two observed run orders
(this slot's default order, and one forced through a `.uniqueid` override), predicts for each
checkout whether the test runs before every administrator-seeding test:

| Checkout (Tellma.Core.IntegrationTests.dll) | Prediction |
|---|---|
| pc-f2, Debug (where PC-F67 saw it on 2026-11-22) | fails: the test runs 1st |
| pc-3, Release | fails |
| `/src`, Debug (the sdk container below) | fails |
| `D:\a\tellma-platform\tellma-platform`, Release (GitHub Windows runner, integration-windows) | fails |
| `/home/runner/work/tellma-platform/tellma-platform`, Release (PR-tier Linux CI) | passes: an administrator-seeding test runs 2nd, this one 4th |
| pc-1, pc-2, pc-4, pc-f (Debug and Release), the main checkout | passes |

That explains every observation in D-197: PC-F67's slot failed on the base commit too, PC-096
passed in another slot on 2026-11-21, and PR-tier CI on Linux stays green. The date was a
coincidence. The next nightly `integration-windows` run was predicted to fail on the same test.
The last nightly ran before PC-090 added the test.

The membership window is not involved: no membership this test seeds or reads has a `StartsAt` or
`EndsAt`, and the guard's `StartsAt <= SYSUTCDATETIME()` is only evaluated over administrator
memberships, of which there were none. No clock moves the outcome, so the clock-crossing check the
brief asked for in that case does not apply.

### Reproduction

These runs were made on platform-core a854b5e5, 2026-11-22 from 08:56 UTC, with Testcontainers
SQL Server 2022. A `.uniqueid` file beside the test DLL forces a chosen order. Each one was
removed after its run.

| Run | Before the fix | After the fix |
|---|---|---|
| Windows, the test alone (fresh fixture) | fails: `Access.LastAdministrator` | passes |
| Windows, the class in pc-2's own order (a seeding test runs 1st) | 18/18 | 18/18 |
| Windows, the class under `PC-F68-order-25` (the model's order confirmed in the trx) | 18/18 | not rerun |
| Windows, the class under `PC-F68-order-2` (the test runs 1st) | 17/18, the test fails | 18/18 |
| Linux, sdk:10.0 container from `/src` (the test runs 1st, per the trx) | 17/18, the test fails | 18/18 |
| Windows, each of the 18 tests alone on a fresh fixture | not run | 18/18, one at a time |

The last row is the worst case for every test of the class, since no sibling seeds anything first.
It shows that no other test of the class depends on a sibling's state.

The "after the fix" column was measured with a local edit equal to PC-143's: one
`fixture.SeedAdministratorAsync("Ann")` ahead of the test's other seeds. That edit was then
reverted, as the orchestrator directed.

### What was built, and where

Nothing but this file. The orchestrator assigned the test change to PC-143 (pc/PC-143 at
69012f0a). Its `UserSelfServiceTests` seeds an administrator in the bag test and in both of its new
notification-settings tests, which are also users'-stack actions and so also guarded. Its test
email test makes no persist. The local edit above is the same fix and passed every run in the
table, so PC-143's change is expected to hold in any order. Its two new tests were not run here.

No platform bug was found:

- **The guard counts correctly.** L1 is spec 0014 §8.3's text verbatim (`AccessGuards.InvariantsText`).
  It is monotone in time: a started, permanent membership stays started. `State` is computed from
  `InvitedAt` and `JoinedAt`, not from the clock (§3.2). The fixture's seeds use no membership
  window, so `SYSUTCDATETIME()` cannot change the outcome.
- **The guard joining `preferences/set` follows the specs.** The action is the administrative
  "another user's bag" entity action (spec 0018 §3.5), not the self-service `me/preferences/set`.
  The self-service action does not join the guards (`UserService.Me` writes only
  `core.UserPreferences`), and that is why the own-bag test passes alone without an administrator.
  The entity action joins them because spec 0015 §6.7 step 4 counts "the stack's tables" among an
  action persist's declared writes, which is the open risk below. In a tenant where L1 holds, which
  every guarded write keeps true, the join never refuses anything.

### Audit for the same defect elsewhere

A static scan of the 16 classes that take `IClassFixture<ConnectDatabase>` flagged tests that call a
write API without seeding an administrator. Apart from this test, it found only false positives:
tests that deactivate a user through raw SQL, or that fail before their persist, like
`The_same_member_s_save_of_their_own_row_through_the_users_save_is_refused_for_want_of_a_grant`,
refused at the type-level grant check. The scan is a heuristic over helper names. The per-test
alone runs above are the stronger check, and they cover this class only.

### Deviations

None.

### Open risks

- Other fixture-sharing classes could hide the same order dependence through a helper the scan does
  not recognise. Running a class's tests one at a time on fresh fixtures finds it. Forcing orders
  with a `.uniqueid` file beside the test DLL is a cheaper probe.
- Observation for the orchestrator, not changed: spec 0014 §8.3 says the guards join a persist
  "whose `WrittenTables` meets" a security table. Spec 0015 §6.7 step 4, which the code follows,
  widens that to "the stack's tables and every group's". So every action of the users' stack takes
  the tenant's security lock and asserts L1-L3, `preferences/set` and `preferences/delete` included,
  although they write only `core.UserPreferences`. That is harmless while L1 holds, and the two
  specs should state one rule.

### Local gates

- `dotnet build Tellma.slnx`: Build succeeded, 0 warnings (with the local edit in place).
- `dotnet format Tellma.slnx --verify-no-changes`: clean. The only output line is the
  workspace-loading notice.
- The CI unit filter over `Tellma.Core.IntegrationTests` matches no test.

### What reviewers should examine hardest

- Whether the order model and its predictions hold. The scripts are reproducible: an ordinal sort
  of SHA-256 ids over `assemblyPath`, then the collection (`Test collection for <class> (id:
  <ForType>)`), the class, the method and the case.
- Whether any other test of the class depends on state a sibling leaves. The alone runs say no.
- Whether PC-143's two new notification-settings tests pass alone. They seed an administrator, but
  they were not run here.
