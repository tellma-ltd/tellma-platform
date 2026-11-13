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
  error-dialog, splitter-touch). Each geometry read there already follows a web-first
  assertion on the rendered state it depends on.

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
