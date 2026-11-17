# PC-F52: VersionTagLockTests lock-wait flake on LocalDB

## Implementer notes

### Cause: the test's barrier, not the guard

CI run 43972112319 (a11022e4, `integration-windows`, LocalDB) failed
`A_tag_the_guard_read_is_held_until_commit_so_a_bump_of_it_waits` with "The batch at 1 was expected
to wait for a lock and finished without waiting", raised by the second barrier (line 123). Batch 1
is B, the bump. B returned normally (the barrier awaits a finished batch first, so a failed B would
have surfaced its own exception), so B committed its bump while A, still running, held no lock on
the tag row.

The barrier was `WaitForLockWaitersAsync(count, …)`: it waited until **at least `count` requests in
the fixture database had any `LCK%` wait**. It never checked *whose* wait it saw, nor *which* lock.
The first barrier stood in for "A waits at the gate, past its guard, holding the guard's shared
lock on the tag", but any lock wait in the database satisfied it: another session's, or A's own
before it reached the guard. When that happens, B starts while A has not yet run its guard. B is a
short batch (no prologue, no guard, one bump), so it reaches the tag row first and commits, and the
second barrier then finds B finished. That is exactly the CI message.

Evidence:

- **The mechanism reproduces deterministically.** A scratch variant of the old test (built to
  `.tmp/pcf52-bin4`, not committed) adds one foreign request that waits on the gate before A starts,
  so the old first barrier is satisfied by a wait that is not A's. 5 runs: 4 failed with the CI
  message verbatim ("The batch at 1 was expected to wait for a lock and finished without waiting"),
  and 1 failed with A's `50412 StaleVersionTag` (B's bump landed before A's guard read). The same
  foreign waiter against the new barrier (`.tmp/pcf52-bin5`): 10/10 green.
- **The product holds the lock.** The new test reads `sys.dm_tran_locks` while A is paused at the
  gate: A's session holds a granted `S` lock on the tag row's key, and B's session has a waiting
  request on that same key, blocked by A. A diagnostic build logged every barrier observation
  (session, blocker, wait type, wait resource, current statement) across 120 class runs on LocalDB
  (three loops in parallel) and three full `Tellma.Core.IntegrationTests` runs on LocalDB (373/373
  each): about 1,950 observations, every first-barrier wait being A's gate read and every second
  one B waiting on the expected tag or stamp key, blocked by A. No natural recurrence in that load.
- **PC-050 is not in this path.** The test drives `DataBatch` directly with the fixture's
  connect-prologue stand-in: no `IScopeConnection`, no guarded runner, no request-context
  initializer runs. PC-050's data-layer diff (the connect writes kept out of `WrittenTables`, the
  attempt start time, the audited writes) changes nothing in the text these batches send.
- **The extra wait in CI is not recoverable from its log**, which records only the message. The
  barrier counted every lock wait whose request's database was the fixture's. Candidates under the
  full assembly's load on one LocalDB instance: a short lock wait of A before its guard (the
  prologue and the guard declare table variables, whose tempdb work runs in the fixture
  database's request), or a system or other session's lock wait in that database. None occurred
  locally; the fix makes the test independent of which it was.

### Fix (test only)

- `WaitForBlockedAsync(scope, holder, …)` replaces the count. It waits until **the scope's own
  batch session** has an `LCK%` wait whose `blocking_session_id` is **the session expected to hold
  the lock**, and returns that session. Each batch's session comes from the fixture's per-scope
  connection provider, which now records `SqlConnection.ServerProcessId` of the connection it
  opened last (`DataBatchDatabase.LastSessionOf`; a re-run's reopened connection replaces it).
  The side transaction and the deadlock test's rival report their own `@@SPID`.
  - First barrier: A blocked by the gate holder. Only the gate read waits on that session, and
    the gate follows the guard in A's text, so A has run its guard in this attempt.
  - Second barrier: B blocked by A's session. Nothing but A's locks satisfies it.
  - A batch that finishes before its wait is observed still fails at once, as before.
- `TagLocksAsync(name)` reads the lock requests on a tag row's key from `sys.dm_tran_locks` (the
  row's `%%lockres%%` in the clustered index's hobt). The two "held until commit" tests and the
  deadlock-victim test assert A's granted `S` on the tag row while it is paused; the bump and
  deactivation tests assert B waits on that row. The serialisation tests assert B waits on the
  first name both lock (`entities`, `permissions`, `settings`), as their comments state.
- README and the class summary describe the barrier as it now is.

### Why the test now catches the real defect deterministically

A mutant whose guard reads the expected rows under `READCOMMITTEDLOCK` instead of `REPEATABLEREAD`
(so it releases the shared locks after the read) fails 6 of the 9 tests, the target among them, on
every run: the target fails at the assertion that A holds `S` on the tag row before B even starts,
and the others fail with "expected to wait for a lock that session N holds and finished without
waiting" or with B waiting on the wrong row. Neither assertion depends on timing: the only time
bound is the 30-second ceiling of a wait that never comes.

### Stability

From the final build, the class alone (9 tests per run):

- LocalDB (SQL Server 2019 Express): 40/40 green; another 40/40 from an earlier build of the
  change, run while the solution's unit filter loaded the machine.
- SQL Server 2022 container: 39/39 green runs that executed tests. One more launch never ran any
  test: the xUnit runner's assembly-info query read "Waiting 10 seconds for foreground threads to
  exit..." mixed into its JSON and reported a catastrophic failure before discovery. That is the
  runner's launch, not the class; it is listed so the count is complete.
- The full `Tellma.Core.IntegrationTests` integration filter: 376/376 on Testcontainers, 373/373
  (3 skipped by environment) on LocalDB.

### Not changed

- No product change: the guard read's lock is held to `COMMIT` (observed directly).
- `DEADLOCK_PRIORITY HIGH` is set on pooled connections by the deadlock-victim test's rival and by
  two other classes' rivals. Whether the pool's reset clears it was not examined; no test in this
  class depends on another session's priority, so it is left as it is.

### What reviewers should examine hardest

- That `blocking_session_id = holder` on the scope's own session is the exact state each test
  claims (in particular the first barrier implying "past the guard").
- That `LastSessionOf` names the right session when a batch re-runs (the deadlock-victim test's A
  re-runs on a fresh attempt after 1205).
