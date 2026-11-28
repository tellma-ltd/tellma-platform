# PC-F28: deterministic reconciliation timer tests

## Review 1

Verdict: the root cause is right and removed by construction, not masked. No blocking or major
findings. One minor finding, in a sibling test this commit did not touch.

What I checked and found sound:

- **The ordering the tests rely on holds.** `HostedSchedule.DelayUntil` calls
  `Task.Delay(wait, time, token)`, which creates its timer on the `TimeProvider` synchronously.
  Both services call the `WaitingUntil` hook only after that call returns
  (`SessionSweepService.cs:47-49`, `MembershipReconcileService.cs:74-76`). So when the test reads a
  due time from the channel, the fake timer exists, and the previous run (and its writes to the
  store or rows) has already finished. Every `Advance` in `TimerTests` follows such a read, and
  the "nothing yet" assertions (`TimerTests.cs:51`, `:76`, `:87`) sit strictly before the armed
  due time. No real-time sleep is left in the file. The only real-time bound is the 10 s
  failure limit in `NextWaitAsync`.
- **The reconciliation test's `firstRun` is stable.** `SnapshotLoadedAt` is the registry's
  `ConfirmedAt`, which moves on every refresh. But it is stamped from the fake clock, which
  nothing moves between `TimerTests.cs:65` and the service's read. The host's own reconciliation
  instance writes to the host's `FakeReconcileStore`, not the test's, so leaving it in place does
  not interfere.
- **The schedule's semantics.**
  - Coalescing matches `PeriodicTimer`. For example, take a 15 min interval and a run that ends
    at 47: the next run is at once (45), then at 60. `PeriodicTimer` gives the same.
  - A timer firing a few ms early against the wall clock gives `passed = 0`, so the next run is
    one interval on and there is no double run.
  - The first reconciliation still runs at the first snapshot + 5 min, then every interval, as
    spec 0011 §3.8 requires. The grid now stays on the first due time instead of drifting by
    each run's length, which is closer to the spec's cadence.
  - The sweep still runs every `SweepInterval` from start (§5.5). Spec 0011 does not prescribe
    `PeriodicTimer` for either service. D-066 (5) is unchanged: one run per instance per interval.
- **Clock steps.** Due times are now on the wall clock, while the delay itself is monotonic.
  - A backward step of Δ lengthens one interval by Δ.
  - A forward step of an interval or more (for example a resume on Linux, where the monotonic
    clock excludes suspend) causes at most one extra immediate run.
  - Both runs are idempotent, so neither is a defect. Old: the reconciliation's first wait was
    already wall-clock based.
- **Cancellation and errors.**
  - When the due time has passed, `DelayUntil` returns a completed task even if the token is
    cancelled. The run then receives the cancelled token and its `OperationCanceledException`
    is caught by the `when (stoppingToken.IsCancellationRequested)` filter. The loop also checks
    the token at its head.
  - Run failures are still caught inside `SweepOnceAsync` and `RunOnceAsync`.
  - Zero or negative intervals are refused by options validation, so `Next` cannot divide by
    zero.
  - The over-49-day `Task.Delay` limit applies as `PeriodicTimer`'s did, at the same point in
    each loop. That is not a regression.
- **API and AGENTS.md.**
  - `HostedSchedule` and both `WaitingUntil` hooks are `internal` on `internal` types, so the
    public API is unchanged.
  - XML docs are present, and section comments are inline.
  - No spec, PC, `plans/` or `docs/` reference appears in the code, the test names or the
    messages.
  - Both READMEs are updated.
- **Runs (this review).**
  - Build: 0 warnings, 0 errors.
  - `Tellma.Core.AspNetCore.Tests` unit tier: 465/465.
  - `TimerTests`: 8/8 sequential runs, then 15/15 runs in 5 parallel lanes. Each run is 5/5.
  - Logs are in `.tmp/pcf28-r1-*`.

### Findings

**R1-1 (minor). A sibling test keeps the lost-`Advance` race this commit fixes, and can hang
instead of failing.**
- `test/core/Tellma.Core.AspNetCore.Tests/Sessions/SessionLifecycleTests.cs:553-560`, in
  `An_identity_server_that_does_not_answer_is_given_up_on_at_the_redemption_s_deadline`.
  - The test polls until `Redemptions > 0`, makes one `host.Time.Advance(Timeout + 1 s)`, then
    awaits `pending` with no real-time bound.
  - The redemption's deadline is armed before the request reaches the fake server
    (`OidcTokenRedeemer.cs:51`), so it fires. Nothing orders the request's own wait,
    `running.WaitAsync(FlightWait, time)` (`SessionRefresher.cs:175`), before that `Advance`.
    The flight runs on another thread (`Task.Run`, `SessionRefresher.cs:160`). The request
    thread arms its wait only afterwards, and can be descheduled in between on a starved runner.
- Failure scenario:
  - The request's wait is armed after the single `Advance`.
  - The stalled redemption times out as Indeterminate, so the flight's retry pause
    (`Task.Delay(RetryPause, time)`, `SessionRefresher.cs:246`) is also armed after the move.
  - Nothing ever moves the clock again. Neither the flight nor the request completes, and
    `await pending` hangs until the run's timeout.
  - The window is narrow, but it is the same class of bug as the CI failure behind this item.
- Suggested fix: do what the neighbouring refresh tests already do.
  - Replace the single move with
    `await AdvanceUntilAsync(host, () => pending.IsCompleted, OidcTokenRedeemer.Timeout + TimeSpan.FromSeconds(3))`.
  - Await `pending.WaitAsync(RealTimeLimit, TestContext.Current.CancellationToken)`.
  - Optionally, replace the `while (Redemptions == 0) await Task.Delay(10)` loop with the
    bounded `UntilAsync`.

### Responses

- R1-1: fixed in the review 1 fix commit (see `git log`; the fix commit cannot cite its own sha).
  - The test now waits for the redemption with the bounded `UntilAsync`. It then moves the clock a
    second at a time with `AdvanceUntilAsync` until `pending` completes, up to
    `OidcTokenRedeemer.Timeout + 3 s`. It awaits `pending` bounded by `RealTimeLimit`.
  - A request wait or retry pause armed after one move is released by the next move. A regression
    now fails within the real-time limit instead of hanging.
  - Scan of the rest of `SessionLifecycleTests`:
    - Every other single `Advance` comes before the request it affects, so no timer is in flight
      when the clock moves.
    - The fixed-span `AdvanceAsync` steps are already 1 s with a bounded await on `pending`.
    - The two concurrency tests' `Task.WhenAll` awaits were unbounded and are now bounded by
      `RealTimeLimit`.
