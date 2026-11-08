# PC-F05: timing-sensitive e2e tests

## Implementer notes

### Root causes

1. **Double-tap (grid-touch "a double-tap opens the editor …").** The product code is sound. The
   grid has no double-tap detector of its own: `touch-action: manipulation` lets Chromium's gesture
   recognizer turn two taps into a `dblclick`, and `onDblClick` opens the editor. No `Date.now`
   delta is involved, so the product is unchanged. The test was fragile. It sent the pair as two
   `locator.tap()` calls. Each call runs its own actionability checks (visible, stable for two
   frames, receives events) before it dispatches, and each event is stamped with the wall clock.
   On a loaded runner the second tap could land past the recognizer's double-tap window, so the
   pair became two single taps.
   Probe (touch project, grid-editable, raw CDP `Input.dispatchTouchEvent`, scratch spec not
   committed). The recognizer counts taps by the events' `timestamp`, not by arrival time:
   - timestamps 120 ms apart, dispatched 1.5 s apart in wall time: `click` detail 1 then 2,
     `dblclick`, editor open;
   - timestamps 2 s apart, dispatched back to back: detail 1 and 1, no `dblclick`;
   - no timestamps, 1.5 s apart: no `dblclick` (the window is real);
   - no timestamps, back to back: `dblclick`.
2. **Axe scans (entity-picker popup states; grid-entity dropdown dark; grid-a11y-editable
   populated).** The time went into `AxeBuilder.analyze()`'s bookkeeping, not into the rules or
   the waits. Every call re-evaluates the whole axe-core source (1.29 MB) in the page. It then
   runs `runPartial`, opens a new blank page in the context, evaluates the source again there,
   runs `finishRun` and closes that page. Measured per scan (one worker, CI=1 prod build, same page
   state; "direct" means `axe.run` in the page with axe-core already loaded and
   `resultTypes: ['violations']`):

   | state | AxeBuilder.analyze | legacy mode | direct axe.run | direct + resultTypes |
   |---|---|---|---|---|
   | picker, `Al` results | 1.7-3.6 s | 0.96-1.09 s | 0.62-0.68 s | 0.30 s |
   | grid-entity, dropdown open (dark) | 2.1-4.0 s | 1.4-1.8 s | 0.9-1.2 s | 0.36 s |
   | grid-editable, populated | 4.6-7.7 s | 2.7-3.2 s | 2.4-2.9 s | 0.94-1.21 s |

   In the popup-states test, the five scans took 85-90 % of the test time. The fills and waits
   took 1.3-4.6 s; the scans took 9-14 s on a quiet machine and 34-38 s under the other slots'
   load. That is the 30 s timeout.
3. **PC-F03 did not make it slower.** I built the showcase twice from the same tree, once with
   HEAD's `tm-entity-picker.ts` and once with 4c89ae1's (before PC-F03). Then I ran a step-timed
   copy of the popup-states test on both, in alternating rounds (3 rounds × 8 runs each, 2
   workers). Total, median: HEAD 14.1 s, before 14.3 s. Waits, median: 1.8 s and 2.1 s. Scans,
   median: 11.2 s and 11.9 s. Per round (HEAD / before): 17.4/18.6, 14.2/14.0, 11.7/12.4 s. The
   round-to-round swing (machine load) is far larger than any difference between the builds. The
   earlier 16.1 s against 15.5 s was load noise, so there was no regression to fix.

### What changed

- `client/e2e/support/axe.ts`: `expectNoAxeViolations` runs axe-core itself. It loads the source
  into each frame once per document (it checks for `window.axe` first), then runs one `axe.run`
  from the top document, with the same WCAG 2.1 A/AA tags and the same placeholder exclusion. The
  scan still evaluates every rule on every node. `resultTypes: ['violations']` only trims the
  serialized passes, incomplete and inapplicable groups to one node each, and no assertion reads
  those groups. Two parity checks, in the same probe:
  - (a) on every measured state, AxeBuilder, legacy mode and direct `axe.run` returned identical
    rule/node-count summaries for violations, passes and incomplete;
  - (b) on a page seeded with five violations (image-alt, button-name, color-contrast, label,
    link-name), the new scan reported the same rule ids and the same node targets as AxeBuilder.
  Frames: axe-core's frame messaging here answers only same-origin frames, so a cross-origin
  frame inside the scan's scope now fails the scan with a clear error. (Corrected after Review 1:
  `AxeBuilder`'s default `runPartial` mode did scan cross-origin frames, so this gives up a
  capability, not a silent drop. No story embeds such a frame.) None of the scanned stories has a
  frame. No scan is scoped further than before. Measurement showed that scoping the picker
  scan to the story's main element and the overlay container saved nothing measurable
  (0.27-0.30 s either way), because the shell is small. It would also have dropped the shell's
  landmarks and controls from every scan.
- `@axe-core/playwright` is removed from `client/package.json` and the lockfile (it is unused
  now; the lockfile diff is exactly its three entries). `axe-core` stays a direct dependency at
  4.12.1. The comment in `Directory.Packages.props` that pointed at it now names axe-core.
- `entity-picker.spec.ts` "axe floor": the one test per theme with five scans is now five tests
  per theme, each with one scan. A `POPUP_STATES` table holds an ordered step per state, and each
  test replays the steps up to its state. Every state is reached exactly as before and scanned
  exactly as before, with the same assertions on the state.
- `tooltip.spec.ts` "tooltip-visible page is axe-clean (light + dark)" looped two scans in one
  test. It is now one test per theme (found by the search below).
- `grid-touch.spec.ts`: a new `tapTwice(page, target, gapS)` dispatches touchStart/touchEnd pairs
  over CDP with explicit timestamps (each press 30 ms, the pair `gapS` apart in event time). The
  double-tap test uses a 120 ms gap. The events still go through the browser's real touch
  pipeline and gesture recognizer, which synthesizes the clicks and the dblclick itself. The test
  proves the same things: the neighbouring cell's handles, then the editor visible and focused,
  the handles hidden, and Escape restoring them. A new test, "two taps further apart than the
  double-tap window never open the editor", sends a 2 s gap and asserts that the clicks were
  detail 1 and 1, that the cell is focused and that no editor opened. The negative is final, not
  early: the clicks are recorded in the page, and a dblclick would be dispatched right after the
  second click. This test also guards the helper. If the browser ever stopped honouring event
  timestamps, it would fail, because its back-to-back dispatch would then double-tap.
- Step 3 search (same patterns elsewhere):
  - **Wall-clock gesture window, fixed:** tooltip "the surface is hoverable (1.4.13) …". Leaving
    the host starts a 100 ms hide grace (`HIDE_GRACE_MS`), and the crossing onto the panel was an
    8-step `mouse.move`, so 8 CDP round trips. On a loaded runner the step that leaves the host and
    the step that enters the panel can be more than 100 ms apart. The test now installs Playwright's
    clock, pauses it for the crossing, advances it 250 ms (past the grace, as the old
    `waitForTimeout(250)` meant to), resumes it, waits for `appStable` (so a fired hide would have
    rendered) and asserts the panel is still visible.
  - **Several scans in one test, fixed:** the tooltip test above. Every other axe test already
    scans once (checked file by file, including scans inside loops).
  - **Checked and left:**
    - The long-press tests (grid, tooltip-touch) hold the finger until the menu or tooltip is
      visible, which is a wait on a condition.
    - Mouse `dblclick()` (grid-editing) carries `clickCount` explicitly.
    - The drag and auto-scroll tests poll for a condition.
    - The entity-picker timing tests already run on the fake clock (PC-F02).
    - tooltip-touch's `waitForTimeout(700)` / `(400)` are negative checks ("no tooltip after a
      tap", "still shown"). A slow runner can only make them pass early, not fail. The one
      load-sensitive gap in them is the press of a single `tap()`. That is two back-to-back CDP
      messages, with no actionability check between them, against a 500 ms long-press timer, the
      same as every other `tap()` in the suite.
- `CONTRIBUTING.md` "Flaky tests": two new bullets. A gesture with a time window is never two
  separate Playwright actions: use CDP timestamps for a window the browser measures, and the
  paused clock for a window a page timer measures. And one axe scan per test.

### Timings before and after

- Per scan: see the table above. The new runner is the last column.
- Popup-states, five scans in one session (step-timed copy, 2 workers): before (24 runs), median
  total 14.1 s, of which scans 11.2 s, and the worst run 37.4 s; after (16 runs), median total
  3.7 s, of which scans 2.0 s, and the worst run 10.2 s.
- The real tests under the stress runs below (median / max per test, 20 runs each):

  | test | 10 workers | 20 workers |
  |---|---|---|
  | entity-picker axe floor, each of the 10 split tests | 6.1-7.6 s / ≤19.5 s | 12.7-19.1 s / ≤25.9 s |
  | grid-entity "dropdown open (dark)" | 6.5 s / 9.8 s | 11.8 s / 14.9 s |
  | grid-a11y-editable "populated" (light, dark) | 7.0-7.3 s / ≤19.6 s | 14.0-14.2 s / ≤20.7 s |
  | grid-touch double-tap | 4.3 s / 9.4 s | 7.9 s / 14.6 s |

  For comparison, the old popup-states test took 12-25 s per run in the PC-F03 runs, and up to 38 s
  in my baseline. At 20 workers (twice the default, on a 20-thread machine that other slots were
  also building on), ordinary non-axe tests slowed by the same factor. The slowest test of that run
  was entity-picker "arrowing past the fold …" at 31.0 s (it passed; no axe). So the axe tests are
  no longer outliers.
- No timeout was raised and no `test.slow()` was added.

### Stability under load (CI=1 prod build, `--retries 0` unless noted, local Windows, 20 logical CPUs, other slots building)

- The touched specs: entity-picker, tooltip, grid-a11y, grid-a11y-editable and grid-entity on
  Chromium, and grid-touch and tooltip-touch on touch. That is 113 tests.
  - `--repeat-each 20` at the default worker count (10): **2259/2260 passed** (36.7 min). The one
    failure is in a test this change does not touch, tooltip "the wrapper pattern gives disabled
    controls a working tooltip" (see Open risks).
  - `--repeat-each 20 --workers 20`, with `--trace retain-on-failure`: **2260/2260 passed**
    (33.4 min).
- The tooltip hover tests alone ("hover shows …", "one tooltip at a time …", "the wrapper pattern
  …"), `--repeat-each 200`, 10 workers, with traces: **600/600 passed** (23.6 min). So the wrapper
  failure did not reproduce.
- A probe of the paused-clock crossing: the pointer leaves for blank space instead of the panel,
  under the same pause, `runFor(250)`, resume and `appStable` sequence, and then a one-shot count
  is taken. The panel was already gone (5/5). So the crossing test's final assertion would catch
  a fired grace.
- Full PR tier (`--project chromium --project touch --grep-invert "@nightly|@perf|@quarantine"`,
  CI=1 semantics, so retries 1 and `failOnFlakyTests`): **478/478 passed, 0 flaky** (3.7 min).

### Gates

lint, typecheck, lint:test, tokens:check, build, test, api:check, docs:build, size:check and
build:showcase pass. lint and typecheck were re-run after the last spec edit (the tooltip axe
split). Size is unchanged, because no library code changed. The e2e runs above served a genuine
`build:showcase` of this tree. The A/B builds were only copied in and out for the measurement.
The `Directory.Packages.props` edit is a comment only, so no .NET gate applies.

### Deviations

- none. No product code changed (the double-tap path was sound).

### Open risks

- **tooltip "the wrapper pattern gives disabled controls a working tooltip" failed once, 1 of
  2260, under the 10-worker stress run.** It is untouched by this change. The test is `goto`,
  `hover()` on the span that wraps a disabled button, then `toHaveText` on the panel. The panel
  never appeared within 5 s, and the test took 7.2 s. No trace exists (retries 0, and traces are
  kept only on the first retry). The failure did not reproduce in 600 runs of the hover tests
  with traces, nor in the 2260-test 20-worker run.
  - The hover path waits on a 500 ms `setTimeout` (the show delay) while the pointer must stay
    over the host. A plausible cause is a layout or hover-state change under a starved renderer
    within that window, but I could not confirm it.
  - I did not change it speculatively. If it recurs, run it on Playwright's clock (as the
    crossing test now does) with a trace.
  - The orchestrator should track it under LEDGER "Flaky" if CI ever shows it.
- The grid's DOM-move focus reclaim is vetoed for 200 ms after an outside pointerdown
  (`grid-core.ts`, `Date.now() - lastOutsidePointerDown > 200`). That is a wall-clock window in
  product code. A test that relies on the veto could flake if the render that drops focus lands
  more than 200 ms after the press. No current test has flaked on it. It is noted here, not
  changed.
- Event-time stamping relies on Chromium's gesture recognizer reading CDP timestamps, as it does
  today. The negative double-tap test fails loudly if that ever changes.

### What the reviewer should examine hardest

- `support/axe.ts`: that the direct `axe.run` checks the same things as `AxeBuilder.analyze()`.
  Look at the same tags, the same exclusion, `resultTypes` touching only serialization, the frame
  handling (inject into every frame; cross-origin refused), and re-injection after navigation (the
  `window.axe` check is per document).
- That each split popup-state test reaches exactly the state the old sequence scanned. The
  `POPUP_STATES` steps are the old code verbatim, replayed cumulatively.
- `tapTwice`: that the timestamped pair is still honest touch input, and that the negative test
  makes it non-vacuous.
- The tooltip crossing on a paused clock: that the assertion would fail if pointerenter on the
  panel did not cancel the grace (the hide fires inside `runFor(250)`, and `appStable` lets it
  render before the assertion).

## Review 1

Reviewed d67c757 only. Evidence (CI=1 prod build, `--retries 0` unless noted, default 10 workers):

- Parity probe (scratch, not committed): `@axe-core/playwright` 4.12.1 restored from the local pnpm
  store, run against the new `expectNoAxeViolations` on the same page state.
  - Real stories: entity-picker `Al` (light, dark), tooltip hover (dark), grid-editable, tree-grid
    with `include('tm-tree-grid')`, file-preview with the image open. Violations agree (none), and
    the rule/node-count summaries of passes and incomplete agree exactly with an `axe.run` using
    the new context and options.
  - Seeded page (image-alt, button-name, color-contrast, label, link-name, plus a button and an
    image inside an open shadow root, plus a low-contrast `.tm-select__value--placeholder`): the
    same rule ids and node targets, shadow targets included, and the placeholder excluded by both.
  - `include`: a seeded button outside `tm-tree-grid` is ignored by both, and one inside it is
    reported by both.
  - Reload, then a seeded button: reported (axe is re-injected into the new document). A second
    scan on the same document picks up a newly added image (the reused axe sees DOM changes).
  - Frames: see R1-1 and R1-2.
- `@axe-core/playwright` has no other users in the repository (only the comment in axe.ts
  mentions `AxeBuilder`). The lockfile diff is exactly its three entries. `axe-core` 4.12.1 stays a
  direct devDependency.
- The touch project is Chromium (Pixel 7), so `newCDPSession` and the CDP timestamps are safe
  there. grid-touch is never run by firefox or webkit (`testMatch`/`testIgnore`, and it has no
  `@cross-engine` tag).
- entity-picker, tooltip (chromium) and grid-touch (touch), `--repeat-each 10`: **690/690 passed**
  (8.3 min).
- PR tier (`--project chromium --project touch --grep-invert "@nightly|@perf|@quarantine"`, CI
  retries 1 and `failOnFlakyTests`): **478/478 passed, 0 flaky** (3.8 min).
- (c) Coverage is kept. Each split popup-state test replays the old steps verbatim up to its state
  and scans once, so all five states are reached and scanned as before, in both themes. The
  tooltip axe gate scans the same two themes. The paused-clock crossing keeps the old 250 ms
  intent, and the implementer's leave-for-blank-space probe shows the final assertion is not
  vacuous.

### Findings

**R1-1 (major) Same-origin `about:blank` and `srcdoc` frames fail the scan as "cross-origin".**
`client/e2e/support/axe.ts:122` and `:128` compare `location.origin`. For an `about:blank` or
`about:srcdoc` frame, `location.origin` serializes the URL's origin, which is `"null"`, even though
the document inherits its parent's origin. Probe on the tooltip story, with a `srcdoc` iframe and a
script-filled `about:blank` iframe: `location.origin` gives `null` for both, and `self.origin` gives
`http://localhost:4350` for both.
- Failure scenario: any scanned page with such a frame fails with "The frame at about:srcdoc is
  cross-origin (null); the scan cannot reach it." The old runner scanned these frames and reported
  the seeded button and image inside the `srcdoc` frame. The product makes such frames:
  file-preview's `print()` appends a blank iframe (`tm-file-preview-content.ts:320`), and rich-text
  or print previews will too. So a correct page fails with a wrong diagnosis.
- No current test hits it, so it is not blocking.
- Suggested fix: compare `self.origin` (the document's origin) in both evaluates. With axe loaded
  in every frame, `axe.run` from the top document reports the violations inside both frames
  (verified in the probe). Add a check with a seeded `srcdoc` frame to whatever self-test the
  runner gets (see R1-2).

**R1-2 (minor) The cross-origin refusal narrows the old runner, ignores the scan's scope, and the
notes misstate the old behaviour.** `client/e2e/support/axe.ts:115-133`.
- In its default `runPartial` mode, `AxeBuilder.analyze()` did scan cross-origin frames. Probe: a
  `http://127.0.0.1` frame on a `localhost` story doubled the passes (button-name 6 to 12,
  link-name 30 to 60). So the commit message ("instead of dropping out") and the implementer notes
  ("dropping out silently") are wrong about the old runner: this is a loss of capability, not a
  fix. It fails loudly, which is acceptable while no story has such a frame.
- `loadAxe` walks every frame in the page whatever `selector` is. So `expectNoAxeViolations(page,
  'tm-tree-grid')` on a page that has a cross-origin frame elsewhere (for example Chromium's PDF
  viewer frame, if a PDF-preview scan is ever added) throws, although the scan's scope never
  reaches the frame.
- Suggested fix: state the old behaviour correctly in the notes, and in the `loadAxe` TSDoc say
  that cross-origin frames are unsupported, not only "unreachable". Either limit the origin check
  to frames whose owner element lies inside the scan context, or say in the TSDoc that the check
  is page-wide.

**R1-3 (minor) The negative double-tap test's editor check is early, not final.**
`client/e2e/specs/grid-touch.spec.ts:310-315`. The comment says the editor's absence "is final,
not merely early" once the second click has landed. That holds for the dblclick dispatch, which
the `[1, 1]` click counts already rule out. It does not hold for the editor's render.
- Failure scenario: a regression where a second single tap on the active cell opens the editor
  (tap-to-edit). The editor would render a frame later. `toHaveCount(0)` resolves on its first
  poll, so the test passes although its title says the editor never opens. That is the early
  negative D-018 targets.
- Suggested fix: `await appStable(page)` (from `../support/settle`) before
  `expect(editor(page)).toHaveCount(0)`, and reword the comment: the click counts prove no
  dblclick, and the settle proves no editor.

### Responses

R1-1: fixed in 0073b2b. Both origin reads in `loadAxe` now use `self.origin`, the document's own
origin, which an `about:blank` or `srcdoc` frame inherits from its parent. A new self-check
spec, `e2e/specs/axe-runner.spec.ts`, covers the runner on the button story:
- the story alone is clean;
- a `srcdoc` frame with an unnamed button reports exactly `button-name`, and the target is inside
  the iframe;
- a script-filled `about:blank` frame (the print-helper shape) with an `alt`-less image reports
  exactly `image-alt` inside the frame;
- a clean `srcdoc` frame keeps the scan clean.

The runner gained `axeViolations(page, selector?)`, the non-asserting form that
`expectNoAxeViolations` now wraps, so a test can assert on what a scan reports. With
`location.origin` restored as a check, the three frame tests fail with "The frame at about:srcdoc
is cross-origin (null)…", and they pass with the fix.

R1-2: fixed in 0073b2b, per the orchestrator's ruling. A cross-origin frame inside the scan's scope
still fails loudly. The check now covers only the scan's scope: with a selector, a frame is
checked and loaded only when its outermost ancestor frame's owner element matches the selector or
lies inside a match (the walk crosses shadow roots to their hosts). Frames outside the scope are
left alone. The `loadAxe` TSDoc says cross-origin frames are unsupported, and the module comment
states the capability given up against `AxeBuilder`. The implementer note above is corrected. The
claim in commit 1's message ("instead of dropping out") cannot be amended, so this fix commit's
message corrects it. New test in `axe-runner.spec.ts`: a `data:` frame (opaque origin) appended
outside the story's main element makes a whole-page scan throw `cross-origin`, while a scan scoped
to `main.story-main` passes. On commit 1's page-wide check, the scoped half would throw.

R1-3: fixed in 0073b2b. The negative double-tap test now waits for `appStable(page)` before
`expect(editor(page)).toHaveCount(0)`. The comment now says the click counts prove there was no
dblclick, and the settle makes the editor's absence final.

Round-1 verification (local Windows, CI=1 prod build):
- Gates: lint, typecheck and lint:test pass. Only e2e files changed, so no library gate is
  affected.
- The touched specs (entity-picker, tooltip, grid-a11y, grid-a11y-editable, grid-entity and
  axe-runner on Chromium; grid-touch and tooltip-touch on touch), `--repeat-each 10 --retries 0`
  at the default 10 workers: **1180/1180 passed** (15.5 min).
- PR tier (`--project chromium --project touch --grep-invert "@nightly|@perf|@quarantine"`, CI
  retries 1 and `failOnFlakyTests`): **483/483 passed, 0 flaky** (4.2 min). That is the 478 tests
  from before plus the 5 axe-runner self-checks.
- Integration held: the orchestrator holds it until platform-core CI is green (PC-F06).
