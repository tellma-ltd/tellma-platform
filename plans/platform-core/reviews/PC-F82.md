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
