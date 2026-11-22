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
