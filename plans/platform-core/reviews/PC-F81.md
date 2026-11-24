# PC-F81: Job engine gaps after a proven commit

## Implementer notes

### The gaps, and the tests that showed them

PC-B05's FCS-1 fix left two gaps on the proven-commit path, where a batch surfaces as
`DataAccessAmbiguousException(CommitVerdict.Committed)` and the executor runs none of its commit
callbacks (spec 0012 §6.5):

- **(a)** On a proven job completion, the commit callbacks registered by the writes the handler
  enlisted (persist effects that publish events, version-tag bumps) published nothing. JobRunner
  sent only its own hard-coded `job.changed` and `inbox.changed`.
- **(b)** On a proven tick step 2, `ScheduleTick` set `fired = null` and skipped its nudge of the
  poller (spec 0020 §7.6).

I wrote both integration tests first, against platform-core's product code. Each failed for the
reason it names:

- `JobRunTests.A_completion_whose_response_is_lost_after_its_commit_still_sends_the_events_its_enlisted_writes_announce`
  (Snapshot and Locking). An enlisted shipment save carries a persist effect. That effect
  publishes `test.shipped` on the persist batch and bumps `settings`. On platform-core only
  `job.changed` went out (failing `Assert.Single` on `test.shipped`).
- `ScheduleTests.A_tick_whose_step_two_response_is_lost_after_its_commit_still_wakes_the_poller_for_the_jobs_it_fired`.
  On platform-core `Wakes == 0`.

The two existing FCS-1 tests passed on platform-core and still pass.

### What was built

The design is an internal proven-commit seam. It leaves 0012's `ExecuteAsync` and the executor
unchanged.

- `src/core/Tellma.Core/Data/Batches/IBatchProvenCommit.cs` (new, internal) has one member,
  `OnCommitProven(Action)`. A composer registers an announcement on it that needs neither the
  batch's results nor its `BatchOutcome`.
- `DataBatch` implements the seam:
  - `_proven` holds the announcements.
  - `OnCommitProven` refuses once sealed, as `OnCommitted` does.
  - `internal RunProvenCommitCallbacks(Action<Exception> failed)` refuses a batch that has not
    executed and refuses a second run. It runs the announcements once, in order, and hands each
    failure to `failed` while the rest still run.
  - Nothing calls it but its owner. The executor never does.
- `RecordingBatch` implements the seam too. It records the announcement like a commit callback, so
  every replay registers it on the batch that executes. It forwards to an inner batch that takes
  announcements and ignores one that does not. When the view has a failure sink, it wraps the
  announcement with that sink.
  - The sink is now `Action<Delegate, Exception>`, and `WriteRun.CallbackFailed` takes a
    `Delegate`. A failing announcement is therefore attributed to its component exactly as a
    failing callback is.
- Four composers register announcements:
  - **`ClientEventPublisher.Publish(batch, …)`** registers the same `Flush` as the batch's
    announcement. Every event published on the batch goes out once: `job.changed`, an enlisted
    effect's events, the inbox verbs' events.
  - **`Notifier`** now records, per request, the inbox recipients and whether the statement may
    enqueue, and per withdrawal its named recipients (null for every recipient, and for
    `WithdrawEach`). Its announcement `Proven`:
    - wakes the poller when any request may have enqueued;
    - otherwise publishes one `inbox.changed` to the union of the named recipients, which is a
      superset of the changed inboxes;
    - publishes to the whole tenant (`UserIds` empty) when a withdrawal named none;
    - never throws, logging `InboxEventFailed`.
  - **`VersionTagBumpContributor`**'s announcement `Proven` meters with `userTags: 0` and
    publishes `ProvenEvents(plan, batch.Tag)`:
    - each tenant-level event as after any commit (`settings` and `entity:*` with their wire value,
      `permissions` without one);
    - then each user-level column (`permissions`, `preferences`) to the tenant group without a
      value, deduplicated by tag.
    - It reads no outcome. That answers PC-B05's objection that the bump "announces from the
      outcome's snapshot".
    - `Events` was split into `TenantEvents` and the per-user part, with unchanged behaviour.
  - **`ScheduleTick`** registers its nudge as an announcement next to its `OnCommitted` nudge
    (when any job fires).
- Two owners run the announcements:
  - **`JobRunner.AfterProvenCommit(DataBatch?, tenantId, handler)`** wakes unconditionally, as
    before, then runs the batch's announcements. Each failure is logged as `JobCompletionFailed`
    (…`ProvenCommitNext`).
    - Its hard-coded `job.changed` and `inbox.changed` are gone, because the publisher's and the
      notifier's announcements now send them, and from the same batch-coalesced set.
    - In `SendAsync` the announcements now run before the enlisted writes' post-commit phase
      (`AfterCommitAsync(frame, null)`). That mirrors the committed path, where the callbacks run
      inside `ExecuteAsync`.
    - `CompleteAsync` captures the last batch the guarded runner composed.
  - **`ScheduleTick.FireAsync`** captures the last composed batch. Its `Committed` catch runs that
    batch's announcements and logs a failure as the new `SchedulerTickAnnouncementFailed` (Error).
    The pause notices' `inbox.changed` and delivery wake ride the notifier's announcement.
- `src/core/Tellma.Core/README.md` covers the seam in the batches section, the proven-commit
  behaviour in the bump, the publisher and the notifier, and the completion and tick behaviour
  under "Leases, outcomes and the completion transaction".
- `ARCHITECTURE.md` holds no job-engine or batch-callback internals, so nothing there changed.

### Test fixture changes

- **`JobRunDatabase.CommittedEvents`** is now a sink behind the platform's real
  `ClientEventPublisher`, created per scope through `PublisherFor`. Before, it was a hand-written
  `IClientEventPublisher` that registered `OnCommitted` per event. The real publisher's refusals,
  its coalescing and its proven-commit flush are now what the job tests exercise. `InboxDatabase`
  uses the same sink.
- **`SuspendableAudit`** has two arming seams:
  - It gained `LoseResponse(TableName writing)`, which arms the next batch that declares a write to
    that table.
  - The count-based `LoseResponse(int)` now counts only writing batches. A cold settings cache adds
    a read batch to scope creation, which shifted the count. That bit the handler-less FCS-1 test
    once an earlier test bumped `settings`.
  - The tick test arms by `JobStatements.Schedules`, which only step 2's pause writes.
- **`CommittedNotices`** (the fake notifier) records the requests of a proven batch apart, in
  `Proven`. `AssertAnnouncedAfterLostResponse` checks those recipients instead of an
  `inbox.changed` that JobRunner no longer sends itself. The tick test asserts its pause notice
  there.
- **`JobRunFeature`** contributes a scripted `PersistEffect<Shipment>` and the `test.shipped` client
  event.

New unit tests:

- `DataBatchAssemblyTests`: `RunProvenCommitCallbacks` runs once, in order, after execution, with
  each failure handed over; `OnCommitProven` is refused once sealed.
- `RecordingBatchTests`: an announcement reaches the batch and every replay, a failure is reported
  with its delegate, and a batch without the seam ignores it.
- `ClientEventPublisherTests`: a proven commit sends the batch's events once.
- `NotifierTests`: the superset `inbox.changed` plus the wake, and a tenant-wide event with no wake
  for an unnamed withdrawal.
- `VersionTagBumpTests`: `ProvenEvents`.

### Deviations

1. **The internal `IBatchProvenCommit` seam on `DataBatch` and `RecordingBatch`.** It is not part
   of `IDataBatch`, and `ExecuteAsync` and the executor are unchanged. It is a data-layer addition
   that spec 0012 does not describe. Only the job engine calls it.
2. **`inbox.changed` after a proven commit goes to a superset of the affected users.** Spec 0021's
   events table says "the affected users":
   - It goes to every recipient a request named.
   - It goes to the whole tenant when a withdrawal named no recipients.
3. **User-level `cache.changed` after a proven commit goes to the tenant group without a value.**
   Spec 0013 §2.5 addresses user-level events to each affected user with a value. The affected
   users were never read, so the event follows the tenant-level `permissions` pattern and the
   refresh-caches precedent.
4. **JobRunner's FCS-1 events are now produced by the components' own announcements.** On a proven
   composed completion, the announcements run before the enlisted writes' post-commit phase rather
   than after it.
5. **New log event `SchedulerTickAnnouncementFailed` (Error).**
6. **`RecordingBatch`'s failure sink and `WriteRun.CallbackFailed` now take a `Delegate`.** A
   failing announcement is therefore attributed to its component like a failed callback.

### Open risks

- A tenant-wide `inbox.changed` (an unnamed withdrawal on a proven commit) makes every connected
  client of the tenant refetch its inbox summary once. Proven commits are rare, so I judged that
  cheaper than a stale badge. The alternative is to drop that event and leave those clients to their
  next read.
- The hosted proven-commit sites (`SaveRun`, `DeleteRun`, `ActionRun`, `OpenWriteFrame.SendAsync`,
  settings) still run no announcements. Spec 0015 §8 states their callbacks do not run. They could
  adopt `RunProvenCommitCallbacks` later, which would be a decision for spec 0015.
- An announcement registered by a component outside these four is simply never run. That is the
  behaviour of today's callbacks on this path.
- The proven path still does not replace the process's version-tag snapshots
  (`ReplaceSnapshots` is skipped by the throw). That is unchanged, and the next batch's prelude
  re-reads them.

### What reviewers should examine hardest

- Whether any announcement registered here can read a result or outcome:
  - the publisher's `Flush` reads only composed events;
  - `Notifier.Proven` reads only shaped recipients;
  - the bump reads only `plan` and `batch.Tag`.
- `RecordingBatch.OnCommitProven` on a deferred view, and on replay onto a recomposed persist. Each
  registration must reach the executing batch exactly once, never accumulating across replays: a
  replay targets a fresh `DataBatch`.
- `ClientEventPublisher` registering both `OnCommitted` and `OnCommitProven` on a `RecordingBatch`.
  The second goes through `Record` and its guard right after the first.
- The guarded runner's recompose. `sent` must be the batch that actually executed and threw.
- The real publisher now behind `CommittedEvents`. Its per-batch coalescing could hide a duplicate
  that a test used to see.

### Tests run locally

- `dotnet build` of the touched projects; 0 warnings.
- Unit tests, run with the unit filter narrowed to `Tellma.Core.Tests.Notifications`, `.Caching`,
  `.Data.Batches`, `.Jobs` and `.Crud`: 1165 passed.
- Integration tests (`Category=Integration&Category!=Nightly&Live!=true`):
  - narrowed to `IntegrationTests.Jobs`, `.Notifications`, `.Outbox`, `.Blobs.BlobSweep` and
    `.Data.VersionTag`: 338 passed;
  - narrowed to `IntegrationTests.Crud`, `.Caching` and `.Settings`, which cover the
    `RecordingBatch` and `WriteRun` change: 220 passed, 34 skipped.
- `dotnet build Tellma.slnx`: 0 warnings, "Build succeeded".
- `dotnet format Tellma.slnx --verify-no-changes`: its output is clean.
- The two new integration tests were first run against platform-core's product code, where they
  failed as described above.

## Review 1

Scope: commit f99bc1b6 only. Checked against spec 0012 §6.5, spec 0013 §2.5, spec 0020 §5.6 and
§7.6, spec 0021 §3.4 and its events table, PLAN.md §7.1, and the 6 deviations above.

What I ran and checked:

- **The new integration tests on HEAD.** `Tellma.Core.IntegrationTests` was built with 0 warnings,
  then run with `Category=Integration&Category!=Nightly&Live!=true&FullyQualifiedName~response_is_lost`.
  All 7 passed: both new tests (Snapshot and Locking for the completion test), and the two FCS-1
  tests in both isolation modes.
- **The new tests against the parent's job engine.** I used a throwaway detached worktree of
  f99bc1b6, since removed, with `JobRunner.cs` and `ScheduleTick.cs` restored from f99bc1b6^. The
  fixture now calls `IBatchProvenCommit`, so it cannot compile against the parent's product code
  as a whole. The same filter then failed 7 of 7:
  - the completion test, in both modes, on `Assert.Single` of `test.shipped`;
  - the tick test on `Assert.True(fixture.Wakes > 0)`;
  - the FCS-1 tests on the new `Notices.Proven` assertion.
- **The unit tests the change touches.** `Tellma.Core.Tests` was built with 0 warnings and run
  with the filter narrowed to `.Notifications`, `.Caching`, `.Data.Batches` and `.Jobs`: 682
  passed. The commit's own list adds `.Crud` (1165 in total) and the Crud, Caching and Settings
  integration suites. I did not re-run those.
- **CI.** The pc/PC-F81 push run (45315658495) was skipped, so there is no CI result to cite yet.

The orchestrator's questions:

1. **The seam.**
   - `DataExecutor`, `IDataBatch` and `ExecuteAsync` are untouched. The executor still throws
     `DataAccessAmbiguousException(Committed)` before the commit callbacks (DataExecutor.cs:209,
     :247-260, :486, :533, :641), and nothing in it calls the new method.
   - **No announcement reads a result or outcome.** The publisher's `Flush` reads only the events
     composed before execution. `Notifier.Proven` reads the shaped recipients, the `Slices` flag and
     `Connection.TenantId`. The bump's `Proven` reads `plan` and `batch.Tag`. The tick's wake reads
     nothing.
   - **No announcement runs twice.** `_provenRan` guards the second call, and
     `DataBatchAssemblyTests` tests it. `Flush` drains its events, so even a double registration
     through a `RecordingBatch` would send each event once.
   - **No announcement runs without a proven commit.** It runs only in the three `Committed`
     catches (JobRunner.cs:663, :839; ScheduleTick.cs:236). On the batch passed to it, the executor
     has already skipped the commit callbacks.
   - **`sent` is the batch that threw.** The guarded runner composes and executes one batch per
     loop (GuardedBatchRunner.cs:79-88). Its other round trips are reads, which carry no probe and
     so cannot surface `Committed`.
   - **Proof of commit means the body ran.** The probe looks for the body's own inserted roots
     (`DeclareInsertedRoots`), so a `Committed` verdict cannot come from a batch whose guard failed.
   - **Confinement.** Registration is everywhere, but only the job engine runs the seam, which is
     an accepted, stated deviation. `IBatchProvenCommit` is internal.
2. **The audience deviations.** Both stay within the tenant: an event with empty `UserIds` reaches
   only the publishing scope's tenant group, and both payloads carry no data. On load:
   - A tenant-wide `inbox.changed` makes each connected leader tab call `inbox/summary` at once,
     with no jitter (spec 0021 §5.4). It needs a proven commit whose batch carries a withdrawal
     naming no recipients. In the job engine only a handler's enlisted writes produce one: neither
     the completion's own notices nor the tick withdraw anything. I accept it.
   - A `cache.changed` without a value makes the client refresh `me` after up to 10 s of jitter
     (tm-cache-refresher.ts:426-429), the same cost as today's tenant-level `permissions` event.
     `TellmaJson`'s `WhenWritingNull` keeps `value` off the wire, so the client takes the
     no-value branch.
   - Neither event can be narrower without the lost result set. Dropping them instead is the only
     other option, and it also departs from the spec. I accept both as the least departure.
3. **The fixture changes.**
   - The real publisher can no longer show a duplicate event within one batch. Production
     coalesces those too, so no existing assertion now tests something production does not do.
     `JobRunTests.Poll`'s `Published.Count == 4` and `JobServiceTests`' lists were unaffected.
   - Counting only writing batches leaves the 3 `LoseResponse(int)` callers armed on the batch
     their comments name. The handler and handler-less FCS-1 tests still assert
     `Notices.Raised` empty, which only a lost response satisfies.
   - The one test that now passes without the lost response is the new one (R1-1).
4. **Confirmed**, with the caveat stated in R1-1.
5. **Deviations 1-6.** Each is justified.
   - 4 makes the proven path match the committed one, where the callbacks also run inside
     `ExecuteAsync`, before `AfterCommitAsync`.
   - 6 is a mechanical widening.
   - 5's event is unreachable in practice, since every current announcement catches its own
     failures. It is harmless as a guard.

### Findings

#### R1-1 (minor): the enlisted-writes test passes without the lost response it exists to test

- **Where:** test/core/Tellma.Core.IntegrationTests/Jobs/JobRunTests.cs:324-360 (the arming is at
  :342; the fixture's `SuspendableAudit.AfterAsync` is at JobRunDatabase.cs:570-596).
- **Scenario:** every assertion in
  `A_completion_whose_response_is_lost_after_its_commit_still_sends_the_events_its_enlisted_writes_announce`
  also holds on the ordinary committed path: one `test.shipped`, one `cache.changed` for
  `settings` with its wire tag, one `job.changed`, and the committed payload.
  - I replaced line 342 (`fixture.Audit.LoseResponse(batchesAhead: 0);`) with a comment, on HEAD's
    product code. Both the Snapshot and Locking variants still passed.
  - So if the completion ever stops being the next writing batch after the handler's
    `PersistAsync`, the arming lands nowhere and the test keeps passing without testing the
    proven path. That can happen if a validation round starts writing, or the job progress flush
    or another scope's activity stamp runs in between. The two FCS-1 tests do not have this gap:
    `Assert.Empty(fixture.Notices.Raised)` holds only when no commit callback ran.
- **Fix:** have `SuspendableAudit` count the responses it lost, for example
  `public int ResponsesLost`, incremented next to each `throw`. Assert it is 1 in this test, and in
  the other three lost-response tests. Alternatively, assert an outcome only the proven path
  produces, such as `Assert.Empty(fixture.Notices.Raised)` after giving the job a success notice.

### Responses

- R1-1: fixed in the "PC-F81: fix review 1" commit.
  - `SuspendableAudit` now counts the responses it lost in `ResponsesLost`. Both arming paths
    throw through one `LostResponse()` helper that increments it, and `Reset()` clears it before
    each test.
  - All four lost-response tests assert `Assert.Equal(1, fixture.Audit.ResponsesLost)`: the two
    FCS-1 completion tests, the enlisted-writes completion test and the tick test.
  - Vacuity check: I commented out the enlisted-writes test's arming
    (`fixture.Audit.LoseResponse(batchesAhead: 0);`) locally and rebuilt. Both the Snapshot and
    Locking variants then failed on the new assertion (Expected: 1, Actual: 0, JobRunTests.cs:352).
    I restored the line, and the file is byte-identical to the backup taken first.

### CI fix (integration run 45361322163 on b4eb5796)

- **Failure:** the tick test failed on Linux at `Assert.Single(await JobsOfAsync(fired))`, after
  `ResponsesLost` had already read 1. It passed locally because the local order differs.
- **Root cause (test seam, not product):** `IDataBatchAudit.AfterAsync` runs at the end of every
  executed batch, whether or not its body ran. It always receives the batch's declared writes.
  `SuspendableAudit` lost the response of the first batch that *declared* a write to the armed
  table.
  - On CI the take-over test ran just before this one. It moved the settings tag, and the next
    reset moved it again by SQL. That left the host's settings cache warm but stale.
  - So step 2's first attempt failed its system settings guard and committed nothing. The seam
    still threw `Committed` for that attempt, which stopped `GuardedBatchRunner` from
    recomposing. No job was ever inserted.
  - The real probe proves a commit only by finding the batch's rows, so it can never say that.
    (In production, tick step 2 is not probeable at all: it reserves no ids, so a dropped
    connection there surfaces `Unknown`.)
- **Reproduced deterministically:** warm the cache with a tick, then bump the settings tag by
  SQL before arming. With the old seam, the test failed the same way on Windows.
- **Fix:**
  - While a lost response is armed, `SuspendableAudit` reads `CHANGE_TRACKING_CURRENT_VERSION()`
    in `BeforeAsync`. In `AfterAsync` it loses the response only for a batch that change tracking
    saw write: the armed table, or, for count-based arming, one of the batch's declared tables.
  - A batch that wrote nothing, such as a failed-guard attempt, neither loses its response nor
    counts toward `batchesAhead`.
  - The tick test now starts with the warm-cache settings bump, so every run exercises a
    failed-guard first attempt followed by the recomposed attempt that loses its response.
