# PC-F82: Concurrent grant race in TellmaPrincipalFactory

## Implementer notes

### The faults, and the tests that showed them

PC-114's R1-4 (D-266) found that two grants of one user to one client, run at once, race on the
permanent authorization `TellmaPrincipalFactory` attaches. Probing it turned up a second race of the
same scenario, one step later, in the session registry. Both make one of two tabs fail its sign-in.

- **(a) Extending one authorization.** Both grants read the client's authorization, which lacks a
  resource they ask for, and both `UpdateAsync` it. The store refuses the second write on the
  authorization's concurrency token, and the grant answered `temporarily_unavailable`, which the
  BFF callback turns into a 500 problem page.
- **(b) A session's first grant to a client.** Both grants find no `SessionClients` row for
  `(sid, client)` and both insert one. The second insert fails on `PK_SessionClients`, and the
  unhandled `DbUpdateException` makes the authorize endpoint itself answer 500.

I wrote `Flows/ConcurrentGrantTests` first, against platform-core's product code. All four tests
failed there, each for the reason it names:

- `Two_tabs_extending_one_authorization_at_once_both_get_a_code` (two cases: the same new tenant,
  two different new tenants): one tab got `error=temporarily_unavailable`.
- `Two_tabs_making_a_sessions_first_grant_to_a_client_at_once_both_get_a_code`: one tab got 500.
- `A_grant_that_keeps_losing_its_write_gives_up_after_three_attempts`: one attempt, not three.

The races are staged deterministically, not by timing. An EF `SaveChangesInterceptor`
(`SaveBarrier`, added through `ConfigureDbContext<TellmaIdentityDbContext>` in `ServiceOverrides`)
holds the first two matching saves until both have arrived, so each grant has read what it is
about to write before the other writes it. The barrier is armed only after the setup sign-in, and
each test asserts that both saves did arrive, so a change that stops the race from being staged
fails the test rather than passing it vacuously. A third interceptor (`AuthorizationWriteSaboteur`)
bumps the authorization's concurrency token from another connection before every update, so every
write loses.

### What was built

- `src/apps/Tellma.Identity/Services/AuthenticationPolicy/TellmaPrincipalFactory.cs`
  - The find-create-or-extend moved into `AttachAuthorizationAsync`, a loop of at most
    `MaxAuthorizationWriteAttempts` (3) attempts. Each attempt re-runs `FindAuthorizationAsync`:
    a covering authorization is used as it is; with none at all, one is created; otherwise the last
    one found is extended. A lost write (`ConcurrencyException`, still caught in
    `TryAddResourcesAsync`) is logged (Information) and the loop reads again; the third lost write
    is logged (Warning) and the grant answers `temporarily_unavailable` as before.
  - `ForgetAuthorizationsRead` detaches every `Unchanged` `OpenIddictEntityFrameworkCoreAuthorization`
    from the request's `TellmaIdentityDbContext` before the re-read. This is load-bearing:
    OpenIddict 7.5's EF store (`UpdateAsync`, verified by decompiling the package) catches
    `DbUpdateConcurrencyException`, sets the entry back to `Unchanged` and throws. The tracked copy
    keeps the grant's own edits and a new concurrency token the database never saw, and EF hands
    that same tracked instance back to every later query. Without the detach, the retry lost again
    on every attempt: with the call disabled, both cases of the two-tab test still failed with
    `temporarily_unavailable`. Only `Unchanged` entries are detached, so no pending change is
    dropped.
  - The factory now takes `TellmaIdentityDbContext` and `ILogger<TellmaPrincipalFactory>`. Both are
    already registered; the factory stays scoped.
- `src/apps/Tellma.Identity/Services/Sessions/SqlSessionRegistry.cs`: `RegisterClientAsync` catches
  a `DbUpdateException` whose inner `SqlException` is 2627 or 2601, only when it inserted the row.
  It detaches the refused row (left tracked, every later save on the request's context would insert
  it again), re-reads the winner's row, records this grant's authorization id on it as a later
  registration would, and saves again. If the winner's row is gone, it rethrows.
- `ISessionRegistry.RegisterClientAsync`'s doc and `src/apps/Tellma.Identity/README.md` state the
  concurrent behaviour.

No protocol contract, endpoint, claim or token shape changed. The error a grant that keeps losing
gets is the same `temporarily_unavailable`, with the same description.

### Deviations

1. **The session-registry race is fixed here too.** The item names only the factory's find-and-add.
   The registry's insert race is the same scenario (two tabs of one session, a client new to the
   session) and fails the same way, with a 500 instead of a `temporarily_unavailable`. Fixing only
   (a) would have left the item's symptom in place for every session's first grant to a client.
2. **Two first grants at once still create two authorizations.** Both find none, and the store has
   nothing to refuse. Both sign-ins now succeed; on platform-core the second one 500ed on the
   registry insert, so its authorization was created but never got a token. The session's one
   registration names whichever grant registered last, so a session logout revokes that
   authorization and not the other. See open risk 1. I did not fix this here: a correct fix
   serializes a user's grants to a client, for example with a transaction-owned `sp_getapplock`
   around the find, create or extend and the registration. That is a design choice beyond this
   item.

### Open risks

1. **Duplicate authorizations and session logout.** After (b) is fixed, two simultaneous first
   grants give the tabs tokens on two authorizations, and session logout revokes only the
   registered one. The practical exposure is small:
   - A BFF drops every BFF session of the sid on back-channel logout, so the other tab's tokens
     are no longer held.
   - Sign-out-everywhere also changes the security stamp, which refresh checks.
   - The same gap already exists without any race: grants of one client with different scope sets
     find different authorizations (`FindAsync` filters by scopes), and the registry holds one per
     `(sid, client)`.
   A ruling is needed: accept, or serialize grants per `(subject, client)`, or make the registry
   hold every authorization a session's grants used.
2. **Three attempts.** Each lost write means another grant's write landed, so a grant survives two
   others extending the same authorization at once. A fourth simultaneous tab asking for a fourth
   new place could still get `temporarily_unavailable`. The BFF's handling of that error (a 500,
   not a re-challenge) is unchanged. R1-4 listed a re-challenge as optional.
3. **The detach is scoped to OpenIddict's default EF entity type.** That holds today:
   `UseEntityFrameworkCore().UseDbContext<TellmaIdentityDbContext>()` with the default models. A
   custom authorization entity would need `ForgetAuthorizationsRead` changed. The two-tab test
   would catch that, since it fails without the detach.

### Tests run (locally)

- `dotnet build` of `Tellma.Identity.Tests` and `Tellma.Identity.IntegrationTests` in the inner
  loop, then `dotnet build Tellma.slnx` once at the end (0 warnings, Build succeeded).
- `dotnet format Tellma.slnx --verify-no-changes`: clean.
- Identity unit tests, the whole project with the unit filter: 1360 passed. The factory's
  constructor changed; every integration flow below resolves it from the request scope.
- Identity integration, `ConcurrentGrantTests` plus the sign-in and authorization classes
  (TenantResource, Consent, AuthorizationCodeFlow, DeviceFlow, BackchannelLogout, EndSession,
  SessionPrune, ResourceIndicator, ParAndPkce, AssurancePolicy, RefreshToken, SecurityStampRefresh,
  CompositionParity, DocumentClient): 78 passed.
- `ConcurrentGrantTests` alone, 20 consecutive runs: 20 of 20 green (4 of 4 tests each run).
- The same four tests against platform-core's product code (src stashed): all 4 failed, as listed
  above.

### What reviewers should examine hardest

- Whether detaching every `Unchanged` authorization from the request's context is safe for
  everything else in the request. The authorization endpoint and the device verification both
  call the factory. After it, OpenIddict's sign-in handling reloads the authorization by id.
- Deviation 2 and open risk 1: whether duplicate authorizations from simultaneous first grants are
  acceptable.
- The registry's duplicate-key path: it detaches only the row it added, and the session's
  `LastSeenUtc` change is saved with the second save.

## Review 1

Scope: commit 04056370 only, against the LEDGER row PC-F82 and PC-114 R1-4 (D-266). No
CHECKPOINTS entry, and WATCHLIST.md has no spec 0003 section.

Verified locally on 04056370: `dotnet build test/apps/Tellma.Identity.IntegrationTests` printed
Build succeeded, 0 warnings. I then ran `dotnet test` on that project with `--no-build`, filtered to ConcurrentGrantTests,
BackchannelLogout, ConsentTests, DeviceFlow, EndSession and SessionPrune, plus
`Category=Integration&Category!=Nightly&Live!=true`: 28 passed, 0 failed. The pc/PC-F82 push's CI run (45390556674) was
skipped, so there is no CI evidence yet. The brief let me modify only this file, so I did not
re-run the red-without-fix cases. I checked them by reading the code instead (below).

What holds, checked item by item:

- **The LEDGER row.** It asks for a retry of the find-and-add, up to 3 times, on the concurrency
  error, plus an integration test with two parallel grants. Both are done
  (`TellmaPrincipalFactory.cs:273-305`, `ConcurrentGrantTests`). Deviation 1 (the registry's
  duplicate-key 500) is justified: without it the same two-tab scenario still fails, one step later.
- **The barrier reproduces production.** `SaveBarrier` holds each request in `SavingChangesAsync`,
  after that request's `FindAsync` reads and before its SQL is sent. So both grants read before
  either writes, which is the real two-tab interleaving. On release, SQL Server serializes the two
  statements on the row or key lock. The second `UPDATE … WHERE ConcurrencyToken = @old` then
  matches 0 rows, and the second `INSERT` gets error 2627. Each request has its own scope and
  context, as two real requests would. A barrier that is not reached fails the test
  (`BothArrived`), so the test cannot pass vacuously. The 30 s timeout keeps a lock-induced
  deadlock from hanging the run.
- **Each test fails without its fix.**
  - Without the retry, both `Two_tabs_extending…` cases get `temporarily_unavailable`, and the
    give-up test counts 1 attempt, not 3.
  - Without the registry catch, `Two_tabs_making_a_sessions_first_grant…` gets the 500, because
    the barrier forces both inserts.
  - Without the detach, the `(Tenant18, Tenant19)` case cannot pass under either reading of what
    the refused tracked copy holds:
    - If the copy keeps the loser's own edit `{17,19}`, the re-read finds it covering, returns
      without writing, and `CoveredResourcesAsync` misses 19.
    - If it holds the read-time values with the stale original token, every retry loses again.
  - So the detach is pinned by the theory's second case. The `(18,18)` case alone would not pin it.
- **The detach cannot drop a legitimately tracked entity.**
  - It runs only after a lost write (`:303`), and only on `Unchanged` authorizations.
  - Neither controller that calls the factory (`AuthorizationController`,
    `DeviceVerificationController`) reads authorizations itself.
  - The tokens OpenIddict loads in those requests carry no authorization navigation. The PAR
    request token, and the device and user codes before approval, have no authorization. So no
    tracked graph points at a detached instance that a later `Context.Update(token)` could
    re-attach beside the fresh copy.
  - The code token's `SetAuthorizationIdAsync` resolves the id through `DbSet.FindAsync`, so it
    gets the fresh tracked copy.
- **The retry cannot loop or mask other errors.**
  - The loop is bounded: `attempt == MaxAuthorizationWriteAttempts` returns null on the third loss.
  - Only `OpenIddictExceptions.ConcurrencyException` is caught (`:385`). Validation and store
    errors propagate as before.
  - A concurrent revocation or prune also surfaces as a concurrency loss. The re-read then filters
    on `Statuses.Valid` and creates a new authorization, which is correct and never extends a
    revoked one.
- **The duplicate-key filter distinguishes only that error.**
  - It requires `added is not null`, so this save inserted the registration, and an inner
    `SqlException` 2627 or 2601.
  - `SessionClients` has no unique index besides its primary key
    (`EngineEntityConfigurations.cs:49-54`), so 2601 cannot come from anything else here.
  - A misattributed duplicate on another pending entity would find no winner row and rethrow
    (`SqlSessionRegistry.cs:94-97`).
- **The BFF re-challenge on `temporarily_unavailable` (D-266 "optional").** It was not built, and
  one path still reaches it.
  - `TellmaPrincipalFactory.cs:215-219` is now the only producer of that error in `src`.
  - A grant reaches it only after losing three writes in a row. Each loss needs another grant's
    write to land between this grant's read and its write, so it takes four or more simultaneous
    grants of one user and client, each naming a place the authorization does not cover yet.
  - `BffOidcEvents` has no `RemoteFailure` handler, so that case is still the BFF's 500 page.
  - It is rare enough to leave, with the e2e `signInAs` retry kept as D-266 says. It needs no fix
    item of its own.
- AGENTS.md and PLAN.md §7.1:
  - XML docs are on every new member.
  - The log messages and test names cite no spec, PC id or decision.
  - The README is updated, and ARCHITECTURE.md does not describe the registry, so it needs no
    change.
  - The commit lists the tests it ran. They cover every flow that resolves the factory or the
    registry (authorize, consent, device, end-session, back-channel logout, session prune).

### Findings

**R1-1 (major): The race now leaves a live authorization that session logout does not revoke, and
the same gap already exists without the race. The registry should hold every authorization.**

- **Where:** `SqlSessionRegistry.cs:71-74` and `:99-102`, the one `AuthorizationId` per
  `(sid, client)`, overwritten by each registration. Also `TellmaPrincipalFactory.cs:286-289`,
  where two first grants each create one authorization.
- **Failure scenario:**
  1. Two first grants of one public client (the CLI or a native app, with `offline_access`) run at
     once in one SSO session. Two CLI invocations opening two browser tabs is one way.
  2. Each creates an authorization, A1 and A2, and each gets a code and then a refresh token.
  3. The session registers only the one written last, say A2.
  4. The user signs out (`Logout`, or `EndSession` with the global default). The sign-out calls
     `TerminateSessionAsync`, which revokes A2 only (`BackchannelLogoutService.cs:59-68`).
  5. A1's refresh-token family keeps renewing, on its 7-day sliding window, for as long as it is
     used. The refresh path checks only the security stamp (`TokenController.cs:193-202`), and a
     single-session logout does not bump it. Only sign-out-everywhere and password reset do
     (`Manage/Sessions.cshtml.cs:60`, `ResetPassword.cshtml.cs:111`).
- **Why this breaks the spec:** spec 0003 §7.3 says the logout's mechanisms "stop token renewal".
- **What this commit changed:** on platform-core the losing grant 500ed at the registry insert, so
  A1 got no token, which failed closed. This commit converts that 500 into a live, unregistered
  refresh token. It is a revocation regression for the race, traded for availability.
- **Who is exposed:**
  - BFF clients are not exposed: back-channel logout reaches the BFF for the registered client,
    and `BffEndpoints.cs:370` (`RevokeBySidAsync`) drops every BFF session of the sid, including
    the tab holding A1's tokens.
  - The exposure is public and native clients without a back-channel endpoint.
- **The non-race variant, already on platform-core:** a later grant of the same session asks for a
  scope the client's authorization lacks. `FindAsync` filters by scopes, so the grant creates a
  second authorization, and `:73` re-points the registration at it. The earlier authorization's
  refresh tokens then survive session logout too, deterministically. That makes it the larger
  exposure of the two.
- **Ruling recommendation:**
  - **Do not just accept it**, because of the regression for public clients.
  - **Do not use `sp_getapplock`.** A transaction-owned lock per `(subject, client)` would close
    only the race. It would not close the scope-set variant, and it would wrap OpenIddict's store
    saves on the shared request context in a transaction and lock on every grant.
  - **Have the registry hold every authorization a session's grants used.** For example, a
    `SessionClientAuthorizations` child table keyed `(Sid, ClientId, AuthorizationId)`, or that
    key on `SessionClients` itself. `NotifyAsync` would revoke them all while delivering one
    logout token per client. This closes the race, the scope-set variant and the duplicate-key
    overwrite at once. Keep the duplicate-key catch for two grants with the same authorization.
  - **Where to do it:** it needs an Identity migration, so it fits a new fix item better than this
    one. This commit can land if that item is opened and the gap is recorded as an open risk until
    then.
  - **Alternative:** if Ahmad prefers to stay fail-closed meanwhile, the registry catch could
    refuse with `temporarily_unavailable` whenever the winner row names a different authorization.
    That keeps the 500 fixed for the common same-authorization case.

**R1-2 (minor): No test pins the registry catch's narrowness or its fail-closed rethrow.**

- **Where:** `SqlSessionRegistry.cs:84-97`.
- **Failure scenario:** every test still passes if a later edit does either of these:
  - widens the filter, for example to `catch (DbUpdateException) when (added is not null)`, which
    would also swallow the FK violation 547;
  - drops the `winner is null` rethrow.
- **Why it matters:** the security-relevant case is a session terminated or pruned between the
  registration read and the insert. Today the insert fails on the FK and the grant gets no code.
  A widened catch would detach the row, find no winner, and with the rethrow gone would save only
  `LastSeenUtc`. A code would then be issued for a session that no longer exists, with no
  registration, so logout can never revoke it.
- **Suggested fix:** add a `ConcurrentGrantTests` case.
  1. Arm a `SaveBarrier` on `IdentitySessionClient` `Added` for a single arrival, or add a
     one-shot interceptor.
  2. From another connection, delete the `Sessions` row while the save is held.
  3. Assert the authorize response carries no `code`.

### Responses

- **R1-1: fixed in fix1 (D-277).** The session registry now holds every authorization a session's
  grants used, and logout revokes them all.
  - Schema: a new `SessionClientAuthorizations` table keyed `(Sid, ClientId, AuthorizationId)`,
    beneath `SessionClients` (FK on `(Sid, ClientId)`, cascade), and `SessionClients.AuthorizationId`
    is dropped. The client row stays one per `(sid, client)`, so `NotifiedUtc` and the back-channel
    delivery remain one per client. `AuthorizationId` is `nvarchar(100)`, which keeps the three-column
    key within the 900-byte index limit; OpenIddict's ids are GUID strings. There is no FK to
    `OpenIddictAuthorizations`, as before: OpenIddict's prune deletes authorizations by set, and
    the revocation path already skips a missing one. The migration
    `20261124122036_SessionClientAuthorizations` was generated by `dotnet ef migrations add` (the
    Migrations README command). Its only change after scaffolding is the normalization every
    committed file gets: the BOM removed and CRLF turned to LF. `TellmaIdentityConstants.LatestMigration`
    points at it, as its unit test requires, and `MigrationsTests` expects the new table.
  - `SqlSessionRegistry.RegisterClientAsync` adds whichever of the two rows is missing (the client
    under the session, the authorization under the client) and never replaces an authorization. A
    lost insert (2627/2601) detaches the refused rows and reads again, up to 3 attempts. Each loss
    means another grant wrote one of at most two rows, so the third attempt has nothing left to
    lose. Any other failure propagates.
  - `TerminateAsync` and `TerminateAllAsync` load `Clients.Authorizations`.
    `BackchannelLogoutService.NotifyAsync` revokes every recorded authorization and audits each
    revocation, then delivers one logout token per client as before.
  - Readers of the old single `AuthorizationId`: the only one in `src` was `NotifyAsync`, which
    serves `Logout`, `EndSession` and sign-out-everywhere (`Manage/Sessions`). Session prune
    deletes `Sessions` by set, and the new rows cascade with `SessionClients`. No admin path reads
    the registry.
  - Spec 0003 §7.3 names only "a session registry keyed on `sid`", not its columns, so this is no
    deviation from it.
  - New tests, each red on 04056370's product code, both with "valid" where "revoked" was expected:
    - `Two_tabs_making_a_sessions_first_grant_at_once_both_get_tokens_that_a_logout_ends` (the
      former first-grant test, extended): two authorizations, both registered; after the
      end-session sign-out both are revoked and neither refresh token renews (`invalid_grant`).
    - `A_grant_asking_for_more_scopes_gets_its_own_authorization_and_a_logout_ends_both` (no race,
      `+ profile`).
    - To run them on 04056370, `RegisteredAuthorizationIdsAsync` was substituted with the
      authorization list, since the new entity does not exist there.
  - The implementer notes' deviation 2 and open risk 1 are resolved by this. The factory's and
    the README's text now say the registry keeps every authorization.
- **R1-2: fixed in fix1.** `A_grant_whose_session_ends_while_it_registers_the_client_gets_no_code`.
  - A one-shot `SessionEnder` interceptor deletes the `Sessions` row from another connection when
    the grant's save is about to insert the `SessionClients` row.
  - The test asserts that the deletion happened, that no code was returned and that no
    authorization was registered.
  - It passes on 04056370 too, as it should, because it pins existing behaviour.
  - A mutation made it fail: a catch that swallows the failure and returns made the grant return a
    code.
- **Gates (fix1).**
  - `dotnet build Tellma.slnx`: "Build succeeded", 0 warnings and 0 errors.
  - `dotnet format Tellma.slnx --verify-no-changes`: clean. The only output was the usual
    workspace-loading warnings line.
  - Identity unit tests: 1360 passed.
  - Identity integration tests, filtered to `ConcurrentGrant`, `BackchannelLogout`, `Consent`,
    `DeviceFlow`, `EndSession`, `SessionPrune`, `Migrations`, `StoreCheck`, `AccountEntry`,
    `RefreshToken`, `SelfServiceNav`, `PathBaseRedirect`, `Discovery`, `SecurityStampRefresh`,
    `AuthorizationCodeFlow`, `TenantResource`, `CompositionParity` and `Seeding`: 95 passed and 1
    failed.
    - The failure was `StoreCheckTests`' migrator-entry test. It asserted a single migration
      history row, which assumed the store had one migration.
    - It now checks that a second migrate leaves the history unchanged. `StoreCheck` and
      `Migrations` then passed 13 of 13.
  - Acme integration tests, filtered to `MigratorTests` and `WebHostTests`: 14 passed.
  - `ConcurrentGrantTests` run 20 times: 20 of 20 runs were green, each running all 6 tests.

## Review 2

Scope: 04056370 and 322845ef together, against the LEDGER row PC-F82, D-266 (R1-4), D-277, spec
0003 §7.3, spec 0010 Part A (identity migrations and the store check) and spec 0012 §4.3 (the N−1
rule that 0010 Part A cites). There is no CHECKPOINTS entry, and WATCHLIST.md has no spec 0003
section; its 0010-A leads are about fixtures and client metadata, not the registry.

Verified locally on 322845ef:

- `dotnet build` of `Tellma.Identity.IntegrationTests` and `Tellma.Identity.Tests`: Build succeeded,
  0 warnings, 0 errors.
- Identity unit, `IdentityStoreMigrationsTests` (the latest-migration pin and the collation pin): 2
  passed.
- Identity integration, `--no-build`, with `Category=Integration&Category!=Nightly&Live!=true`:
  - ConcurrentGrant, Migrations, StoreCheck, BackchannelLogout, EndSession, SessionPrune, Consent,
    DeviceFlow, Revocation, SecurityStampRefresh, AuthorizationCodeFlow and TenantResource: 64
    passed, 0 failed.
  - DocumentClient, ParAndPkce, ResourceIndicator, AssurancePolicy, TokenExchange and Userinfo
    (other flows that reach `RegisterClientAsync`, which fix1's list left out): 31 passed, 0 failed.
  - `ConcurrentGrantTests` alone, 5 consecutive runs: 5 of 5 green, 6 of 6 tests each.
- The pc/PC-F82 CI runs (45390556674, 45397993516) were skipped, so there is no CI evidence yet.

The orchestrator's five questions:

1. **Does every path that should stop renewal revoke all of a session's authorizations?** For
   registrations made after the upgrade, yes, with the exceptions in R2-1 (sessions that exist at
   upgrade) and R2-3 (a logout racing a grant).
   - All three callers go through `BackchannelLogoutService.NotifyAsync`, which revokes every row:
     `Logout.cshtml.cs:45`, `EndSessionController.cs:130` and `Manage/Sessions.cshtml.cs:61`.
   - No admin, operator or user-disable path exists in `src`. `OperatorController` has only
     get-user and issue-TAP, and nothing sets `UserLifecycleState.Disabled`. Password reset relies
     on the stamp, as before.
   - Nothing in `src`, `test` or `distributions` reads a single authorization any more.
2. **Is the migration EF-generated, reversible and safe for existing rows?**
   - It is EF-generated as far as can be checked without re-scaffolding. The operations come in
     the differ's order (drop, then create), and the Designer and snapshot agree with the model.
     `MigrateAsync`, which refuses pending model changes, runs in `MigrationsTests` and
     `StoreCheckTests`.
   - It is reversible in shape, but lossy in both directions, and it is not safe for existing rows
     (R2-1).
3. **Can the duplicate-key retry loop or misattribute?** No.
   - It cannot loop: the filter needs `attempt < 3`, so the third failure propagates.
   - It cannot turn a foreign failure into a success. The filter needs rows this save added and an
     inner `SqlException` 2627 or 2601. A duplicate from any other pending entity fails again on
     every attempt until the third one rethrows. The session-deleted cases (FK 547, or the 0-row
     `DbUpdateConcurrencyException` on the session's `LastSeenUtc` update) are not matched.
   - The detach and re-read are exercised for a conflict on the client row. A conflict on the
     authorization row alone is not (R2-2).
4. **Do the two new tests fail without the fix, and is the interleaving real?** Yes to both.
   - On 04056370 the registry kept one `AuthorizationId` per `(sid, client)`, overwritten by each
     grant, and `NotifyAsync` revoked only that one. In both tests one authorization therefore
     stays valid, and `AssertEndedAsync` fails on its status and on its refresh token, as the
     implementer's red run reports.
   - The barriers still stage the real two-tab interleaving:
     - `creates` holds both creates until both grants have read and found nothing.
     - `registrations` holds both registry saves until both have run `StageRegistrationAsync`.
     - The loser's retry is the third matching save, so it passes straight through.
     - The `BothArrived` asserts remain, so neither test can pass vacuously.
5. **Is the StoreCheckTests change legitimate?** Yes, it generalizes the assertion without weakening
   it.
   - `Assert.Single` meant "the second migrate applied nothing more" only because the chain held
     one migration.
   - `Assert.Equal(applied, …)` checks the same property for any chain length.
   - `Assert.Contains(LatestMigration, applied)` still checks that the first run reached the end of
     the chain.

Review 1's responses: the implementer accepted both findings, so there is no pushback to judge.

- R1-1's fix holds for grants made after the upgrade, and the two new tests prove it. It does not
  hold for sessions that exist at upgrade (R2-1).
- R1-2's test pins the property that matters: a session deleted mid-registration gets no code.
  - The exact mutation R1-2 named, a widened filter, no longer fails that test.
  - Under fix1's bounded loop that mutation cannot issue a code either, because the third attempt
    rethrows. So the property is still pinned.

### Findings

**R2-1 (major): The migration drops `SessionClients.AuthorizationId` in the same release that stops
using it. That breaks the N−1 rule every identity migration must follow, and it discards the
revocation link of every session that exists at upgrade.**

- **Where:**
  - `src/apps/Tellma.Identity.Migrations/Migrations/20261124122036_SessionClientAuthorizations.cs:14-17`:
    the drop, with no copy into the new table.
  - `:45-55`: `Down`.
  - The property's removal from `IdentitySessionClient`, and from its mapping in
    `EngineEntityConfigurations.cs:49-52`.
- **The rule:**
  - Spec 0010 Part A: every migration after `Initial` "follows the N−1 rule of spec 0012 §4.3. It
    lands before the engine deploys, so the previous engine release runs on it during a rollout."
  - §4.3 drops a column only "when no version that can still be running writes it", and it moves
    data in three releases: add and copy, then copy again, then drop.
  - `src/apps/Tellma.Identity.Migrations/README.md:24` repeats the rule.
  - Neither commit records a deviation from it.
- **Failure 1, during a rollout or after a rollback:**
  1. The migrator applies `SessionClientAuthorizations` before the new engine is deployed, or the
     engine is rolled back after it.
  2. The previous engine starts. Its store check needs only its own `Initial` row, and later rows
     do not fail it (`IdentityStoreCheck.cs:224-231`).
  3. Its model still maps `SessionClients.AuthorizationId`. These calls select every mapped column,
     so they fail with "Invalid column name 'AuthorizationId'":
     - `RegisterClientAsync`'s `FindAsync`;
     - the `Include(Clients)` in `TerminateAsync` and `TerminateAllAsync`.
  4. Every authorize request with a session then answers 500. So do logout, end-session and sign-out
     everywhere, until the new engine runs everywhere.
- **Failure 2, rows that exist at upgrade:**
  1. A session that exists at upgrade has a `SessionClients` row whose `AuthorizationId` the
     migration discards. It gets no `SessionClientAuthorizations` row.
  2. After the upgrade, the user logs out. `TerminateAsync` returns the client with an empty
     `Authorizations`, and `NotifyAsync` revokes nothing.
  3. A public or native client's refresh tokens issued before the upgrade keep renewing. This is
     R1-1's break of spec 0003 §7.3 ("stop token renewal"), now for every pre-upgrade session
     instead of one race. It lasts until that session makes another grant to the client.
  4. `Down` loses data the same way. It drops the table and re-adds an empty column, so a store
     migrated back loses every registration's authorization.
- **Tests:** `MigrationsTests` migrates only an empty database, so no test runs an upgrade over
  existing rows.
- **Suggested fix.** It needs no hand-edited migration, so it stays within D-277.
  1. Keep `IdentitySessionClient.AuthorizationId` mapped (nullable, 450) for one more release,
     documented as kept for the previous engine. Have `RegisterClientAsync` keep writing the latest
     authorization id to it, so a rolled-back engine still revokes what it always did.
  2. Have `NotifyAsync` revoke the union of the rows and that column. That covers sessions that
     exist at upgrade, and registrations the previous engine writes during the rollout.
  3. Re-scaffold the migration. It then only creates the table.
  4. Add an upgrade test:
     - migrate to `Initial`, and insert a session and a client registration that carries an
       authorization id;
     - migrate to the latest, end the session, and assert that the authorization is revoked.
  5. Open a fix item for the contract step one release later: copy the column into the table, then
     drop the column.
- **Alternative:** Ahmad may rule that no identity store at `Initial` has been deployed anywhere, so
  no previous release can ever run on this migration. In that case, record the ruling as a
  deviation from 0010 Part A in the fix commit, rather than leave the rule silently broken.

**R2-2 (minor): Three new paths have no test, and the first one would fail silently.**

- **(a) Sign-out everywhere.**
  - `TerminateAllAsync` loads the rows through the new `.ThenInclude(static c => c.Authorizations)`
    (`SqlSessionRegistry.cs:123`).
  - The old id was a scalar column and always loaded. The rows load only through this include,
    since the context has no lazy loading.
  - If an edit dropped the include, `NotifyAsync` would see empty collections and revoke nothing,
    with no error.
  - No test checks the authorizations' status after `OnPostSignOutEverywhereAsync` or
    `TerminateAllSessionsAsync`. A refresh-only assertion would not catch the loss either, because
    the stamp bump already fails the refresh.
  - `TerminateAsync`'s include is pinned, by the end-session tests here and by
    `BackchannelLogoutTests`.
- **(b) Session prune.**
  - `Retention_removes_long_terminated_rows_and_their_client_registrations` registers with
    `authorizationId: null` (`SessionPruneTests.cs:69`).
  - So the retention pass never deletes a session that holds a `SessionClientAuthorizations` row,
    and the new cascade (Sessions → SessionClients → SessionClientAuthorizations) is never
    exercised.
  - If a later regeneration or model edit lost that cascade, every retention sweep would fail on
    FK 547, and no test would show it.
- **(c) A conflict on the authorization row alone.**
  - Scenario: two tabs of one session, with the client already registered, both record an
    authorization that is new to the session.
  - Only the conflict on the client row is staged (`Two_tabs_making_a_sessions_first_grant…`).
- **Suggested fix:**
  - (a) A `ConcurrentGrantTests` case: make two authorizations, post the Manage/Sessions sign-out
    everywhere, and assert that both are `revoked`.
  - (b) Pass an authorization id in the retention test, and assert that
    `SessionClientAuthorizations` holds only "recent".
  - (c) Optional: after one ordinary grant has registered the client, a `SaveBarrier` on
    `IdentitySessionClientAuthorization` `Added`.

**R2-3 (minor): A logout that races an in-flight grant of the same session can still leave a valid
authorization.**

- **Where:** `BackchannelLogoutService.cs:66-79`, and `SqlSessionRegistry.cs:203-229` together with
  `TellmaPrincipalFactory.cs:280-305`.
- **(a) A lost revoke is dropped silently.** This is pre-existing, but it is in the loop fix1
  rewrote.
  - `NotifyAsync` reads the authorization (`FindByIdAsync`, `:66`) and then calls
    `TryRevokeAsync`, which writes on the authorization's concurrency token.
  - When that write loses, `TryRevokeAsync` catches the store's concurrency exception and returns
    `false`. That happens when a second tab's extend of the same authorization commits between the
    two calls.
  - `NotifyAsync` treats `false` as nothing to do: no log, no audit row, no retry.
  - The authorization stays valid, so every refresh token it backed before the logout keeps
    renewing.
- **(b) The new retry turns the opposite order into a live grant.**
  1. The revoke commits first, so the tab's extend loses.
  2. The re-read (`Statuses.Valid`) finds nothing, and `CreateAuthorizationAsync` makes a new
     authorization.
  3. `RegisterClientAsync` records it under the session that `TerminateAsync` has just closed:
     `StageRegistrationAsync` never checks `TerminatedUtc`.
  4. A code is issued, and no later logout of that sid reaches the new authorization.
     `TerminateAsync` returns `[]` for a session already terminated, and `TerminateAllAsync`
     filters such sessions out.
  - On platform-core the same grant got `temporarily_unavailable`.
  - It is minor because the outcome is already reachable without the retry: a first grant in
    flight, or any request still carrying the pre-logout SSO cookie, reaches it too, since spec
    0003 §7.3 defers server-side cookie termination.
- **Suggested fix.** A follow-up fix item is enough.
  - (a) On `false`, detach the request's copy, read the authorization again and retry the revoke a
    bounded number of times. If it still fails, log a Warning and audit `TokenRevoked` with outcome
    `failure`.
  - (b) Make `RegisterClientAsync` refuse a terminated session atomically:
    - The session-row write it already makes in the same transaction as the inserts should apply
      only while `TerminatedUtc IS NULL`, for example by making `TerminatedUtc` a concurrency
      token. When no row matches, fail the grant with `login_required`.
    - `TerminateAsync` should set `TerminatedUtc` before it reads the registrations, or read them
      again after its save.
    - A registration then either lands before the terminate's read, or fails.

### Responses

- **R2-1: fixed in fix2 (D-279), as an expand step under the N−1 rule.**
  - `IdentitySessionClient.AuthorizationId` is mapped again (`nvarchar(450)`, nullable, as on
    `Initial`). `StageRegistrationAsync` writes it as before: the authorization on a new
    registration, or the latest one over an existing registration. It still adds the
    `SessionClientAuthorizations` row beside it. A rolled-back engine therefore still revokes what
    it always did.
  - `BackchannelLogoutService.NotifyAsync` revokes the column's authorization plus every row, each
    id once (`UsedAuthorizationIds`, distinct and ordinal), so no id is audited twice. That covers
    sessions that exist at upgrade, and registrations the previous release makes during a rollout.
  - The fix1 migration was removed with `dotnet ef migrations remove`, which reverted the snapshot
    to 04056370's content.
  - It was re-scaffolded with the README's `dotnet ef migrations add` command as
    `20261124132011_SessionClientAuthorizations`. It now only creates the table, its key and its
    cascade foreign key, and `Down` only drops the table.
  - The only edit after scaffolding is the normalization the committed files already show. The
    migration file loses its BOM and the scaffolder's CRLF. The Designer and snapshot keep the BOM,
    as `Initial`'s Designer and the snapshot do. (fix1 had stripped the snapshot's BOM, which is
    now back.)
  - The migration has no data-copy SQL. `TellmaIdentityConstants.LatestMigration` points at the new
    id.
  - The column is not dropped. That is PC-F90's contract step.
  - New upgrade test,
    `A_session_registered_before_the_upgrade_is_still_ended_by_a_logout_after_it`:
    1. The CLI is granted and redeemed.
    2. The store is migrated back to `Initial` (the migration's `Down`), then forward to the latest
       (`Up`), through the host context's `IMigrator`. What remains is exactly what the previous
       release writes: the registration with the authorization in the column, and no rows (the
       test asserts that the rows are empty).
    3. The end-session logout then revokes the authorization, and the refresh token no longer
       renews.
    - It was red on 322845ef's product code ("valid" where "revoked" was expected): fix1's `Down`
      re-added an empty column and its `Up` dropped it.
- **R2-2: fixed in fix2.** Three tests, each shown to fail under a mutation of what it pins. fix1
  already has the behaviour, so none can be red on 322845ef.
  - (a) `Signing_out_everywhere_revokes_every_authorization_a_session_used`:
    - Two authorizations under one session (a wider scope set makes the second). Posting the
      Manage/Sessions sign-out-everywhere form revokes both.
    - Mutation: drop the `Include(Authorizations)` from the registrations read that both terminate
      paths now share. Only the column's latest authorization is then revoked, and the test fails
      ("valid").
  - (b) `Retention_removes_long_terminated_rows_and_their_client_registrations`:
    - It now registers `authorization-<sid>` and asserts that `SessionClientAuthorizations` keeps
      only `authorization-recent`.
    - Mutation: the migration's cascade changed to `NoAction`. The retention delete then fails on
      FK `FK_SessionClientAuthorizations_SessionClients_Sid_ClientId`.
  - (c) `Two_tabs_recording_an_authorization_new_to_the_session_at_once_both_get_a_code`:
    1. Another session of the user's creates authorization A (default scopes, tenant 17).
    2. This session registers the CLI with its own B (`+ profile`, tenant 18).
    3. Two tabs then both use A at once, held by a `SaveBarrier` on
       `IdentitySessionClientAuthorization` `Added`, so they race on that row alone.
    - Both get a code. The session records {A, B} and the other session records {A}.
    - Mutation: retry only when the client row was refused. The losing tab then answers 500.
- **R2-3(b): fixed in fix2.** Registration refuses a terminated session atomically.
  - `RegisterClientAsync` now returns `bool`. It runs in its own transaction, inside the context's
    execution strategy, so a host that configures retries still works.
  - After the rows are saved (a lost duplicate key rolls back to EF's savepoint and retries as
    before), it runs `UPDATE Sessions SET LastSeenUtc … WHERE Sid = @sid AND TerminatedUtc IS NULL`
    in the same transaction. On 0 rows it rolls back, forgets the rows it tracked and returns
    `false`. The factory then answers `login_required` ("The session has ended; sign in again.").
  - The update replaces the old tracked `LastSeenUtc` write. It always issues, so it always takes
    the session row's lock, even when the clock value is unchanged.
  - `TerminateAsync` and `TerminateAllAsync` now write `TerminatedUtc` first and read the
    registrations after that save (a shared `ReadRegistrationsAsync`). A registration therefore
    either committed before the terminate's write took the session row, and is read, or it sees
    the end and is refused.
  - Two tests, both red on 322845ef:
    - `A_grant_whose_session_a_logout_ends_while_it_registers_the_client_gets_no_code`: the
      `SessionEnder`, now parameterized by its statement, sets `TerminatedUtc` from another
      connection when the registration's save is about to run. On fix1 a code was issued.
    - `A_logout_that_waits_on_a_grants_registration_revokes_the_authorization_it_registers`
      pins the terminate's read order.
      1. A `DbTransactionInterceptor` holds the registration's commit with its rows written and its
         locks taken.
      2. The end-session confirmation is posted. The test waits on `sys.dm_exec_requests` until the
         logout's `SET [TerminatedUtc]` waits on that lock, then releases the commit.
      3. The grant gets its code, and the logout revokes its authorization.
      - On fix1 the logout had already read the registrations, so the authorization stayed
        "valid".
  - A refused grant's authorization, created before the registration, stays valid but registered
    nowhere, and no token was issued from it. A later grant that it covers registers it in that
    grant's own session.
  - R2-3(a), the dropped lost revoke, is PC-F89 and was left.
- **Gates (fix2).**
  - `dotnet build Tellma.slnx`: "Build succeeded", 0 warnings, 0 errors.
  - `dotnet format Tellma.slnx --verify-no-changes`: clean. The only output was the usual
    workspace-loading warnings line.
  - Identity unit tests (whole project): 1360 passed.
  - Identity integration tests, filtered to the classes R2 ran (ConcurrentGrant, Migrations,
    StoreCheck, BackchannelLogout, EndSession, SessionPrune, Consent, DeviceFlow, Revocation,
    SecurityStampRefresh, AuthorizationCodeFlow, TenantResource, DocumentClient, ParAndPkce,
    ResourceIndicator, AssurancePolicy, TokenExchange, Userinfo): 100 passed, 0 failed.
  - Acme integration tests, filtered to `MigratorTests` and `WebHostTests`: 14 passed.
  - `ConcurrentGrantTests` run 20 times: 20 of 20 runs green, each with all 11 tests passing.
  - Red runs:
    - On 322845ef's product code: the upgrade test, the logout-race test and the logout-wait test
      failed. The other 12 tests in the run passed.
    - Under the three R2-2 mutations: all 3 tests failed.
