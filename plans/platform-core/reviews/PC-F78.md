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
