# PC-F65: Load-starved fixed waits in Core.AspNetCore.Tests

## Implementer notes

### Root cause

On a loaded machine the suite's fixed wall-clock waits measure how long the host takes to do its
own work, and that work grows 10 to 40 times. They do not measure anything the tests pin. Times
were taken with a temporary probe test (deleted before commit) on a warm process. "Loaded" means
three looping copies of the whole assembly plus whatever the other lanes ran:

| Step | Idle | Loaded |
|---|---|---|
| Build a `TellmaTestHost` | 2-3 s | up to 32 s |
| Start it | ~2 s | 13-15 s |
| Its first request (cold: front door, prologue, guard, first JIT of each path) | 1-1.2 s | up to 7.3 s |
| A second request | 70-100 ms | 190-314 ms |

Every fixed wait sat inside these stretches:

- 10 s bounds on "the endpoint started" (`RealtimeTestHost.Patience`, the handler-start waits in
  RateLimitingTests and RequestCancellationTests, the redemption joins in SessionLifecycleTests).
- 30 s "real-time limits" over fake-clock scenarios.
- A 200 ms `Task.Delay` used as "the other requests have joined the flight by now".
- A clock advanced one second at a time with `Task.Delay(25)` between steps, as if 25 ms were
  enough for the released work to run.
- Lowered real request timeouts (300 ms, 1 s) that started at the timeout stage, before the cold
  front door. So they could fire before the endpoint ran, while the test expected them during it.
- The SignalR client's default 15 s handshake and 30 s server timeouts, and the hub's 15 s handshake.
- A `Stopwatch` comparing the tracker's close with `2 × SendBound`, which measures the runner as much as the overlap.

Two failures were not timeouts. Both were orderings the load exposed:

- `RealtimeTestHost.ConnectAsync` read `HubConnection.ConnectionId` after `StartAsync`. A
  connection the hub refuses is closed as soon as its connect ends. Once the client has seen that
  close, its id reads null, and the probe threw `ArgumentNullException`.
- The stalled-send test (rewritten here) read the ids after the close, for the same reason.

### Fix

Two shared helpers in `Tellma.Testing.WebHost`:

- **`Patience`.** A single 2-minute limit that only ends a hang. Every wait for something a correct
  host does on its own uses it: `WaitAsync(task)`, `UntilAsync(condition, what)`, and
  `SignalledAsync(signal, request)`. The last fails at once, naming the request's outcome, if the
  request ends before the endpoint signals, so a broken endpoint does not wait out the limit.
- **`TestClock`.** A `FakeTimeProvider` that also records the timers armed on it: each one's next
  due instant, plus a running arm sequence. `AdvanceToAsync(mark, due, count)` waits until `count`
  timers armed after `mark` are due exactly at `due`, then moves the clock there. It returns a mark
  taken before the move, for the next step. So a move never lands before the wait it is meant to
  end, however slowly the host arms it. `TellmaTestHost.Time` is now a `TestClock`. Its public
  surface is a superset, so the Azure suites are untouched.

Every wait changed, and why:

| Where | Before | After | Why |
|---|---|---|---|
| RealtimeTestHost `Patience` constant | 10 s | removed; `Patience.Limit` | handshake waits only bound a hang |
| `ConnectedProbe.WaitAsync`, `HubClient.ClosedAsync` | `WaitAsync(10 s)` | `Patience.WaitAsync` | same |
| `HubClient.NextAsync` | `WaitAsync(10 s)` | read cancelled at `Patience.Limit` (linked CTS) | a timed-out `WaitAsync` left the read pending, and it would take a later event |
| `HubConnection.HandshakeTimeout` / `ServerTimeout` (client), `HubOptions.HandshakeTimeout` (server) | 15 s / 30 s / 15 s defaults | `Patience.Limit` | not under test; a cold handshake on a loaded runner took seconds |
| `RealtimeTestHost.ConnectAsync` | waited on `ConnectionId` read after `StartAsync` | waits on the connection token taken from the WebSocket's connect URI; the probe also signals by the server-side `id` query | a refused connection's id reads null once the client saw the close (ArgumentNullException before the fix) |
| HubConnectionTrackerTests stalled sends | `Stopwatch` < `2 × SendBound` | `RecordingLifetimeManager` records each stalled send with whether every earlier one still waited on its bound; the test asserts the second started while the first waited, and both bounds fired | the overlap is proven by the sends' own tokens, not by elapsed time; ids taken while the connections are open |
| HubConnectTests probe waits, `WaitUntilAsync` | `WaitAsync(10 s)`, 20 ms poll under a 10 s CTS | `Patience.WaitAsync`, `Patience.UntilAsync` | hang bounds only |
| RateLimitingTests report test | a 10 s handler-start wait; the permit released before the batch was sent; default 30 s web timeout | `SignalledAsync` on per-lane start signals (interactive and batch); release in `finally`; RequestTimeout/LongRequestTimeout lifted to 1 h for this host | the held report must outlive the test's other requests, and the batch now runs while the interactive permit is still held; the web timer is not what it pins |
| RequestTimeoutTests 504 test | a 300 ms timer expected to fire while the endpoint waited; 10 s wait on the cancellation | split in two: the lowered 300 ms timer kept where the answer is the same wherever it fires (nothing written), plus one that fires the timeout from inside the endpoint (`FireTimeout`, the reflection seam D-155 accepted) and asserts the data access is cancelled | the cold front door could outlast 300 ms, so the timer fired before the endpoint registered |
| RequestTimeoutTests connection-loss tests | 10 s `WaitAsync` on the send; failure type checked after | `SignalledAsync(finished, sent)`, then the send's failure under `Patience` | "Expected not TimeoutException, actual TimeoutException" was the test's own 10 s bound expiring on a slow abort |
| RequestTimeoutTests long-endpoint test | Long policy at 1 min | Long lifted to 1 h | only has to outlast the whole request |
| RequestTimeoutTests cut-short test | 10 s read bound | `Patience` read; asserts the failure is not the bound's `TimeoutException` | a read that only waits out the bound is a hang, not an abort |
| RequestCancellationTests | 10 s handler-start and cancellation waits; default 30 s web timeout | `SignalledAsync`, `Patience`; web and long timeouts lifted to 1 h for this host | the blocked read is ended by the client alone |
| SessionLifecycleTests single-flight tests (2) | `Task.Delay(200)` then open the gate | `JoinedAsync`: wait until `TestClock` holds the 8 requests' flight waits plus the held redemption's deadline, all due at `start + FlightWait` | the gate opens once every request has joined, which is what the test pins |
| SessionLifecycleTests deadline, retry and backoff tests (6) | clock moved 1 s at a time with `Task.Delay(25)`, `AdvanceUntilAsync` loops, 30 s real-time limit | `AdvanceToAsync` to each exact instant (`start + Timeout`, `+ RetryPause`, `+ RetryPause × attempt`) once its timers are armed; `SetUtcNow(start + leeway + 5 s)` for "past the leeway" | each step waits for the timer it is meant to fire, not for a guess at the host's pace |
| TimerTests `NextWaitAsync` | `WaitAsync(10 s)` | read cancelled at `Patience.Limit` | no pending read left to take a later wait |
| WebApiProjectionTests disposal | `WaitAsync(10 s)` | `Patience.WaitAsync` | hang bound only |
| InProcIdentityTests dispatch recorder | `WaitAsync(30 s)` | `Patience.WaitAsync` | hang bound only |

Audited and left as they are:

- The rate-limit windows (fixed, sliding, concurrency) are one minute, measured from the first
  request that reaches the limiter, so slowness inside a test cannot cross them.
- `HubConnectionTracker.SendBound` (2 s, real) is product code under test. The test no longer
  times it.
- The 200 ms/800 ms pair in the long-endpoint test: the endpoint's own delay, see the risks.
- Fake-clock-only tests (sweep, reconcile, memos) were already deterministic.

No timeout was raised as a fix, except where the wait only bounds a hang (the `Patience` limit)
or the timer is not the subject (the hosts that lift the web timeout). Nothing is retried or
quarantined (D-011, D-113).

### Verification (local, Windows, Debug)

The load came from three looping copies of the whole assembly, built at the branch base, so
they ran the old code. They ran from a frozen copy of the bin folder, so the slot could rebuild.
The other lanes' runs (pc-1, pc-3) loaded the machine on top. A full run took 26-58 min under
this load, against about 6 min idle.

**Before** (the base's code, under that load):

- The load copies' own full runs: 15 runs of 933 tests. 10 of them failed, with 25 failures
  across 8 tests:

  | Test | Failures | Message |
  |---|---|---|
  | RequestTimeoutTests `A_read_whose_answer_began_after_its_timeout…` | 6 | IsNotType: TimeoutException (the test's own 10 s bound) |
  | RequestTimeoutTests `A_write_sent_before_its_timeout_whose_large_answer…` | 5 | same |
  | RequestTimeoutTests `A_write_sent_before_its_timeout_whose_action_answer…` | 3 | same |
  | RateLimitingTests `A_refusal_is_counted_as_rate_limited…` | 4 | TimeoutException at the 10 s handler-start wait |
  | RequestCancellationTests `A_client_that_goes_away…` | 3 | TimeoutException (10 s) |
  | HubConnectionTrackerTests `A_stalled_send_delays_no_other…` | 2 | "The close took 00:00:19.82" |
  | SessionLifecycleTests `Requests_that_join_one_refresh…` | 1 | TimeoutException |
  | FrameworkProblemsTests `A_bare_status…(403)` | 1 | message not kept (see the open risks) |

  Even the later, lighter rounds (26-35 min a run, about the after runs' pace) failed 2 of 6.
- Seven of the touched classes plus the timing probe (96 tests), 5 rounds from the slot's bin under the same
  load: round 2 failed `A_write_sent_before_its_timeout_whose_action_answer…`, the same
  TimeoutException. It also failed `A_connection_joining_an_unavailable_tenant…(Active)` with
  ArgumentNullException in `ConnectedProbe.Signal`, the refused connection's null id.
- The other lanes, as reported to this item:
  - PC-089's R2 full run (pc-1) timed out at RateLimitingTests' 10 s handler-start wait (line
    152 there) and at BlobEndpointTests' first-head wait.
  - PC-090's integration full run (pc-3) failed `A_write_sent_before_its_timeout_whose_action_answer…`
    with the same IsNotType/TimeoutException at the 10 s send cap.
  - Each class passed alone.

**After** (this branch, same load, the three old-code copies still looping):

- Touched classes plus WebApiProjectionTests and InProcIdentityTests (169 tests): 1 round, green, 30 min.
- `Tellma.Core.AspNetCore.Tests` in full, unit filter (the assembly has no traits, so that is
  every test), three times in a row: 934/934, 934/934, 934/934 (36, 30 and 29 min).
- `Tellma.Defaults.Azure.Tests`: 19/19.
- `dotnet build Tellma.slnx`: Build succeeded, 0 warnings, 0 errors.
- `dotnet format Tellma.slnx --verify-no-changes`: exit 0; its only output is the usual
  workspace-load notice.
- The 934th test comes from splitting the 504 test in two.

### Carries (same pattern elsewhere, not changed)

- `test/defaults/Tellma.Defaults.Azure.AspNetCore.IntegrationTests`:
  - `AzureSignalRTestHost` waits `Task.Delay(1 s)` before its probe and calls
    `ConnectedProbe.WaitAsync(ConnectionId!)`, so it has the refused-connection null-id race.
  - `AzureSignalRLiveTests` uses a fixed 3 s `Settle`.
  - Both are live/integration and should move to `WaitForTokenAsync` and a signal.
- PC-089 (in flight), `BlobEndpointTests`: a 1 s RequestTimeout, `Task.Delay(200)` fake-clock
  pacing and 10 s bounds. It also has a fixed-bound first-head wait (`ReadHeadAsync`), which timed
  out under load in PC-089's R2 full run (see the evidence below). When it integrates, it should
  adopt `Patience` (`WaitAsync` or `SignalledAsync` for the head, `UntilAsync` for conditions) and
  `TestClock.AdvanceToAsync` for its deadline moves. It is not on this branch, so it is untouched
  here.
- Core.Tests, the same shape (fixed `WaitAsync`/`Task.Delay`/`CancelAfter` bounds around fake
  clocks or background loops):
  - TenantRegistryTests, TenantRegistryRefreshServiceTests, TenancyCompositionTests, RegistryHarness
  - TenantPollerTests, JobDispatcherTests, JobRenewalSchedulerTests, JobItemProgressTests, JobWorkerFakes
  - ModelLockRetryTests, FileSystemBlobStoreHarness, WriteOperationTests.Actions, ValidationSchedulerPropertyTests
- Identity.Tests: ClientMetadataFetchBudgetTests.
- Integration suites (Core.IntegrationTests Jobs/Migrator/Data/Catalog, Identity.IntegrationTests)
  carry many more of these. They are out of this item's scope.

### Open risks

- Every tenant request in the shared host still runs under the real 30 s web timeout. That is
  far above the loaded first request (7.3 s), but it is still a wall-clock bound on cold work.
  Lifting the shared default was deferred because PC-089's BlobEndpointTests compute deadlines
  from those options.
- `A_long_endpoint_outlasts_its_groups_timeout`: the endpoint's `.WithRequestTimeout(Long)`
  replaces the group's policy, so the 200 ms group timer never arms for it. The test cannot fail
  by load, but it would also pass if the group timer were ignored entirely. Its comment's
  "armed at the timeout stage" premise deserves a reviewer's look.
- One old-code load failure, `FrameworkProblemsTests.A_bare_status_a_component_set_answers_its_problem(403)`,
  was overwritten before its message was kept. That test has no wait of its own. The only wall
  clocks on its path are the shared host's 30 s web timeout and the rate-limit windows (one
  request). A cold first request past 30 s, answering 504, is the likely cause. It points at the
  risk above and argues for lifting the shared default once PC-089 has integrated. Lifting only
  `RequestTimeout` to the 5-minute `LongRequestTimeout` default would leave PC-089's deadlines
  alone. It did not recur in the three after runs.
- `HubClient.HasUnread()` absence checks can pass falsely (an event still in flight), but they
  cannot fail falsely.
- The stalled-send overlap check relies on the tracker starting both sends within one 2 s bound.
  That holds unless a single thread is descheduled for 2 s between two synchronous calls.

## Review 1

### Findings

### Responses
