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
with a comment saying why the asserts are safe. The tracker tests already assert `Count == 1`
right after `ConnectAsync` the same way.

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
### Responses
