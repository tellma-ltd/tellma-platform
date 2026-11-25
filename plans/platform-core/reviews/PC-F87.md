# PC-F87: tab-bus close flake

## Implementer notes

### The failure

CI 45387562810, client-windows (PC-114 integrate, 80f95902; PC-114 does not touch the tab bus):
`tm-tab-bus.spec` "closes its channels when the app ends" failed in `drain()`. Its
`vi.waitFor` (default 1 s) still saw `[]` where the fresh `BroadcastChannel` listener should have
heard `'mark'`. The CI log was not read (D-294). The analysis below rests on the ledger's failure
text and on probes run locally in real Chromium through the unit runner (`ng test core --include`).
The probes were throwaway spec files, deleted before the commit.

### Root cause

The ledger gave three candidates: (a) the listener was created after the post, (b) a channel was
closed early, (c) BroadcastChannel latency under load. Probes in one page, on an idle machine:

| Probe | What it does | Result |
|---|---|---|
| A | Sender opened a task earlier; fresh listener, then post in the same task (the old `drain`) | 1500/1500 heard; p50 0.5 ms, max 2.8 ms |
| A2 | Sender and listener both opened in the task of the post | 1500/1500 heard; max 2.6 ms |
| B | Post, then open the listener | 0/300 heard (spec-conformant: destinations are fixed at the post) |
| C | As A, page main thread blocked 40 ms of every 50 ms, plus a MessageChannel flood | 300/300 heard; max 40 ms |
| D | Earlier end, 5 posts, later fresh end hears the mark: did the earlier end hear all 5 first? | 0/500 violations |
| E | As D, but the mark comes from a second sender in the same page | 0/500 violations |
| F/G | Page main thread stalled 1.1 s right after the post, old `vi.waitFor` vs new wait | 10/10 ok for both |
| Load | Browser process kept busy (500 / 2000 / 6000 queued 512 KB posts on an unrelated channel), then the old drain vs the new wait | mark landed at 269 / 801 / 1770 ms. At 1770 ms the old drain failed with "expected [] to include 'mark'", the CI signature. The new wait passed. The page stayed responsive throughout (one 20 ms ticker fired 90 times in those 1770 ms). |

- (a) and (b) are refuted. In Chromium, a listener opened in a page hears every later post from
  that page (A, A2: 3000/3000), and posts reach the page's ends in post order, across senders (D,
  E). Nothing in the failing test closes the user channel's sender or the listener before the
  wait. The bus's own channel is closed, but that is the test's intent and has no bearing on the
  listener.
- (c) is the cause. Chromium carries every post through the browser process, even between two ends
  in one page. When the browser process is busy, delivery takes seconds while the page itself is
  idle and responsive (Load row), and in-page stalls barely matter (C, F/G).
  - On the windows-latest runner, `ng test` runs the core suite's files in parallel pages, each in
    its own Playwright context, all in one Chromium browser process (PC-F86 counted 12 pages at
    once, D-294). Playwright's CDP traffic shares that process too.
  - A delivery over 1 s therefore needs only a busy browser process, not a starved page.
  - `vi.waitFor` then fails at 1 s on wall time alone. It also rejects at its timeout without a
    final check. That is why the report shows `[]` even when the mark lands just afterwards
    (probe: mark heard at 1770 ms, failure reported at about 1000 ms).
- The PC-F86 delay harness is not involved. It is local orchestrator tooling that CI does not run,
  and the Load probe reproduces the failure without it.
- Cross-talk on the shared `tm:acme*` names: none possible from other spec files.
  - @vitest/browser-playwright 4.1.9 opens a separate `browser.newContext()` per session
    (`createContext`), so parallel files never share a storage partition, and BroadcastChannel is
    scoped to one partition.
  - Files of one session run in order in one page (`isolate: false`), and Angular's TestBed reset
    destroys any real bus between them.
  - Any spec that does reach the real `TmBroadcastTabBus` with slug `acme` lives in its own page's
    order, and the specs that stand in for other tabs provide a fake `TmTabBus` (`TmRecordingTabBus`,
    `TmFakeTabBus`, the session and leader specs' own buses).
  - Within this spec, every channel the spec opens is closed in `afterEach` (now through
    `OtherTab.closeAll()`, which also covers a test that fails mid-way).

### What was built

Test-only. `client/projects/core/tellma-core/src/state/bus/tm-tab-bus.spec.ts`:

- **Long-lived listeners.** `OtherTab` opens two ends of its channel: the end that posts and records
  `heard`, and a *witness* end that hears the tab's own posts. Both open in `beforeEach` through
  `OtherTab.open()`, before the bus posts anything. `open()` returns only once the channel has
  delivered a drain mark to the witness, so the listener is proven live rather than assumed live.
- **`drain()` waits on delivery itself.** It posts a numbered mark that the bus ignores (no envelope)
  and waits until the witness heard that exact mark. Unique numbers are needed now that the listener
  lives across drains. The ordering facts in D and E make this a flush of everything the page posted
  on the channel before the mark, the bus's own posts included. `heard` leaves the marks out.
- **Bounded by a named condition.** Every wait goes through `until(condition, description)`, which
  is `tmWaitUntil` with one real task per step. It uses the shared 4 s deadline
  (`TM_WAIT_DEADLINE_MS`, below the 5 s test timeout) and an error that names the condition.
  - I used `tmWaitUntil` rather than `tmSettleUntil` on purpose. `tmSettleUntil` calls
    `TestBed.tick()` at each step, and in "closes its channels when the app ends" the testing module
    has already been reset. `TestBed.tick()` would build a fresh module there
    (`TestBedImpl.tick` injects `ApplicationRef`).
  - No rendering is involved anywhere in this spec. The two tests that need the profile effect to
    run call `TestBed.tick()` explicitly, as before.
- **Every `vi.waitFor` is gone.** The spec's other waits, on the bus's posts reaching another tab,
  also gave the browser-process round trip 1 s. They now drain the channel and then assert the exact
  contents. Two assertions are tighter as a result:
  - The bfcache test asserts `user.heard` exactly instead of `toContainEqual`.
  - "closes its channels when the app ends" now drains the device channel too, before asserting it
    heard nothing. Before, it relied on the user channel's drain.
- (Review 1 fixes) `pause()` is gone. "holds the posts made before the Core chunk loads" drains both
  channels before it asserts that neither heard anything, so that absence is proven as well.
- With the browser process loaded as in the Load probe (6000 posts, mark at about 1.8 s), the new
  spec passed all of its assertions, including the failing test, through a temporary copy that was
  deleted afterwards.

### Deviations

The specs are untouched, and ARCHITECTURE.md is unaffected: this is a test-only change. One
deviation, from the ledger row rather than a spec (recorded in Review 1 fixes):

- The row asks that `drain()` "wait on delivery itself, not wall time", and says that raising the
  waitFor budget alone is not a fix. The wait cannot be free of wall time. What it waits for is
  delivery latency in the browser process, and no condition inside the page can stand in for that
  delivery, so any wait on it must be bounded.
  - The bound is now the repo's shared deadline (`TM_WAIT_DEADLINE_MS`, 4 s, below the 5 s test
    timeout) rather than a budget local to the spec.
  - What goes beyond a raise:
    - the long-lived listeners, opened and proven live before the bus posts anything;
    - the numbered marks, so that each drain waits for its own;
    - an exact drain in place of every former `vi.waitFor` and of the one remaining wall-time
      pause;
    - the final check (the condition is tested before the deadline at every step, which
      `vi.waitFor` does not do);
    - the named error, which reports the channel and the mark.

### Tests run

Corrected in Review 1 fixes; this list now matches the commit messages.

- `ng test core --include=…/tm-tab-bus.spec.ts`: 10/10, three consecutive runs at commit 1 and
  three again after the fixes. The probe runs above come on top of these.
- `eslint` on the file: clean.
- Client gates of the touched package: `lint`, `typecheck`, `lint:test`, `tokens:check`, all exit 0.
  `tokens:check` was not re-run for the fixes, which touch no token.
- `dotnet build Tellma.slnx`: Build succeeded, 0 warnings, 0 errors. `dotnet format Tellma.slnx
  --verify-no-changes`: clean.
- Left out of PLAN.md §7.1 item 4: `build`, `api:check`, `docs:build` and `size:check`. Only a spec
  file changed, and none of them builds or reads a spec (the package build, its API report, its docs
  and its bundle exclude `*.spec.ts`). No e2e spec was run: the change is confined to a unit spec, which no e2e exercises.
- CI: none yet. It runs at integration.

### Open risks

- A browser-process delay over 4 s would still fail, but now with an error naming the channel and
  mark, as every `tmWaitUntil` would. That is the repo's deliberate bound, below the 5 s test
  timeout.
- The drain's flush guarantee rests on Chromium's in-page ordering (D, E), which the HTML spec also
  requires: each post queues one task per destination, in order. Firefox and WebKit are not in the
  unit runner.
- `TmTabBus.ɵopened` says a message another tab posts "from then on" reaches this one. Across
  processes, that rests on the browser handling this tab's channel registration before the other
  tab's post. It does in practice, but no spec can pin it. This is not in scope here and is noted
  only for completeness.

### What reviewers should examine hardest

- Whether the `drain()` doc's flush claim ("every end heard what came before it") holds for every
  call site, notably where the bus, not the `OtherTab`, made the earlier posts (probe E).
- Whether `OtherTab.open()`'s first drain could leak a mark into any assertion. Marks are filtered
  from `heard`, and the bus rejects them as non-envelopes.
- Whether choosing `tmWaitUntil` over `tmSettleUntil` is acceptable under CONTRIBUTING.md's rule.
  The rule names both.

## Review 1

Reviewed at 7bf78094. Re-run by the reviewer:

- `ng test core --include=…/tm-tab-bus.spec.ts`, three consecutive runs: 10/10 each.
- The same, once under the 300 ms lazy-import delay (`inject_lazy_delay.py`, 35 sites in 25 files,
  MessageChannel ticks until `performance.now() + 300`): 10/10. Reverted with `git checkout`; the
  tree is clean.
- `eslint` on the file: clean. `tsc -p projects/core/tellma-core/tsconfig.json` (specs included):
  clean.
- Load reproduction, with temporary copies of the old (7bf78094^) and new spec, deleted afterwards
  together with their failure screenshots:
  - In-page load: 6000 posts of 512 KB on an unrelated channel, made just before the failing
    test's drain. The old spec failed "closes its channels when the app ends" at 1006 ms with
    the CI signature, `expected [] to include 'mark'`. The new spec passed every assertion of
    that test after a 2679 ms wait.
  - Cross-page load, closer to CI: a second spec file in its own runner page, in the same
    Chromium, posting 512 KB messages for 15 s. The old spec failed 3 of 10 tests: the
    user-change test, the bfcache test, and the app-ends test with the CI signature. The new
    spec passed 10/10 under the same load.
  - Under a heavier in-page load (with an in-page receiver deserializing every 512 KB post), the
    new spec also failed, at 4697 ms, with the named error ("the channel tm:acme:0b4f delivered
    drain mark 39"). This is the open risk the notes state, and the error now names what it
    waited for.

Verdicts on the orchestrator's points:

- (a) Cause. Confirmed. Delivery latency through a busy browser process reproduces the CI failure
  with the page responsive (the old `vi.waitFor` gave up on time at 1006 ms), and also with the
  load in another page. No registration race was needed to reproduce it. `vi.waitFor` rejects at
  its timeout. `tmWaitUntil` checks the condition after every step before it checks the deadline,
  so it does make a final check (`tm-app.testing.util.ts:327-335`).
- (b) Cross-talk. Confirmed. @vitest/browser-playwright 4.1.9's `createContext(sessionId)` calls
  `browser.newContext()` per session, unless `persistentContext` is set. Neither the Angular
  unit-test builder nor the workspace sets it. The builder defaults `isolate: false`. Only this
  spec opens real `tm:acme*` channels directly. A real `TmBroadcastTabBus` elsewhere is destroyed
  by the TestBed reset. `OtherTab.closeAll()` runs in `afterEach`, also after a failure part-way
  through `beforeEach`. Deleting the current element while iterating a `Set` is safe.
- (c) `tmWaitUntil` with a real-task step instead of `tmSettleUntil`. Correct, and not a deviation.
  `TestBedImpl.tick()` does `this.inject(ApplicationRef)`, which would build a fresh testing module
  after "closes its channels when the app ends" resets it. CONTRIBUTING.md's rule covers waits
  across work that may load a chunk with `import()`. No wait in this spec crosses one: the bus,
  metadata store and session code it drives contain no `import()`, and the delay-harness run
  passed. `tmWaitUntil` with a real-task step is the primitive that `tmSettleUntil` itself builds
  on, with the same deadline and a named error.
- (d) The tightened assertions are right and not brittle.
  - Bfcache: before the bus's post, nothing reaches the user channel's `OtherTab` end. The tab's
    own posts never reach its own end, device-channel traffic is on another name, and drain marks
    are filtered. So `toEqual([…])` is exact.
  - App ends: the bus's post after `destroyed` is dropped. `device.drain()` now flushes anything
    the page posted on the device channel, so `device.heard` being `[]` is now a proven absence.
    Before, the test only relied on the user channel's drain.
- The drain's flush claim holds at every call site, including the ones where the bus made the
  earlier posts (probe E). The bus's ends ignore marks, which are not envelopes. `heard` filters
  them. `open()`'s first drain cannot leak into an assertion.

### Findings

0 blocking, 0 major, 3 minor.

**R1-1 (minor): one negative check still treats a quiet channel as proof that nothing was sent.**
`client/projects/core/tellma-core/src/state/bus/tm-tab-bus.spec.ts:262-263`. In "holds the posts
made before the Core chunk loads", the test runs `await pause(); expect([device.heard,
user.heard]).toEqual([[], []])`. That is a 50 ms wall-time wait before a negative assertion. The
spec's new header comment says the spec "never concludes from a quiet channel that nothing was
sent". `pause()`'s doc assumes the very thing under test, that no channel is open yet.

Failure scenario: a regression makes the bus post before it holds the posts, for example by
opening the channels from an earlier source of the slug, and still delivers the queued posts once.
Under the browser-process load reproduced above, the early delivery takes longer than 50 ms. The
check sees `[]`, and the later exact assertions still see one message each, so the test passes.

Fix: replace `await pause()` with `await device.drain(); await user.drain();`. A drain flushes
everything this page posted on the channel, the bus's posts included. The check then proves the
absence, and `pause()` can be removed.

**R1-2 (minor): the record of tests run contradicts itself.** The commit message's "Tests:" line
lists "client lint, typecheck, lint:test, tokens:check; dotnet build and format". The review
file's "Tests run" (`plans/platform-core/reviews/PC-F87.md:95-101`) lists only the unit runner and
`eslint` on the file, and says "Not run: the full client gates". PLAN.md §7.1 item 4 has reviewers
check that list, and one of the two is wrong.

The reviewer's own `eslint` and `tsc` runs are clean, so nothing is broken. But the record can't be
relied on. It also leaves out build, api:check, docs:build and size:check, for which "spec-only:
not affected" is the reason.

Fix: make "Tests run" match what actually ran, and name the item-4 gates that were left out and
why.

**R1-3 (minor): what cures the flake is the deadline, and the record does not say so against
the ledger row.** The LEDGER row asks that `drain()` "wait on delivery itself, not wall time" and
says "Raising the waitFor budget alone is not a fix". The old drain already waited on delivery,
through a fresh listener hearing the mark. With the registration race refuted, the long-lived
witness is hardening, not the cure. What turns the reproduced failure green is the move from 1 s
to the shared 4 s deadline (`TM_WAIT_DEADLINE_MS`), with a final check and a named error. The
reviewer's heavier load shows this: the new spec fails at 4.7 s.

That choice is right. With cause (c), any wait has to be bounded, and 4 s is the repo's bound below
the 5 s test timeout. But it is the outcome the row warned against, and the commit says
"Deviations: none".

Fix: add a sentence to the notes (or a Deviations line), saying that the row's "not wall time"
cannot hold for latency in the browser process. The wait stays bounded by the shared deadline
rather than a budget local to the spec. The long-lived witness, the exact drains at every former
`vi.waitFor`, and the named error are what go beyond the raise.

### Responses

- **R1-1: fixed in fix 1.** "holds the posts made before the Core chunk loads" now runs
  `await device.drain(); await user.drain();` before its negative assertion, in place of the 50 ms
  `pause()`. Each drain flushes everything the page posted on its channel, the bus's posts
  included, so the empty `heard` lists prove that nothing was sent. `pause()` had no other user and
  is removed, so no wall-time wait is left in the spec.
- **R1-2: fixed in fix 1.** "Tests run" in the Implementer notes now lists what actually ran, and
  matches the commit messages: the unit runner three times at each commit; `eslint` on the file;
  `lint`, `typecheck`, `lint:test` and `tokens:check`; and `dotnet build` and `format`. It also
  names the item-4 gates left out (`build`, `api:check`, `docs:build`, `size:check`) and why: only
  a spec file changed, and none of them builds or reads a spec.
- **R1-3: fixed in fix 1.** Recorded as a deviation from the ledger row, in the Implementer notes'
  "Deviations" and in the fix commit's Deviations. The wait is on delivery latency in the browser
  process, and no in-page condition can replace that delivery, so the wait stays bounded by wall
  time. That bound is now the shared deadline (4 s) instead of a budget local to the spec. What
  goes beyond a raise:
  - the long-lived listeners, proven live before the bus posts;
  - the numbered marks;
  - an exact drain at every former `vi.waitFor`, and at the former pause;
  - the final check;
  - the named error.
