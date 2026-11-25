# PC-F89: Bounded retry, log and audit for a lost back-channel revoke

## Implementer notes

### What was built

Fix item from PC-F82 R2-3(a) (D-279). `BackchannelLogoutService.NotifyAsync` dropped a revoke
that `TryRevokeAsync` reported as failed (`false`): no log, no audit row, no retry. A logout that
raced a grant extending the same authorization could leave the authorization valid, so its refresh
tokens kept renewing.

- `src/apps/Tellma.Identity/Services/BackchannelLogout/BackchannelLogoutService.cs`
  - New private `RevokeAsync(subject, registration, authorizationId, ct)`, called once per
    authorization id from `NotifyAsync` (the `UsedAuthorizationIds` loop is unchanged).
    1. `FindByIdAsync`. If the authorization is gone, return (as before: nothing left to renew).
    2. `TryRevokeAsync`. On `true`, audit `TokenRevoked` / `success` and return.
    3. On `false`, detach the refused copy from the request's `TellmaIdentityDbContext`
       (`Entry(authorization).State = Detached`).
    4. At attempt `MaxRevokeAttempts` (3, `internal const`, the same bound as the grant side's
       `MaxAuthorizationWriteAttempts`), log a Warning (`GaveUpRevoke`) and audit `TokenRevoked` /
       `failure`, then return. The logout goes on: other authorizations and the deliveries are
       unaffected. Otherwise log Information (`LostRevoke`) and loop: the re-read loads the
       authorization as the winning writer left it, and the next revoke is made over that.
  - The service now takes `TellmaIdentityDbContext` (scoped, the same instance the OpenIddict EF
    stores use, exactly as `TellmaPrincipalFactory` does). No DI registration change was needed.
  - Both audit rows (success and failure) now carry `DetailsJson`
    `{"authorizationId": "...", "attempts": n}`. Before, the success row had no details, so two
    revoked authorizations under one registration produced indistinguishable rows; the failure row
    needs the id so an operator can find the authorization left valid.
  - No retry delay: a refused write means a newer version was already committed, so an immediate
    re-read sees it (the grant side retries the same way).
- `src/apps/Tellma.Identity/README.md`: the session-registry paragraph describes the retry, the
  log, the failure audit and the new details.
- `test/apps/Tellma.Identity.IntegrationTests/Flows/ConcurrentGrantTests.cs`
  - `AuthorizationWriteSaboteur` is generalized: a target predicate (`EveryUpdate`,
    `CliRevocations`), how many targeted saves it overtakes (default all), and an optional resource
    the "other writer" appends to `tellma:resources` with `JSON_MODIFY`, as a concurrent extend
    would. It still writes from its own connection just before the save, so the race is staged
    deterministically: no sleeps, no retries in the test. The existing gives-up grant test passes
    `EveryUpdate` and is otherwise unchanged.
  - `A_logout_whose_revoke_loses_to_a_grant_revokes_the_authorization_as_that_grant_left_it`:
    the first revoke of the CLI's authorization is overtaken by an extend to tenant 18. Asserts two
    attempts, the authorization revoked and the refresh token dead, the authorization still covering
    tenants 17 and 18 (the second write was made over the winner's write, not the stale copy), and
    one `success` audit row with `attempts: 2` and the authorization id.
  - `A_logout_whose_revoke_keeps_losing_logs_and_audits_the_authorization_it_left_valid`: every
    revoke of the CLI's authorization is overtaken. Asserts three attempts, the authorization still
    `valid`, one `failure` audit row (subject, session, authorization id, `attempts: 3`), one
    Warning naming the authorization, and that the logout went on: the `acme` distribution's
    authorization in the same session was revoked (`success`, same sid).
  - The saboteur's revoke predicate is scoped to the CLI because `ConsentTests.SignInAsync` signs in
    through `acme`, which registers a second client and authorization under the same session (my
    first run counted 6 attempts for that reason).

### Why the detach is essential (verified against OpenIddict 7.5.0)

Decompiled from the 7.5.0 package and read in the openiddict-core source:

- `OpenIddictAuthorizationManager.TryRevokeAsync` returns `true` at once when the status is
  already `revoked`. Otherwise it sets the status and calls `UpdateAsync`. It returns `false` on
  `ConcurrencyException` and on any non-fatal exception, without restoring the status.
- The EF store's `UpdateAsync` sets a new `ConcurrencyToken` and, on `DbUpdateConcurrencyException`,
  only sets the entry to `Unchanged`.
- The EF store's `FindByIdAsync` returns an already tracked entity as it is, without querying.

So a naive retry gets the tracked copy back, sees `revoked`, and returns `true` without writing: a
false success. Mutation runs, with the two new tests:

- Detach removed: the lost-once test fails (3 attempts where 2 were expected). The stale copy was
  written again by change detection at the audit row's save. The comment at the detach records this.
- Original behaviour (return on the first `false`): both tests fail (1 attempt).

### Deviations

- Each `TokenRevoked` row from a logout now carries `authorizationId` and `attempts` details, the
  success row included. Neither spec 0003 nor the item asked for success-row details; it is an
  additive audit improvement.
- Spec and vocabulary: spec 0003 §15 lists revocation among the audited actions but defines no
  failure vocabulary. The row reuses the existing `TokenRevoked` action with outcome `failure`, as
  the revocation endpoint's audit already does, rather than adding a new action.

### Open risks

- A revoke that loses three times leaves the authorization valid. The warning and the audit row
  make it visible, but nothing revokes it later automatically. A deferred sweep (for example,
  revoking the authorizations registered under terminated sessions) would close that; none exists
  today. Three losses need three grants committing to the same authorization within one logout's
  read-to-write windows, so this is very unlikely in practice.
- `TryRevokeAsync` also returns `false` for a non-concurrency failure (for example, a SQL timeout).
  That is retried the same way, and after three it is audited as a failure too. The manager has
  already logged the cause (Warning 6166).

### Where reviewers should look hardest

- The detach in `RevokeAsync`: the entity entry, the included `Application` (left tracked, not
  modified), and whether anything else on the logout's context could re-flush the refused copy.
- The ordering: detach before the failure audit's save.
- The saboteur's `CliRevocations` predicate relies on `Application` being loaded. `FindByIdAsync`
  includes it, and a tracked application fixes it up on the re-read. If it were not loaded, the
  saves would go through untargeted and the attempt assertions would fail loudly, not pass vacuously.

### Gates

- `dotnet build Tellma.slnx`: "Build succeeded", 0 warnings, 0 errors.
- `dotnet format Tellma.slnx --verify-no-changes`: clean. The only output was the usual
  workspace-loading warnings line.
- Identity unit tests (whole project, unit filter): 1360 passed.
- Identity integration tests, filtered to ConcurrentGrantTests, BackchannelLogoutTests,
  EndSessionTests, RevocationTests and SessionPruneTests: 24 passed, 0 failed. The two new tests
  were also run alone and under the two mutations above.
- No migration, no nightly-tier tests.

## Review 1

### Findings

Scope checked: the PC-F89 row (CHECKPOINTS.md, LEDGER.md), D-279 R2-3(a), spec 0003 §7.3 and §15,
PLAN.md §6-§7, and the diff of 6cc0fad8. WATCHLIST.md has no section for spec 0003.

Verified, with no finding:

- **The detach is needed, and it is sufficient under OpenIddict 7.5.0.** I decompiled the 7.5.0
  packages myself.
  - `OpenIddictEntityFrameworkCoreAuthorizationStore.FindByIdAsync` returns a tracked entity from
    `ChangeTracker.Entries<TAuthorization>()` without querying.
  - `UpdateAsync` does `Attach`, a new `ConcurrencyToken`, `Update` and `SaveChangesAsync`. On
    `DbUpdateConcurrencyException` it only sets the entry `Unchanged` before it throws
    `ConcurrencyException`.
  - `OpenIddictAuthorizationManager.TryRevokeAsync` returns `true` at once for a `revoked` status.
    It returns `false` on `ConcurrencyException` (Debug 6165) and on any non-fatal exception
    (Warning 6166), and it does not restore the status.
  - The manager's scoped entity cache (`OpenIddictAuthorizationCache`) is not a second source of the
    stale copy. `Manager.UpdateAsync` calls `Cache.RemoveAsync` before `Store.UpdateAsync`, which
    cancels the id's expiration signal, so the re-read misses the cache and reaches the store.
  - The one path that skips that eviction is a `ValidationException` raised before it. That path
    cannot be reached here, because the status is non-empty and the type is unchanged.
- **Nothing else tracked is disturbed.** EF Core 10.0.11's `NavigationFixer.StateChanged` runs fixup
  only when an entry leaves `Detached`, or goes from `Added` or `Deleted` to `Detached`. So
  `Unchanged` or `Modified` → `Detached` touches no other entry.
  - The included `Application` stays tracked and `Unchanged`. Its `Authorizations` collection and
    the relationship snapshot both keep the detached instance, so `DetectChanges` sees no
    navigation change.
  - The two new tests show this in practice: the re-read and later saves on the same context
    neither raise an identity conflict nor re-attach the stale instance.
  - The service's `TellmaIdentityDbContext` is the store's context: both are scoped, and the service
    is `AddScoped`, with OpenIddict on `UseDbContext<TellmaIdentityDbContext>()`.
- **The audit save cannot re-write the stale copy.** `SqlAuditLogger` saves on the same context.
  The detach comes before both the failure audit and the next attempt, so the refused entity is no
  longer tracked when that save runs.
- **Bound, levels and templates are correct.**
  - The bound is 3: a failure is audited on attempt 3 only, and attempts 1 and 2 log `LostRevoke`
    at Information.
  - `GaveUpRevoke` logs at Warning and names the authorization, client and session.
  - Neither template cites a spec or PC id.
- **Audit row is correct.** It reuses `TokenRevoked` with outcome `failure`, as the revocation
  endpoint does (`AuditTokenResponseHandler.cs:249-261`). It carries subject, client and sid,
  which matches §15's sub, client_id and correlation id, and it has `DetailsJson`
  `{authorizationId, attempts}`. No code reads `TokenRevoked` details, so the new success-row
  details break nothing.
- **Saboteur equivalence holds.** With `EveryUpdate` and `overtakes = int.MaxValue`, every save
  that has a modified authorization is counted and overtaken, as before. With `@resource` NULL,
  `Properties` is unchanged. The new `Assert.Equal(1, rows)` is only stricter. The race is staged
  deterministically from the interceptor's own connection, with no sleeps.
- **The lost-once test is discriminating.** Its `[17, 18]` assertion shows that the second write
  was made over the winner's write. The refresh assertion is meaningful because a single-session
  logout does not bump the security stamp.
- **Tests rerun.** I built `Tellma.Identity.IntegrationTests` ("Build succeeded", 0 warnings) and
  ran it filtered to `ConcurrentGrantTests|BackchannelLogoutTests` with
  `Category=Integration&Category!=Nightly&Live!=true`: 15 passed, 0 failed. The pc/PC-F89 CI run
  (45632923032) was skipped, as it is before integration.

R1-1 (minor): the gives-up test's "the logout went on" assertion does not show that the logout
continued past the revoke it gave up on.

- Where: `test/apps/Tellma.Identity.IntegrationTests/Flows/ConcurrentGrantTests.cs:425-429`
  (`A_logout_whose_revoke_keeps_losing_logs_and_audits_the_authorization_it_left_valid`).
- Why the assertion proves nothing: `NotifyAsync` revokes registrations in the order
  `SqlSessionRegistry.ReadRegistrationsAsync` returns them, and that query has no `ORDER BY`. With
  the `(Sid, ClientId)` key, `acme` normally comes back before `tellma-cli`, so the `acme`
  authorization is revoked before the CLI's revoke fails.
- Failure scenario: a regression that ends `NotifyAsync` after a gave-up revoke would still pass
  this test. For example, a `return` or `break` out of the revoke loop would skip the remaining
  revokes and every back-channel delivery.
- Suggested fix: assert something that runs only after every revoke. Seed the distribution with a
  `backchannelLogoutUri` (`TestData` already takes one, and `BackchannelLogoutTests` has the
  receiving harness), then assert its `BackchannelLogoutSent` audit row or the receiver's hit.
  Deliveries start only after the whole revoke loop, so this holds whatever the registration
  order. If you would rather not, reword the comment so it claims only that the other client's
  revoke was unaffected.

### Responses

- **R1-1: fixed in fix1 (the test, not the comment).**
  - `ConsentTests.SignInAsync` takes an optional `backchannelLogoutUri`, passed to
    `TestData.ProvisionDistributionAsync`. Other callers are unchanged (the default is none).
  - The gives-up test seeds `acme` with `https://acme.app.tellma.com/backchannel-logout`. After the
    logout it asserts one logout token at the recording handler, and one `BackchannelLogoutSent`
    `success` row for `acme` under the same sid. Deliveries start only after the whole revoke
    loop, so this holds whatever order the registrations come in.
  - The `acme` revoke assertion stays. Its comment now claims only that the other client's revoke
    was unaffected.
  - A new helper `AuditsAsync(factory, action, clientId)` backs both this assertion and
    `RevocationAuditsAsync`.
  - Mutation: `RevokeAsync` returns `false` on giving up, and `NotifyAsync` returns on `false`.
    The build succeeded and the test failed at `Assert.Single(factory.BackchannelLogouts.LogoutTokens)`
    (the collection was empty). Under that mutation the `acme` revoke row was still written, which
    confirms the finding. The code was restored with `git checkout` and the restored build re-run.
  - Gates for fix1 are listed in the commit message.
