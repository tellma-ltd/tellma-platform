# PC-F50: ./tiles re-split

## Implementer notes

### What was built

Tree mode moved out of `./tiles` into a new entry point, `@tellma/core-ui/tiles-tree`. A flat list no longer downloads the tree code, the grid engine or the spinner.

- ./tiles measures **9,359 B (9.14 KB)**, down from 10,576 B (10.33 KB) at 7c082d95. That saves 1,217 B.
- Its ceiling is lowered from 10.35 to **9.24** (the measure plus 0.1 KB).
- The new ./tiles-tree measures 2,323 B (2.27 KB); its ceiling is **2.37**.
- Every number comes from the gate's own measurer (`tools/bundle-measure.mjs`).

**The seam (`tiles/internal/tm-tiles-tree-seam.ts`, exported from `./tiles` as ɵ types)**

`tm-tiles` now owns an interface for tree mode, `ɵTmTilesTree<T>`. The tiles still render, window and navigate the rows, and ask the tree for the rest:

- `rows`: the rows on show, or `null` while no parent reader is bound;
- `place(id)`: the level, expanded state, loading state, posinset and setsize;
- `arrow(id, forward)`: the tree's Right and Left;
- `reveal(id)`;
- `toggle(id)`: a twisty click;
- `shownAncestor(id, shown)`: the vanished-row keeper;
- `affordance`: the component that draws a tree tile's chrome.

`TmTiles` has one new internal member, `ɵtree`, a signal. Its `tree`, `visible`, `lines`, keydown, click and `reveal` paths read the seam instead of their own tree state.

**`TmTilesTree` (`tiles-tree/tm-tiles-tree.ts`)**

- It is a directive with the selector `tm-tiles[parentId]`.
- It holds the four spec'd tree inputs: `parentId`, `hasChildren`, `expandedIds` (a model) and `loadChildren`.
- It registers itself on the host tiles through `ɵtree` in its constructor.
- These parts were ported line for line from the old `TmTiles`:
  - the tree resolution (`ɵtmResolveTree`, `ɵtmFlattenVisible`) and the posinset map;
  - `loadingIds`;
  - the batch-flush effect and microtask, `flushLoads` with its settled and in-flight sets, and the failure collapse and announcement (Intl.ListFormat);
  - `setExpanded`;
  - the arrow logic, reveal, and the ancestor walk.

**`ɵTmTilesTreeAffordance` (`tiles-tree/tree-affordance.ts` and `.css`)**

- The twisty span: same classes, `data-tm-twisty`, `aria-hidden`, the `--open` class, and a tm-icon when the row can expand.
- The reserved spinner slot with tm-spinner, when a loader is bound.
- Its host is `display: contents`, so the twisty and the slot lie in the tile's flex row exactly where they were.
- The tiles render it through `NgComponentOutlet` with `{ place }` as its input.
- Its CSS rules moved verbatim from `tm-tiles.css`.

**Guards that fail if the tree is pulled back into ./tiles**

- `staticGraphExcludes` for `./tiles` now covers `./tiles-tree`, `./grid-engine` and `./spinner`, beside the grid entry points. `./tiles-tree` must not reach `./grid`, `./tree-grid` or `./grid-editing`.
- An eslint rule bans any import of `@tellma/core-ui/tiles-tree` from `tiles/**`, type imports included. The existing type-only grid rule now covers `tiles-tree/**` too.
- The lowered ceiling.
- `tiles-tree/tm-tiles-tree.spec.ts` checks these cases:
  - a `tm-tiles` without the directive is flat, with no affordance and no tree ARIA;
  - with the directive and no parent reader, the tiles stay flat;
  - unbinding the reader makes the tiles flat again;
  - the chrome draws a chevron only on rows that can expand, and only the expanded row's twisty is open;
  - a loading slot appears only with a loader, with a spinner while loading.

**Unchanged behaviour evidence**

- `tiles/tm-tiles.spec.ts` keeps every test, the tree-mode block included.
- Its one host now also imports `TmTilesTree`, so all flat tests run with the directive applied but unbound, and all tree tests run through the seam.
- The harness and the e2e locators did not change: `.tm-tiles__twisty`, `[data-tm-twisty]`, `tm-spinner` and `--tm-tiles-level` are still descendants of the tile.

**Consumers and docs**

- The `Tree` example moved to `tiles-tree/tm-tiles-tree.examples.ts`, wired into `docs-examples.spec.ts`.
- The showcase `tiles-tree` story imports `TmTilesTree`.
- The core-ui README has a new row.
- The grid-engine comments name the new consumer.
- ARCHITECTURE.md's tiles paragraph is updated (see Deviations).
- The API goldens and components.json are regenerated.

### Sizes (gzip bytes, the gate's linked measure)

| Entry point | Before | After | Old ceiling | New ceiling | Spec cap |
|---|---:|---:|---:|---:|---:|
| ./tiles | 10,576 (10.33) | 9,359 (9.14) | 10.35 | **9.24** | 7 |
| ./tiles-tree (new) | — | 2,323 (2.27) | — | **2.37** | none set (tiles' 7) |
| . (root) | 9,386 | 9,386 | 9.18 | 9.18 (untouched) | 5 |

- A flat-tiles page saves 1,217 B.
- A page that renders a tree of tiles pays 9,359 + 2,323 = 11,682 B, which is **+1,106 B** over the old 10,576. The moved code no longer compresses against the rest of the tiles, and it carries its own directive and component definitions. That is the same effect PC-F44 recorded for ./tree-grid.

### Why ./tiles cannot reach 7 KB

These ablations were measured on the 10,576 B FESM, by deleting each part and re-measuring:

| Part | Saving |
|---|---:|
| keyboard model | 435 |
| pointer path | 292 |
| skeleton | 232 |
| afterRender | 217 |
| flash | 95 |
| commitSelection | 47 |
| slice jumps | 41 |
| all lazy-able parts together | 877 |
| tree, lower bound | 750 |

- A real lazy-keyboard prototype nets only **81 B**, or **22 B** with an idle preload. That prototype used an InjectionToken loader, a PendingTasks hold, ErrorHandler routing and key replay while the chunk is in flight. The loader machinery costs nearly what it moves, so a lazy chunk does not pay.
- The pointer path must call `preventDefault` synchronously, so it cannot wait for a chunk.
- The rest is needed at first paint: the class core (lattice geometry, windowing and slot reuse, the roving active tile, activation and selection), the template, about 1.3 KB of styles, and the skeleton.
- The tree split is the only honest split with a real saving. A new cap for ./tiles is for Ahmad to rule; none is proposed in code.

### Deviations

1. **The tree inputs moved off `TmTiles`.**
   - Spec 0023 §8.5 declares `parentId`, `hasChildren`, `expandedIds` and `loadChildren` on `TmTiles`. They now live on `TmTilesTree` (`tm-tiles[parentId]`) in `@tellma/core-ui/tiles-tree`.
   - The template syntax is unchanged. A page that binds them imports `TmTilesTree` beside `TmTiles`. Without it, `[parentId]` on `tm-tiles` is a compile error, so the mistake cannot go silent.
   - The directive's selector carries an eslint-disable for the tm-prefixed-attribute rule, following the precedent in `tm-grid-column.ts`.
2. **Spec 0023 §1 needs a new row and a changed one.**
   - New row: `tiles-tree`, exporting `TmTilesTree`. Its dependencies are `tiles`, `grid-engine`, `icon`, `spinner` and `contracts`, with `grid` as types only. It is in the collection group with no cap.
   - The `tiles` row's dependencies lose `spinner` and `grid-engine`. The spec text says tiles are windowed by grid-engine's `tmComputeAxisWindow`, but the tiles already window their own lines (since the 10,000-tile scroll rework). Only the tree resolution used grid-engine, and it moved.
3. **The tree chrome is a component.**
   - It has a `display: contents` host element (`tm-tiles-tree-affordance`) inside each tree tile, in place of inline markup.
   - Layout, classes, the ARIA attributes and the descendant locators are identical. The only change is the extra host element in the DOM.
4. **ARCHITECTURE.md** now says tree mode is `tiles-tree`'s: it plugs into a seam the tiles own and shares grid-engine's tree resolution. It also lists what the static gate now excludes.

### Open risks

- **The search page.** Spec 0030 shows both flat tiles and trees of tiles on one page. If that page imports `TmTilesTree` statically, it pays the +1,106 B on every route. To keep the saving there, the tree view would sit behind `@defer`, or in a component used only for tree entities. That is a decision for the search-page checkpoint, not this item.
- **The `tiles.loadFailed` string carry (D-098).**
  - After the split, the one reader of `tiles.loadFailed` is ./tiles-tree. Its English string still sits in the "." table.
  - The seam that lets an entry point register its own strings (`ɵtmRegisterUiStrings`) is PC-193's. PC-193 is held and not on platform-core, so this item cannot use it.
  - Carry: whichever of PC-193 and PC-F50 integrates second moves `tiles.loadFailed` into ./tiles-tree's own registration. That move takes the Arabic counterpart and the parity test with it, and lowers "." by what leaves.
- **The seam timing.** `TmTilesTree` sets `ɵtree` in its constructor, inside `untracked`, during the host template's creation pass. The tests pass, but this is the one new timing edge.
- **The `NgComponentOutlet` inputs object.** `{ place }` is a fresh literal for each tile on each change detection. NgComponentOutlet calls `setInput` on each check, which skips a value that is `Object.is` the last one. `place` is the object the `lines` computed built, so the chrome re-renders only when the lines recompute, as the inline markup did.

### What reviewers should examine hardest

1. That `TmTilesTree` is a faithful port. Diff it against the removed `TmTiles` members:
   - `flushLoads` and the settled/in-flight semantics;
   - the arrow logic: the old `treeArrow` versus `arrow()` plus the caller in `onKeydown`, including the `'first'` case;
   - `reveal`, and the vanished-row keeper.
2. The `lines()` line place versus the old line `tree` object:
   - `expanded` is `null` for a row that cannot expand;
   - `loading` is `null` without a loader, which drives both `aria-busy` and the slot;
   - setSize comes from the effective parent.
3. That the seam ɵ types exported from `./tiles` are the only coupling, and that no value path leads from `./tiles` to `./tiles-tree`. `size:check` prints it.
4. The docs extractor output for `TmTilesTree`: its tmGroup, its a11y notes, and that the example moved.

### Tests run

- **Client gates, all passing:** lint, typecheck, tokens:check, build, size:check, api:check (after `api:approve`), docs:build, lint:test (248 tests) and test.
- **The full unit run:**
  - core-ui had one failure, `l10n/tm-instant.spec.ts` "warning once in development", where the warn spy counted 2 calls instead of 1. That is a stray console.warn from another file sharing the page. It passes when run alone, and nothing under l10n changed.
  - The runs after core-ui (core-ui test-csp, core-ui-tokens, locale-ar, showcase) were rerun individually, and all pass.
  - The tiles, tiles-tree and docs-examples specs pass: 250 tests.
- **PR-tier e2e (chromium and touch):** 1,116 passed and 1 failed. The failure was the tiles "10,000 rows › wheel scrolling" test, an `appStable` timeout under load. Run alone 3 times, it passes every time.
  - That rerun also ran the `@perf` 50 ms test by mistake, because the grep-invert was missed. It failed with long tasks of 51-92 ms on this heavily loaded machine. The test is nightly and out of scope (D-100).
- **.NET:** `dotnet build Tellma.slnx` printed Build succeeded with 0 warnings. `dotnet format --verify-no-changes` is clean. The PR-tier unit filter passes.

## Review 1

Reviewed commit b04c57968c3c14e6abaca43f75acfaf39462212b against the PC-F50 fix-item row, D-031, D-057, D-067, D-092, D-094, D-098 and D-100, spec 0023 §1, §8.5, §11 and §12, spec 0030 §8, PLAN.md §6-§7, and the PC-F44 precedent.

What I checked and ran:
- **Size, with the gate's own measurer.** `pnpm run build` (exit 0, no warnings), then `pnpm run size:check`: "bundle-size gate OK". ./tiles is 9.14 against 9.24, and ./tiles-tree 2.27 against 2.37. Both staticGraphExcludes lines print OK: ./tiles reaches none of grid, tree-grid, grid-editing, tiles-tree, grid-engine or spinner. A byte-level probe through `createBundleMeasurer` gives the implementer's figures exactly: 9,359 B and 2,323 B.
  - The built FESM of ./tiles imports only @angular/*, lucide, ".", badge, icon, image, kbd and skeleton. ./tiles-tree adds grid-engine, spinner and tiles. The emitted `tellma-core-ui-tiles.d.ts` keeps the `@internal` member `ɵtree`, which tiles-tree compiles against.
- **The guards.** `eslint --stdin` with a tiles/ filename and `import type { TmTilesTree } from '@tellma/core-ui/tiles-tree'` fails with the new message. A value import of grid-engine or spinner from tiles/ is not a lint error, but the static-graph gate fails it. A pulled-back tree also crosses the lowered ceiling. The guards are strong enough.
- **The 7 KB argument.** It agrees with D-092's independent measurement, which was 8.89 KB with tree, skeleton and flash all removed, before PC-F46 added about 0.3 KB. With the tree gone, removing the skeleton (232 B) and the flash (95 B) still leaves about 8.8 KB. The prototype that loaded the keyboard model on demand netted 81 B. I found no other honest split worth a chunk. Selection, the image path and custom content are small, and they are first-paint for the pages that use them.
- **Faithful port.** I compared `TmTilesTree` line by line with the removed `TmTiles` members:
  - `info` matches `treeInfo`;
  - `rows` matches the tree branch of `visible`;
  - `loadingIds` adds a `rows === null` guard, which is equivalent;
  - the effect, `flushLoads` with its settled and in-flight sets, the failure collapse and the Intl.ListFormat announcement are unchanged, except that they read `tiles.data`, `tiles.rowId` and `tiles.layout`;
  - `setExpanded`, `reveal` and the ancestor walk are unchanged.

  `arrow()` plus its caller in `onKeydown` is the old `treeArrow`: `'first'` maps to `move first`, a returned id to `setActive(reveal, focus)`, and `null` to nothing.

  `place()` matches the old line `tree` object and tile fields:
  - `level` is `node.level + 1`;
  - `expanded` is `null` unless the row can expand;
  - `loading` is `null` with no loader, which drives both `aria-busy` and the slot, the same as the old `@if (loadChildren())`;
  - `setSize` comes from the effective parent.

  `--tm-tiles-level` falls back to 1 when flat, as `tile.level` did.
- **Chrome.** No rule left in tm-tiles.css targets `.tm-tiles__twisty` or `.tm-tiles__spinner-slot`, its forced-colors block included. The harness and the e2e locators are descendant selectors, so the `display: contents` host breaks none of them. `{ place }` compiles to a pure function of `place`, so NgComponentOutlet's per-check `setInput` is a no-op until `lines` recomputes. The affordance is created under the tiles' element injector, so `mirrorInRtl` reads the live direction.
- **A missing import.** `[parentId]` (or `[hasChildren]`, `[(expandedIds)]`, `[loadChildren]`) on a `tm-tiles` without `TmTilesTree` is NG8002 in an AOT build. In TestBed it is NG0303, which docs-examples.spec.ts turns into a failure through `errorOnUnknownProperties`. It does not fail silently.
- **SSR and hydration.** Neither is used by the client (no `provideServerRendering` or `provideClientHydration`).
- **Tests.**
  - core-ui unit, `ng run core-ui:test`: 121 files, 2,536 passed, tm-instant included.
  - Tiles e2e on chromium and touch, PR tier: `CI=1 node scripts/e2e.mjs --project chromium --project touch --grep-invert "@nightly|@perf|@quarantine" --workers 2 e2e/specs/tiles.spec.ts e2e/specs/tiles-touch.spec.ts`. It ran on a fresh production build of the showcase: 20 passed, the RTL tree with lazy loads and the failure announcement, and the 10,000-row wheel test, included.
  - `docs:build` passes, and so does `lint:test` (39 files, 248 tests). `api:check` passes: all 68 goldens report `ok`, core-ui.tiles.api.md and core-ui.tiles-tree.api.md included. eslint and stylelint pass on tiles, tiles-tree and the showcase tiles stories.
  - Every old tm-tiles test survives; the spec diff only adds the import and the host comment. The tree block runs through the seam, and "keeps tree rows true to their level, position and expansion while lines scroll past" covers recycled slots in tree mode.
  - Logs are in `.tmp/pcf50-r1-*`.
- **The two reported flakes.** Neither is caused by this change.
  - **tm-instant "warning once".** The cause is the unrestored `vi.spyOn(console, 'warn')` at `grid/tm-grid-scroll-to-row.spec.ts:200`, which carries no `mockRestore`. When that file runs earlier in the shared page, tm-instant's `spyOn` gets back the same spy, with the grid's call still recorded, so the count reads 2.
    - That is the leak class LEDGER records for tm-tree. PC-F42 fixed this file on platform-core (`mockRestore` in a `finally`), but PC-F42 is not in this branch's base.
    - The new spec file only changes the order of files. Neither `TmTilesTree` nor the affordance emits a warning.
    - The flake goes away when the branch merges platform-core.
  - **"10,000 rows › wheel scrolling".**
    - That story is flat. The flat path's per-scroll work is unchanged: the only template change is an `@if` that reads `line.tree` where it used to read `tree()`, and the windowing, slicing and slot reuse are untouched.
    - The test passed in my targeted run, as in the implementer's three solo runs. The timeout under a full loaded run is load, not the split.

### Findings

**R1-1 (minor): the tree's five tokens are now documented by no component.**
- Before this commit, tm-tiles.css used these five tokens, so `components.json` (and llms.txt and the MCP `tokens` data) listed them under `tm-tiles`:
  - `--tiles-twisty`;
  - `--tiles-twisty-size`;
  - `--tiles-twisty-target`;
  - `--tiles-spinner-slot`;
  - `--tiles-spinner-size`.
- After it, the docs extractor no longer finds them anywhere:
  - It reads tokens only from the documented class's own `styleUrl` (`client/tools/docs/extract-components.mts:235-246`).
  - The rules now live in `client/projects/core/tellma-core-ui/tiles-tree/tree-affordance.css`. Its component (`tiles-tree/tree-affordance.ts:45`) is internal and never extracted.
  - The directive that is extracted (`tiles-tree/tm-tiles-tree.ts:80`) has no stylesheet.
- After my `docs:build`, `tm-tiles` lacks all five, and `tm-tiles[parentId]` has `"tokens": []`. The tokens still exist in `tellma-default.css:562-566`.
- Failure scenario: a theme author or an agent asks the MCP server, or reads llms.txt, for what restyles a tree tile's twisty or loading slot, and gets nothing. The tree grid's re-split (PC-F44) kept its affordance rules in the tree grid's own stylesheet, so its tokens stayed documented. This split loses them silently.
- Fix: let the extractor attach these tokens to the `tm-tiles[parentId]` entry. Either way below works. Then add an assertion to the docs-extractor tests (beside `tools/tests/extract-examples.spec.mts`) that `tm-tiles[parentId]` lists the five tokens.
  - Option 1: for a class with no `styleUrl`, read the stylesheets of the internal components its entry point declares.
  - Option 2: an explicit JSDoc tag on `TmTilesTree` that names `./tree-affordance.css`.

**R1-2 (minor): `tiles.loadFailed` is now read only by ./tiles-tree, but no carry moves it out of "." (D-098).**
- After the split, the one reader of `tiles.loadFailed` is `client/projects/core/tellma-core-ui/tiles-tree/tm-tiles-tree.ts:400`. The English string stays in the "." table, at `client/projects/core/tellma-core-ui/i18n/strings-en.ts:374-376`.
- D-098 rules that later entry points whose strings only they read register them through the string seam, so that "." stops paying for them.
- The seam (`ɵtmRegisterUiStrings`) is PC-193's, which is held at 5a16ff3e and not on platform-core, so this commit cannot use it. The review file records no carry, though: neither the open risks nor the deviations mention it.
- Failure scenario: PC-193 and PC-F50 integrate in either order, and the string stays in "." for good. This is the drift D-098 was ruled to stop.
- Fix: add the carry to "Open risks". Whichever of PC-193 and PC-F50 integrates second moves `tiles.loadFailed` into ./tiles-tree through the seam, with its Arabic counterpart and the parity test. The orchestrator then records the carry on that item's row.

### Responses

**R1-1: fixed.**
- The docs extractor already reads a styleUrl-less directive's tokens from the stylesheet that `"tellma".docs.globalStyles` names for its entry point.
- `./tiles-tree` now maps there to `tiles-tree/tree-affordance.css`, the stylesheet of the internal chrome component.
- I chose this over the reviewer's two options:
  - Option 1 (deriving the stylesheets of an entry point's internal components) would also change the tokens of other styleUrl-less directives.
  - Option 2 (a TSDoc tag) would ship a file path in the public TSDoc.
  - The mapping is explicit, one line, and needs no new mechanism. The extractor's header comment now says the mapping covers an internal chrome stylesheet as well as a global one.
- The per-file loop body of `extractComponents` is now an exported `extractSource(project, source)`, with `SOURCES` and `createDocsProject` also exported. The new test `tools/tests/extract-components.spec.mts` extracts only the tiles-tree source and asserts all five tokens on `tm-tiles[parentId]`. A whole extraction took 138 s on this loaded machine, too slow for a test.
- `docs:build` regenerates components.json and llms.txt. They are gitignored, so there is no committed diff.

**R1-2: fixed.** The carry is recorded under Open risks.


Gates rerun after the fixes, all passing:
- lint, typecheck, build, size:check (./tiles 9.14, ./tiles-tree 2.27) and api:check;
- docs:build: `tm-tiles[parentId]` now lists the five tokens;
- lint:test (40 files, 249 tests);
- core-ui test (121 files).
