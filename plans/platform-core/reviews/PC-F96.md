# PC-F96: HubConnectTests disconnect metric race

## Implementer notes

### What was built

Fix item from PC-F83's red CI 45567894634 (windows, on a10330ee; D-292). Test-only change in
`test/core/Tellma.Core.AspNetCore.Tests/Realtime/HubConnectTests.cs`,
`A_member_connects_and_is_tracked_counted_and_logged`.

**Root cause (D-011).** `TellmaHub.OnDisconnectedAsync` runs on the hub's threads after the client's
`StopAsync` returns. It removes the connection from `HubConnectionTracker` first, then records the
-1 on `tellma.realtime.connections`, then logs `HubDisconnected`. The test waited for
`tracker.Count == 0` and then asserted the -1 straight away, so a run that polled in the gap
between `Remove` and `RecordDisconnected` saw only the +1. It already waited for the log.

**Fix.** The close now awaits each of its three effects in its own right through
`WaitUntilAsync` (`Patience.UntilAsync`): the empty tracker, the -1 measurement (same predicate as
before: value -1, mode `in-process`), and the `HubDisconnected` debug log. Each wait stands alone,
so the test does not depend on the order in which the hub runs its statements. There are no
retries, no quarantine and no loosened assertion: the predicate is unchanged and it still fails on
a hang, at `Patience.Limit`.

**Connect side (clarity, not a race).** The connect half also waited on `Count == 1` and then
asserted the counts and the log. That was never racy, because `RealtimeTestHost.ConnectAsync`
returns only after `ConnectedProbe` (a hub filter wrapping the whole `OnConnectedAsync`) signals
the end of the connect, and that comes after `TryAdd`, `RecordConnected` and `HubConnected`. The
poll implied a dependency that does not exist, so it is now a plain `Assert.Equal(1, tracker.Count)`
with a comment saying why the asserts are safe. Two tracker tests already assert `Count == 1`
right after `ConnectAsync` the same way (HubConnectionTrackerTests lines 216 and 441). A third,
at line 92, still waits on `Count == 1` after `ConnectAsync`; that wait is redundant and harmless,
not a race, so it is left as it is.

### Proof that the race is gone

- With a temporary `Thread.Sleep(500)` between `tracker.Remove` and `metrics.RecordDisconnected`
  in `TellmaHub.OnDisconnectedAsync` (not committed), the old test fails with
  `Assert.Contains() Failure: Filter not matched` at line 49, the same failure CI saw, and the new
  test passes.
- Without the delay: the realtime unit classes
  (`FullyQualifiedName~Tellma.Core.AspNetCore.Tests.Realtime`, unit filter, 86 tests) passed
  20 runs out of 20, and the test alone passed 50 runs out of 50.

### Sweep of the sibling realtime tests (wait or effect, then an unguarded assert)

I checked every realtime test that asserts a metric, a log or the tracker after an asynchronous
step: `HubConnectTests`, `HubConnectionTrackerTests`, `HubDeliveryTests`,
`RealtimeCompositionTests`, `RealtimeAlertQueryTests`, `TellmaHubClaimsTests`,
`Defaults.Azure.Tests/AzureSignalRCompositionTests` and the live `AzureSignalRLiveTests`. No other
instance of the race exists:
- **Connect, admitted or refused by the tracker or the claims check.** `ConnectAsync` waits for
  `ConnectedProbe`, which completes after the hub's whole `OnConnectedAsync`. On a refusal the
  metric and the log come before `TellRefusedAsync` and `Abort`, so they come before the client's
  `NextAsync` and `ClosedAsync` too. Covers the claims-missing test over LongPolling (Count 0:
  never added).
- **Tracker closes** (`SessionsTerminatedAsync`, `TenantAccessRevokedAsync`, `OnStateChangedAsync`,
  `OnRemovedAsync`, and the store's `EndAsync` through `SessionTerminationNotifier`). Each test
  awaits the call, which awaits `TellAsync` and then runs `Close` (abort, `RecordClosed`, log)
  synchronously before it returns.
- **Refused invocations.** `TellmaJsonHubProtocol.TryParseMessage` counts the refusal before the
  framework dispatches and answers it.
- **Negotiate refusals.** `TenantMiddleware` counts before it rethrows.
  `HubRefusalMiddleware.Answered` runs after `next` but still inside the pipeline. The tests read
  the whole response (`SendAsync` defaults to `ResponseContentRead`, then `Problem.ReadAsync`), and
  TestServer completes the response body only after the application delegate returns. No source
  calls `Response.CompleteAsync`.
- **Events metric.** `ClientEventPublisher.PublishAsync` awaits `DeliverAsync`, which records after
  the send. `RealtimeTestHost.PublishAsync` awaits it.
- **Logs.** Serilog forwards to the providers synchronously (`writeToProviders: true`, no async
  sink). `CapturingLoggerProvider` is a `ConcurrentQueue`.

### Deviations

None. Test-only change; no spec section applies.

### Open risks

None known. If the hub ever records its -1 off the disconnect's path (fire-and-forget), the wait
still holds, since it polls for the measurement itself.

### What reviewers should examine hardest

- Whether the sweep missed a test that asserts after an un-awaited server-side step, especially
  the claim that `HubRefusalMiddleware.Answered` happens before TestServer completes the response
  body.
- Whether `ConnectedProbe` really wraps the whole `OnConnectedAsync`, which the connect half's
  switch from a wait to an assert relies on.

## Review 1
### Findings

None (0 blocking, 0 major, 0 minor).

What I checked, so the next round need not repeat it:

- **D-011.** The close half's -1 predicate is unchanged (value -1, mode `in-process`). It is now
  polled for itself instead of read once after `Count == 0`. There is no retry, no quarantine and no
  loosened assertion, and a hang still fails at `Patience.Limit`. The root cause matches the product
  code: `TellmaHub.OnDisconnectedAsync` (TellmaHub.cs:97-101) runs `tracker.Remove`, then
  `RecordDisconnected`, then `HubDisconnected`.
- **Connect-half claim.** `RealtimeTestHost.ConnectAsync` (RealtimeTestHost.cs:108-120) awaits
  `ConnectedProbe.WaitForTokenAsync`. The probe signals in a `finally` after `await next(context)`
  (RealtimeTestHost.cs:209-228), and the only hub filter registered anywhere is the probe, plus
  `ConnectProbe` in one unrelated test. So `next` is the hub's own `OnConnectedAsync`, which runs
  `TryAdd`, `RecordConnected` and `HubConnected` before it returns. The signal's TCS orders memory.
  To test the claim, I put a temporary `await Task.Delay(500)` before `RecordConnected` and another
  before `HubConnected`. The new test's plain asserts at HubConnectTests.cs:45-48 passed 3 out of 3.
  The claim holds, and switching from a wait to an assert adds no race.
- **Reproduction.** I added a temporary `Thread.Sleep(500)` between `Remove` and
  `RecordDisconnected` and ran the pre-fix test file. It failed 3 out of 3 with
  `Assert.Contains() Failure: Filter not matched in collection` at line 49, CI's failure. The new
  test, and the whole `Tellma.Core.AspNetCore.Tests.Realtime` namespace (86 tests), passed under the
  same delay. No sibling depends on the gap. Both edits are reverted, the project is rebuilt clean,
  and `git status` is clean.
- **Repeat runs (no delay).** The Realtime namespace passed 7 runs out of 7, 86 tests each. The fixed
  test alone passed 10 out of 10. `dotnet build` of the test project reported 0 warnings, and
  `dotnet format --verify-no-changes` on the changed file is clean. The file is LF with no BOM, like
  its parent.
- **Sibling sweep, re-checked class by class.**
  - **Tracker closes.** `Close` (HubConnectionTracker.cs:468-485) aborts, records and logs
    synchronously at the end of each awaited close method. `CatalogSessionStore.EndAsync` awaits
    `SessionTerminationNotifier`, which awaits each listener. So the asserts after `ClosedAsync` at
    HubConnectionTrackerTests.cs:73-76 and 286-289 are ordered. `ClientEventFailed` (:289, :311) and
    `HubSessionTenantsUnread` (:136) are logged inside the awaited call.
  - **Tracker counts.** `Close` never removes from the tracker; `OnDisconnectedAsync` does. So a
    count after an admitted connection's close would be this same race. Every `tracker.Count` assert
    in the sweep follows only refused connections, which `TryAdd` never added, or `ConnectAsync`
    admissions: HubConnectionTrackerTests.cs:170, 191, 216, 441 and HubConnectTests.cs:199.
    HubConnectionTrackerTests.cs:92 still waits on `Count == 1` after `ConnectAsync`. That wait is
    harmless and not a race. The implementer notes say the tracker tests "already assert Count == 1
    right after ConnectAsync", which is true of :216 and :441 only.
  - **Refused invocations, negotiate refusals and the events metric.** Confirmed as stated:
    TellmaJsonHubProtocol.cs:52-55 counts at parse time, and HubRefusalMiddleware.cs:30-36 counts
    after `next` but inside the pipeline. Serilog has `writeToProviders: true` and no async sink.

### Responses

None needed.
