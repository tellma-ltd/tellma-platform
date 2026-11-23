# PC-F72: Root-cause the ScheduleTests take-over flake

## Implementer notes

### The failure

`Tellma.Core.IntegrationTests.Jobs.ScheduleTests.A_schedule_paused_for_its_inactive_owner_is_taken_over_then_activated_and_its_notice_withdrawn_with_the_commit`
failed once on the ubuntu integration job of PR run 45044779464 (job 137276870221, commit
08ebc1cc) with `Assert.True() Failure` at ScheduleTests.cs:516. Line 516 was:

```
Assert.True((await StateAsync(id)).Get<DateTimeOffset>("NextDueAt") > await DatabaseNowAsync());
```

(The second frame, line 517, is only the async state machine's next sequence point.) The job log
stamps the `[FAIL]` line at 16:43:00.070 UTC, and the test ran for 357 ms: it ran across the turn
of the minute 16:43:00.

### Root cause: a minute boundary between two different clocks' reads (the test was wrong)

The schedule is inserted with the default expression `* * * * *`. The activation computes its next
due time in C#, as spec 0020 §7.8 requires: `next = GetNextOccurrence(RequestContext.Now, zone)`,
"the one place the application clock enters". `RequestContext.Now` is not the activation's own
instant: `RequestContextHolder` stamps it on the scope's first read and keeps it for the scope's
lifetime, and `JobRunDatabase.UserScopeAsync` reads it while building the scope. So `Now` is the
instant the test opened its scope (line 499), before the refused activation, the take-over, their
reads and the real activation.

With an every-minute expression, `NextDueAt` is the first whole minute after that instant. The
assertion then compared it with a fresh read of the database clock after all of that work. It
fails exactly when a minute boundary falls between the scope's creation and the final
`DatabaseNowAsync()`: `NextDueAt` is that boundary, and the database clock has passed it. The window
is the few hundred milliseconds of the test body, so on a fast runner it fails about once in a few
hundred runs (window / 60 s), which matches one red run among many green ones of the same code.
No ordering, shared fixture state, background job or xUnit order hash is involved: the fixture
resets before each test, and the schedule is the test's own row.

The product is right: the spec fixes the request's instant as the base, and a next due time that a
commit lands just after is still an occurrence after the activation was requested, which the next
tick fires (no paused period is caught up). The test asserted a stronger property (due after the
database's clock at check time) that the spec never promises.

### Proof

Two temporary probes (not committed), run against the original code with the original predicate:

- Forced interleaving: the same steps, with the scope's context re-set to `Now` = 1 ms before the
  database's current minute began, i.e. a request that started 1 ms before the boundary. Result:
  `requestNow=22:48:59.999 next=22:49:00.000 dbAfter=22:49:51.576`, original predicate `false`,
  deterministically; the new assertion passed.
- Natural timing, no manipulation: wait on the database clock until second 59, ms >= 920, then open
  the scope and run the original steps. Result: `requestNow=22:49:59.928 next=22:50:00.000
  dbAfter=22:50:01.956`, original predicate `false`; the new assertion passed.

Log: the probe run's output was in the shared scratch folder as PC-F72-probe1.log.

### The fix (test only)

- The assertion now holds `NextDueAt` to exactly the expression's next occurrence after the
  scope's `RequestContext.Now`, read through `IRequestContextAccessor`. That is the spec's rule
  itself, it is strictly stronger than the old `> now` (it pins the value, and implies it is after
  the request's instant), and it no longer depends on when the check runs.
- Same failure class, same file: `An_uninitialised_built_in_takes_its_next_due_time_without_firing_and_once_due_fires_as_the_platform`
  computed the expected initialisation from a database-clock read taken before the tick, while the
  tick initialises from its own read of the database clock. It would fail if 01:00 in the tenant's
  zone (`BuiltInCron = "0 1 * * *"`) fell between the two reads. It now reads the clock before and
  after the tick and requires the next occurrence after one of them, the same single value unless
  the occurrence fell between them. That race is far rarer (a ~50 ms window once a day) but just as
  real.
- No other assertion in ScheduleTests compares an application-clock-derived value with a later
  database-clock read; the pinned-minute tests (`PinnedMinutesAsync`) were already written for
  boundary crossings, and the `InRange(before, after)` checks are bracketing reads.

### Deviations

None from the spec. No product change.

### Open risks

- The other Jobs suites compare `DueAt` with `DateTimeOffset.UtcNow ± 1 minute`; they tolerate
  boundaries but assume the app and database clocks agree within a minute (true for a local
  Testcontainers SQL Server).

### What reviewers should examine hardest

- That `IRequestContextAccessor.Current.Now` read after the activation is the instant the
  activation used (the holder stamps once per scope; nothing in the activation path re-sets `Now`).
- That the built-in test's two-candidate check cannot pass a wrong value: the expression is daily,
  so the candidates differ only when 01:00 fell between the reads.

## Review 1

Scope: commit 3899a16c, `test/core/Tellma.Core.IntegrationTests/Jobs/ScheduleTests.cs` only (no
product change). Verified against spec 0020 §7.3, §7.4, §7.5, §7.8, §7.9 and the code paths the
assertions depend on.

What I checked and found sound:

- Root cause proven. The CI log of job 137276870221 stamps `[FAIL]` at 2026-11-22T16:43:00.0700Z
  for a 357 ms test, so the scope opened at about 16:42:59.71. `ScheduleService.ContributeAsync`
  (src/core/Tellma.Core/Jobs/ScheduleService.cs:158) takes `context.Context.Now`.
  `RequestContextHolder` stamps `Now` once, through `RequestContextDefaults.Tenantless()`, and the
  only constructor that sets `Now` is `RequestContext.Tenantless`. `JobRunDatabase.UserScopeAsync`
  and `RequestContextInitialization.RunAsync` only use `with`, so `Now` survives every `Set`.
  `IRequestContextAccessor` resolves to the same scoped `RequestContextHolder`
  (TenancyComposition.cs:68). The implementer's probe log shows the old predicate false in both the
  forced and the natural interleaving, with the new assertion passing.
- The new activation assertion (ScheduleTests.cs:528-530) is deterministic. Product and test both
  call Cronos's exclusive `GetNextOccurrence` on the same `Now`, and a whole-minute result survives
  `datetimeoffset(3)` exactly. It is stronger than `> DatabaseNowAsync()`: it still fails when the
  value is computed from the stale `NextDueAt`, from `LastScheduledFor` or from `default`, and it
  now also fails when the value lands in a later minute, which the old check passed. If the product
  regressed to a later clock read (the database clock, or `UtcNow` at activation), the new check
  would fail at minute boundaries, so it exposes that regression instead of hiding it.
- The either-read check in the built-in test (ScheduleTests.cs:78-90) is not vacuous. The tick
  takes `Now` from `SYSUTCDATETIME()` cast to `datetimeoffset(3)` (§7.4; ScheduleOccurrences.cs:119
  reads `row.Now`), and `DatabaseNowAsync` applies the same cast, so before <= tick <= after holds.
  `0 1 * * *` yields two distinct candidates only when 01:00 in the tenant's zone falls between the
  reads. At every other time the set has one element and the check is the old `Assert.Equal`.
- Sweep of Jobs and Notifications. Only ScheduleTests uses Cronos. Its other derived-time checks are
  safe: pinned minutes (`PinnedMinutesAsync`) tolerate a tick in a later minute, `InRange(before+1h,
  after+1h)` brackets the reads, the Riyadh save pins only H:M:S, and the stale `> minute` check
  holds by construction. The other suites compare `DueAt` with `UtcNow ± 1 min` or use
  second-to-day offsets. None of those rounds to a minute or a day boundary, so none has this race.
- No retry, timeout raise, skip or quarantine (D-011). Inline comments cite the spec only in code
  comments; test names are unchanged.
- Tests run. The implementer's logs show Jobs and Notifications at 251/251 three times, the
  solution build with 0 warnings and 0 errors, and format clean. I rebuilt
  Tellma.Core.IntegrationTests (Build succeeded, 0 warnings) and ran
  `FullyQualifiedName~Tellma.Core.IntegrationTests.Jobs.ScheduleTests` twice: 21/21 each. CI has
  not run on this commit.

### Findings

R1-1 (minor, test adequacy). ScheduleTests.cs:529. The activation assertion recomputes the next
due time "in the tenant's zone", but `* * * * *` returns the same instant in every zone. So a
regression of the activation path's zone handling would still pass: for example
`ScheduleCron.TenantZone` falling back to UTC, or `Next` ignoring the schedule's `TimeZoneId`. The
`TenantZoneAsync()` term reads as coverage that does not exist. The old assertion had the same gap,
so this change does not weaken anything. Failure scenario: `ContributeAsync` passes
`TimeZoneInfo.Utc` instead of `tenantZone` on activation, and every Jobs test stays green. The only
zone check on the stack path is the save test's explicit `Asia/Riyadh`
(ScheduleTests.cs:443-451). Suggested fix: in this test, before the activation, set the schedule's
expression to a zone-sensitive one, for example `0 3 * * *` with an explicit non-UTC `TimeZoneId`,
or a tenant zone with a non-zero offset. Keep the same exact-equality assertion against `requested`.
Alternatively, drop the zone term from the comment and leave zone coverage to a separate
activation test. Either way it stays test-only.

### Responses

R1-1: fixed in the "PC-F72: review 1 fixes" commit (child of 3899a16c), per the orchestrator's
ruling. The test's schedule now runs `0 3 * * *` with no zone of its own. Before the tick, the
test moves the tenant's zone to `Asia/Riyadh` and moves the settings tag, so the activation's
request reads the new zone. The assertion stays exact: `NextDueAt` equals the expression's next
occurrence after the scope's `RequestContext.Now`, evaluated in `Asia/Riyadh`. A guard asserts
that the same evaluation in UTC gives a different instant, so the test stays zone-sensitive. It
is still free of the boundary race, because both sides use the request's instant. The fixture's
reset restores the seeded `UTC` zone and moves the settings tag only when a test changed the
zone, so the other tests keep their baseline (`JobRunDatabase.TenantZone`).

Wrong-zone probe (temporary, reverted): `ScheduleService.ContributeAsync` was changed to pass
`TimeZoneInfo.Utc` instead of the tenant's zone on activation. The test then failed at the
equality: expected 2026-11-24T00:00Z (03:00 Riyadh), actual 2026-11-24T03:00Z. With the product
restored, it passes.
