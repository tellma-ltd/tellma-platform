# PC-F37: tree-grid showcase story hands the grid a fresh rows array on every contentKey switch

## Implementer notes

### What was built, and where

- **`client/projects/internal/showcase/src/app/grid/tree-grid-story.ts`.** The story switched
  `contentKey` (the a/b toggle and the depth select, `<record>:<depth>`) over one shared rows
  array. Under D-077 a content bound over the outgoing rows runs no lazy loads until a new rows
  array arrives, so content b only looked right because a's lazy children were already in the
  shared array, and it showed a's children as its own. Now every key change comes with a new
  array, set in the same handler as the key:
  - **a/b toggle (`switchContent`).** a and b are separate records. Each owns its rows array and
    row objects (`seedRecord()` deep-copies `SEED_ACCOUNTS`). The outgoing record's array is
    parked in a private `parked` map, and the incoming record's array is bound (seeded on its
    first bind). Each record keeps its own edits and loads its own lazy children.
  - **Depth select (`onDepthChange`).** The depth is part of the key but not a different record,
    so the same record re-binds through a shallow copy of its array. A select change to the same
    depth is a no-op (defensive: the native `change` event does not fire for it).
  - **Loader (`loadChildren`).** It captures the bound record when the grid calls it, and appends
    the lazy children (fresh objects) to that record through `updateRecord`: to the model when
    the record is still bound, else to its parked array. A load that settles after the user
    switched away therefore lands in its own record; the grid ignores that settle (its load
    generation moved on). The "once" check is per record.
  - TSDoc added to the members that had none (the field tree, the toolbar signals, the
    accessors, the handlers), and the class TSDoc states the fresh-array rule.
- **`client/e2e/specs/tree-grid.spec.ts`**, "lazy loading" group, three new tests, each holding
  the loader through the story's `loader` gate so the in-flight state is asserted while it
  provably lasts:
  1. *a switched-to content loads its own lazy children*: switch to b with the gate holding. One
     load is held, b shows 60 rows (no children), the lazy root is expanded with its spinner, and
     the model has no children of 800. After the release: 63 rows, no spinner, b's children under
     the root.
  2. *a depth switch re-binds its content with a fresh array*: switch to b (its load held), then
     depth 1. The `b:1` bind loads the lazy root again (two loads held), the row shows its
     spinner at depth 1 (22 rows). After the release: 25 rows, no spinner, children under the
     root, and the record holds them exactly once.
  3. *a load that settles after its content switched away lands in that content*: switch to b
     (load held), back to a, release, then switch to b with the gate still holding. b shows its
     children with no new load (held count 0, no spinner).
  - The existing "expansion state survives a contentKey switch" test is unchanged except for a
    comment: b's 63 rows now arrive after b's own load (the 400 ms delay), which the auto-retrying
    assertion waits out.

### Why an e2e test, not a unit test

The broken pattern lived in the story, not the library: the grid's side of the D-077 contract is
already unit-tested in `tm-tree-grid.spec.ts` ("a content bound over the outgoing rows loads
nothing until its own rows arrive"). The story is only exercised through the Playwright battery,
so the tests that would fail on the shared-array pattern are e2e tests against the story.

### Mutation checks

Each against a production build of the mutated story (`build:showcase`, then
`CI=1 node scripts/e2e.mjs --project chromium --workers 2 --retries 0 --grep "lazy loading" tree-grid.spec.ts`;
script `.tmp/f37-mutations.sh`, output `.tmp/f37-mutations.out`):

- **The story as it was** (one array shared by every content): all three new tests fail at their
  first held-count assertion (expected 1 held load, received 0: b never loads), and the existing
  lazy-root test passes (`.tmp/f37-mut-shared.log`).
- **Separate a/b records, but a depth change keeps the same array**: the depth-switch test fails
  (expected 2 held loads, received 1); the other three pass (`.tmp/f37-mut-depth-shared.log`).

### Verification

- Targeted, dev server: tree-grid.spec.ts and grid-find.spec.ts (which uses the story's depth
  select) on Chromium, 32/32 (`.tmp/f37-e2e-targeted-new.log`).
- Client gates (logs `.tmp/f37-gate-*.log`): lint, typecheck, lint:test (234/234), tokens:check,
  build, test (core 484, core-testing 22, core-ui 2426, core-ui test-csp 2, core-ui-tokens 27,
  locale-ar 9, showcase 1), api:check (no golden change: only the showcase and an e2e spec
  changed), docs:build, size:check, build:showcase all pass.
- PR-tier e2e in CI mode after build:showcase
  (`CI=1 node scripts/e2e.mjs --project chromium --project touch --grep-invert "@nightly|@perf|@quarantine" --workers 2`):
  1093 passed, none flaky, 32.8 min (`.tmp/f37-e2e.log`).
- No .NET project changed, so no unit filter or integration suite is touched. For completeness:
  `dotnet build Tellma.slnx` prints Build succeeded, 0 warnings (`.tmp/f37-dotnet-build.log`), and
  `dotnet format Tellma.slnx --verify-no-changes` is clean (exit 0, no diagnostics;
  `.tmp/f37-dotnet-format.log`).

### Deviations

1. **The a/b records are separate records, not one array copied.** The item says to hand the
   story a fresh array per switch. A copy of one shared array would satisfy array identity but
   still give b a's rows and lazy children. The records model what contentKey stands for (an
   invoice id, ...), so b loads its own children and each record keeps its own edits. Only the
   depth select, which re-views the same record, copies the array.
2. **The loader routes a late settle to its own record.** This is outside the item's wording, but
   it follows from separate records. Without it, a b load released after switching back to a
   would append b's children to a whenever a lacked them (a failed or still-held load of its
   own).

### Open risks

- `grid-states-story.ts` (the flat grid's states story) also switches `contentKey` (A/B) over one
  shared rows array. The TSDoc contract ("a new key comes with its own rows array") covers every
  grid, but a flat grid has no lazy loads, so nothing visible breaks there. It is out of this
  item's scope and is reported to the orchestrator.

### What reviewers should examine hardest

- The order inside `switchContent` and `onDepthChange`: the key signal and the model are set in
  the same handler, so the grid's content-switch effect sees the new array
  (`rows !== reconciledRows`) and loads at the bind instead of deferring.
- Whether the three tests would fail on the shared-array pattern (see the mutation checks).
- That the row counts asserted at depth 1 (22, then 25) and at b's bind (60) follow from the seed.
