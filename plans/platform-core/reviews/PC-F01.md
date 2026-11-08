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

Checked and found sound: (a) `--list` with `CI=1` on this commit gives 435 for the PR-tier command
and 440 with `@quarantine` left out of the invert. The sorted, line-stripped diff of the two lists is
exactly the five tests, all `[chromium]`. `--grep "@quarantine"` lists exactly those five. The
nightly command lists 125: 45 chromium, of which 40 are `@cross-engine` and the 5 quarantined ones.
No quarantined test reaches firefox, webkit or touch. (b) The pattern is the same in ci.yml:195,
CONTRIBUTING.md:94, nightly-tier.yml:114 and CONTRIBUTING.md:117. No other e2e tier invocation
exists in the repo. (d) client/e2e/playwright.config.ts is untouched: `retries: 1` and
`failOnFlakyTests: !!CI` are unchanged. (e) The change adds no declarations. No PC id or plans/
path appears in a test title, tag, comment, workflow or CONTRIBUTING prose.

R1-1 (major) nightly-tier.yml:113-114, CONTRIBUTING.md:116-118. The quarantined tests run in the
same step as every other nightly Playwright test, so a known flake decides the nightly verdict.
Scenario: the nightly runs `CI=1` with `retries: 1` and `failOnFlakyTests`, all four projects in a
single parallel invocation, so the runner is more contended than in the PR-tier run where these five
flaked. Any one of the five failing its first attempt turns `client-e2e`, and the whole nightly-tier
run, red. That can happen most nights until PC-F02 lands. A new failure, for example a real
`@cross-engine` regression on WebKit, then arrives in a run that is already red for a known reason.
PROCESS.md §5.3 triage reads the latest nightly run and turns "each new failure" into a PC-Fnn item,
and a red that is red every night teaches the reader to skim past it. The open risk in the
implementer notes calls this intended, but visibility of the flake does not need to cost the
verdict of everything else. A second effect: a quarantined test that turns from flaky into
deterministically failing, a real regression in that code path, looks exactly like the flake it
replaced. Suggested fix: give the quarantined set its own clearly reported job.
  - Main nightly step: `node scripts/e2e.mjs --grep "@nightly|@cross-engine|@perf" --grep-invert "@quarantine" --pass-with-no-tests`.
  - New job `client-e2e-quarantine`, Chromium only, same setup:
    `node scripts/e2e.mjs --project chromium --grep "@quarantine" --repeat-each 10 --pass-with-no-tests`,
    with its own report artifact name. `--repeat-each` makes the job measure a flake rate rather
    than a coin toss: about 50 short runs per night, a few seconds of runner time. That rate is
    also the bar PC-F02 has to clear (0 failures in N), and it separates "still flaky" (some
    passes) from "now broken" (none).
  - Split the CONTRIBUTING.md nightly command the same way and update the nightly-tier.yml header
    comment (lines 18-21).

  Whether the quarantine job also sets `continue-on-error: true`, so the run conclusion reflects
  only non-quarantined tests, or stays blocking with triage reading per-job conclusions
  (`gh run view <id> --json jobs`), is the orchestrator's call: see the decision line.

R1-2 (minor) entity-picker.spec.ts:582-583 and :605-606, grid-entity.spec.ts:130-131,
grid-keyboard.spec.ts:239-240, popover.spec.ts:91-92. The five quarantine comments are identical
("flaky on Linux CI … until the race is fixed"), so they break the rule this commit adds at
CONTRIBUTING.md:35-36: quarantine with "a comment saying how it failed". The per-test failure modes
exist only in this review file, and plans/ is deleted at the merge into main (D-012). After that,
whoever picks up a still-quarantined test has to re-derive them. "The race" also prejudges the
cause: popover.spec.ts:100 missed a 1 px tolerance by 0.11 px (measurement or rounding), and the
grid-entity and grid-keyboard cases may be product bugs. Suggested fix: one or two lines per test
stating the observed failure, for example
`// Quarantined: on Linux CI, supplierId was still null after the input showed 'Newco'; passed on retry.`
and for popover
`// Quarantined: on Linux CI the panel's top sat 0.11 px above the 1 px tolerance; passed on retry.`
Drop "the race".

R1-3 (minor) CONTRIBUTING.md:37-39. The text says "A quarantined test is tracked and fixed, never
left" but does not say where it is tracked, and CONTRIBUTING.md may not point at plans/.
Scenario: after the merge into main, or for any contributor outside this run, nothing records a
quarantine and nothing bounds how many accumulate. The tag then becomes the silent retirement the
paragraph forbids. Suggested fix: name a durable tracker, for example "open an issue labelled
`flaky-test` and link it in the quarantine comment". Or say the count is visible because the
nightly lists every `@quarantine` test in its quarantine job's summary, which R1-1's separate job
makes possible (`--list --grep "@quarantine"` into `$GITHUB_STEP_SUMMARY`).

### Responses

R1-1: fixed in the "PC-F01: review 1 fixes" commit, following the orchestrator ruling (a separate,
non-blocking quarantine job that reports per test and keeps failOnFlakyTests). In
`nightly-tier.yml`, the main `client-e2e` step now runs
`--grep "@nightly|@cross-engine|@perf" --grep-invert "@quarantine"`. A new job,
`client-e2e-quarantine`, has job-level `continue-on-error: true` and runs
`node scripts/e2e.mjs --grep "@quarantine" --repeat-each 10 --pass-with-no-tests --reporter list,json,html`
on all projects, so a quarantined `@cross-engine` test would still reach every engine. The config's
`failOnFlakyTests` and `retries: 1` are unchanged, so the job's own conclusion stays honest. A new
script, `client/scripts/e2e-quarantine-summary.mjs`, turns the JSON report into a per-test,
per-project table in the job's step summary: passed, flaky and failed run counts, a verdict
("passed every run", "flaky", "failing every run") and the first error. The HTML report, the JSON
report and the results upload under their own artifact name whatever the outcome. A missing
report makes the summary step fail, since it means the run did not finish. A local dry run under
`CI=1` with `--repeat-each 2` and two temporary probes (one that passes only on retry, one that
always fails; deleted, never committed) produced the expected table: the five quarantined tests
"passed every run" 2/2, the probes "flaky" 0/2/0 and "failing every run" 0/0/2, the Playwright
step exit 1, the summary exit 0. A missing report gave exit 1, an empty one "No test is
quarantined." `--list` counts: main nightly 120 (40 per engine, with no quarantined test),
quarantine job 50 (5 × 10), PR tier 435. CONTRIBUTING.md now gives the two nightly Playwright
commands separately. The workflow header and the ARCHITECTURE.md sentence are updated.

R1-2: fixed in the "PC-F01: review 1 fixes" commit. Each quarantine comment now states the failure
the CI log showed, with no guess at the cause: a null supplierId after the input showed 'Newco';
no active option within 5 s after the click and ArrowUp; 5 searches instead of 4; focus still
inside the grid after Tab; the panel top 0.11 px past the 1 px tolerance. Each ends "passed on
retry". "The race" is gone.

R1-3: fixed in the "PC-F01: review 1 fixes" commit, using the reviewer's second option.
CONTRIBUTING.md now names the record: the nightly quarantine job lists every quarantined test each
night in its summary, with its counts, so none can sit unseen, and a test that fails every run is
flagged as broken. I did not adopt the issue-tracker option: opening issues is outside what this
run may do, and a CONTRIBUTING rule that the five existing quarantines do not follow would be
broken from day one. The orchestrator can still add a tracker rule when issues are in use.
