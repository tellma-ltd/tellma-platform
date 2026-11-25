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

### Responses
