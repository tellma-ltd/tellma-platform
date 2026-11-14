# PC-F38: entity multi-picker keyboard e2e and live-region restore races

## Review 1

Commit reviewed: `2cafee3`, parent `821a6cf`.

Verdict: the diagnosis is right on both counts, and both fixes remove their races by
construction. No blocking or major findings. One minor finding, about a test that does not guard
the property it claims to guard.

What I checked and found sound:

- **The e2e race is gone by construction.** With the dropdown open, the picker's capture-phase
  keydown (`tm-entity-picker.ts:2111-2125`) steps from `keyboardActiveKey()`, which reads the
  pending `highlightRequest` before the DOM's active option (`:2360-2372`), over `optionKeys()`,
  which is the picker's state (`resultItems` then footer rows, `:1731-1736`). So N presses move
  exactly N rows whatever the render timing. The test now:
  - waits for `appStable` before reading anything;
  - checks that nothing is highlighted (the browse query is empty, so `:1679` schedules no
    auto-highlight);
  - reads the row labels once, presses ArrowDown (index + 1) times, then uses auto-waiting
    assertions.
  The `options` locator excludes `.tm-entity-picker__action`, and footer rows sit after the
  results in `optionKeys()`, so the DOM index matches the state index. No read happens between
  presses, so there is nothing left to race.
- **The restore cannot leak or hold the app unstable forever.** `PendingTasks.add()` in
  @angular/core 22.0.5 returns an idempotent remover that also notifies the scheduler. Every path
  removes the task:
  - the timer fires and removes it;
  - a newer `ɵannounce` cancels it (clearTimeout + remove);
  - destroy cancels it (`:1231`).
  If `ɵannounce` is called after destroy (a late multi-picker continuation), the timer still fires
  and removes its task within one macrotask. The signal write on a destroyed view is harmless.
- **The restore's render is covered.** `liveText` is read by this component's template (`:263-265`),
  so `liveText.set(message)` in the zoneless scheduler adds its render task synchronously, before
  `removeTask()`. Removal also notifies the scheduler, so `whenStable` cannot resolve before the
  restored text is in the DOM.
- **Rapid repeats and newer messages.** A repeat while a restore is pending cancels it. The region
  reads `''`, which differs from the message, so the message is set directly. A different message
  cancels the restore and wins. Before this change the late restore overwrote it. The newer
  message winning is the right reading of spec 0006 §8 and spec 0010's multi-picker announcement
  rules (additions, removals and the `maxItems` refusal are each announced politely).
  - Pre-existing, not a regression: two identical announcements in the SAME task end with the
    region holding the message, with no render of `''` in between, so nothing is re-read. The old
    code behaved the same way. No call site in either picker produces two identical announcements
    in one task, so this is not a finding.
- **SSR.** Announcements come only from user interaction and fetch completion, so none runs during
  a server render. If one did, the 0 ms timer would end the task within a macrotask.
- **Single picker.** `announceKey` and the result-count, no-results, failure and picked
  announcements go through the same `ɵannounce`. Their only change is that a repeated count
  ("5 results" for "a", then for "al") now holds stability for one macrotask, and that a newer
  message is no longer overwritten. The same pattern is already used in `grid-core.ts:4779` and
  `:4807`. Nothing in the entity-picker folder uses fake timers, so `whenStable` cannot hang there.
- **The new overwrite test is red against the old component.** Both clicks are dispatched
  synchronously. The Bob pick is refused at the cap (`agentIds` stays `[]` after the removal,
  which proves the refusal ran before the removal), and then the removal announces. In the old
  component, the restore timer then writes "You can choose up to 1" over "Adam Brown removed".
  If the leading `cancelAnnounceRestore?.()` were dropped, the test would fail the same way.
- **AGENTS.md.** The new field and the injected `PendingTasks` have TSDoc. The non-trivial lines
  have inline comments. No PC ids, `plans/` paths or spec references appear in TSDoc, test names
  or messages. The `ɵannounce` TSDoc is still accurate.
- **Size.** I did not rebuild the library dist, so I did not re-measure. The reported 13.98 of
  14 kB passes, but it leaves about 20 bytes gzipped on a ratchet, so the next change to the
  picker will need either a recorded raise or a reduction. One small saving is available here:
  the timer callback can call the cancel closure after the write
  (`setTimeout(() => { this.liveText.set(message); cancel(); })`). Then the null assignment and
  `removeTask()` appear once (`clearTimeout` on a fired timer is a no-op). That is a few bytes,
  not real headroom, so it is not a finding.

Re-runs (logs under `.tmp/`):

- Unit tests, entity-picker folder (`ng test core-ui --include
  "projects/core/tellma-core-ui/entity-picker/**/*.spec.ts"`): 2 files, 208 passed. The run was
  concurrent with a showcase build, so the CPU was contended. Log: `.tmp/pcf38-r1-unit.log`.
- e2e, after a fresh `build:showcase`:
  `CI=1 node client/scripts/e2e.mjs --project chromium --workers 2 --repeat-each 10 --retries 0
  e2e/specs/entity-multi-picker.spec.ts` gave 100 passed and 0 failed (3.1 min). That covers 20
  runs of the keyboard test across ltr and rtl. Log: `.tmp/pcf38-r1-e2e.log`.

### Findings

**R1-1 (minor).** `client/projects/core/tellma-core-ui/entity-picker/tm-entity-multi-picker.spec.ts:688-694`:
"a refused message repeated is announced again" does not guard the pending-task property its
comment claims for it.

- *What is wrong.* `clickRow` ends in `settle`, which is `whenStable`, then `sleep(0)`, then
  `whenStable`. The `sleep(0)` is registered after the restore's 0 ms timer, so that timer usually
  runs first even when nothing tracks it.
- *Failure scenario.* Revert `ɵannounce` to an untracked `setTimeout`, keeping the cancel. This
  test then still passes on nearly every run. The CI failure it originally showed was 1 run in
  40. Only the new overwrite test would stay red, and that test checks cancellation, not
  stability. So the rule "the restore is covered by the app's stability" has no deterministic
  test, and the comment "No extra wait: the restore is a pending task, so stability covers it"
  overstates what the test proves.
- *Suggested fix.* Dispatch the second click directly, as the new test does, then
  `await fixture.whenStable()` once, with no `sleep`, before disconnecting the observer and
  asserting `seen[0] === ''` and the restored text.
  - With the pending task, the single `whenStable` waits for the restore and its render.
  - With an untracked timer, it resolves after the render of `''`, so the test fails every time.

### Responses

- R1-1: fixed in `PC-F38: review 1 fixes` (this commit, child of 2cafee3). "a refused message repeated is announced
  again" now dispatches the second click directly and awaits `fixture.whenStable()` once, with no sleep, before
  asserting that the region read `''` first and the restored text last. Mutation check: with the restore's pending
  task replaced by a no-op (an untracked timer, cancel kept), the test failed 40 of 40 in one run
  (`.tmp/pcf38-fix1-mutation40.log`); restored, the maxItems block passed 156 of 156 over 40 repeats
  (`.tmp/pcf38-fix1-unit40.log`). The row lookup is now a shared `rowLabelled` helper. The size trim is taken too: the
  timer callback writes the message and then calls the cancel closure.
