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

- (Addressed in review 1 fix 1.) Every tenant request in the shared host ran under the real 30 s
  web timeout, a wall-clock bound on cold work. The shared host now sets `RequestTimeout` to five
  minutes, the `LongRequestTimeout` default, so PC-089's deadlines, which read
  `LongRequestTimeout`, are untouched. Tests that pin a timeout set their own.
- (Comment fixed in review 1 fix 1.) `A_long_endpoint_outlasts_its_groups_timeout`: the
  endpoint's `.WithRequestTimeout(Long)` replaces the group's policy, so only the long timer is
  armed. If the override were lost, the 200 ms group timer would answer 504 within the
  endpoint's 800 ms, so the test pins what its name claims.
- Flake candidate, kept on record: `FrameworkProblemsTests.A_bare_status_a_component_set_answers_its_problem(status: 403, code: "forbidden")`.
  - What happened: it failed once, in an old-code load copy (copy 3, run 3, 08:05, 933 tests,
    4 failures). The copy's next run overwrote its log, so the message was lost.
  - The test has no wait of its own. The only wall clocks on its path were the shared host's 30 s
    web timeout and the rate-limit windows, and it sends one request.
  - Likely cause, not shown: a cold first request past 30 s, answering 504. The worst first
    request measured was 7.3 s, but the machine was more heavily loaded at that point.
  - Status: it did not recur in the three after runs, nor in the old-code runs after it. Review 1
    fix 1 removes that cause. If it recurs, its message decides whether it is something else.
- `HubClient.HasUnread()` absence checks can pass falsely (an event still in flight), but they
  cannot fail falsely.
- The stalled-send overlap check relies on the tracker starting both sends within one 2 s bound.
  That holds unless a single thread is descheduled for 2 s between two synchronous calls.

## Review 1

Scope: commit d10f3b46 (test code under `test/core/Tellma.Core.AspNetCore.Tests` and
`test/shared/Tellma.Testing.WebHost`, plus this file).

What I checked:

- **No product code changed.** `git show --stat` touches only `test/` and this review file. The
  ConnectedProbe race does not point at a product bug. The hub refuses the connection in its
  connect, as designed, and closes it. `HubConnection.ConnectionId` reads null once the client has
  seen the close, which is how the SignalR client behaves. Only the probe read it too late.
  The token fix is sound. `?id=` on the WebSocket connect URI is the negotiate's connection token.
  The server reads the same query from `GetHttpContext()` of the transport request, and both
  sides URL-decode it. `AzureSignalRTestHost` builds its own `HubConnection` and still waits by id
  (listed as a carry).
- **Root cause and each changed wait.** Every changed wait now waits on one of three things:
  - an endpoint signal, through `SignalledAsync`, which fails fast with the request's outcome;
  - armed timers on the `TestClock`;
  - the request's own completion, under a hang-only bound.

  No bound was lengthened to make a timing pass. The 1 h web-timeout lifts sit only on hosts whose
  scenario never relies on that timer: RateLimiting report, RequestCancellation and the
  long-endpoint Long policy.
- **SessionLifecycleTests against SessionRefresher/OidcTokenRedeemer.**
  - `FlightWait == OidcTokenRedeemer.Timeout == 5 s`, so a request's `running.WaitAsync(FlightWait, time)` and
    the redeemer's `new CancellationTokenSource(Timeout, time)` both come due at `start + 5 s`. That
    makes `count: 2`, and `8 + 1` in `JoinedAsync`, the exact set.
  - The retry `Task.Delay(RetryPause, time)` is armed after the deadline fires, so it is due at
    `start + 6 s`. The write retries fall at `start + RetryPause × attempt`.
  - `PastTheLeeway` is the 30 s leeway + 5 s = 35 s, the same total the old 1-second stepping
    reached in four of the six tests.

  Each step's timers are armed only by work the previous move released, so they read the moved clock.
- **TestClock.** No lock-order inversion. `_gate` is never held while the fake clock's own lock
  is taken, because `GetUtcNow()` is read before `_gate` in `Record` and `Waiting`, and
  `Inner.Dispose` runs outside `_gate`. `NextDue` is right for one-shot and periodic timers.
  Taking the mark before the move is the right order, since callbacks the move runs inline may
  arm the next timers. One theoretical gap: `Record` reads the clock (line 95) separately from
  `base.CreateTimer` (line 85). A timer armed on another thread at the instant the test moves the
  clock could be recorded at a due time that is not its real one. No current test arms a timer it
  waits for concurrently with a move, so this is not a finding.
- **Stalled-send test.** `TrySendAsync` (HubConnectionTracker.cs:439-447) passes its own `bound.Token`
  through `IClientProxy.SendAsync` to `SendConnectionAsync`. The `Bound.IsCancellationRequested`
  assertion therefore also rules out a send made without its bound. A one-after-the-other close
  would start the second send with the first's bound already cancelled, and would fail
  `EarlierStillWaiting`. All sends are created synchronously in `TellAsync`'s `Select`, so the 2 s
  window the notes flag needs a 2 s deschedule inside a synchronous loop. I accept that risk.
- **504 split (the declared deviation).** The cancellation case still runs the framework's real
  timeout path. `FireTimeout` cancels the timeout feature's own source (D-155's accepted seam), so
  `RequestTimeoutsMiddleware` writes the 504 and `DataCancellation` sees the same token. Only the
  timer's firing moves to the lowered-timer test (see R1-3).
- **RateLimiting.** Sending the tile while the interactive permit is still held makes the test
  stronger. Before, the permit was released first, so a batch lane that shared the interactive
  permit would have passed.
- **Build and tests (mine, idle-ish machine, Debug).**
  - `dotnet build test/core/Tellma.Core.AspNetCore.Tests` printed Build succeeded with 0 warnings.
  - The touched classes plus WebApiProjectionTests and InProcIdentityTests passed 169/169 in 4 m 54 s
    (PC-F65-r1-touched.log, session scratchpad).
  - I did not repeat the loaded full runs.
- **Evidence method.** It is a fair A/B. The old-code copies were both the load and the control,
  running at the same time as the new-code runs, from a frozen bin. But three green full runs are
  weaker evidence than the summary suggests. Against the matched-pace control (the lighter rounds
  failed 2 of 6), three greens happen by chance with p ≈ (4/6)³ ≈ 0.30. Against the overall 10 of 15,
  p ≈ 0.04. The mechanistic case is what carries the item: each of the 7 named failures maps to a
  wait that was replaced, and the probe timings back it. The runs are consistent with that case;
  on their own they do not prove it.

### Findings

**R1-1 (minor) — the held-report test's refused request is still an unbounded wait, and in a
queueing regression the test hangs indefinitely.**

- Where: `test/core/Tellma.Core.AspNetCore.Tests/Limits/RateLimitingTests.cs:169`,
  `second = await host.Client.SendAsync(…, Token)`.
- How it hangs: the permit is released only in the `finally` that follows this await. The
  report's lane is enforced by `UseRateLimiter`, which runs ahead of `UseRequestTimeouts`
  (LimitsComposition.cs:88-91), so a queued request has no web timer at all. The held endpoint
  awaits `release.Task` and ignores its token.
- Failure scenario: a regression that queues the second report instead of refusing it (for
  example, `ReportQueuePerUser = 0` no longer honoured) deadlocks the test. CI then ends it at the
  45-minute job timeout with no test name. The old code hung the same way, so this is not new.
  This change, though, introduces the tool for it, and its stated rule is that every wait on what
  a correct host does on its own uses it.
- Fix: `second = await Patience.WaitAsync(host.Client.SendAsync(…, Token));`. The `finally` then
  releases the permit after a 2-minute `TimeoutException` that names the wait.

**R1-2 (minor) — the long-endpoint test's new comment states a false premise.**

- Where: `test/core/Tellma.Core.AspNetCore.Tests/Limits/RequestTimeoutTests.cs:95-97`.
- What it gets wrong: the comment says the group's timer "is armed at the timeout stage, before
  the endpoint runs, so the endpoint's wait outlasts it". ASP.NET Core's `RequestTimeoutsMiddleware`
  arms one timer per request, from the endpoint's most specific policy metadata. The endpoint's
  `.WithRequestTimeout(Long)` wins over the group's `Web` policy, so the 200 ms timer is never armed
  for `/long`. The implementer's open risk reads it correctly, and the comment contradicts it.
- What the test does pin is still right. If the endpoint's override were lost, the 200 ms group
  timer would fire during the 800 ms wait, and the request would answer 504. The test therefore
  cannot pass falsely on what its name claims. Only its explanation is wrong.
- Failure scenario: a later reader trusts the comment and "fixes" the 200/800 ms pair, or the
  override, on the strength of a mechanism that does not exist.
- Fix: say that the endpoint's Long policy replaces the group's, so only the long timer is armed.
  The 200 ms group setting is there so that a lost override answers 504 within the endpoint's
  800 ms.

**R1-3 (minor) — `A_request_past_its_configured_timeout_answers_504` passes when the configured
timeout is ignored.**

- Where: `test/core/Tellma.Core.AspNetCore.Tests/Limits/RequestTimeoutTests.cs:41-62`.
- Failure scenario: if `Tellma:Api:RequestTimeout` stopped reaching the `Web` policy
  (LimitsComposition.cs:49), the 30 s default would fire instead. That still answers 504
  `timeout`, well inside the 2-minute `Patience`, so the test stays green.
- Why it matters now: the gap is older than this change, since the old test's send was unbounded
  too. After the split, though, this is the only test that a real configured timer fires, and its
  new name claims exactly that. No other test reads the policy that the setting produces.
- Fix: keep the timing-free shape and add a deterministic pin, for example
  `Assert.Equal(TimeSpan.FromMilliseconds(300), host.Services.GetRequiredService<IOptions<RequestTimeoutOptions>>().Value.Policies[TellmaRequestTimeouts.Web].Timeout)`.
  Lifting the shared default (R1-4) would also make the test discriminating: a 5-minute default
  outlasts `Patience` and fails the test.

**R1-4 (minor) — one observed load failure is left unexplained, and the cheap fix for its likely
cause need not wait for PC-089.**

- Where: `test/shared/Tellma.Testing.WebHost/TellmaTestHost.cs`, the default settings (around
  line 127). The failure is `FrameworkProblemsTests.A_bare_status_a_component_set_answers_its_problem(403)`.
- What is unexplained: the test failed once in the old-code load runs, and its message was lost.
  The proposed cause is a cold first request past the shared 30 s web timeout, answering 504. The
  measured worst first request was 7.3 s, though, so the cause is not shown. Every other
  shared-host test still runs under that 30 s real timer.
- Failure scenario: the same flake recurs on a loaded CI runner. It then has no root cause on
  record and no LEDGER flake entry, which D-011 and D-113 expect for a flake that is not fixed.
- Fix: in fix 1, set `Tellma:Api:RequestTimeout` to the `LongRequestTimeout` default (5 min) in
  the shared host's defaults. This does not need to wait for PC-089:
  - PC-089's deadlines read `LongRequestTimeout`, which this leaves alone.
  - Its BlobEndpointTests set their own 1 s `RequestTimeout` explicitly.
  - On this branch every test that pins a timeout sets both values itself, and
    `A_long_timeout_shorter_than_the_web_timeout_fails_startup` sets both.
  - A request that hangs then fails at `Patience` with the test's name, not as a 504.

  If the failure recurs after that, it is not the timeout: record it as a flake candidate with
  its message kept.
- My view on the implementer's open question: yes, lift it. Do it now rather than after PC-089,
  for the reasons above. It is the last wall-clock bound on cold work in this assembly.

### Responses
