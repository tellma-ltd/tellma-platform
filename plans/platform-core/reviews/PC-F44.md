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

Reviewed commit 7bb434ddabce0397f4b4578fc764748fe7014598 against D-057, D-067, D-086, D-094, spec 0010 §C.11/§C.15 and PLAN.md §6-§7.

What I checked and ran:
- **Size, with the gate's own measurer.** `pnpm run build`, then `pnpm run size:check`: exit 0. ./grid 40.63/40.73, ./grid-editing 7.81/7.91, ./tree-grid 2.94/3.04, and both staticGraphExcludes checks OK. A byte-level probe through `tools/bundle-measure.mjs`'s `createBundleMeasurer` gives the implementer's figures exactly: ./grid 41,603 B, ./grid-editing 7,994 B, ./tree-grid 3,015 B.
  - The ./grid ceiling (41,707 B) is truly lowered: 1,301 B under the 42 KB cap (43,008 B), and the measure is 1,405 B under it. That meets LEDGER's "at least 0.5 KB of real headroom" and D-094.
  - Neither raise is new growth. Every line added to ./grid-editing (`popup-keys.ts`, `popupKeydown`) and to ./tree-grid (the affordance template, its CSS, the glyph constants, `treeOptions`) is moved code. ./tree-grid stays far under its spec 8.
  - Totals fall for both consumer kinds. A tree-grid page downloads 44,618 B (was 45,175), and an editable grid 49,597 B (was 50,630).
- **Parity of the moved pane keys.** I compared `ɵTmGridEditing.popupKeydown` (grid-editing/editing.ts:455) line by line with the removed `onPopupKeydown`:
  - The order is the same: the session/view gate, `revealActiveCell`, the IME guard, then find.
  - So are the `isDropdownOpen` and `openDropdown` gating, the cancel/commit paths (`commitEditor`/`cancelEditor` delegate to `editing.commit`/`cancel` once editing is installed) and the read-only branch (close, then `onBodyKeydown`).
  - The dropped `editing === null` branch is dead. `[data-tm-popup]` renders only from grid-editing, and `installEditing` sets `this.editing` before any mount.
  - `TAB_STOP_CANDIDATES` equals the core's `HEADER_CONTENT_SELECTOR` character for character.
- **Tree affordance.**
  - No CSS outside `tm-tree-grid.css` targets the moved classes. A grep of every `.css` and `.ts` under client/ finds only the e2e `.tm-grid__twisty` locator. grid-view.css has no descendant or element selectors (`button`, `tm-icon`, `span`) that the affordance's nodes used to match.
  - `TmIcon` and `TmSpinner` inject only root-level services, so the declaration-side injector changes nothing.
  - Angular 22.0.5's NgTemplateOutlet forwards a changed context through its proxy without recreating the view.
- **Dev-mode folds.** Every production path is unchanged: the duplicate-gridId warning stays. The guard matches the repo's existing pattern (entity-picker, editor-session, tm-instant). The dev-only throws are still covered by passing unit tests ("requires a readonly grid", "has no key", "has no editor").
- **e2e.** After `build:showcase` (exit 0), `CI=1 node scripts/e2e.mjs --project chromium --grep-invert "@nightly|@perf|@quarantine" --workers 2` over tree-grid, tree-grid-restore, grid-rtl, grid-glyphs and grid-popup-editor: 75 passed, 0 failed, 0 flaky. That covers the indent geometry in both directions, glyph centring, expander and lazy-load spinners, the restore paths, axe on the expanded tree, and every pane spec.
- **Gates.** The commit lists every §7.1 item 4 gate, including the lint:test reruns (D-078/D-085) and the PR-tier e2e. ARCHITECTURE.md and the core-ui README are updated. The API golden change is limited to the two protected glyph members.

### Findings

**R1-1 (minor): two branches of the moved pane-key dispatch have no test that fails if they break.**
- The refactor rewrote both branches:
  - Mod+F in a pane: `client/projects/core/tellma-core-ui/grid-editing/editing.ts:468` calls `host.findFromEditor`, which is the new shared helper at `client/projects/core/tellma-core-ui/grid/internal/grid-core.ts:2880-2892`. The in-cell editor path also uses it (`grid-core.ts:2829`).
  - Alt+ArrowDown in a pane: the `openDropdown` case at `editing.ts:488-492`.
- No unit or e2e test exercises either one:
  - `tm-grid-popup-editor.spec.ts` and `grid-popup-editor.spec.ts` contain no Mod+F and no Alt+ArrowDown.
  - `grid-find.spec.ts` presses Mod+F only from the grid itself.
  - `popup-keys.spec.ts` proves only that the resolver returns `openDropdown`, not what the dispatch does with it.
- Failure scenarios the suites would not catch:
  - `findFromEditor`'s read-only branch commits instead of closing the view, or the find bar never opens. The browser's find dialog then shadows the grid's find from inside a pane or an in-cell editor.
  - `popupKeydown` loses the `!viewing` gate, which opens a read-only view's dropdown. Or it loses the conditional `preventDefault`, so the key is consumed with no dropdown open.
- The pre-move code was just as untested. This commit is the one that merged and moved these branches, though, and item (c) asks that each moved behaviour has a test.
- Fix: add four tests to `tm-grid-popup-editor.spec.ts` on a searchable host:
  1. Ctrl+F in an editing pane commits the pane's value, opens the find bar, and prevents the default.
  2. Ctrl+F in a read-only view closes it with nothing committed and opens the find bar.
  3. Alt+ArrowDown in a pane that hosts a built-in dropdown editor (select or date) opens its panel.
  4. Alt+ArrowDown in a read-only view does nothing and leaves the default alone.

  One more test, Ctrl+F from an in-cell editor committing and opening find (in `tm-grid-editing.spec.ts`), covers the other caller of the shared helper.

**R1-2 (minor): the controller's file comment now says the opposite of the split.**
- `client/projects/core/tellma-core-ui/grid-editing/editing.ts:10-11` still reads "the grid's own core keeps every path a readonly grid runs, the editing keys included".
- After this commit a readonly grid with a popup column runs its pane's keys from this file (`popupKeydown`, `popup-keys.ts`).
- The next re-split will plan what can move from this header, and the AGENTS.md rule that inline comments explain the logic is not met while it says otherwise.
- Fix: amend the header to say that the popup pane's keys, which only exist once this entry point has arrived, live here. Everything else a readonly grid runs, and the in-cell editing keys, stay in the core.

### Responses

**R1-1: fixed in the review 1 fixes commit.** Six tests were added. Each moved branch was then broken on purpose and the relevant spec rerun (`.tmp/f44-mutants.sh`):

`grid/tm-grid-popup-editor.spec.ts`:
- **"the find shortcut"**: the host gains `[searchable]`, off by default. Three tests:
  - Mod+F in an editing pane commits the pane's value, closes the pane, opens the find bar and prevents the default.
  - Mod+F in a read-only view closes it, leaves the model untouched (same array) and opens the find bar.
  - Mod+F in a pane on a non-searchable grid stays with the browser.
- **"a popup pane with a dropdown editor"**: a new host with a popup `enum` column. I used enum rather than date because the date picker's own input consumes Alt+ArrowDown, so the grid's branch never runs behind it. The select's trigger leaves the key to the grid. Two tests:
  - In an editing pane, Escape first closes the panel the pane opens with (the session stays). Alt+ArrowDown then re-opens it (`aria-expanded` true) and is consumed.
  - In a read-only view, Alt+ArrowDown leaves `aria-expanded` false and the default alone, and the model unchanged.

`grid/tm-grid-editing.spec.ts`:
- Mod+F from an in-cell editor commits it, closes it and opens the find bar. The host gains `[searchable]`, off by default.

Mutation results:

| Mutant | Fails |
|---|---|
| M2 `findFromEditor` never opens find | both pane Mod+F tests |
| M3 drop the `!viewing` gate on `openDropdown` | read-only Alt+ArrowDown |
| M4 always `preventDefault` on `openDropdown` | read-only Alt+ArrowDown |
| M5 `popupKeydown` skips `findFromEditor` | both pane Mod+F tests |
| M6 the in-cell path skips `findFromEditor` | in-cell Mod+F |
| M1 the read-only branch calls `commitEditor` instead of `closePopupView` | none: equivalent mutant |

Why M1 survives: a read-only view has no session, so `commitEditor` does nothing. `openFind` then focuses the find input, and the grid's focusout rule ("a read-only popup view closes, committing nothing") closes the view. The observable result is identical: the view closes, nothing commits, find opens. The test pins that result. The explicit close stays because it does not depend on the focus order.

Reruns:
- The two specs: 108 passed (26 popup, 82 editing).
- lint and build: clean.
- lint:test: 37 files, 234 tests passed.
- size:check: OK, unchanged (./grid 40.63/40.73, ./grid-editing 7.81/7.91, ./tree-grid 2.94/3.04).

**R1-2: fixed.** The `grid-editing/editing.ts` file comment now says that the popup pane's keys live in this entry point, because a pane, even a read-only grid's view, only exists once it has arrived. The core keeps every other path a readonly grid runs, and the in-cell editing keys.

## Review 2

### Findings

### Responses
