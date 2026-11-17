# PC-F49: tree-grid late-load test waits for the content switch before scrolling

## Review 1

Reviewed commit f3c8d69910e96a448333934fdafdf3528e58ae55 (test-only change to
`client/e2e/specs/tree-grid.spec.ts`) against CI run 43814037969, spec 0004 §12 and spec 0010
§C.8/§C.9. Spec 0023 has no tree-grid scroll-restore section.

What I checked and ran:
- **(a) Diagnosis, against the CI log.** `gh run view 43814037969 --log-failed` shows exactly one
  flaky test: `tree-grid.spec.ts:281` first attempt failed at line 296 (pre-fix numbering, the
  `expectCellText(page, 55, 0, 'Lazy child one')` call), "element(s) not found" for
  `[data-tm-cell][data-row="55"][data-col="0"]` after 5 s. Retry #1 passed in 1.3 s.
  - "Not found" means row 55 was never rendered, so the grid sat away from the tree's end. A missing
    lazy load would look different: b without its children has 60 rows, and row 55 would exist as
    "Orphan account", failing with a "Received" text, not "not found". The log matches a reset
    scroll, not a lost load.
  - The race is real in the old code. `setScrollTop` (`e2e/support/grid.ts:168`) sets `scrollTop`
    synchronously, and Playwright's `click()` returns before the zoneless change detection runs.
    The old wait for 63 rows passed on a's 63 rows. Then the switch's tick runs the contentKey effect
    (`grid-core.ts:5917-5947`): `persistContentState` stores a's scroll from the scroll signals,
    and `restoreState(false)` sets `pendingScroll` to b's remembered `{0, 0}` (`grid-core.ts:5812-5818`).
    The `afterRenderEffect` (`grid-core.ts:6401-6425`) then writes `scrollTop = 0` in that same tick,
    which undoes the test's scroll.
  - I did not download the CI artifact (error-context.md, trace), so the page state at failure is
    inferred from the error text and the code, not observed.
- **(b) A real product race behind it?** None found.
  - The story's button text (`Content: {{ contentName() }}`) and the grid's `[contentKey]` come from
    the same signal and the same template (`tree-grid-story.ts:191-218, 318`). The contentKey effect
    is a view effect (component injector, `tm-grid-base.ts:232`). The scroll restore is an
    `afterRenderEffect` that `scrollRestoreRequest` dirties during that tick. All three run inside
    one synchronous `ApplicationRef.tick`, so no frame paints b before its restore lands. A user cannot
    scroll b before b's restore.
  - A user who scrolls in the frame after the click, before the tick (only when the scheduler's rAF
    beats its setTimeout), is scrolling content a, which is still what is painted. That scroll lands
    in a's slice through `onScroll` → the scroll signals → `persistContentState`, and b then starts
    at its remembered position. That is spec 0004 §12 ("a `contentKey` change … snapshots the outgoing
    key's slices … and loads the incoming key's slices"), not a bug.
  - The only deferred restore is a non-origin scroll waiting for `totalHeight > 0`
    (`grid-core.ts:6414`). With zero content height there is no vertical extent for a user to scroll,
    and this story never takes that path: b's remembered scroll is the origin, and its rows bind in the
    same tick.
- **(c) Does "Content: b" prove the restore applied?** Yes, for this story. The button text and the
  restore are written in the same tick (above), and the origin restore is never gated. So the toHaveText
  wait closes the race rather than narrowing it. The 63-row check after it is now meaningful too: in
  the same tick b would show 60 if its bind had started a load. The final `heldResponses` '0' check
  backs this up, because the hold is still on. The same "wait for the content label, then act"
  pattern is already used in `grid-virtualization.spec.ts:157-166`.
- **(d) Same ambiguous wait elsewhere.** None. Every other wait in `tree-grid.spec.ts` that follows
  a state change waits on a value the previous state cannot show:
  - line 75: depth 0 gives 10 rows (from 63);
  - lines 155, 168/175/179: 62, then 43/63/43 alternating;
  - lines 196/200/203: 43 → b 63 → a 43;
  - lines 237-239: held '1' plus b 60;
  - line 249: 60 → 63;
  - lines 264-266 and 272: held '2', 22 → 25;
  - line 288: b showed 60, so a's 63 is distinct.

  In `tree-grid-restore.spec.ts`, the waits that keep a 12-row count (lines 263, 284, 300) are
  "nothing changed" assertions next to a distinguishing wait (`aria-expanded`, the live region,
  `batchLog`), not sequencing waits before a scroll or input. `releaseResponses` itself waits for the
  held count to read '0', and the loader's append runs in the release's own microtasks, before that
  count re-renders.
- **Runs.** `pnpm run tokens:build-css` and `pnpm run build:showcase` exited 0. The only output
  beyond the build was the existing initial-bundle budget warning. Then
  `CI=1 node scripts/e2e.mjs --project chromium --grep-invert "@nightly|@perf|@quarantine"
  e2e/specs/tree-grid.spec.ts --repeat-each 20 --workers 4 --retries 0`: 400 passed, 0 failed,
  0 flaky (28.1 min on this host). That is 20 clean runs of line 281 with the fix.

### Findings

None.

### Responses
