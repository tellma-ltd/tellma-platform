# PC-F56: LocalDB assembly cleanup failure after PC-068 (integration-windows)

## Implementer notes

### Diagnosis

- **The failure.** In CI run 44110298098 (1e9332ad), the integration-windows Core suite passed
  (395 passed, 3 skipped). Then `[Test Assembly Cleanup Failure] Xunit.Sdk.TestPipelineException`
  appeared 64 s after the last test output (15:15:12 → 15:16:16). The job log carries only the
  exception type. Its message and stack are not printed at the console logger's default verbosity.
- **The source.** `SqlServerFixture` is the Core suite's only assembly fixture, so the failure is
  its `DisposeAsync`. On an existing server, that method ran one loop on one `master` connection:
  `ALTER DATABASE … SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE …` for each database,
  under SqlClient's 30 s default command timeout. The first error stopped the loop and left every
  later database on the server. This code dates from PC-034 and PC-F47, not PC-068.
- **Intermittent, and not tied to PC-068's code paths.**
  - Run 44113932638 (05467a2f) tested identical code: its diff from 1e9332ad is only
    `plans/platform-core/LEDGER.md`. Its integration-windows job was green.
  - The full Core suite on local LocalDB at 1e9332ad was also green, cleanup included
    (395 passed, 3 skipped, 9.5 min).
  - In green runs of the same job, cleanup takes 4 to 5.5 min, measured from the last test output
    to the summary line:

    | Run | Last test output → summary |
    |---|---|
    | 44104115244 | 14:41:28 → 14:45:57 |
    | 44100563804 | 14:22:38 → 14:26:42 |
    | 44100453494 | 14:20:54 → 14:26:02 |
    | 44113932638 | 15:31:49 → 15:37:15 |

    So the red run failed about a quarter of the way through the drops.
  - None of the 40 most recent platform-core runs shows another cleanup failure. The only other
    integration-windows failure among them, run 43972112319, was a test failure
    (VersionTagLockTests).
  - PC-068's `QueryPipelineDatabase` disposes its `ConnectDatabase` the same way the six older
    fixtures dispose theirs. Its two `CreateInstance` providers are `await using`, and nothing it
    adds runs after its class ends. It adds 2 databases to the roughly 200 that get dropped.
- **Why each drop was slow (measured, local LocalDB 15.0, `.NET` file-based bench in the session
  scratchpad).** Rolling back the idle pooled sessions in a database cost its drop about 3 s,
  whatever their number: 3.0 s for 1, 6 and 12 sessions, and 3.8 s for 24. Eight databases took
  24.2 s with the pools kept and 0.46 s with `SqlConnection.ClearAllPools()` first (21 to 104 ms
  each).
  - So nearly all of the old cleanup's minutes went to killing the suite's own pooled sessions,
    one database at a time.
  - Each of those drops waited on server-side session terminations. A drop on a slow runner then
    had little margin under the 30 s default, which is the most likely cause of the red run.
  - The exact SQL error cannot be recovered from the log. It was either a timeout or a transient
    held-database error (3702 in use, 5061 lock).

### What was built

- **`SqlDatabaseDrop`** (`test/shared/Tellma.Testing.Support/Sql/`), the one drop path:
  - `DropAllAsync` calls `SqlConnection.ClearAllPools()` first. It then drops each database on
    one `master` connection. The drop is an `IF DB_ID(…) IS NOT NULL` guard around
    `SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE`, run under
    `SqlDatabaseCreation.CommandTimeout` (5 min).
  - A run that fails only because the database was held for a moment is rerun on
    `CreateDatabaseRetry.Default`'s waits, through its internal seam overload, under the drop's
    own classifier. The transient errors are 3702, 5061, 5064, 924, 952 and a 1205 deadlock,
    alone or with the 5069 that follows. Each rerun clears the pools again first. Retries are
    logged as test output, or as a diagnostic message outside a test.
  - A failed database does not stop the others. If the connection is lost, the remaining
    databases are left untried.
  - At the end, one `AggregateException` names every database left behind (failed or untried),
    with each failure as an inner exception. Nothing is swallowed: a cleanup that leaves databases
    behind still fails the run, but now it reports all of them, not the first.
- `SqlServerFixture.DisposeAsync` routes through it, after removing duplicate names under the
  list's lock. `SqlDatabaseCreation.Quote` is now internal so both helpers can share it.
- **Tests** (`Tellma.Core.Testing.Tests/Sql/`):
  - `SqlDatabaseDropTests` (unit) covers:
    - the statement;
    - the classifier, including the follow-up error alone, a missing database, the model lock,
      a timeout and a mixed real error;
    - the loop: all dropped, a failure in the middle, a lost connection, cancellation.
  - `SqlDatabaseDropServerTests` (Integration) covers a database with an idle pooled session and an
    open-transaction session being dropped, and a reserved-but-never-created database being passed
    over. The tests ran on both Docker and LocalDB.
- **Effect.** The full Core integration suite on local LocalDB took 3.3 min in total, against
  9.5 min before; nearly all of the difference is cleanup.

### Deviations

1. `ClearAllPools` clears every pool in the process. Where the fixture is a collection or class
   fixture (Identity, `Tellma.Core.Testing.Tests`), other collections still running lose their
   idle pooled connections and reopen them. In-use connections are not touched; they are discarded
   when they are returned.
2. A command timeout is not retried, the same as for creations. The 5-minute bound is the remedy;
   a timeout at that bound is a real hang and should surface.
3. The drop reuses `CreateDatabaseRetry`'s loop and waits through the classifier seam, rather than
   adding a second retry policy. The class remarks say so.

### For reviewers

- Is the classifier too broad or too narrow? 5064 and 924 only clear if the session holding the
  database leaves. The pool clear before each rerun covers this process's sessions; a foreign
  session would exhaust the retries and surface.
- When the connection is lost, the remaining databases are reported untried, not retried on a
  new connection. A broken `master` connection at cleanup means the server is going away, and
  reconnecting to each database in turn would multiply connect timeouts.
- Decision for the orchestrator (not done here; CI configuration is outside this item): run the
  CI test steps with `--logger "console;verbosity=normal"`, or at least integration-windows, so a
  future cleanup failure shows its message and stack and not only the exception type.
