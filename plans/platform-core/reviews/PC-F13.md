# PC-F13: stop-the-line fix: Windows unit failures on d554208 (PC-021)

## Review 1

Reviewed commit d8e214c. No blocking or major findings; three minor ones.

### Findings

**R1-1 (minor) — A listener that returns no task now ends the whole delivery, unlogged.**

- Where: `src/core/Tellma.Core/Tenancy/TenantRegistry.cs:600` (`Task.WhenAny(call, expiry)`),
  with the invocation at lines 583-597.
- Scenario: a listener returns `null` instead of a task (a non-async implementation that returns
  `null!` or `default!`, or a hand-written stub). `Task.WhenAny(Task, Task)` throws
  `ArgumentNullException` for a null argument, and the call sits outside every `try`. So
  `CallAsync` faults, `DispatchAfterAsync` faults with it (its loop has no `try`), and the
  listeners and changes still queued for that reload are never called. Nothing is logged, the
  call's scope is never disposed, and the bound's timer runs to its 10 s end.
- Before this commit the same listener hit `call.WaitAsync(...)` inside the `try`, the
  `NullReferenceException` was logged as `TenantStateListenerFailed`, the scope was disposed and
  the queue moved on. `ITenantStateListener`'s contract says exactly that: a call that throws is
  logged and the queue moves on.
- Fix: refuse the null inside the first `try`, where the existing `catch` already cancels the
  bound, logs and disposes the scope, for example
  `call = (change.Removed ? listener.OnRemovedAsync(...) : listener.OnStateChangedAsync(...)) ?? throw new InvalidOperationException("The listener returned no task.");`.
  Add a test beside `A_failing_listener_is_logged_and_the_others_still_hear` with a listener that
  returns null ahead of a healthy one.

**R1-2 (minor) — Neither behaviour change in `CallAsync` is pinned by a deterministic test.**

- Where: `src/core/Tellma.Core/Tenancy/TenantRegistry.cs:578-581` (the bound starts before the
  listener is resolved and invoked) and `:612-620` (a listener's own `TimeoutException` is now
  `TenantStateListenerFailed`, no longer `TenantStateListenerTimedOut`). The only test near them is
  `test/core/Tellma.Core.Tests/Tenancy/TenantRegistryTests.cs:214`.
- Scenario: someone later folds the two tasks back into `call.WaitAsync(timeout, time)`. Every
  test still passes on almost every run, because the hung-listener test only loses the race under
  load, and the Windows flake this item fixes is back. The reclassified `TimeoutException` has no
  test at all, so that half can regress silently too.
- Fix: two tests, both deterministic on the fake clock.
  1. A listener that advances the fake clock by the bound inside its synchronous part and then
     returns a task that never completes. With the bound started first, the call times out with no
     further `Advance` from the test (`DeliveredAsync` completes, one `TenantStateListenerTimedOut`,
     the next listener hears). With a bound started after the listener returns, `DeliveredAsync`
     never completes.
  2. A listener that returns `Task.FromException(new TimeoutException())`: one
     `TenantStateListenerFailed`, no `TenantStateListenerTimedOut`, the next listener hears.

**R1-3 (minor) — The commit message names a test that does not exist.**

- Where: commit d8e214c, item 2: `TenancyCompositionTests.The_refresh_loop_...`.
- The test changed is
  `TenancyCompositionTests.The_host_loads_the_snapshot_at_start_and_the_loop_probes_every_interval`
  (`test/core/Tellma.Core.Tests/Tenancy/TenancyCompositionTests.cs:43`). A search of the suite for
  the name in the commit finds nothing, so the record misleads whoever traces this fix later.
- Fix: no code change. State the real name in the response below and in the follow-up commit's
  message, so the ledger and the merge carry it.

#### What was checked and found sound

- **Model pinning (cause 1).** The diagnosis holds. `TellmaModelCacheKeyFactory` keys by the
  definition's reference, every Tellma context shares one internal service provider
  (`TellmaSqlServerOptionsExtension` hashes to 0), and EF's model cache refuses new entries once it
  is full, so a late composition's contexts each rebuilt their model. `UseModel` with the runtime
  model the factory built is a fix of that cause, not a cover: the factory no longer depends on a
  cache hit. EF leaves the model out of the service-provider key, so the contexts still share one
  provider. A context asked for its design-time model still goes through EF's model source,
  because the pinned model is a runtime model; the migration, scaffolding and fingerprint tests
  exercise that path and pass. The model source's lock and the factory's `Lazy` cannot invert,
  since building a model never reaches a factory. A model that cannot be built is now cached as a
  failure by the `Lazy`; `EntityMetadataSource` already cached that outcome, so nothing observable
  changes. Spec 0012 §1.3 asks that every host and the migrator build one model, which this keeps
  and strengthens. No README or ARCHITECTURE.md text describes the old behaviour.
- **Reload wait (cause 2).** `FakeCatalogReader` counts a reload on entry, and the registry logs
  `TenantSnapshotReloaded` after the swap and after the refusals are reported, so the new wait
  cannot see the old snapshot. No timeout was widened, nothing retries, nothing is quarantined.
- **Listener bound (cause 3).** `Task.Delay` over the time provider creates its timer before the
  listener runs, and `HangingListener` signals `Entered` after that, so the test can no longer
  advance the clock ahead of the timer. Ties still go to the call: `Task.WhenAny` returns its first
  argument when both are complete. The cancelled delay is never faulted, so it raises no unobserved
  exception.
- **The open item: `TenancyCompositionTests` and the fake clock. Not a live flake.**
  `TenantRegistryRefreshService.LoopAsync` uses one `PeriodicTimer`, created synchronously inside
  the hosted service's `StartAsync`, so it exists before `host.StartAsync` returns. Nothing re-arms
  it. A periodic timer keeps one latched tick: a tick that fires while the loop is busy completes
  the next `WaitForNextTickAsync` at once. Each test advances the clock a second time only after
  `Probes == 1`, which means the loop has already consumed the first tick, so the second tick is
  either delivered or latched and never lost. Nothing else in the composed host creates a timer on
  the fake clock while a test advances it. The reload assertions hold in either order of the
  first probe's version read and the test's write. One weak spot that cannot fail a run:
  `A_failing_refresh_never_ends_the_loop` clears `Failure` after `Probes == 1`, which is counted
  before the reader checks `Failure`, so in a rare interleaving the first probe succeeds and the
  test passes without having seen a failure.
- **PLAN §7.1 and AGENTS.md.** Every new member has XML docs, the new sections have inline
  comments, and no doc, test name or comment cites a spec, a PC id or `docs/`. `Tellma.Core` is in
  the reference closure of `Tellma.Core.Tests` and `Tellma.Core.IntegrationTests` only, and the
  commit lists both.

#### Verification (Windows, this worktree)

- `dotnet build Tellma.slnx`: Build succeeded, 0 warnings, 0 errors.
- `dotnet format Tellma.slnx --verify-no-changes` over the five changed files: no output, clean.
- The three touched classes (`ModelCompositionTests`, `TenantRegistryTests`,
  `TenancyCompositionTests`, 51 tests): 57 runs, all green. 1 alone; 32 as four parallel loops
  of 8; 24 as four parallel loops of 6 with all 20 cores also held by busy loops (a run took 30 s
  to 69 s there, against 8 s to 10 s unloaded).
- The full `Tellma.Core.Tests` unit filter (1354 tests): 6 runs, three at a time, all green.
- The new `A_factory_keeps_its_model_however_many_other_compositions_the_process_builds` takes
  about 5 s (fifty model builds). It was not run against the old factory, so the commit's claim
  that it fails there is unverified.
- Linux was not run here either. Nothing in the change is platform-specific; CI's Linux leg is the
  check.

### Responses
- R1-1: fixed in the "PC-F13: review 1 fixes" commit. A listener that returns no task is refused
  inside the first `try` with `InvalidOperationException("The tenant-state listener returned no
  task.")`. The existing `catch` then cancels the bound, logs `TenantStateListenerFailed`, disposes
  the scope and lets the queue move on. New test:
  `A_listener_that_returns_no_task_is_logged_and_the_others_still_hear`. It fails against d8e214c's
  registry and passes now.
- R1-2: fixed in the "PC-F13: review 1 fixes" commit with the two tests the review suggests, built
  on a new `ScriptedListener` in the harness.
  `The_listener_bound_starts_before_the_listener_runs`: the listener advances the fake clock by the
  whole bound in its synchronous part, then returns a task that never completes. The call times
  out with no further `Advance`.
  `A_listener_failing_with_its_own_timeout_is_logged_as_failed_not_timed_out`: one
  `TenantStateListenerFailed` carrying the `TimeoutException`, no `TenantStateListenerTimedOut`,
  and the next listener hears.
  Against the registry of 3d7aa7b (bound started after the call), both fail. The first fails at
  `DeliveredAsync`'s 30 s wait, the second on the event it logged.
- R1-3: accepted; no code change. The test changed in d8e214c's item 2 is
  `TenancyCompositionTests.The_host_loads_the_snapshot_at_start_and_the_loop_probes_every_interval`,
  not `The_refresh_loop_...`. The review 1 fixes commit message names it.
- The fill-the-cache test: before committing d8e214c I ran
  `A_factory_keeps_its_model_however_many_other_compositions_the_process_builds` against the old
  `TellmaModelFactory` (from 3d7aa7b), restored temporarily. It failed with "Assert.Same() Failure:
  Values are not the same instance" (13 tests, 1 failed), and it passes with the pinned model.
