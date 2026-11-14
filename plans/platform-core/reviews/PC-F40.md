# PC-F40: tree-grid-restore reduced-motion flash e2e race

## Review 1

Commit reviewed: `7f8041ff`, parent `d65edc08`. One file changed:
`client/e2e/specs/tree-grid-restore.spec.ts`.

Verdict: the diagnosis is right, and the fix removes the race by construction. No blocking or
major findings. One minor finding.

What I checked and found sound:

- **The race is real, and it is in the test.** `flash()` (`grid-core.ts:2136-2150`) starts the
  `--grid-flash-duration` timer synchronously inside the click handler. The class comes off when
  that timer fires. The old test checked `toHaveClass`, then read the probe colour and the cell
  background in two more round trips. On a loaded machine, the timer can fire between them, and
  the cell then reports its own transparent background. That matches the reported
  `rgba(0, 0, 0, 0)`. The component behaves as spec 0010 §C.8 says.
- **The observer cannot miss the class.** The callback ignores the mutation records and re-reads
  the DOM: it queries `.tm-grid__row[data-tm-key="#212"]` and checks the class. It catches both
  ways the class can arrive: a new row inserted already carrying it (`childList` + `subtree`,
  which is the reduced-motion case, since 212 is under the collapsed Finance), or an attribute
  change on a row that is already rendered (the forced-colors case, where 212 is visible).
  - At install time, 212 is either absent or not flashing. Only `scrollToRow` flashes, and the
    restore does not. So no class is present before the observer exists.
  - There is one click and one flash. The observer disconnects once the class leaves, so a second
    flash cannot overwrite the record.
  - `data-tm-key` is rendered once per row (`grid-view.ts:205-222`), with no duplicate pane. So
    `querySelector` hits the right element.
  - The probe's append and remove queue one more callback. That callback takes neither branch,
    because the row is still flashing and the record is already set.
- **The sample is taken before any frame passes.** Mutation-observer callbacks run in the
  microtask checkpoint of the task that rendered. Rendering cannot run in between. The first
  `getComputedStyle(cell)` flushes style and creates the CSS animation, which is still
  play-pending with current time 0. `atStart` is therefore the `from` keyframe however loaded the
  machine is.
- **Pausing and seeking measure the right thing.** The seek reads the keyframe output at 90 % of
  the animation's own duration. Under `step-end`, that output is still the tint. Under the default
  `ease-out`, it is almost transparent. So this check tells reduced motion apart from normal
  motion, which the old start-only read never did. It is stronger than the assertion it replaces.
  - Restoring with `currentTime = 0; play()` only restarts the visual run a few microseconds
    late. The class-removal timer is independent of the animation, so the hold measurement is
    untouched.
- **The hold check cannot flake in the failing direction.** `clickedAt` is taken in a
  document-level capture listener, before the button's handler runs. The handler then calls
  `setTimeout(endFlash, 1200)` synchronously, after `syncRows` and the ancestor expansion. Class
  removal then needs that timer, a render, and the observer microtask. So `heldFor` is 1200 ms
  plus the handler's own work plus the render, which is well clear of `performance.now()`'s
  100 µs coarsening. Load only makes it longer. There is no upper bound to flake on. Measuring from the click rather than from
  the class appearing is the correct anchor, because the timer starts at the click.
  - If the click were never seen, `heldFor` would be `NaN`, and `toBeGreaterThanOrEqual` would
    fail loudly. It would not pass by accident.
- **The forced-colors test has the same fix.** The outline is now read in the same synchronous
  sample as the class.
- **Engines.** Neither flash test is `@cross-engine` (`playwright.config.ts:59,74`), so they run
  on Chromium only. `getAnimations`, `CSSAnimation` and `getComputedTiming` exist on all three
  engines anyway.
- **AGENTS.md.** `FlashRecord`, its members, both helpers and the in-page `Watch` interface carry
  TSDoc. The listener, the time parser, the observer and the seek have inline comments. Test
  names, comments and messages carry no PC ids, `plans/` paths or spec references.
- **The animation-name regex.** Angular's emulated encapsulation prefixes keyframe names with the
  component scope, which is why the pattern is anchored only at the end. The first flash uses
  keyframes 1 (`flashKeyframes` starts at 2 and flips), so the name is `…tm-grid-flash`. The
  regex also admits `reflash`.

Re-run (showcase build at 06:57 is newer than the last `client/projects` commit at 01:49, and the
tree is clean, so no rebuild was needed):

- `CI=1 node client/scripts/e2e.mjs --project chromium --workers 2 --repeat-each 10 --retries 0
  e2e/specs/tree-grid-restore.spec.ts`: 160/160 passed in 8.7 min, exit 0. That includes 20 runs
  each of the reduced-motion and forced-colors flash tests, 10 in ltr and 10 in rtl. Test times reached 7.6 s, so the machine was under real load. Log:
  `.tmp/pcf40-r1-chromium.log`.
- No Firefox or WebKit run: neither flash test is `@cross-engine`.

### Findings

**R1-1 (minor). The tint's hold is checked against the animation's own length, not against
`--grid-flash-duration`.** `tree-grid-restore.spec.ts:147-149`, `:324-328`.

- The rule (spec 0010 §C.8) is that under reduced motion "the tint holds still for the same
  time", meaning `--grid-flash-duration`. The test seeks to 90 % of
  `animation.effect.getComputedTiming().duration`, and it checks separately that the *class*
  stays on for at least the token duration. Nothing ties the animation's duration to the token.
- Failure scenario: `grid-flash.css:19` changes to a fixed or shorter duration, for example
  `animation: tm-grid-flash 300ms ease-out`. Under `step-end`, the tint then clears at 300 ms
  while the class stays on for 1200 ms. All four assertions still pass: the tint is present at 0
  and at 90 % of 300 ms, and `heldFor` is at least 1200.
- The old test did not catch this either, so it is a gap the rewrite leaves open, not a
  regression. The commit message's wording ("a hold of at least --grid-flash-duration") suggests
  the tint's hold is covered.
- Suggested fix: record `total` in the `FlashRecord` (for example as `animationDuration`) and add
  `expect(flash.animationDuration).toBe(flash.duration)`. That closes the gap with one field and
  one assertion, and it takes no extra round trip.

### Responses

- R1-1: fixed in the "PC-F40: review 1 fixes" commit (child of 7f8041f). `FlashRecord` now
  carries `animationDuration`: the CSS animation's own `getComputedTiming().duration`, sampled
  in the same observer callback. The reduced-motion test asserts that it equals
  `--grid-flash-duration` before checking the tint at 0 % and 90 %. Mutation check: with an
  injected `.tm-grid__row--flash > * { animation-duration: 300ms !important; }`, the test fails
  with `Expected: 1200, Received: 300` (log `.tmp/pcf40-r1-mutation.log`). The override was then
  removed.
