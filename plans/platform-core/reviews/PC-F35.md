# PC-F35: search-field clear e2e waits for the trailing emit

## Review 1

Commit reviewed: `7dfcd25`, parent `ebd5d08`.

Verdict: the diagnosis is right, and the fix removes the race by construction. The e2e now waits
on the page's own signal (the emitted log) instead of on time, and its final assertion is stricter
than before, not weaker. No blocking or major findings. One minor finding, on the new unit case.

What I checked and found sound:

- **The component's behaviour is correct per spec 0023 §6.2.**
  - §6.2 says `search` emits "at once on Enter and on clear" and "never with the value it last
    emitted". The trailing edge emits the burst's text at the window's end, and after a clear that
    text is `""`.
  - `clear()` (`tm-search.ts:142-164`) clears through `execCommand('delete')`. Its own input
    event is skipped by the `clearing` flag (`:171`). It then calls `flush()`, which cancels the
    pending trailing timer and emits `""` (`:127-135`).
  - So a clear inside the window emits `""` at once, and "jed" is never searched. Even without the
    timer cancel, a late trailing edge would read `""`, equal to the last emitted value, and stay
    silent. Emitting a stale "jed" after the clear would contradict "at once on clear": the
    consumer would load results for text that is gone.
  - Enter already closes the burst in the same way, and the existing unit case "emits at once on
    Enter and closes the burst" pins it.
- **The diagnosis matches the CI evidence.** Playwright's `keyboard.type('jed')` sends the keys
  back to back. "j" leads at once, and "e" and "d" fall inside the 50 ms window. The old test then
  waited only on `search-value` (updated per keystroke) and clicked clear. When the click landed
  within 50 ms of "d", the log was exactly `["j",""]`, as CI saw. The old assertion
  `/"jed",""\]$/` then retried until its 5 s timeout, which fits the 5.4 s run.
- **The fix removes the race by construction.**
  - The new wait `/^\["j",.*"jed"\]$/` at `search-field.spec.ts:43` is satisfied only once "jed"
    has been emitted, so the clear can no longer land before it.
  - If "jed" came out on the trailing edge, the burst is already closed. If it came out on a
    leading edge (a slow "d"), the trailing timer is still pending but would emit nothing, since the
    text equals the last emitted value. Either way, the clear's `""` is the next and last entry.
  - The `.*` admits the one other legal shape, `["j","je","jed"]` on a slow runner, as the comment
    says.
  - The post-clear assertion is now anchored at both ends (`:50`), so it is stricter than the old
    suffix match. No timeout was widened.
  - The Mod+Z step afterwards emits "jed" again as a new leading edge, but nothing asserts the log
    after it, so it adds no race.
- **Re-runs on a fresh showcase build.** The slot's previous build came from another branch, so I
  rebuilt first (`.tmp/pcf35-r1-build.log`).
  - `CI=1 node client/scripts/e2e.mjs --project chromium --workers 2 --repeat-each 10
    --retries 0 e2e/specs/search-field.spec.ts`: 70/70 passed, the clear test 10/10
    (`.tmp/pcf35-r1-e2e.log`).
  - The test is tagged `@cross-engine`, so I also ran the clear test on firefox and webkit with
    `--repeat-each 5 --retries 0`: 10/10 passed (`.tmp/pcf35-r1-e2e-xengine.log`).
  - `ng test core-ui --include **/search-field/tm-search.spec.ts`: 16/16 passed
    (`.tmp/pcf35-r1-unit.log`).
- **The new unit case catches the regressions it targets.**
  - It fails if the clear stops emitting at once, for example if the clear leaves the `""` to the
    trailing edge (first assertion).
  - It fails if the clear emits the old text before clearing (first assertion).
  - It fails if the trailing edge searches a value captured at schedule time ("ab" after the
    advance).
  - The gap is below in R1-1.
- **AGENTS.md is followed.** No new declarations need TSDoc. The new inline comments explain the
  wait and the unit case. No test name, comment or message cites a PC id, a `plans/` path or a
  spec section. Only the commit message names the review file.
- **No other e2e spec has this race.** I checked every e2e spec that types into a debounced field:
  `tmSearch` (used only by the search-field story), the entity picker's leading and trailing
  window, and the grid's 250 ms find scan.
  - `entity-picker.spec.ts`: each step uses one `fill` (a single input event) or the fake-clock
    `typeOnClock`, and waits on the final options or status before acting (`:129-142`,
    `:159-166`, `:179-182`, `:190-194`).
  - `grid-entity.spec.ts`, `grid-paste-resolution.spec.ts:196`: the story's search is synchronous
    unless it is held (`grid-entity-story.ts:302-316`). The picker skips the window after a sync
    search, so there is no burst to land in. The held-search cases wait on the held-response count
    (`grid-entity.spec.ts:204`, `:309`). The click-elsewhere commits at `:368`, `:402`, `:422`,
    `:442`, `:460` and `:507` test the commit or resolution path itself.
  - `grid-find.spec.ts`: every `fill` is followed by a wait on the find counter before
    Enter or Next (`:74-76`, `:137-140`, `:164-167`, `:175-178`).
  - `grid-search-page.spec.ts:167-171` and `grid-header-actions.spec.ts:95-97`: both wait on the
    final rendered result before the next key.

### Findings

- **R1-1 (minor): the new unit case does not check its second claim, that the clear drops the
  burst's trailing edge.**
  - Location:
    `client/projects/core/tellma-core-ui/search-field/tm-search.spec.ts:131-142`.
  - Scenario: suppose `clear()` emitted `""` directly instead of through `flush()`, and left the
    trailing timer pending. The case still passes. At +50 ms the leftover timer reads `""`, which
    equals the last emitted value, so `queries()` stays `['a', '']`. The burst would then stay open
    for 50 ms after the clear, so a key typed in that time would not lead at once. The test name
    says the trailing edge is dropped, but only the emissions are checked, and they cannot tell
    the two apart. The Enter case at `:121-129` has the same blind spot.
  - Suggested fix: pin the burst's closure directly. Right after `search.clear()`, type one more
    key and expect it to emit at once (`['a', '', 'c']` with no timer advance). Alternatively,
    rename the case to what it proves: "emits the empty query at once on a clear inside a burst,
    and never searches the cleared text". (`vi.getTimerCount()` would also work, but only if the
    TestBed schedules no fake timers of its own.)

### Responses

- R1-1: fixed in the "PC-F35: review 1 fixes" commit. Right after the clear, the case types one
  more key and expects `['a', '', 'c']` with no timer advance, so a burst left open by the clear
  would fail it. The Enter case gets the same check (`['a', 'ab', 'abc']`). Both then advance
  100 ms and expect nothing more. tm-search.spec.ts passes (16/16), and lint and typecheck are
  clean.
