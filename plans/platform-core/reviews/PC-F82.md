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
