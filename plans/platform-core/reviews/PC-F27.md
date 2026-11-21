# PC-F27: flaky splitter e2e "a shrinking container clamps the list" (CI red on 89c4708)

## Implementer notes

### Symptom

- CI run 43372513599 failed `e2e/specs/splitter.spec.ts:146` on its first attempt (the PR-tier
  Chromium e2e job) with `expect(received).toBeLessThan(expected)` and "Expected: < NaN". The
  retry passed.
- The NaN is `asked`, which the test parsed from the story's `List: …` readout right after
  pressing End.

### Root cause: the test read the readout before the render (test race)

- The splitter's `keydown` handler measures the container synchronously and sets `size` at once.
  The product is correct: End always lands on the clamp's upper end.
- The story's readout (`List: {{ listSize() ?? 'default' }}`) and the pane's flex basis change
  only on the next zoneless render. Playwright's `keyboard.press` resolves once the key events
  are dispatched, not after that render.
- On a starved runner, the test's `textContent()` ran first and read `List: default`, so
  `Number('default')` gave NaN. `toBeLessThan(NaN)` can never pass, so the poll timed out.
- The failure's page snapshot confirms it. By the time the poll gave up, the readout said
  `List: 663` (the End key had worked), while `asked` was NaN.
- The same race was latent in the drag tests. They read the pane's width right after
  `mouse.move` and `mouse.up`, which set `size` at once but repaint on the next render.

### Fix

- `e2e/specs/splitter.spec.ts` now waits for the showcase app to be stable (the existing
  `appStable` helper, `ApplicationRef.whenStable`) before every read:
  - `listWidth` settles first.
  - A new `reportedListSize` settles first, then parses `List: N`. It fails with the text it
    read if the readout is not a number, so a regression names itself instead of producing NaN.
- The keyboard tests already waited on the readout with `toHaveText` before reading geometry,
  and they keep doing so.
- No retries, timeouts or skips were added, and the product is unchanged.
- I checked the other PC-026 specs (card, page-header, description-list, confirm,
  error-dialog, splitter-touch). This claim was too broad, and review 1 corrected it (R1-2).
  - page-header, confirm, error-dialog and splitter-touch do read geometry only after a
    web-first assertion on the state they depend on.
  - `card.spec.ts` (the heading width) and `description-list.spec.ts` (the row counts and the
    inline test) read right after `goto`, before the render and before the fonts swap in.
    Both are fixed in review 1's commit.

### Evidence

All runs use CI mode (`CI=1`, the production showcase build), Chromium, `--workers 2` and
`--retries 0`. The load was 18 busy threads (`.tmp/pcf27-burn.mjs`: every core but two).

- **Reproduced before the fix.** `--repeat-each 30` on the test under that load: 3 of 30 failed
  with CI's exact signature ("Expected: < NaN", received 429). The snapshot showed
  `List: 663`.
- **After the fix, same load.**
  - `--repeat-each 30` on the test: 30 of 30 passed.
  - `--repeat-each 5` on the whole splitter spec (12 tests): 60 of 60 passed.
- **Gates.**
  - lint and typecheck pass.
  - build:showcase passes.
  - The full PR-tier client e2e in CI mode (chromium + touch, `--workers 2`, no load): 906
    passed, 0 flaky.

## Review 1

Verdict: the root cause is right and removed, not masked. No blocking or major findings.

What I checked and found sound:

- **The signal covers every key and pointer read.** In Angular 22.0.5, a signal write from a
  template listener reaches `ChangeDetectionSchedulerImpl.notify`, which adds a `PendingTasks`
  entry at once (`pendingRenderTaskId = this.taskService.add()`). `ApplicationRef.whenStable`
  waits on that entry. Chromium acks a CDP key or mouse event only after the page's handler has
  run, rAF-aligned pointermoves included. So by the time `appStable` evaluates, `size.set`
  has run and the render is pending. `appStable` therefore waits for exactly the render that
  repaints the readout and the flex basis: after End (:166), after the drag moves (:100, :108,
  :140), and after the arrows (:82, :130, which also sit behind a `toHaveText`).
- **The resize read does not depend on that signal, and need not.** After `setViewportSize`
  (:167), the pane's width comes from the CSS clamp (`min(…px, 66%, calc(100% - …))`), so a
  forced layout gives it without a render. The ResizeObserver only feeds the ARIA values.
  `whenStable` may resolve before the observer's callback lands, but no geometry read waits on
  it, and the `expect.poll` covers the layout. See R1-1 for the one assertion this leaves weak.
- **The product is free of fault.** `onKeydown` measures the container synchronously and sets
  `size` to `round(upper)` before returning. The rendered basis is the same clamp in CSS, against
  the same content box (`clientWidth`). The failure's snapshot (`List: 663` = 0.66 × ~1005) agrees.
- **The new helper is sound.** The hook is installed in the bootstrap `.then` microtask, which
  runs before any test can see the bar. `reportedListSize` fails with the text it read, has TSDoc,
  and the section comment explains why. No test names changed, and no spec or PC id appears in
  the code.
- **`splitter-touch.spec.ts:43-44`** reads the pane height after a `toHaveText` on the readout,
  which the same render updates, so it is safe.
- **My run.** CI mode, chromium, `--workers 2 --retries 0 --repeat-each 10`, the whole splitter
  spec (12 tests, 120 runs), under the implementer's 18-thread burn (`.tmp/pcf27r1-e2e.log`).
  - 119 of 120 runs passed, including 9 of 10 of the fixed test.
  - The one failure (`repeat4` of :162) was "Test timeout of 30000ms exceeded while setting up
    'page'": Playwright never finished creating the browser page under the burn, so no line of
    the test ran. It is load on the runner, not the race.
  - No run anywhere showed "Expected: < NaN" or any other assertion failure.

### Findings

**R1-1 (minor)** `client/e2e/specs/splitter.spec.ts:171`: "keeps the size asked for" can pass
before the render it means to check.

- **Scenario.** The only thing that could rewrite `size` after the shrink is the ResizeObserver
  path (`measure()`, then a render). That callback arrives at the next rendering step, possibly
  after `whenStable` has resolved inside the last `listWidth`. `toHaveText('List: 663')` then
  passes at once on the pre-resize DOM. A regression in which the clamp writes back into `size`
  would therefore pass this test on a fast runner and fail it only sometimes: a flake that
  points at a real bug, not a test that catches it.
- **Fix.** Before the readout check, wait for the observer's render with a value only that
  render produces. For example, `await expect(bar(page)).toHaveAttribute('aria-valuenow', '66')`
  (the clamped share of the shrunk container). Then assert `List: ${asked}`. This also pins the
  documented "clamp re-applied whenever the container resizes" on the ARIA side, which no test
  checks today.

**R1-2 (minor)** The implementer notes say each geometry read in the other PC-026 specs "already
follows a web-first assertion on the rendered state it depends on". Two reads do not.

- **`client/e2e/specs/card.spec.ts:51`** reads the heading's width right after `goto`.
  `.tm-card__heading` is `flex: 0 1 auto` (`tm-card.css:50-53`), so its width is its text's width.
  The showcase fonts are `font-display: swap` (`tellma-core-ui/fonts/fonts.css:15`) and are not
  preloaded (`index.html`). If Noto Sans lands after this read, which is likelier on a starved
  runner, `width` is the fallback font's, and the comparison at :59 fails on a correct page.
- **`client/e2e/specs/description-list.spec.ts:37-40` and :59** run `evaluateAll`/`evaluate`
  right after `goto` with no wait. `evaluateAll` does not auto-wait, and the story renders on a
  scheduled tick after bootstrap, so a read that wins that race counts 0 rows. The same font swap
  can also move a wrap point in `dl-auto-*`.
- **Neither caused this flake**, and neither is in this commit. Both are the same class of race,
  though, and the notes' clean bill is what a later triage would rely on.
- **Fix.** Correct the notes, and either fix both here or open a follow-up. Before the first
  read, wait on the rendered state and the fonts: for example,
  `await expect(heading).toBeVisible(); await page.evaluate(() => document.fonts.ready)` in the
  card test, and a `toHaveCount(5)` on `dl-one`'s pairs, followed by `document.fonts.ready`, in
  the description-list tests.

### Responses

- **R1-1: fixed, with one change to the suggested signal.**
  - The test now waits for the resize's own render before checking that the size asked for
    survived.
  - `aria-valuenow` could not serve as that signal: at the 700 px viewport the 66 % cap still
    binds, so the share reads 66 both before and after the shrink. A first try with it failed
    10 of 10 on its own precondition.
  - The test waits instead on `aria-valuemin`, the 240 px minimum as a share, which grows as
    the container narrows. A precondition first asserts that the new value differs from the
    old one.
  - It then asserts `aria-valuenow` equals the clamped share of the measured `clientWidth`,
    the pane's width is that clamp to within 1 px, and the readout still says `List: {asked}`.
    That also pins "the clamp is re-applied on resize" on the ARIA side.
- **R1-2: fixed, and the notes are corrected.**
  - New `fontsLoaded(page)` in `e2e/support/settle.ts`, which waits on `document.fonts.ready`.
  - `card.spec.ts` waits for the heading to be visible and for the fonts before taking its
    width.
  - `description-list.spec.ts` opens the story through `openStory`. It waits for `dl-one`'s
    five pairs and for the fonts before the layout, inline and type tests read anything.
  - The notes' overbroad claim is rewritten under "Fix".

Evidence: lint and typecheck pass. In CI mode (chromium, `--workers 2`, `--retries 0`,
`--repeat-each 10`), the splitter, card and description-list specs passed 270 of 270.
