# PC-F79: Core flake: job lease remaining 5001 ms after a flush past the final renewal

## Implementer notes

### Root cause

PC-F75's pc run 45184031545 failed
`JobRunLockingTests.A_flush_after_the_final_renewal_extends_the_lease_only_to_the_end_that_renewal_set`.
It read `DATEDIFF(millisecond, SYSUTCDATETIME(), [LeaseExpiresAt])` after the handler's
`RenewFinalAsync(leaseSeconds: 5)` and `FlushAsync`, and asserted 1..5000, but got 5001. There
are two faults. The measurement explains the 5001. The product fault is real, and the old assertion
could not see it.

1. **The measurement.** The lease statements declare `@now datetimeoffset(3) = SYSUTCDATETIME()`,
   which rounds the datetime2(7) clock to the nearest millisecond, sometimes up. `DATEDIFF`
   counts millisecond boundaries from the raw clock. SQL Server on Linux also steps
   `SYSUTCDATETIME()` in ~4 ms ticks, so a statement started a moment after the flush often reads
   the same clock value. When it does, and that value's sub-millisecond part is .5 or more, the
   result is 5001. Deterministic SQL reproduction (case 1). It gives `@now` = `.001`, expiry
   `05.001`, and `DATEDIFF` = **5001**:

   ```sql
   DECLARE @f datetime2(7) = '2026-11-23T10:00:00.0006000';  -- the flush's clock
   DECLARE @m datetime2(7) = '2026-11-23T10:00:00.0009000';  -- the test's read, 0.3 ms later
   DECLARE @now datetimeoffset(3) = @f;
   SELECT DATEDIFF(millisecond, @m, DATEADD(second, 5, @now));  -- 5001
   -- case 2: final renewal at .0001, flush 50 ms later sending CEILING(5 - 0.05) = 5 s
   SELECT DATEDIFF(millisecond,
       DATEADD(second, 5, CAST(CAST('2026-11-23T10:00:00.0001000' AS datetime2(7)) AS datetimeoffset(3))),
       DATEADD(second, 5, CAST(CAST('2026-11-23T10:00:00.0501000' AS datetime2(7)) AS datetimeoffset(3))));  -- 50
   ```
   - Clock resolution, measured with 20 000 `SYSUTCDATETIME()` reads in a loop:
     - `mssql/server:2022-latest` in Docker: 35 distinct values over 140 ms, steps of 4.0–4.2 ms.
     - Windows SQL Server 2025: ~1.2 ms steps.
2. **The product.** After the final renewal, `JobLease.RenewalSeconds` sends
   `ceil(seconds - elapsed)`, measured on the instance's clock. So a flush a few milliseconds
   later sends 5 s again from its own `@now`. That ends the lease later than the end the final
   renewal wrote, by up to a second (repro case 2: a 50 ms overrun). This breaks the rule that
   JobRenewal, JobLease and the Core README all state: a flush after the final renewal extends the
   lease only to that renewal's end. The old assertion measured from the read, not from the final
   end, so it could not catch the overrun. The new test caught it before the fix: two runs (locking
   and snapshot) overran by 180 ms and 84 ms.
   - In production the window is between a tenant's final renewal and the abandonment at the end of
     `LeaveLeasedAsync`. In practice that is the time the other tenants' final renewals take. A
     flush in that window delays the re-claim after the process exits by up to a second.

### What was built

- **Product.**
  - `JobLease.KeepsStopEnd` / `KeepStopEnd()` (`src/core/Tellma.Core/Jobs/JobLeases.cs`). When a
    final renewal judges a lease still held, it records that the lease's end was written
    (`JobRenewal.RenewOnceAsync`).
  - Every later renewal sends that lease's token in a new third table parameter (`GuidList`), and
    `JobStatements.Renew` keeps `[LeaseExpiresAt]` for those tokens. The SQL is
    `CASE WHEN [k].[Id] IS NULL THEN DATEADD(...) ELSE [j].[LeaseExpiresAt] END`. The row is still
    judged, its cancel request is still read, and its progress is still written.
  - `RenewalSeconds`, the estimate rounded up to whole seconds, now serves only when the final
    renewal failed. In that case the database end is unknown, and rounding up is the safe direction,
    because a lease that lapsed early would let another instance run the job while its handler
    lives.
  - No schema change: `GuidList` is already a core table type, so no serialized migration is
    touched.
- **Tests.**
  - The flaky test is renamed `A_flush_after_the_final_renewal_keeps_the_lease_end_that_renewal_wrote`
    and made exact:
    - It reads the final renewal's end with the clock rounded the way the statements round it, in
      the same statement (new fixture helper `JobRunDatabase.LeaseAsync`). It asserts
      `0 < end - now <= 5 s`, which is exact because rounding is monotonic.
    - It waits until the database clock passes that read (`UntilTheClockPassesAsync`), then
      flushes, and asserts that the end after the flush **equals** the final end and that the
      flush wrote percent 50.
    - Before the product fix it failed in both modes, every time. Because of the clock wait, the
      old code's flush always reads a later `@now`.
  - New statement test
    `A_renewal_keeps_the_stored_end_of_a_kept_lease_and_extends_the_others_from_its_own_clock`.
  - Unit tests: the `Renew` golden (the CASE, the third join, the kept table's rows) and
    `A_lease_keeps_the_end_its_final_renewal_wrote_only_once_bound_to_the_hosts_stop`.
  - `JobRow.LeaseExpiresAt` was added.
- **Sibling.** `A_drain_that_times_out_renews_the_running_lease_once_by_the_shutdown_timeout_and_abandons_the_handler`
  (`JobRunTests.Poll.cs`) had the same `DATEDIFF`-against-an-upper-bound shape (≤ 3000 ms). It now
  uses `LeaseAsync` with the exact bound. The other jobs and schedules tests compare against the
  database clock with second- or minute-scale slack, or already read
  `CAST(SYSUTCDATETIME() AS datetimeoffset(3))` (`ScheduleTests.DatabaseNowAsync`), so they are
  exact. `BlobSweepTests.MinutesToExpiryAsync` uses `DATEDIFF(second)/60` against 59..60, which
  absorbs the rounding (outside jobs; left).
- README (`src/core/Tellma.Core/README.md`, the drain bullet) states the kept end and the fallback.

### Deviations

- Spec 0020 §5.2's renew statement gains a third table parameter of kept tokens and a CASE on
  `[LeaseExpiresAt]`. §6.7 does not say what a flush after the final renewal does. The code already
  promised "only to that end", and only the database's own stored end meets that promise exactly.

### Open risks

- If the final renewal failed, or its outcome was ambiguous, later renewals still estimate the end
  on the instance's clock, rounded up (as before). This is deliberate (see above).
- The lower bound `end - now > 0` in the two lease tests assumes the read follows the final renewal
  within its lease (5 s and 3 s). This is unchanged from the old `>= 1 ms` bound.

### What reviewers should examine hardest

- `KeepStopEnd` is set only for leases the final renewal judged held. Check that this proves the
  UPDATE wrote that row's end. A lease with some slots completing: the slots being completed are
  not judged, but they are not renewed later either.
- The kept CASE must never keep an end that a non-final renewal should extend. Only leases bound by
  a final renewal ever reach the kept list.
- The determinism argument of `UntilTheClockPassesAsync`: the final renewal's `@now` is no later
  than the read's rounded clock, which is earlier than the flush's rounded clock.
