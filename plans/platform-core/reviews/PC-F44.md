# PC-F44: ./grid size headroom (re-split tree and popup-pane code, fold dev-mode checks)

## Implementer notes

### What was built

./grid now measures **41,603 B gzip (40.63 KB)**, down from 43,029 B (42.02 KB) at a1cf6850.
- That is 1,426 B saved and **1,405 B (1.37 KB) of headroom** under spec 0010 §C.11's 42 KB.
- The ceiling is lowered to 40.73 KB (measure + 0.1).
- PC-F26's ~30 B of forced-colors CSS fits under the new ceiling with ~70 B to spare. To merge it the ceiling must rise by 0.03 KB, which keeps ~1.37 KB under 42 KB.

Every number below comes from the gate's own measurer (`tools/bundle-measure.mjs`: the Babel linker, esbuild minify, gzip). Each step was built and measured sequentially, in this order.

1. **S1: dev-mode checks fold away in production.**
   - `isDevMode()` is a call into @angular/core, so esbuild cannot fold it away. The warning text it guards stayed in the production bundle.
   - Each guard now reads `typeof ngDevMode !== 'undefined' && ngDevMode`, which the production build defines `false`. The repo already uses this pattern in entity-picker and editor-session.
   - Sites: `tm-grid-base.ts` (four configuration-error effects), `internal/grid-core.ts` (the scrollToRow unknown-column warning, the data-irregularity warning, the entity column without [format] warning), `tm-grid-header-action.ts` (the accessible-name warning) and `tm-grid-state-store.ts` (the duplicate-gridId throw; the production warning stays).
   - The same fold was applied in `grid-editing/editing.ts` (`admits()`) and `grid-editing/editor-session.ts` (the "mounted nothing" error), which pays back part of E1's cost there.
2. **E1: the popup pane's keys move to ./grid-editing.**
   - A popup pane renders only from the editing code: D-086 already moved the pane there. Its keydown handling was still in the core.
   - That handling covered the resolver, the Enter-owner selector, the edge-of-pane Tab test, and the whole cancel/commit/commit-and-move dispatch.
   - New file `grid-editing/popup-keys.ts` holds `tmResolvePopupKey` (moved verbatim from `grid-keymap.ts`), `tmPopupKeepsEnter` and `tmPopupTabLeaves`.
   - The dispatch is now `ɵTmGridEditing.popupKeydown`, a line-for-line port of the old `onPopupKeydown`. It calls back into the host for the read-only view, the find shortcut, the body keys and commit-and-move.
   - The core routes a pane keydown with `this.editing?.popupKeydown(event)`. The old `editing === null` branch could never run, because no pane exists until the editing code is present.
   - The Mod+F handling that the editor keydown and the pane keydown duplicated is now one core helper, `findFromEditor`.
   - The resolver tests moved with the resolver (`grid-editing/popup-keys.spec.ts`), with new tests for the two DOM predicates.
3. **T: the tree affordance and tree options move to ./tree-grid.**
   - The hierarchy cell's affordance is now an `<ng-template #affordance>` declared by `tm-tree-grid`: the level indent, the pointer-only expander with its chevron glyphs, and the reserved lazy-load spinner slot. Its styles moved to `tm-tree-grid.css`.
   - The shared view renders it through `NgTemplateOutlet` from the new `ɵTmGridViewCore.treeAffordance`, which is `null` for a flat grid.
   - The template is declared in tm-tree-grid's own view, so its nodes carry tm-tree-grid's emulated-encapsulation attribute. The moved rules apply unchanged, and the DOM (classes, `data-tm-expander`, `data-tm-childspin`, ARIA) is identical.
   - The engine's tree options (`buildTreeOptions`) are now built by the shell (`treeOptions` in `tm-tree-grid.ts`). `ɵTmGridTreeConfig` carries `options` and `affordance` in place of the four input closures.

### Per-feature bytes (gzip, the gate's measurer)

| Feature | ./grid before | ./grid after | Moved to | Net there |
|---|---:|---:|---|---:|
| Dev-mode check text (S1) | 623 | 0 (folded) | nowhere | grid-editing −300 from its own folds |
| Popup pane keys (E1) | 401 | 0 | ./grid-editing | +693 |
| Tree affordance markup, glyphs, CSS and tree options (T) | 402 | 0 | ./tree-grid | +869 |
| **Total** | **43,029** | **41,603** | | |

The first column is each step's measured saving on ./grid, so it includes the change in gzip context. A "moved" feature costs more in its new home than it saved in ./grid: the resolver is cut off from shared minified names and gzip context, and ./tree-grid now links TmIcon, TmSpinner and the two lucide glyph modules itself.

| Entry point | Before (B) | After (B) | Old ceiling | New ceiling | Spec ceiling |
|---|---:|---:|---:|---:|---:|
| ./grid | 43,029 (42.02 KB) | 41,603 (40.63 KB) | 42.03 | **40.73** | 42 |
| ./grid-editing | 7,601 (7.42 KB) | 7,994 (7.81 KB) | 7.52 | **7.91** | (on-demand) |
| ./tree-grid | 2,146 (2.10 KB) | 3,015 (2.94 KB) | 2.20 | **3.04** | 8 |

Each changed ceiling has a new budgetNotes entry in `projects/core/tellma-core-ui/package.json`, prepended, with the earlier note kept after "Earlier:".

File attribution of ./grid, measured as the gzip bytes that disappear when that file's output is removed. The deltas overlap, so they do not sum to the total. Source maps through the same linker and esbuild options.

| File | Before | After | Change |
|---|---:|---:|---:|
| grid-core.ts | 21,422 | 20,786 | −636 (dev warnings, popup keys, tree options) |
| grid-view.ts | 4,881 | 4,662 | −219 (affordance markup, glyphs) |
| grid-keymap.ts | 4,660 | 4,473 | −187 (popup resolver) |
| tm-grid-base.ts | 1,179 | 890 | −289 (four dev effects) |
| tm-grid-header-action.ts | 668 | 567 | −101 (dev warning) |
| clipboard-dom.ts | 1,096 | 1,082 | −14 (context only) |
| find-bar.ts | 866 | 870 | +4 (context only) |
| tm-grid-state-store.ts | 672 | 671 | −1 |

grid-view.css, the hierarchy rules, does not show in source maps. It is part of the T step's measured 402 B.

Before the change, the largest sections by the same measure were grid-core's popup editors (2,548), context menu (2,179) and find (1,061).

### Rejected candidates

- Paste parsing, the field writer, column parse and the error tally: under the D-060 draft window these must work before ./grid-editing has arrived, so moving them would change behaviour and errorCount.
- Find, touch handles and the context menu: read-only list grids use them.
- Changing the measurer (for example esbuild's charset): that would game the gate rather than save bytes.

### Deviations

1. The popup pane's keys live in ./grid-editing, not ./grid. Spec 0010 §C.15 lists popup editors under ./grid's budget, but D-086 had already moved the pane. The keys follow the pane, and they cannot run without it.
2. The tree affordance is a template that tm-tree-grid declares and the shared view renders, rather than markup in the shared view. The DOM output is unchanged.
3. `ɵTmGridTreeConfig` changed shape. It is an internal ɵ type: `options` and `affordance` replace `parentId`, `parentIdKey`, `hasChildren` and `defaultExpandedDepth`. `ɵTmGridViewCore` gains `treeAffordance`, and `ɵTmGridTreeAffordanceContext` and `ɵTmGridPopupMoveTarget` are new ɵ exports.
4. The development-mode checks are guarded by `ngDevMode` instead of `isDevMode()`. Production behaviour is the same, since neither emits anything there. In a development build that never defines `ngDevMode`, the checks no longer run; the Angular CLI and the test builder both define it.
5. The ./grid-editing and ./tree-grid ceilings rise (by 0.39 KB and 0.84 KB). ./tree-grid stays far under its spec ceiling of 8.
6. `TmTreeGrid`'s API report gains two protected glyph members (`expandedGlyph`, `collapsedGlyph`), which moved from the internal view.

### Open risks

- `viewChild.required('affordance')` is read lazily: the view reads `treeAffordance()` only while it renders cells, after the shell's template has been created. A hierarchy cell rendering before tm-tree-grid's view is created would throw NG0951. The shell's template creates the ng-template before `<tm-grid-view>`'s first change detection, and the tree-grid unit and e2e suites exercise first render, lazy load and restore.
- Change detection: the transplanted template is declared in tm-tree-grid's view and inserted in grid-view's. Angular marks transplanted views for refresh, and the cell context is a fresh object each render. Expander state and the spinner come from the cell's signals (`cell.expander`, `cell.loadingChildren`), and the e2e tree specs cover expand, collapse and lazy-load spinners.

### What reviewers should examine hardest

- The parity of `ɵTmGridEditing.popupKeydown` with the removed `onPopupKeydown` (git show the diff side by side). Check especially:
  - the IME guard order;
  - `revealActiveCell` before the guards;
  - the read-only view path (close, then `onBodyKeydown`);
  - Alt+ArrowDown going to the editor's dropdown only when editing.
- `tmPopupTabLeaves`' TAB_STOP_CANDIDATES, which replaces the core's HEADER_CONTENT_SELECTOR. The strings were the same at a1cf6850; confirm.
- The encapsulation assumption behind moving the affordance CSS to `tm-tree-grid.css`, and `grid-rtl.spec`'s `.tm-grid__twisty` checks.
- Whether any consumer reads the removed `ɵTmGridTreeConfig` members (only `tm-tree-grid.ts` builds one).

### Tests run

**Client gates**
- Clean (exit 0): lint, typecheck, tokens:check, build, test (core-ui 2,429 passed, with grid, grid-editing and tree-grid included), docs:build.
- size:check: OK. ./grid 40.63/40.73, ./grid-editing 7.81/7.91, ./tree-grid 2.94/3.04. Both staticGraphExcludes checks are OK.
- api:check: drift only in `core-ui.tree-grid.api.md`, which gains the protected `expandedGlyph`/`collapsedGlyph` that the template binds. Card, banner, pager and the others already list glyphs this way. I approved it with `api:approve`.
- lint:test: 4 files failed on timeouts while the .NET unit suite ran concurrently:
  - 3 hit hook or test timeouts: tm-no-hardcoded-sizing, boot-template, emit.
  - build.fixture hit its 20-min hook timeout, then EPERM on cleanup.
  - Rerun alone, all passed: the 3 files 18/18, build.fixture 11/11. The other 33 files passed in the gate run.

**Other gates**
- e2e: `build:showcase`, then a single CI run (`CI=1 … --project chromium --project touch --grep-invert "@nightly|@perf|@quarantine" --workers 2`): 1,090 passed, 0 failed, 0 flaky. That includes every grid, tree-grid, tree-grid-restore, grid-rtl and grid-popup-editor spec.
- .NET (client-only change; run for §7.1):
  - `dotnet build Tellma.slnx`: Build succeeded, 0 warnings.
  - `dotnet format --verify-no-changes`: clean.
  - `dotnet test --no-build` with the unit filter: all 20 assemblies passed.

## Review 1

### Findings

### Responses

## Review 2

### Findings

### Responses
