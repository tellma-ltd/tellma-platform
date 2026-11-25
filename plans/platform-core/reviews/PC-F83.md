# PC-F83: RoundTripBudgetTests intermittent extra round trip

## Implementer notes

### The failure

CI 45334915665 (pc/PC-155, no C# change): `RoundTripBudgetTests` measured
`users/preferences/get` at 2 round trips against its budget of 1. Every other operation in the
same run was at or under its budget. Spec 0018 §8.1 gives the row 1 with warm caches ("+1 on a
cold or stale path"), so the budget stays 1; the test's warming read is meant to make the path
warm, and the extra round trip had to come from something else.

### Root cause

The process-wide tag snapshot stores (`VersionTagSnapshots`, `UserVersionTagSnapshots`,
src/core/Tellma.Core/Caching/VersionTagSnapshots.cs) replaced an entry as two steps:
`_cache.Remove(key)` then `_cache.Set(key, ...)`. Between the two, any concurrent reader of
`Current(tenantId)` got the **empty** snapshot. Every batch's executor replaces the snapshot
(`DataExecutor.ReplaceSnapshots`), so any batch finishing on another thread (a background job's,
another request's) opens that window.

A request that reads the snapshot inside the window sees the `settings` tag as `VersionTag.None`.
`LocaleNegotiationInitializer` asks `TenantSettingsCache.GetAsync()` for the settings under that
tag; `VersionedCache.Matches` never matches `None`, so the request loads the settings: one extra,
bare round trip inside the request's unit of work, counted on its operation tag. That is exactly
the CI signature, +1 on an otherwise warm read. In the CI run, `users/preferences/get` came two
requests after `users/invite`, whose welcome email and notification are jobs the worker runs in
the background, and the integration job runs every test assembly in parallel on an oversubscribed
runner, where a thread preempted between the `Remove` and the `Set` holds the window open for a
scheduling quantum rather than a microsecond.

`VersionedCache.Store` had the same shape (remove the replaced entry, then add the new one) with
the same consequence for a concurrent reader of that key: a miss and a load. It also feeds the
first window: the settings load a reader triggers is offered to the store, which replaced its entry
through the same remove-then-set.

Both were written as remove-then-set so that "a set the full cache refuses must not leave the older
entry behind" and "the size check sees only the new one". Neither is needed: verified against the
runtime in a throwaway test, `MemoryCache.Set` of an existing key at the size limit counts the
replaced entry's size out and lands, and a replacement it refuses drops the replaced entry rather
than keeping it.

### Reproduction (before the fix)

All probes were throwaway tests, deleted before the commit.

- Store level: one thread replacing tenant 1's snapshot in a loop, another reading it for 3 s:
  4,527,763 of 5,844,349 reads returned the empty snapshot. After the fix: 0 of 8,613,323.
- Consequence, deterministic: in the Acme web suite, the tenant snapshot replaced with an empty one
  just before a warm `users/preferences/get`: 5 of 5 took 2 round trips, each with a
  `tellma.cache.requests` `cache.kind=settings` `cache.outcome=stale` request.
- End to end, amplified: a warm `users/me` then `users/preferences/get`, while a thread in the host
  replaced the tenant's snapshot with the same tags back to back (many batches finishing at once):
  100 of 100 took 2 round trips, every one with the settings cache `stale`. After the fix: 300 of
  300 took 1.
- Unamplified, the window was not hit on this machine (20 logical cores): 60 and 250 iterations of
  the budget test's user sequence (save, invite, invitation status, warm read, preferences get);
  150 and 400 iterations with 1 and 16 other members' requests running beside them; and 600 and
  800 iterations with 4 and 8 such members under 20 and 60 busy-loop processes. The window is a
  `MemoryCache.Remove` and a `Set`, about a microsecond unless the thread is preempted between
  them, and CI's runner is far more oversubscribed (every integration assembly, each with its own
  SQL Server container, on a 4-core VM). So the link from this defect to CI run 45334915665 is
  inferred, not observed: it is the one mechanism I found that adds exactly one round trip to a
  warm read without a write, a moved tag or a re-run, and the run's own app logs are not in the CI
  output. Other +1 paths were ruled out from the code: a guard failure needs a moved `permissions`
  tag (no writer of roles, permissions, settings or the administrator's memberships runs at that
  point); a cold connect needs the connect cache to drop the subject (50,000 entries, no expiry);
  a transient re-run needs a transient failure of a read under read-committed snapshot; a read
  re-run (the batch discarded because a re-run dependency moved, then recomposed) needs a tag bump,
  and no job the invite schedules bumps one.
- So that a recurrence leaves a lead, `RoundTripBudgetTests` now reports, beside each operation
  above its budget, what the host recorded while it ran that costs a warm path a round trip: every
  cache lookup but a hit, every connect result but `warm`, every transient re-run, and every
  `tellma.versiontags.stale` event by phase (a `read-rerun` is a read re-run). The instruments are
  process-wide, so a background job's measurements land in the same line.

### The fix

- `VersionTagSnapshots.Replace` and `UserVersionTagSnapshots.Replace` swap the entry with one
  `Set`. A reader sees the older snapshot or the newer one, never the tenant (or user) as absent.
- `VersionedCache.Store` marks the replaced entry with its displacement reason and swaps with one
  `Set`. A replaced entry now leaves with `EvictionReason.Replaced`, so `Evicted` reads the entry's
  displacement for that reason too, and `age` is still metered as `age`.
- The XML docs and inline comments say why there is no remove first.

Regression tests (Tellma.Core.Tests):

- `VersionTagStateTests.A_reader_never_sees_a_tenant_as_never_read_while_another_batch_replaces_its_snapshot`
- `VersionTagStateTests.A_reader_never_sees_a_user_as_absent_while_another_batch_replaces_their_snapshot`
- `VersionedCacheTests.A_reader_never_misses_an_entry_while_another_caller_replaces_it_under_the_same_tag`

Each replaces one entry 20,000 times on one thread while another reads it, and asserts no read
found it absent. On the old code all three fail (29,354 to 86,941 absent reads each); on the
new code they passed 20 runs of 20.

`RoundTripBudgetTests` keeps its measurement and its budgets (the measurement was right; the
budget stays 1); it only gains the cause report above.

### Tests run locally

- `dotnet build Tellma.slnx`: Build succeeded, 0 warnings; `dotnet format --verify-no-changes`: clean.
- Tellma.Core.Tests, unit filter: 6,577 passed, 2 skipped (platform-gated), 0 failed.
- Tellma.Core.IntegrationTests, `VersionTag`, `SettingsService`, `UserConnection` and
  `AccessEvaluation` classes: 94 passed.
- Tellma.Core.AspNetCore.IntegrationTests `RoundTripBudgetTests`: 3 of 3 runs green.

### Deviations

None from the specs. Spec 0013 §3 describes the stores as a private `MemoryCache` with LRU
eviction, which they still are.

### Open risks

- The snapshot store is last-writer-wins across concurrent batches, as before: a batch whose
  prelude read older tags can finish after one that read newer ones and replace the snapshot with
  the older tags. That costs a refresh or a re-run, never a wrong answer, and is not this defect.
- The window is closed, but any other process-wide cache that removes before it sets would have
  the same shape. I found none in src/ (`ConnectCache`, `QueryDiscoveries` and the session store
  set directly or remove only to drop).
- The CI-to-cause link is inferred (see Reproduction). If the budget test fails again, its
  message lists what the whole host recorded during the over-budget operation, background jobs
  included. A settings lookup `stale` or `miss` there is a lead back to this window, not a
  diagnosis: a job's own lookup reads the same. A `re-run stale.phase=read-rerun` points at a moved
  re-run dependency instead.

### What reviewers should examine hardest

- The claim that `MemoryCache.Set` counts a replaced entry's size out at the limit and drops the
  replaced entry when it refuses the new one. The existing
  `A_full_store_still_replaces_a_tenants_snapshot_and_refuses_a_new_tenant_rather_than_keep_a_stale_one`
  pins the first half; the second half was checked in a throwaway test (a size-2 replacement in a
  size-2 cache holding two entries left the key absent).
- The eviction-reason mapping in `VersionedCache.Evicted` after the switch from `Removed` to
  `Replaced` (`Set_meters_the_entry_it_replaces_as_replaced_or_aged_and_remove_as_removed`).

## Review 1

Scope: commit 07b96828 only, against the LEDGER row PC-F83. There is no CHECKPOINTS entry. Spec
0013 §3 (the snapshot stores, `VersionedCache`, §11's `cache.reason` set) and 0018 §8.1 (the
`users/preferences/get` budget of 1) are in scope.

Verified locally on 07b96828:

- `dotnet build` of Tellma.Core.Tests and of Tellma.Core.AspNetCore.IntegrationTests: Build
  succeeded, 0 warnings.
- Tellma.Core.Tests, filtered to `VersionedCacheTests` and `VersionTagStateTests`: 46 of 46 passed,
  5 runs out of 5.
- Tellma.Core.AspNetCore.IntegrationTests `RoundTripBudgetTests`: 1 of 1 passed (25 s).
- The pc/PC-F83 push's CI run (45545277480) was skipped, so there is no CI evidence yet.

What holds, checked item by item:

- **The runtime claims.** I decompiled `MemoryCache` from Microsoft.Extensions.Caching.Memory
  10.0.11, the pinned version, with ilspycmd. `SetEntry` marks the prior entry
  `SetExpired(Replaced)`. `UpdateCacheSizeExceedsCapacity(entry, priorEntry, …)` tests
  `size + entry.Size - priorEntry.Size` against the limit and CASes that value in, so a same-size
  replacement at the limit lands. On a capacity refusal, `coherentState.RemoveEntry(priorEntry)`
  drops the prior entry, subtracts its size once and invokes its callbacks. On success,
  `TryUpdate` swaps the entry, and the size is already net of the prior entry. A scratch
  file-based program against the same package agreed:
  - With remove-then-set, 193,633 of 247,445 concurrent reads were absent. With a single `Set`,
    0 of 67,339 were.
  - A same-size replacement at the limit lands.
  - A refused larger replacement leaves the key absent, with count 1 and size 1. Its evictions
    are `big:Capacity` and `a:Replaced`.
- **A reader during the swap.** `PostProcessTryGetValue` serves an entry that is expired with
  `EvictionReason.Replaced`. So a reader that finds the prior entry after `SetExpired` and before
  `TryUpdate` still gets the older value rather than a miss. The swap really is gap-free, apart
  from R1-3.
- **Metering after the switch (judge item b).** Both callback paths read the displacement the
  same way:
  - A swapped-out entry calls back with `Replaced`, now mapped to its `Displacement`, so `age`
    stays `age` and `replaced` stays `replaced`.
  - The capacity-refusal path meters the prior entry under its displacement and the new entry as
    `capacity`. The old code gave the same pair (`Removed` plus displacement, then `capacity`).
  - `InvokeCallbacks` exchanges the callback list to null, so no entry is metered twice. That
    holds even when two concurrent `Store`s of one key both name the same prior entry.
  - An entry refused by the runtime's own `TryAdd` race still meters `replaced`, because its
    `Displacement` is null, as before.
  - `Remove` still meters `removed`. 0013 §11's closed reason set is unchanged.
  - Size accounting is correct on every non-racing path (but see R1-3).
- **The mechanism against CI 45334915665 (judge item a).** I read the run with
  `gh run view --log-failed`. Only `users/preferences/get` is over budget (2 against 1), and every
  other row is at its budget. No app log is in the output, so the cause cannot be confirmed from
  the run, as the implementer says. The mechanism is sound:
  - `DataExecutor.ReplaceSnapshots` (DataExecutor.cs:275-334) replaces the tenant snapshot after
    every batch, the invite's background jobs included.
  - An empty snapshot makes the settings lookup `stale`, which costs one load inside the
    request's unit of work.
  - I found no job that bumps a tenant tag (no `Bump`/`AffectedUsers` use under the outbox,
    notification or job code). So last-writer-wins with older tags cannot explain it at that
    point.
  - `ConnectCache`, `AccessEvaluator.Invalidate` and `UserConnector` remove only to drop, never
    remove-then-set.

  One other +1 path is not in the implementer's ruled-out list: a read re-run on a moved re-run
  dependency. Nothing I found moves such a tag at that point, so it is unlikely to be the CI
  cause, but the diagnostics do not name it (R1-1).
- **The regression tests (judge item c).** Each test fails on the old shape, which the scratch
  program reproduces at about 78% absent reads. None should flake on the new code:
  - The only residual window is R1-3's expiration scan. A fresh `MemoryCache` sets its last-scan
    time at construction and scans at most once per minute, so no scan runs during these
    millisecond-long tests.
  - Each test class builds its own service provider, so the 20,000 queued eviction callbacks
    cannot reach a later test's collector.
- **The budget.** It stays at 1, as 0018 §8.1 gives it.
- **AGENTS.md and PLAN.md §7.1.** XML docs and inline section comments are present. No spec
  reference appears in a doc, a message or a test name.

### Findings

**Blocking:** none.

**Major:** none.

**Minor:**

- **R1-1 (minor). The cause report misses stale-tag read re-runs.**
  `test/core/Tellma.Core.AspNetCore.IntegrationTests/Serial/RoundTripBudgetTests.cs:53-57,
  182-213`.
  - *Failure scenario.* `Causes` claims "every re-run", but it collects only `tellma.data.retries`.
    That instrument counts only the executor's transient re-runs (DataExecutor.cs:591-594).
    Another path also costs a warm read one extra round trip: the read is discarded because a
    re-run dependency moved (DataExecutor.cs:216-223, `StaleContextException`) and then recomposed
    (GuardedBatchRunner.cs:118-133). That path is metered only on `tellma.versiontags.stale` with
    `stale.phase = read-rerun` (VersionTagMetrics.cs:164-167), which the test does not collect.
    So if the budget test fails that way, the message shows "nothing" or a bare `… lookup stale`
    from the recompose's reload. The implementer's guidance (lines 116-118 of this file) then
    sends the reader back to the snapshot window, which is the wrong cause.
  - *Suggested fix.* Add a fourth collector on `CacheTelemetryNames.Stale` and report each
    measurement as `re-run stale.phase=<phase>`. Name the read re-run in the "ruled out" list too.
- **R1-2 (minor). The cause report also counts other callers' measurements.**
  `RoundTripBudgetTests.cs:54-56, 263-274`.
  - *Failure scenario.* The three collectors are process-wide, and `tellma.cache.requests` has no
    operation tag. Everything the host records between `causes.Clear()` and `Describe()` is
    therefore reported against the operation. That includes the lookups and connects of the
    invite's background jobs, which the implementer names as the likely concurrent batches, and
    the window lasts up to `Patience` (10 s) while `AwaitAsync` polls. A job's own
    `settings lookup miss` would read as the request's. That is the attribution this file's Open
    risks tells a future reader to trust.
  - *Suggested fix.* Label the line "recorded process-wide meanwhile (background jobs included)".
    Filter connect results to the request's variant (`variant = subject`), so a job's connect is
    not counted. Reword the Open-risks guidance to treat a settings `stale`/`miss` as a lead, not
    a diagnosis.
- **R1-3 (minor). "Never absent" holds except in a rare race with the runtime's scan.**
  `src/core/Tellma.Core/Caching/VersionTagSnapshots.cs:24-27, 56-59, 85-87, 113-114`;
  `src/core/Tellma.Core/Caching/VersionedCache.cs:431-434`.
  - *Failure scenario.* `SetEntry` marks the prior entry expired before `TryUpdate`. Two runtime
    passes remove any entry whose `CheckExpired` is true, and that includes a prior entry
    mid-swap:
    - `ScanForExpiredItems`, at most once per `ExpirationScanFrequency` (default 1 min).
    - `Compact`, which a full `VersionedCache` kind triggers on every refused add.

    If either pass hits that sub-microsecond window, a reader sees the key absent until the
    runtime's fallback `TryAdd` lands. The size is also subtracted twice: once by the CAS and
    once by `RemoveEntry`. So the estimated size and the gauge drift low by the prior entry's
    size per occurrence. The effect is negligible: one extra load, never a wrong answer. Still,
    the docs and comments state "never", and the next investigator of a +1 will trust them.
  - *Suggested fix.* Qualify the sentences, for example "outside a rare race with the runtime's
    expiration scan or compaction, which costs one load". No code change is needed.

### Responses

- **R1-1: fixed.** `RoundTripBudgetTests` collects `tellma.versiontags.stale` as a fourth cause
  and reports each event as `re-run stale.phase=<phase>`, so a read re-run on a moved re-run
  dependency is named. The Implementer notes now list the read re-run among the ruled-out +1 paths,
  with the reason (no job the invite schedules bumps a tag).
- **R1-2: fixed in part.** The over-budget line now reads "recorded process-wide meanwhile
  (background jobs included)", and the class and `Causes` docs say the instruments are host-wide and
  name no request. The Open-risks guidance now treats a settings `stale`/`miss` as a lead, not a
  diagnosis. Not done: filtering connect results to the request's variant. `tellma.access.connect.results`
  carries only `result` (spec 0014's metrics table fixes its tag set to `result`); `variant` is on
  the prologue-duration histogram alone, which records warm and cold prologues alike, so it cannot
  stand in. Filtering would need a `variant` tag on the product instrument, a spec change outside
  this checkpoint; the connect entries stay, under the process-wide label.
- **R1-3: fixed.** The class remarks and inline comments in VersionTagSnapshots.cs (both stores)
  and VersionedCache.cs `Store` now say a reader does not see the entry absent outside a rare race
  with the runtime's expiration scan or compaction, which costs one load, never a wrong answer. No
  code change. The regression tests keep asserting zero absent reads: a fresh `MemoryCache` scans
  at most once a minute and none of these caches compacts during the tests, as the review notes.
