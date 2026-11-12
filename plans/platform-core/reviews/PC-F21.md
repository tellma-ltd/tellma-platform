# PC-F21: the ./grid bundle budget (D-057)

## Implementer notes

### Where ./grid's bytes went

Measured the way the size gate measures (esbuild bundle of the entry FESM, `@tellma/*` external,
minified, gzip). Per-source shares come from the FESM's own source map, scaled by the gzip ratio,
so they are approximate (gzip is not additive). Script: `.tmp/pcf21-attribute.mjs` (not committed).

Before: ./grid 43,008 B (42.00 KB). The editing-only code in it, now in ./grid-editing:

| Source | KB gzip (approx.) |
|---|---|
| built-in editors (text, number, date, enum, entity) | 3.0 |
| editing controller (open, keydown, commit-and-move, typed-commit search decision) | 1.5 |
| status bar | 0.8 |
| editor mount (cell-scoped injector, TM_CELL_EDITOR_HOST capture) | 0.6 |
| editing key map | 0.2 |

What remains in ./grid after the split (top items): grid-core 12.4, grid-view 6.8, tm-grid-base
1.7, tm-grid-column 1.6, find bar 1.2, navigation key map 1.2, touch handles 0.7, clipboard DOM 0.7.

### Approach: a secondary entry point the grid loads itself

New entry point `@tellma/core-ui/grid-editing`. Everything in it is ɵ-prefixed and it is excluded
from the API goldens, like `./private`. The grid loads it with
`import('@tellma/core-ui/grid-editing')` behind an injection token (`ɵTM_GRID_EDITING_LOADER`, the
test seam). The import graph is one-way: grid → grid-editing (types only, plus the one dynamic
import); grid-editing never imports grid. It declares the shapes it needs (`ɵTmGridEditingHost`,
`ɵTmGridEditingColumn`, `ɵTmGridStatusSource`, `ɵTmGridEditorTemplateContext`) structurally, and
grid-core implements them.

Why an entry point and not a relative lazy chunk:
- ARCHITECTURE's "Lazy boundaries are entry points" rule; `@tellma/core`'s `./config-check` is the
  precedent.
- A relative `import()` inside ./grid becomes an in-entry chunk that the size gate's esbuild bundle
  inlines back into ./grid's weight, so the gate would not see the saving. A cross-entry-point
  `import()` stays external and is measured on its own.
- The editor controls (date-picker, entity-picker, select, input, number) leave the grid's static
  graph as well. On a readonly route the app bundle saves far more than the 4.94 KB the gate
  measures, because those entry points are measured separately and not counted in ./grid.

Load and hold-and-replay:
- The load is a `PendingTasks.run` task, so `whenStable` (and the tests) wait for it. It preloads as
  soon as `editable()` turns true (a field-bound grid that is not readonly). A readonly or
  `data`-bound grid never asks for it.
- If a key arrives before the code does, `openEditor` opens the engine's edit session at once (so the
  cell shows as editing and the model's state is right), and holds the open options. Keys that
  reach the editing keydown are held and `preventDefault`ed. A commit holds `{refocus}`; a cancel
  drops what is held and cancels the session.
- On arrival, `resume` mounts the editor and replays the held work in order. Characters and
  Backspace extend the seed text (`editor.seed`), except on enum editors. Other keys are dispatched
  as synthetic keydowns on the focused element (else the active cell or scroller). Held commits are
  re-run. After a held commit, focus goes back to the active cell if it was inside the grid, or
  stays on the outside element the user moved it to.
- A failed load goes to the error handler, cancels the held session, and the next edit attempt
  retries.
- The status bar renders through `NgComponentOutlet` inside a fixed-height `.tm-grid__status` slot
  that the grid draws itself, so the band (height, rule, background, forced-colours border) is in
  place before the code arrives and its arrival shifts nothing.
- An ESLint rule (`@typescript-eslint/no-restricted-imports`, `allowTypeImports: true`) stops
  grid/ and tree-grid/ sources from value-importing grid-editing or the editor-control entry
  points. The size gate measures each entry point alone, so it cannot catch such an import pulling
  the editors back into the grid's static graph.

### Sizes

| Entry point | Before | After | Ceiling |
|---|---|---|---|
| ./grid | 42.00 KB (43,008 B) | 37.06 KB (37,948 B) | 42 → 37.5 |
| ./grid-editing | (new) | 7.06 KB (7,231 B) | 7.16 |
| ./tree-grid | 0.97 | 0.97 | 8, unchanged |
| ./grid-engine | 14.84 | 14.84 | 15, unchanged |

./grid saves 4.94 KB. Under the spec's 42 KB, PC-051 (§C.4, §C.14) and PC-054 (§C.8, §C.9) have
4.94 KB between them. Under the new 37.5 ceiling there is 0.44 KB, so each of them raises ./grid by
what it adds, up to the spec's 42. §C.15's popup editors belong in ./grid-editing.

### Tests

`grid/tm-grid-lazy-editing.spec.ts` (9 tests, counting and gated loaders through the token):
1. A readonly grid never asks for the editing code: typing, Alt+ArrowDown, double-click.
2. A field-bound grid asks once it becomes editable, and only once.
3. The first key typed during the load seeds the editor.
4. Characters, Backspace and Enter typed during the load replay in order (commit and move).
5. A key typed after a held commit opens the next editor.
6. Escape during the load cancels the held session.
7. A press on another cell during the load commits the held text and focuses that cell.
8. Focus moved outside during the load commits and stays outside.
9. A failed load reports the error and the next edit retries.

The existing editing specs and e2e run unchanged against the lazy path (their grids are editable,
so the preload lands before the first interaction, and `whenStable` waits for it).
`grid/internal/grid-keymap-editing.spec.ts` moved with its code to
`grid-editing/editing-keymap.spec.ts`.

### Deviations

1. Spec 0010 §C.11 says loading the editable-only machinery on demand "is not part of this
   amendment" (Part C flag 6). D-057 ruling 2 supersedes that for this fix. §C.11's table does not
   list ./grid-editing.
2. IME: a composition started during the load window is not captured (only keydowns are held). In
   practice the preload starts when the grid becomes editable, so the window is the first chunk
   fetch.
3. On a misconfigured entity column (the dev-mode guards), a key typed during the load is consumed,
   and the session is cancelled when the code arrives and the guard throws. Before, the key was
   refused synchronously.
4. New internal surface: `ɵTM_GRID_EDITING_LOADER` and `ɵTmGridEditingCode` live in
   `grid/internal/grid-core.ts` and are not exported from ./grid's public API (the specs import them
   from the internal path). `ɵTmGridViewCore` gained `statusBar`. Plus the lint boundary rule.
5. The field writer and paste label resolution stay in ./grid: `applyTransaction`, paste and every
   model write need them on paths that are not cell editing.
6. The status bar's band (height, top rule, background) moved from the bar's `:host` to the grid's
   slot so it renders before the bar loads. The bar now fills the slot.

### API goldens

`api:check` reported drift in `core-ui.grid.api.md` and `core-ui.tree-grid.api.md`; `api:approve`
accepted it. No public declaration changed:
- Both gain `import { ApplicationRef } from '@angular/core'`. `ɵTmGridCore` now implements
  `ɵTmGridEditingHost`, which exposes `appRef` (the editing controller ticks the app once to force
  the editing cell's outlet into existence before it mounts the editor, as the in-entry code did).
  The ɵ class body is filtered out of the golden, but the report's import list is not.
- `TmGridBase.rowActivation` prints as `InputSignal<"none" | "row">` instead of `<"row" | "none">`.
  The source is unchanged (`input<'none' | 'row'>('none')`); TypeScript prints a union in
  type-creation order, which the moved code shifted. The new order matches the source.

`./grid-editing` is excluded from the goldens (`apiGoldens.exclude`), like `./private`.

### Gates

All PR-tier client gates pass on the final tree (logs in `.tmp/pcf21-*.log`):
- unit (`pnpm run test`): every project green; core-ui 87 files, 1,829 tests.
- lint, lint:test, typecheck, tokens:check: pass.
- build: pass. docs:build: pass, no tracked output changed.
- size:check: pass. ./grid 37.06 / 37.5, ./grid-editing 7.06 / 7.16, other entry points unchanged.
- api:check: drift in two goldens, approved as explained above.
- e2e PR tier (`--project chromium --project touch --grep-invert "@nightly|@perf|@quarantine"
  --workers 2`): 779 passed.

### Misalignments for Ahmad

- Spec 0010 §C.11 excludes the on-demand split and its table has no ./grid-editing row; D-057 asked
  for the split. §C.11 should list ./grid-editing (7.16) and the lowered ./grid ceiling.
- ARCHITECTURE.md now describes the grid's read/edit split next to the chart's lazy engine.
