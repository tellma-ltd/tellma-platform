# PC-F01: quarantine five flaky e2e tests (PC-000)

## Implementer notes

### Trigger

CI run 42451990728 on platform-core sha 8a1ebf8 went red in the `client` job. The PR-tier e2e step
reported "5 flaky, 435 passed" (3.2 min, Linux, Chromium). Since PC-000 the Playwright config sets
`failOnFlakyTests` on CI, so those five fail the run. `gh run view 42451990728 --log-failed`
confirms the orchestrator's list exactly. Each of the five failed its first attempt (`✘`) and passed
`retry #1` (`✓`). None failed twice, so none is a deterministic failure. All five also passed in
the full local PR-tier run during PC-000 (Windows, dev server, 440 passed).

### What was built

- A Playwright tag `@quarantine`. The PR tier excludes it: `ci.yml` and the CONTRIBUTING.md PR-tier
  command now use `--grep-invert "@nightly|@perf|@quarantine"`. The nightly tier still runs it:
  `nightly-tier.yml` and the CONTRIBUTING.md nightly command use
  `--grep "@nightly|@cross-engine|@perf|@quarantine"`. That is Chromium only, since none of the five
  is `@cross-engine`, and under `failOnFlakyTests` a flake stays visible as a red nightly.
- The five tests carry `{ tag: '@quarantine' }`, using the `tag` option so titles and test ids stay
  unchanged, plus an inline comment saying why.
- CONTRIBUTING.md: a tag-table row and a "Flaky tests" section with the rule: a flaky test fails
  CI, gets quarantined, is tracked and fixed, and is never left. The fix removes the tag in the same
  change. The no-prefix rule now covers `@quarantine` as well.
- ARCHITECTURE.md CI/CD: one sentence on the flake rule and the quarantine tag. Workflow header
  comments updated.
- `failOnFlakyTests` and the CI retry count are unchanged.

### Failure modes per test, from the CI log (for the root-cause fix)

Line numbers are as in the CI log. After this change the tests start at entity-picker.spec.ts:584 and
:607, grid-entity.spec.ts:132, grid-keyboard.spec.ts:241 and popover.spec.ts:93.

1. `entity-picker.spec.ts:582` "modal round-trips › the create page prefills the query; its pick
   applies with the fresh label" (676 ms).
   `expect((await model(page)).supplierId).toBeGreaterThanOrEqual(100)` received `null` (line 598).
   The previous line, `await expect(input(page)).toHaveValue('Newco')`, had already passed. So the
   input showed the fresh label before the bound model held the created id. Either the label is
   painted ahead of the model write, or the read is a one-shot `await model(page)` with no retry,
   racing an asynchronous write. Check which order the component guarantees. If the model is
   allowed to lag the label, poll the model with `expect.poll`. If it is not, this is a product
   ordering bug.
2. `entity-picker.spec.ts:601` "modal round-trips › the edit page targets the committed id; rename
   re-applies, delete clears" (5.5 s).
   After `populated.click()` and `populated.press('ArrowUp')`,
   `expect(locator('.tm-entity-picker__option[data-active="true"]')).toHaveText('Edit…')` found no
   element for the full 5 s (line 609). Either the ArrowUp landed before the panel and listbox were
   open and wired, so the keystroke was dropped, or the wrap-to-last highlight never happened. The
   test does not wait for the panel or the focus between the click and the key. This is the same
   class as the grid's documented post-click focus race. Look for a missing "panel open"
   precondition, or a key handler attached after the first frame.
3. `grid-entity.spec.ts:130` "unresolved commit text resolves through the column resolver AFTER
   editor teardown" (520 ms).
   `expect(await searchCalls(page)).toBe(searchesBefore)` expected 4 and received 5 (line 147): one
   picker search more than the snapshot. The snapshot is taken right after
   `toHaveValue('Zebra')`, so a debounced search for the last keystrokes was probably still pending
   and fired after the snapshot. Otherwise the click-elsewhere commit itself triggered a search,
   which is the duplicate round trip the assertion exists to catch. Settle the debounce before the
   snapshot (wait for the search that 'Zebra' triggers) and see whether the extra call persists. If
   it does, it is a product bug.
4. `grid-keyboard.spec.ts:239` "tab stop and mid-grid exit › a readonly grid is a single tab stop:
   Tab from a focused cell leaves it".
   After `cell(3,1).click()`, `toBeFocused()` and `keyboard.press('Tab')`, a one-shot
   `page.evaluate` found `document.activeElement` still inside `[role="grid"]` (line 251: expected
   false, received true). Either focus leaves asynchronously after Tab, which the non-retrying
   evaluate catches too early, or the grid's async post-click focus restoration (see the comment in
   `playwright.config.ts` about the dev server widening that race) pulled focus back into the cell
   after the Tab. Convert the check to `expect.poll` first. If it still fails, the post-click focus
   settle is overriding a user Tab, which is a product bug.
5. `popover.spec.ts:91` "positioning (DoD 16) › block-end/start alignment mirrors under RTL"
   (352 ms).
   `expect(panelBox.y).toBeGreaterThanOrEqual(triggerBox.y + triggerBox.height - 1)` expected
   `>= 244.859375` and received `244.7482…` (line 100), 0.11 px short of the 1 px tolerance. The
   boxes are read once, right after `toBeVisible()`. The panel was probably measured mid-entrance
   (a transform or opacity transition, or a position not yet final after the anchor computation)
   or with sub-pixel rounding. Wait for the panel's final position (for example two equal
   successive boxes, or the animation's end) before measuring, or measure under reduced motion.

### Verification

- `--list` with `CI=1`: the PR-tier command selects 435 tests (440 - 5), none of them the five;
  the nightly command selects 125 (45 chromium = 40 `@cross-engine` + 5 quarantined, 40 firefox,
  40 webkit).
- The five quarantined tests, run locally (`node scripts/e2e.mjs --project chromium --grep "@quarantine"`):
  5 passed. The re-indentation did not break them.
- eslint and `tsc -p e2e/tsconfig.json` are clean on the four spec files.

### Deviations

- none

### Open risks

- With `failOnFlakyTests`, a quarantined test that flakes makes the nightly tier red every night
  until it is fixed. That is intended: the flake stays visible.
- More tests may prove flaky on later runs. Each one is quarantined the same way.

### What the reviewer should examine hardest

- That the PR-tier and nightly patterns select what they should (verified with `--list`; see the
  commit message).
- That none of the five is actually deterministic. Each passed retry #1 in the same CI run.

## Review 1
### Findings
### Responses
