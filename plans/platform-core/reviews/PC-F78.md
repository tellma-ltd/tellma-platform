# PC-F78: Client: TmHttpApi post-end NG0205 guards

## Implementer notes

### The defect

`TmHttpApi` resolves `TmSession`, `TmSessionPaths`, `TmLaunch`, `TmTelemetry` and its three seams
through the injector at each use, so that their owners can inject `TmApi` without a cycle. Once
the root injector is destroyed (`ApplicationRef.destroy()` in an app, `TestBed.resetTestingModule()`
in a spec), each of those lookups throws NG0205. PC-F77 R1-3 named three paths that reach them
after the end; while writing the tests I found two more of the same shape.

1. **An answer that lands after the end.** `#conclude` ran `#check`, whose NG0205 was caught and
   returned cast as a `TmApiError` (it is not one), then `#launch.settle`, which made the call
   reject with a raw NG0205.
2. **The version-tags observer.** `setTimeout(() => this.#cache.observe(...), 0)` armed before the
   end fired after it, and the `#cache` getter threw NG0205 inside the timer, as an uncaught error.
3. **A read's retry whose wait the end overtook.** After the wait, `#run` calls `#admit`, whose
   `this.#session.state()` threw NG0205; `#run`'s catch cast it to `TmApiError`. A call started
   after the end failed the same way (raw NG0205 from `#exchange`).
4. **An outdated-client refusal whose worker check the end overtook.** `#verdict` awaits the
   worker's update check for up to 3 s (unordered builds, or a rollback's second verdict). If the
   app ended meanwhile, the `#telemetry` getter after the await threw, and the call rejected with
   a raw NG0205.

### What was built

All in `client/projects/core/tellma-core/src/api/tm-http-api.ts`, with PC-F77's pattern: the root
`DestroyRef` as a field `#app`, and `#app.destroyed` checked where deferred work resumes, before
anything is resolved through the injector. No new flag, no `onDestroy`, no API change (the API
report is unchanged).

- `#conclude` returns a `cancelled` attempt (no retry, the server's build and trace id kept)
  before the response check and the observers once the app has ended. The request log records
  the call as `cancelled`.
- The version-tags timer's callback does nothing once the app has ended. The timer is not
  cancelled on destroy: that would need its handle kept and an `onDestroy` callback, which costs
  more bytes, and a callback that returns is the same outcome. PC-F77 made the same choice for the
  display names' frame.
- `#admit`'s first test now also holds the end, before `#session.state()` is read. It joins the
  caller's cancellation and the session-leaving test in one condition with one throw, which saved
  0.01 KB. This covers both a retry after the end and a call made after it.
- `#verdict` returns `null` when the app ended while the worker was asked, and `#refusal` returns
  a `cancelled` attempt for it, before the verdict event and the session's `sh` read.

`client/projects/core/tellma-core/README.md`, "The app's end": the HTTP layer's bullet replaces
the "not yet covered" paragraph.

Checked and left as they are:
- `send()` reads `#session.check()` synchronously, so a send after the end would throw NG0205 to
  its caller. Nothing sends after the end: the telemetry's and the locale write's `pagehide`
  listeners are stopped on destroy, and a send is never deferred.
- `TmRequestLog`, `TmApiOnline` and the host are injected eagerly, so `#run`'s `handle.end`,
  `#unanswered` and `#attempt` touch no injector after the end.
- A read whose retry wait spans the end keeps its wait timer (at most 30 s) and then rejects
  `cancelled` at `#admit`. Aborting `#cancellable` on destroy would end it at once, but costs
  more bytes, and the reads' owners already abort their own signals at the end (the refresher
  since PC-F77, `resource()` with its component).

### Tests

`client/projects/core/tellma-core/src/api/tm-http-api.spec.ts`, new `describe("the app's end")`,
each with `tmUnhandled` (PC-F77's helper) and `TestBed.resetTestingModule()` under fake timers:

- "cancels an answer that lands after the app has ended, unchecked and unobserved": a tagged 2xx
  write answered after the end rejects `cancelled` (a `TmApiError`), the launch mark stays, no
  tags are observed, the log ends `cancelled`.
- "reports no version tags whose task runs after the app has ended": the body is delivered, the
  app ends, the tags' task runs and observes nothing.
- "cancels a read whose retry falls due after the app has ended": a network failure, the end
  during the 1 s wait, the read rejects `cancelled` and is not sent again.
- "cancels a refusal whose worker check ends after the app has ended": a 412 under unordered
  builds, the worker never answers, the app ends, the 3 s cap passes; `cancelled`, no verdict
  event.
- "sends no call made after the app has ended, and cancels it": a read and a write after the end
  reject `cancelled`, unsent.

Each test failed before the fix (all five with NG0205: four as the cast or raw error, one thrown
from the timer). Each guard was then removed alone, and exactly its own tests failed:
`#conclude` → the first; the timer → the second; `#admit` → the third and the fifth; `#verdict` →
the fourth.

Runs: `ng test core` on tm-http-api.spec.ts (149 green); the full core and core-testing unit
projects, three times each (figures in the commit message); client gates build, lint, typecheck,
lint:test, api:check and size:check; `dotnet build Tellma.slnx` and `dotnet format`.

### Budgets

"." measures 45.03 KB against 44.97 on the base (8cd95bad): +0.06 KB, past the 45 KB ceiling. Per
D-031 the ceiling is raised to 45.5 (the measurement rounded up to the next 0.5 KB), with a
budgetNotes entry. Savings taken: the joined test in `#admit` (0.01 KB). Tried and not kept: a
shared helper for the cancelled attempt measured 0.03 KB more, since gzip favours the repeated
literal. The first three guards alone measured 45.00 after the saving (over the ceiling by under
0.01); the verdict guard adds 0.03. Other held items also raise "."; whichever integrates second
re-measures (D-214). The gap to spec 0024's 50 KB cap on this branch is 4.97 KB.

### Deviations

1. Spec 0024 §10.5 says writes are never cancelled, and §10.3 step 4 delivers only a checked
   response. After the app's end the response check cannot run (the session's injector is gone),
   so an answer that lands then rejects `cancelled`, a write's included. The write itself is not
   aborted; it completes on the server as before. Only the outcome reported to a caller in an
   ended app changes, and `cancelled` is the kind every caller treats as quiet.

### Open risks

- `TmApiOnline` (not changed here) re-arms its reachability probe after the end: a probe in
  flight at the end that fails, or a network failure that lands after the end (`#unanswered` →
  `failed()`), schedules another probe, and failing probes keep backing off up to every 30 s
  while the page is visible. It throws nothing (the probe uses the host, injected eagerly), so it
  is a leak, not an NG0205. A guard in its `#schedule` would cost about 0.02 KB of ".". I left it
  out of this item's scope; it should be a follow-up if wanted.
- A write's caller in an ended app now sees `cancelled` rather than NG0205. `TmPreferences.#failed`
  then records a `tm.prefs.flush-failed { kind: 'cancelled' }` event after the end, which the
  telemetry never sends (its flush listeners are stopped). Harmless, but it is an event after the
  end.

### What reviewers should examine hardest

- The `#admit` condition's order: the end must be tested before `#session.state()` is read, and
  the sign-out exception must still apply only to the session-leaving test.
- Whether `cancelled` is the right outcome for a 2xx write that lands after the end (deviation 1),
  rather than delivering the unchecked body.
- `#verdict`'s `null`: the only branch that awaits is the worker's, so it is the only place the
  end can arrive inside `#refusal`.

## Review 1

### Findings

**R1-1 (major): a read whose network failure lands after the app's end still rejects with a raw NG0205.**
`client/projects/core/tellma-core/src/api/tm-http-api.ts:1005-1021` and `:870-871`. Take an
idempotent read in flight when the root injector is destroyed, whose `fetch` then rejects (a
network failure, or the host aborting it at teardown). `#attempt` returns `#unanswered(call,
false)`. The read's own signal is not aborted by the end, so `#unanswered` calls
`#connectivity.failed()` and returns `retry: { reason: 'network' }` while `onLine()` holds. `#run`
then takes the retry branch, and line 871, `this.#telemetry.event('tm.api.retry', ...)`, resolves
`TmTelemetry` through the destroyed injector. NG0205 is thrown inside `#run`, outside any catch, so
the read rejects with a raw NG0205, not a `TmApiError`. This is the same class as the four guarded
paths, inside this item's scope (TmHttpApi's post-end paths). It also makes README.md:163 ("admits
... no retry whose wait the end overtook") read as complete when one retry path still throws. The
new test "cancels a read whose retry falls due after the app has ended" only fails the attempt
*before* the end, so the telemetry event fires while the injector is alive. Found by reading the
code; I did not run a mutated copy, since this review may change only this file. The trace is
direct: `fail()` after `TestBed.resetTestingModule()` reaches line 871 with nothing in between that
could stop it.
Fix: in `#unanswered`, treat the end like the caller's abort:
`if (call.cancel?.aborted === true || this.#app.destroyed)` returns the `cancelled` attempt with
`retry: null`. That costs about 0.01 KB and fits under 45.5. It also stops a failure that lands
after the end from calling `TmApiOnline.failed()`, which re-arms the probe (see R1-3). Add a test
next to the third one: start a read, `TestBed.resetTestingModule()`, then `rig.host.last().fail()`
inside `tmUnhandled`. Expect `unhandled` to be `[]`, the read to reject `cancelled` as a
`TmApiError`, no `tm.api.retry` event, and one request sent. Then extend the README bullet to say
that a call failing after the end is cancelled too.

**R1-2 (minor): the public meaning of `cancelled` does not cover the app's end, so a write's
caller can't tell what it means.**
`client/projects/core/tellma-core/src/api/tm-api-error.ts:17`. The kind's TSDoc reads "the caller
aborted the call, or the session left this tab". After this change, a write whose answer lands
after the end (deviation 1) also rejects `cancelled`, and that write may have completed on the
server. Deviation 1 is the right reading:
- §10.5's "writes ... cancelled by: never" is about aborting the request, and the write is still
  sent and never aborted, as `TmApi.write`'s "sent once and never aborted" promises.
- §10.3 step 4 already rejects a sent write `cancelled` on the changed-user path, so a `cancelled`
  write whose server outcome is unreported has precedent.
- No current caller misreads it. TmPreferences keeps the keys dirty on `cancelled`, so nothing is
  dropped, and the only other caller testing the kind on a write-like path is the value editor's
  upload, which stays quiet.

The kind's documentation is the contract a future write caller will read, though, and it neither
names the end nor says that the server outcome of a `cancelled` write may be unknown.
Fix: extend the bullet, for example "`cancelled` — the caller aborted the call, the session left
this tab, or the app ended; a write cancelled after it was sent may still have completed on the
server; nothing is shown". Run `api:check`; the report should not change.

**R1-3 (minor): TmApiOnline's probe re-armed after the end. The TmHttpApi trigger belongs in this
fix; the probe-in-flight half can be a follow-up.**
`client/projects/core/tellma-core/src/api/tm-api-online.ts:72-76, 86-93, 123-125`. Its `onDestroy`
cancels the armed timer, but `#schedule` has no end check. Two triggers arm it again after the end:
- **A late HTTP failure.** `TmHttpApi.#unanswered` calls `failed()`, then `#schedule()`. This is
  TmHttpApi's own post-end path, and R1-1's guard removes it at no extra cost.
- **A probe already in flight.** It fails after the end and calls `#schedule()` in `#probe`. Each
  failure then re-arms the next probe, backing off to every 30 s, for as long as the document stays
  visible. That is an unbounded chain of fetches for an ended app. It is not an NG0205, since the
  host is injected eagerly, so it lies outside this item's NG0205 scope.

Fix: take the first trigger here through R1-1. For the second, add a follow-up row, or take it here
if the orchestrator prefers one item per service. Set a `#ended` flag in the existing `onDestroy`
and test it first in `#schedule` (about 0.02 KB). Test: probe in flight, reset, the probe fails, and
no timer and no further fetch follow.

Budget and measurement check (no finding). I re-ran `node scripts/bundle-size.mjs` on the built
tree of 3d6efcd1, and "@tellma/core" measures 45.03 against the 45.5 ceiling. The raise follows
D-031: it adds 0.06 KB, rounded up to the next 0.5. The budgetNotes entry names the bytes and the
savings taken. Every byte the change adds is a guard on a path that a working first frame needs, or
on a path inside TmHttpApi that spec 0024 places in the primary, so none of the added bytes can
move. Beyond this change, one block in the primary has no first-frame use: §5.6's verdict machinery
(`#verdict`, `#askWorker`, `#firstOlderVerdict`, `tmCompareBuilds`; roughly 0.3 to 0.4 KB, my
estimate, not measured). It runs only on a 412 or a 404 `route-not-found`, so it could be loaded with
`import()` if D-247's gap needs more bytes. That would deviate from spec 0024 §10's placement and is
not for this item.

Other points checked, no finding:
- The `#admit` order is right: the end is tested before `#session.state()`, and the sign-out
  exception binds only to the session test.
- Every await that can span the end resumes through a guard: the Contracts load ends in `#admit`,
  the body read in `#conclude`, the retry wait in `#admit`, and the worker check in `#verdict`.
  `#headers` (through `#locale`) runs synchronously right after `#admit`.
- Item (2), the version-tags timer: it is not cancelled, but its callback returns once the app has
  ended. Its closure holds `tags`, `tenantId` and `this` for a single 0 ms task, so nothing leaks.
  Cancelling it would only add bytes.
- Item (4), the outdated-client verdict guard: it is correct. The worker branch is the only await in
  `#refusal`, and a `null` verdict rejects `cancelled` without a retry, before the telemetry event
  and the session's `sh` read.
- Each of the five tests fails on its own guard's removal, by reading. Without the `#conclude`,
  `#admit` and `#verdict` guards, the error is the cast or raw NG0205, so `toBeInstanceOf(TmApiError)`
  fails. Without the timer guard, the timer throws inside `advanceTimersByTimeAsync`.
- `ng test core --include=**/api/tm-http-api.spec.ts`: 149 passed. The pc/PC-F78 CI run 45218163333
  was skipped (pc-f branch), so there is no CI result to cite.

### Responses

- **R1-1:** fixed in the "PC-F78: review 1 fixes" commit (child of 3d6efcd1). `#unanswered`'s
  cancel test now also holds the app's end, so a call that fails at the network level or times
  out after the end rejects `cancelled` with no retry, and does not call `TmApiOnline.failed()`.
  New test "cancels a read whose network failure lands after the app has ended": the app ends,
  then the attempt fails; it expects no unhandled error, a `cancelled` `TmApiError`, no
  `tm.api.retry`, one request and `online()` still true. Without the guard it fails with the raw
  NG0205 from the retry's telemetry, as the review traced. The README's "The app's end" now
  states every path: calls and retries refused, a late answer, a late failure or timeout, the
  late verdict, the dropped tags, and that a write cancelled this way was still sent.
- **R1-2:** fixed in the same commit. The `cancelled` bullet of `TmApiErrorKind` now names the
  app's end and says that a write cancelled after it was sent may still have completed on the
  server. `api:check` is unchanged.
- **R1-3:** fixed in the same commit, both triggers, on the orchestrator's ruling to take it here.
  The late HTTP failure is closed by R1-1. `TmApiOnline` keeps the root `DestroyRef` as `#app`
  (its existing `onDestroy` now goes through it), and `#schedule` arms nothing once the app has
  ended, so a probe in flight at the end that fails is the last one. New test "arms no further
  probe once the app has ended, when the probe in flight fails": it fails without the guard (a
  second probe) and passes with it, with no timer left. The README covers it.
- Budget: "." still measures 45.03 KB against the 45.5 ceiling after these guards (no change at
  two decimals); the budgetNotes entry names them.
- Runs: tm-http-api.spec.ts and tm-api-online.spec.ts (157 green; the two new tests fail without
  their guards); `ng test core` (1242) and `ng test core-testing` (28) in full, green; client
  gates build, lint, typecheck, lint:test, api:check and size:check, green.
