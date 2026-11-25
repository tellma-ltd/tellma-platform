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
  a re-run needs a transient failure of a read under read-committed snapshot.
- So that a recurrence names its cause, `RoundTripBudgetTests` now reports, beside each operation
  above its budget, what was recorded while it ran that costs a warm path a round trip: every
  cache lookup but a hit, every connect result but `warm`, and every re-run.

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
  message now says what was recorded during the over-budget operation; a settings lookup `stale` or
  `miss` there points back here, anything else is a different cause.

### What reviewers should examine hardest

- The claim that `MemoryCache.Set` counts a replaced entry's size out at the limit and drops the
  replaced entry when it refuses the new one. The existing
  `A_full_store_still_replaces_a_tenants_snapshot_and_refuses_a_new_tenant_rather_than_keep_a_stale_one`
  pins the first half; the second half was checked in a throwaway test (a size-2 replacement in a
  size-2 cache holding two entries left the key absent).
- The eviction-reason mapping in `VersionedCache.Evicted` after the switch from `Removed` to
  `Replaced` (`Set_meters_the_entry_it_replaces_as_replaced_or_aged_and_remove_as_removed`).
