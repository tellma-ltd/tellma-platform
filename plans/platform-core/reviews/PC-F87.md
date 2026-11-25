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
- `pause()` stays where nothing can be in flight (no channel is open before the Core chunk loads).
- With the browser process loaded as in the Load probe (6000 posts, mark at about 1.8 s), the new
  spec passed all of its assertions, including the failing test, through a temporary copy that was
  deleted afterwards.

### Deviations

None. The specs are untouched. ARCHITECTURE.md is unaffected: test-only change.

### Tests run

- `ng test core --include=…/tm-tab-bus.spec.ts`: 10/10, three consecutive runs (plus the probe runs
  above).
- `eslint` on the file: clean.
- Not run: the full client gates and CI. The change touches no production source, API golden or
  bundle.

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
