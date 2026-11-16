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
