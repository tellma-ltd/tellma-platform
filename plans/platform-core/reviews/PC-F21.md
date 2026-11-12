# PC-F21: the ./grid bundle budget (D-057)

## Implementer notes

These notes record the first submission. Fix round 1 replaced the hold-and-replay design with a
draft; see "### Responses" under Review 1 for the current design, sizes and gates.

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

## Review 1

Reviewer 1 (Opus). What I checked myself on 15ba8ec (logs in `.tmp/r1-pcf21-*`):
- `pnpm run build` (the full build): passes.
- `pnpm run size:check`: passes. ./grid **37.06 / 37.5**, ./grid-editing **7.06 / 7.16**,
  ./tree-grid 0.97, ./grid-engine 14.84. The saving is real.
- The editors really left the static graph. I bundled ./grid and ./tree-grid from `dist` with the
  sibling entry points resolved and code splitting on (`.tmp/r1-pcf21-bundle.mjs`):
  - The static closure of ./grid is grid, menu, kbd, checkbox and grid-engine, plus shared
    overlay, private, spinner, form-field, icon, contracts, l10n and the primary entry.
  - date-picker, entity-picker, input, number, select and modal appear only in the
    `grid-editing` chunk.
  - None of the editor selectors appears in the static ./grid output.
- The ESLint rule fires on value imports and on an `export *`, and lets `import type`
  through (checked with `eslint --stdin` on a probe under `grid/internal/`).
- `pnpm run api:check`: **fails** (R1-1).
- Grid unit specs (`ng test core-ui` on grid/** and grid-editing/**): 8 files, 394 tests, pass.
- Grid e2e (`--project chromium --project touch --grep-invert "@nightly|@perf|@quarantine"
  --workers 2`, grid-* and tree-grid specs): 305 passed in 5.9 min. Their editable grids
  preload before the first interaction, so they do not exercise the load window.

Answers to the questions in the brief:
- **IME.** The deviation is acceptable, and the loss is clean. Composing keydowns during the
  window are neither held nor prevented. The session opens with `ime: true`, so on arrival
  the editor mounts unseeded with an empty pristine baseline. An abandoned session commits
  nothing, and on the new-row placeholder no row is created. Nothing can be half-written.
  It still breaks 0004 §8.4's "type-to-edit must not eat CJK input" for the window. That
  matters for Amharic input methods as much as for CJK, so keep it in the deviations.
- **Concurrent loads, destroy mid-load, toggling editable.** These are sound:
  - Every grid calls the same import.
  - A destroyed grid ignores the arrival, and its held work is dropped in `onDestroy`.
  - A readonly flip or a content switch mid-load drops the held session through
    `cancelEditor()`.
  - A later flip reuses what arrived. None of these is tested with a held session (see R1-3).
- **Status-bar slot.** It causes no layout shift: the band's height, rule and background are
  the slot's, and the bar fills it. Forced colours keep the top rule, because `.tm-grid__status`
  joins the CanvasText border rule.
- **SSR and hydration.** These do not apply: ARCHITECTURE rules out SSR for every distribution.
  `PendingTasks.run` removes its task only when the import settles. See R1-6.
- **Public API.** No public declaration changed. The `ApplicationRef` import lines are right:
  tree-grid passes api:check. The `rowActivation` union order is wrong (R1-1).
- **New tests.**
  - They would catch a broken replay order, a dropped held commit and a lost focus restore
    (tests 4, 7 and 8).
  - They would catch a load from a readonly grid: test 2 asserts zero calls while readonly,
    and one call at the flip with no interaction.
  - They do not cover the paths in R1-2, R1-3 and R1-6.
- **Deviations.** Deviations 1, 2, 4, 5 and 6 hold. Deviation 3 understates the production
  behaviour (R1-10).

### Findings

**R1-1 (blocking): `api:check` fails on a clean full build.**
- **Where:** `client/api/core-ui.grid.api.md:69`.
- **What happens:**
  - The golden was approved with `InputSignal<"none" | "row">`.
  - A fresh `pnpm run build` of 15ba8ec emits `InputSignal<"row" | "none">`
    (`dist/core-ui/types/tellma-core-ui-grid.d.ts:1612`). That is the pre-change order.
  - So `pnpm run api:check` exits 1 with "API DRIFT: core-ui.grid.api.md"
    (`.tmp/r1-pcf21-api.log`).
  - D-057 already records the cause: the order differs between a single-project build and
    the full build, and CI uses the full build. The note's explanation ("the moved code
    shifted" it) does not hold.
  - The approve log is from 02:18, and the last full build log is from 01:53. The approval
    most likely ran against a `dist` that no longer matched the sources.
- **Fix:**
  1. Restore `"row" | "none"` on that line, or rerun `api:approve` straight after a full
     `pnpm run build`.
  2. Correct the "API goldens" paragraph of the implementer notes.

**R1-2 (major): a double-click on another cell during the load discards the text typed
before it.**
- **Where:** `grid/internal/grid-core.ts:2219-2221` (`onDblClick`) and `:3774-3782` (the
  loading branch of `openEditor`); `grid-engine/tm-grid-edit-state.ts:99`.
- **What happens:**
  1. While the code loads, the user types "Q" into cell A. The engine holds a session on A
     with the seed "Q".
  2. The user double-clicks cell B. Each pointerdown only pushes a held commit.
  3. The dblclick calls `openEditor(B, 'edit')`. Its loading branch calls
     `engine.edit.openEdit`, which replaces the open session without committing it.
  4. The branch then runs `this.held = []`, which drops the held keys and both held commits.
  5. On arrival, B's editor opens in edit mode. "Q" is gone, with no error and no undo entry.
- **Before the split:** the first click of the pair committed A synchronously. A touch
  double-tap reaches the same dblclick path, so it fails the same way.
- **Fix:** in the loading branch, never replace an open session. Either hold the open as a
  work item, replayed after the earlier commits so it behaves as before, or return `false`.
  The second loses only the double-click, not data.
- **Test:** a gated-loader test. Type into A, double-click B, release the loader, then assert
  A's value and B's editor.

**R1-3 (major): a held commit leaves the engine session open, so everything after
`commitEditor()` runs against an unfinished edit.**
- **Where:**
  - The hold: `grid/internal/grid-core.ts:3791-3797`.
  - Its callers: `:1922`, `:2146`, `:2246`, `:2265`, `:2291` and `:2380`.
  - Focus that a session suppresses: `:3989` (`requestFocusActive`) and `:4772` (the
    roving-focus effect).
  - The drop on destroy: `:5021`.
- **Background:** before the split, every caller could rely on the session being closed when
  `commitEditor` returned. `engine.clickCell`'s contract states it: "any open editor must be
  settled beforehand". During the load window that no longer holds, and it shows in three
  ways.
- **Commit-on-blur no longer comes before the outside action.**
  - The user types a value, then clicks the page's Save.
  - The focusout holds the commit, Save's click handler reads the model without the edit,
    and the edit lands afterwards. The page is dirty again right after saving.
  - With a Save and close, the page destroys the grid first. `onDestroy` drops the held
    commit, so the edit is lost and no unsaved-changes guard ever sees it.
- **Focus does not follow a press.**
  - The cell press calls `preventDefault` on its pointerdown. While a session is open,
    `requestFocusActive` and the roving-focus effect both skip.
  - So after a focusout-held commit, pressing back on a grid cell leaves focus in the outside
    field, and the next keystrokes type into that field.
  - Checked in Chromium (`.tmp/r1-pcf21-focus-probe.mjs`): a click whose pointerdown is
    prevented, on a `tabindex=-1` element, leaves focus on the previously focused input, and
    typing goes there.
- **Immediate writes overtake the held commit.**
  - Two examples: a boolean-glyph toggle on another cell (`:1980`), and a context-menu Paste,
    Clear or row action taken from a menu opened over a held session (`:2265`).
  - Each of these writes first. The older typed text lands on top afterwards, and the undo
    order is reversed.
  - Example: a menu Paste into the cell being edited is then overwritten by the text typed
    before the menu opened.
- **Fix:** settle the session synchronously when a commit is asked for before the code
  arrives.
  - The common case is a type-to-edit session whose held keys are only characters and
    Backspace. Commit the held text through the engine at once and close the session:
    - `engine.edit.commitText` for text, number and date columns;
    - `engine.commitEditorLabel` plus `runResolutions` for entity columns.
  - Both are already in ./grid, and this matches what the controller does for those editors
    when no search was adopted.
  - Hold only what really needs the editor: enum, template editors, and edit-mode sessions
    with caret keys.
  - With R1-2's fix, a held session never outlives a gesture that expects it closed.
- **Tests:**
  - An outside button's click handler reads the model after a typed cell, during the window.
  - Focus after pressing back into the grid from an outside input.
  - A readonly flip, and a destroy, with a held session.

**R1-4 (major): replay's "typed character" test disagrees with the grid key map, so AltGr
characters vanish and Alt chords become text.**
- **Where:** `grid-editing/editing.ts:644-645`, against `grid/internal/grid-keymap.ts:419-423`.
- **The two tests:**
  - Replay counts a key as text when `key.length === 1 && !ctrlKey && !metaKey`.
  - The grid's type-to-edit test is different: no Ctrl, Alt or Meta; or AltGraph, off Apple;
    or Option without Cmd or Ctrl, on Apple.
- **What happens:** on Windows, during the load window:
  1. **AltGr characters are dropped.** These include `@ € { [ \` on German, French and Nordic
     layouts and `ą ę ł ś` on Polish. They report `ctrlKey` and `altKey`, so they are
     re-dispatched as synthetic keydowns, which type nothing. "ab@c.com" commits as
     "abc.com". After a held commit, the synthetic copy also lacks AltGraph, so the
     character does not even open the next editor.
  2. **A bare Alt chord is typed as text.** Alt+S, a page or shell shortcut, is appended to
     the cell as "s". The shortcut never runs, because its keydown was prevented when it was
     held.
- **Fix:**
  - Share one "is typed text" predicate between the key map and replay. Replay cannot import
    ./grid, so put the predicate in ./kbd or ./grid-engine, or pass it through
    `ɵTmGridEditingHost`.
  - Copy `modifierAltGraph` and `repeat` into the synthetic event.
- **Test:** a Ctrl+Alt+AltGraph "@" typed after the opening key.

**R1-5 (minor): held keys whose effect is the browser's default action are lost on replay.**
- **Where:** `grid/internal/grid-core.ts:1839-1842` (every held key is prevented);
  `grid-editing/editing.ts:642-670` (replay).
- **Why:** a held key is prevented, and a synthetic keydown performs no default action.
- **What is lost during the window:**
  - **Mod+V pastes nothing.** Cancelling its keydown suppresses the paste event, and
    `onPaste` (`:2407`) would return early for the open session anyway. The paste is lost
    silently.
  - **Mod+Z, Mod+A, Ctrl+Backspace and Delete do nothing.**
  - **Caret keys do not move the caret in edit mode.** Home, End, ← and → have no effect, so
    later characters land at the end. "F2, Home, x" gives "Alphax", not "xAlpha".
- **Two text-base slips:**
  - On an entity cell opened in edit mode, the picker's `text()` is `null` while it shows the
    committed label (`entity-picker/tm-entity-picker.ts:2453-2455`). So the first replayed
    character replaces "Alice" with "x" instead of giving "Alicex" (`editing.ts:647`).
  - A template editor without `seed()` loses every held character after the first. The mount
    falls back to `value.set`, but replay does not.
- **Fix:**
  - In the hold branch, let Mod+V's keydown through. In `onPaste`, hold the event's
    `text/plain` as text work when a session is held.
  - For an entity editor, take the base text from the input's own value, or from the
    session's edit text, rather than from `text()`.
  - Fall back to `editor.value.set` for editors without `seed()`.
  - Record the caret-key limitation with deviation 2.

**R1-6 (minor): nothing bounds the load window.**
- **Where:** `grid/internal/grid-core.ts:1839-1842` and `:3734-3755`.
- **Escape is held, not acted on.** The commit message's "Escape drops the held work" only
  becomes true when the code arrives. Until then the cell sits blank in its editing state,
  and Escape, Tab and Shift+Tab are all swallowed.
- **A stalled import traps the keyboard.** The chunk request can stall, for example on a
  first visit before the service worker has cached it, or on a hung connection. Then:
  - a keyboard user can leave neither the cell nor the grid (WCAG 2.1.2);
  - every key is swallowed;
  - the `PendingTasks` task never ends, so `whenStable` never resolves.
- **After a failed load, only the next editor open retries.** An editable grid that the user
  only pastes into never shows its status bar. Its error tally, pending count and
  clipboard-failure notice stay invisible, because an editable grid shows them only in that
  bar.
- **Fix:**
  1. Handle Escape at once in the hold branch: drop the held work, cancel the engine session,
     and keep focus on the cell.
  2. Race the import against a timeout that takes the failure path.
  3. Retry the load on a paste as well as on an open.

**R1-7 (minor): the code's arrival steals focus from wherever the user went.**
- **Where:** `grid-editing/editing.ts:240-270`; `mount` focuses the editor at `:539`.
- **What happens:** `resume` mounts and focuses the editor before it replays a held commit,
  even when focus has left the grid. The outside element blurs and is focused again, with
  side effects:
  - a form field is marked touched, or validated;
  - an outside picker or select closes its panel;
  - an IME composition in that field is committed.
- **With the context menu:** if the user opened the grid's context menu during the window,
  that held commit has `refocus: true` (`grid-core.ts:2265`, `:2291`). Focus moves from the
  open menu to the editor, then to the cell.
- **Fix:** when focus is outside the scroller at arrival and the held work includes a commit,
  mount without focusing the editor. Replay a held `refocus` only while focus is still in
  the grid.

**R1-8 (minor): the boundary lint rule sees direct imports only.**
- **Where:** `client/eslint.config.mjs:280-312`.
- **What happens:** the rule stops grid/ and tree-grid/ from value-importing the editor
  controls. But a static import of one inside any entry point that ./grid already imports
  statically would put it back on every readonly route. That set is menu, checkbox,
  private, overlay, form-field, icon, spinner, kbd, l10n, grid-engine, contracts and the
  primary entry. Neither the lint nor `size:check` would notice, because `size:check`
  measures each sibling as external.
- **Today's graph is clean:** see the bundle check above.
- **Fix:** make that check a gate, in `size:check` or as a build step.
  1. Bundle ./grid and ./tree-grid with `@tellma/*` resolved to `dist` and code splitting on.
  2. Fail if a static output contains grid-editing, date-picker, entity-picker, input,
     number, select or modal.

**R1-9 (minor): spec citations in TSDoc, and undocumented fields in a new file.**
- **Spec citations.** AGENTS.md keeps spec references out of TSDoc. Three TSDoc blocks in
  this change cite spec sections:
  - `grid/internal/grid-core.ts:1833` ("§4") and `:3800` ("§8.2 Esc, §5.1"), both rewritten
    by this change;
  - `grid-editing/editing.ts:372`, which carries "§8.2 Esc, §5.1" into the new entry point's
    TSDoc.
- **Missing TSDoc.** `grid-editing/editor-session.ts:153-156` (`pending`, `mounted`,
  `destroyView` and `activationSub`) have no TSDoc, in a file this change creates.
- **Fix:** remove the section numbers from those TSDoc blocks (inline comments may keep
  them), and document the four fields.

**R1-10 (minor): deviation 3 understates the production behaviour.**
- **Where:** `grid-editing/editing.ts:242-245`; `grid/internal/grid-core.ts:3774`.
- **What happens:** the misconfigured-entity guard changes production behaviour too, not only
  development behaviour. In production `admits()` returns `false` silently. So during the
  window:
  - the key is consumed;
  - the cell turns blank and shows as editing;
  - the session is cancelled when the code arrives.
- **Enter is affected as well.** Before, Enter fell through to activating a cell link or
  moving down (`:3186-3197`). It now does nothing.
- **Fix:** either change, or both:
  - Run the `admits` predicate, without its messages, in ./grid's loading branch, so the
    refusal stays synchronous. Only the development error then waits for the editing code.
  - Record the production case in deviation 3.

### Responses

Fix round 1. Design, then each finding.

#### Design: a draft the engine can commit, in place of hold-and-replay

The window is the time between an editable grid asking for its editing code and the code arriving.
In that window the grid no longer holds key events to replay. It keeps a **draft**: the text of the
session it opened, which it can commit through the engine itself.

- **Opening.** Before the code arrives, `openEditor` checks `hasEditor(cell)` (the `admits`
  predicate without its messages; R1-10). It opens the engine session at once, and the cell shows
  the draft text (`[data-tm-draft]`) with the pending spinner where the editor will be.
  - Type-to-edit: the draft starts with the typed character.
  - F2, Enter, double-click: the draft starts empty and shows the cell's own text.
  - A draft already open is committed first, never replaced (R1-2).
- **Keys** (`onDraftKeydown`, then the shared editing resolver):
  - A typed character extends the draft. The test is `tmIsTypedText`, the key map's own type-to-edit
    predicate, now exported and used by both (R1-4).
  - Backspace removes one code point.
  - Delete is consumed with no effect: the draft's caret is always at its end, so a forward delete
    has nothing to remove, as in an editor.
  - An edit-mode draft's first key starts from `editTextOf(cell)`, the text the editor would open
    with. Enum and template columns start from `''`, since their editors treat typing as a search.
  - Escape cancels the draft at once (R1-6).
  - Enter, Tab and the arrows commit and move through the same commit-and-move as an open editor.
  - F2 toggles the mode. Alt+ArrowDown records that the editor should open its dropdown.
  - Any other key is one an editor would have kept: the caret keys, Mod+V, Mod+Z, Mod+A, Alt
    chords. The draft commits, and the key then acts on the grid as on any cell (R1-5). It is never
    swallowed and never replayed. Mod+V therefore pastes into the grid, and an Alt+S chord reaches
    the page.
- **Commits** (`commitEditor` before the code arrives) are synchronous (R1-3). An entity column
  commits through `commitEditorLabel` plus `runResolutions`; every other column through
  `engine.edit.commitText` and the column's parse. An untouched draft, or one typed back to its
  starting text, cancels instead, so opening and closing writes nothing. Every caller therefore
  finds the session closed when `commitEditor` returns, as before the split:
  - commit-on-blur happens before an outside click;
  - a press back into the grid focuses the pressed cell;
  - a boolean toggle or a menu action writes after the draft, not under it.
- **Arrival.** `installEditing` clears the draft, then `ɵTmGridEditing.resume(opts, text, focus)`
  mounts the editor and installs the draft's text: through `seed()`, or `value.set` for a control
  without one (R1-5). `focus` is true only while focus is inside the scroller (R1-7).
- **The editing keys moved back into ./grid**, with `tmResolveEditingKey`, its spec, and
  commit-and-move. The window and the open editor resolve the same keys through one resolver.
  ./grid-editing keeps the editors, the mount, commit and cancel, and the status bar. The
  controller gained `isDropdownOpen()` and `openDropdown()`, and lost `keydown` and replay.

Why this design rather than the alternative the orchestrator raised (preload and intercept nothing
until the code arrives):
- 0004 §8.4 makes any printable key on an editable cell open the editor seeded with it. With nothing
  intercepted, keys typed in the window would act as on a readonly cell: lost text, and Enter moving
  down rather than editing. That breaks the brief's "a key typed while the chunk loads must not be
  lost".
- A draft keeps the text without needing the editor, because every built-in column commits text
  through the engine already. The paste path proves it: `commitText` with the column's parse, and
  the label ladder for entities.
- 0010 §C.1 and §C.15 change nothing here. Row activation (§C.1) still owns Enter and Mod+Enter on
  `rowActivation="row"` grids, because the window branch only runs once a session is open. §C.15's
  popup editors will mount through the same controller, and their draft behaves like an in-cell
  editor's.

**Deviations of the design (window only), added to the notes' list:**
- **Enum drafts commit through the column's parse.** A full label commits its value. A typeahead
  prefix such as "p" becomes an invalid input showing "p". The editor would have picked the first
  match, "pcs".
- **Caret keys.** In an edit-mode draft, ← → Home End commit and then move, rather than moving a
  caret. Delete does nothing.
- **IME** is unchanged: a composition in the window is not captured, and the draft stays untouched
  and commits nothing.
- **Template editors.** A draft on a template-editor column commits its text through the column's
  parse, as paste does.
- **Entity drafts ask the resolver.** The draft has no type-ahead search to adopt, so an entity
  draft that commits before the code arrives always goes to `resolvePastedLabels`, as a pasted
  label does. The open editor can settle a unique hit or a duplicate label from its own search at
  no resolver cost. In the window, the resolver gives the verdict instead, at the cost of a round
  trip. The PR e2e run caught this on a starved runner, where a test typed before the code arrived
  (see Gates).

#### R1-1 (blocking): fixed

I moved `.angular/cache` aside (`.tmp/pcf21-angular-cache-aside`), removed `dist`, and ran a clean
`pnpm run build`. The clean d.ts declares `TmGridBase.rowActivation` as
`InputSignal<"row" | "none">`, the base's order, so the golden's line is back as it was at 9007c81.
I then ran `api:approve` on that clean dist and `api:check` passes. The earlier explanation ("the
moved code shifted it") was wrong: the cause is the one D-057 records, a warm cache. The golden is
now settled against a clean full build only.

Against 9007c81, the grid golden now differs only in two import lines, and tree-grid in one:
- `import { ApplicationRef }` (grid and tree-grid), from round 0's `ɵTmGridCoreDeps`.
- `import * as lucide from 'lucide'` (grid only). ./grid now exports the loader seam
  `ɵTM_GRID_EDITING_LOADER`, whose type names the editing module's ɵ classes, so api-extractor
  follows them into grid-editing's declarations and their icon imports. The golden filter drops the
  ɵ blocks but keeps the import line. No public declaration changed.

#### R1-2 (major): fixed

A draft is never replaced. `openEditor` commits an open draft before it opens another session. A
double-click's first press already commits it synchronously (R1-3), so A's text lands before B
opens.
- Unit test: "a double-click on another cell commits the draft first, then opens that cell". It
  checks A = "Q" before the release, and B's editor showing "Gamma" after.

#### R1-3 (major): fixed

Commits made before the code arrives are synchronous (see Design). No engine session outlives a
`commitEditor()` call.
- `requestFocusActive` and the roving-focus effect now step aside only for a **mounted editor**,
  not for any open session. A draft's cell keeps focus like any cell.
- On destroy the draft is dropped, as an open editor's view always was. A Save-and-close commits
  earlier, on the blur.

Unit tests, each failing under the hold design:
- "an outside click handler sees the typed value: the blur commits before the click": the handler
  reads "W" synchronously.
- "a press back into the grid from outside focuses the pressed cell".
- "a readonly flip, or the grid going away, cancels the draft and the arrival opens nothing".

#### R1-4 (major): fixed

Both paths use one predicate, `tmIsTypedText(event, apple)` in grid-keymap.ts, which
`tmResolveGridKey` also uses. Replay is gone, so synthetic events and their missing `AltGraph` no
longer arise.
- Unit test: "an AltGr character extends the draft, and a bare Alt chord commits it and reaches the
  page". Ctrl+Alt+AltGraph "@" gives the draft "a@b". Alt+S is not prevented and commits "a@b".

#### R1-5 (minor): fixed

- Mod+V, Mod+Z, Mod+A, Ctrl+Backspace and the caret keys commit the draft and then act on the grid.
  `onPaste` also commits an open draft and pastes into the grid, for a paste that arrives without a
  keydown.
- The entity text base comes from `editTextOf`, never from the picker's `text()`.
- `seed()` falls back to `value.set`.
- The caret limitation is in the deviations above.
- Unit tests: "Mod+V while the code loads commits the draft and leaves the paste to the grid", and
  "F2 then typing while the code loads edits the cell text, and Enter commits it" ("Alphax"; an
  untouched F2 + Enter writes nothing).

#### R1-6 (minor): fixed

1. **Escape acts at once.** It cancels the draft and keeps focus on the cell. Unit test and e2e
   test.
2. **The load is bounded.** It races `ɵTM_GRID_EDITING_LOAD_TIMEOUT` (10 s; tests shorten it).
   - On a timeout or failure, the draft commits as text (an untouched draft cancels), the error goes
     to the error handler, and the `PendingTasks` task ends, so `whenStable` resolves. The next open
     or paste asks again.
   - The keyboard is never trapped in the window: only typed text, Backspace, Delete and Escape stay
     with the draft, and everything else acts on the grid.
   - A later retry gets the cached module, and `installEditing` ignores a second arrival.
3. **A paste retries the load** on an editable grid whose code has not arrived.

Unit tests: "a stalled load times out: the draft commits, the error is reported, and the next open
asks again", and "a failed load reports the error, and a paste asks again".

#### R1-7 (minor): fixed

`resume` focuses the editor only when focus is inside the scroller at arrival. A held refocus no
longer exists. With synchronous commits, a draft is still open at arrival only while focus is in
the grid or in an overlay pane, because focusout into an overlay does not commit.
- Unit test: "the arrival leaves focus where the user moved it" (focus in a `.cdk-overlay-pane`
  input stays there, and the editor mounts with the draft).
- e2e test: an outside click commits, and the arrival leaves focus on the outside button.

#### R1-8 (minor): fixed, as a gate

`scripts/bundle-size.mjs` reads a new `"tellma".staticGraphExcludes` policy. core-ui declares it for
./grid and ./tree-grid against grid-editing, date-picker, entity-picker, input, number and select.
- **How it works.** For each policy entry, the gate bundles the entry point's FESM with esbuild.
  Its library's sibling entry points resolve to their built FESMs, everything else stays external,
  and code splitting is on. The gate then walks the static imports from the entry output and fails
  if an excluded entry point's FESM is among the inputs reached.
- **Cost.** About 2 s on top of `size:check`.
- **Proof that it fires.** Adding `./menu` to tree-grid's list made the gate fail with "'./tree-grid'
  reaches './menu' through static imports" (reverted).
- I added no separate check for `modal`: it is reachable only through entity-picker, which is
  listed.

#### R1-9 (minor): fixed

- The TSDoc blocks that cited §4 and §8.2/§5.1 were rewritten without section numbers. Inline
  comments keep theirs.
- The four `ɵTmGridEditorSession` fields have TSDoc.

#### R1-10 (minor): fixed

`openEditor` checks `hasEditor(cell)` synchronously before the code arrives. A misconfigured entity
column therefore opens no session, the key is not consumed, and Enter falls through to the link or
the move, as before the split. Deviation 3 no longer applies. The development error still fires
from the editing code on the first open after arrival.
- Unit test: "a column with no editor opens no draft: the key acts as on any cell".

#### e2e of the load window

New story `grid-lazy-editing` in the showcase. It provides `ɵTM_GRID_EDITING_LOADER` with a gate
that `window.tmShowcaseReleaseGridEditing()` opens, so the window stays open until the test
releases it. `ɵTM_GRID_EDITING_LOADER` and `ɵTM_GRID_EDITING_LOAD_TIMEOUT` are now exported from
./grid as ɵ names, which are golden-excluded.

`e2e/specs/grid-lazy-editing.spec.ts` (5 tests, trusted input):
1. The draft shows while the code loads; on arrival the editor has the text, focused, and typing
   carries on.
2. Enter commits at once and moves, and the next key opens the next draft.
3. Escape drops the draft at once.
4. An outside click commits, and the arrival leaves focus outside.
5. A click on another cell commits and focuses that cell, which then takes typing.

#### Sizes (clean build, cache moved aside)

| Entry point | Before PC-F21 | Round 0 | Fix round 1 | Ceiling |
|---|---|---|---|---|
| ./grid | 42.00 | 37.06 | **38.27** (39,193 B) | 38.75 |
| ./grid-editing | (new) | 7.06 | **6.27** (6,417 B) | 6.37 |
| ./tree-grid | 0.97 | 0.97 | 0.97 | 8 |
| ./grid-engine | 14.84 | 14.84 | 14.84 | 15 |

The draft, commit-and-move and the editing keys moved back to ./grid: +1.21 KB. That is grid-core
+0.68, the key map +0.19, and the view +0.08, with the rest the load timeout and helpers. Under the
spec's 42 KB, PC-051 and PC-054 have **3.73 KB** between them, which meets the brief's 3 KB floor.
The budgetNotes are updated.

#### Gates (clean build)

All on a clean build: `.angular/cache` moved aside, `dist` removed, full `pnpm run build`.

| Gate | Result |
|---|---|
| `size:check` | pass: ./grid 38.27 / 38.75, ./grid-editing 6.27 / 6.37; both static-graph checks report that ./grid and ./tree-grid statically reach none of their excluded entry points |
| `lint`, `lint:test` | pass |
| `typecheck` | pass |
| `tokens:check`, `docs:build` | pass |
| `test` (all unit projects) | pass; core-ui 1,837 |
| `api:check` | pass (45 reports), after `api:approve` on the clean dist; the golden diff is explained under R1-1 |
| e2e, chromium + touch, PR tier, `--workers 2` | 784 passed |

The first e2e run had one failure: "two rows carrying the SAME label are ambiguous outright, at no
resolver cost" counted one resolver call. On the loaded runner its typing started before the editing
code arrived, so the keys went to a draft, and an entity draft asks the resolver (see the design
deviations). That test is about the editor, so `gotoGrid` now waits for the app to settle. The load
is a pending task, so a settled editable grid has its editors. The lazy-editing spec opens its story
without that wait, because its app stays unsettled until the test releases the code. The rerun
passed in full.

#### Misalignments for Ahmad (added)

- Spec 0004 §8.4's type-to-edit now has a short window in which typing goes to a draft rather than
  an editor, with the behaviours listed under the design deviations. The spec could say so if this
  design stands.

## Review 2

Reviewer 2 (Opus), on c07bef9: the change 15ba8ec plus fix round 1, base 9007c81. Logs, probe
sources and mutation runs are in `.tmp/r2-pcf21-*`. The probes and mutations were temporary, and
the tree is clean.

What I ran on a clean build. `client/.angular/cache` was moved to `.tmp/r2-pcf21-ngcache`, `dist`
was removed, and a full `pnpm run build` took 3 min 9 s.
- `size:check`: pass.
  - ./grid **38.27 / 38.75**, ./grid-editing **6.27 / 6.37**, ./tree-grid 0.97, ./grid-engine 14.84.
  - Both static-graph checks pass.
  - The gate fires: adding ./menu to ./grid's excludes failed it with "'./grid' reaches './menu'"
    (reverted).
- `api:check`: pass (45 reports). The clean d.ts declares `InputSignal<"row" | "none">`, and so does
  the golden (`core-ui.grid.api.md:70`).
- `lint`, `typecheck`, `lint:test` (90 tests): pass.
- Unit tests:
  - `ng test core-ui --include "grid/**/*.spec.ts"`: 8 files, 402 tests pass.
  - tree-grid: 13 pass.
  - grid-editing now has no specs of its own.
- e2e, with `--repeat-each 3 --workers 2`:
  - Run 1, 183 passed in 5.2 min. Chromium ran grid-lazy-editing, grid-editing, grid-date,
    grid-a11y-editable and grid-keyboard-layout. Touch ran grid-touch and grid-touch-search-page.
  - Run 2, 114 passed in 2.9 min: grid-entity and grid-rtl on chromium.
  - The touch project's `testMatch` never runs grid-lazy-editing (R2-9).
- Probes (temporary, now deleted):
  - Unit specs with gated, rejecting and stalled loaders, P1–P9: `.tmp/r2-pcf21-probe.spec.ts.txt`
    and `.tmp/r2-pcf21-probe2.spec.ts.txt`.
  - A trusted-input e2e on the lazy story: `.tmp/r2-pcf21-shift.e2e.ts.txt`.
- Mutations of grid-core.ts against `tm-grid-lazy-editing.spec.ts` (reverted):
  - **A:** three changes, with these results:
    - The commit-first guard in `openEditor` removed: the double-click test still passes.
    - `tmIsTypedText` in `onDraftKeydown` swapped for the old `key.length === 1 && !ctrl && !meta`:
      the AltGr test fails.
    - `focus` forced true in `installEditing`: the arrival-focus test fails.
  - **B:** the press and blur commits of a draft deferred by a macrotask. The "outside click
    handler" test and the double-click test fail.
  - **C:** `requestFocusActive` and the roving-focus effect reverted to step aside for any open
    session. All 17 tests pass.

R1 findings, judged by the code:
- **R1-1:** fixed. Verified on the clean build above.
- **R1-2:** fixed. A double-click's first press now commits the draft synchronously, and mutation B
  shows the test pins that. The extra guard in `openEditor` is unpinned (mutation A) but harmless.
- **R1-3:** fixed for ordering.
  - Blur, a press, a touch tap, a toggle, the context menu, a long-press, Paste, Mod+F, `gotoError`,
    commit-and-move, Escape, a readonly flip and a content switch each close the engine session
    synchronously.
  - No session outlives `commitEditor` or `cancelEditor`. The draft's write lands before a toggle,
    a menu action or a paste.
  - A destroy drops the draft, as it dropped an open editor's text before the split.
  - The focus half is moot now, and its changed checks are unpinned (mutation C).
- **R1-4:** not fixed in a browser. The predicate is shared, but the modifier keydown that comes
  before an AltGr character commits the draft first. See R2-1.
- **R1-5:** fixed, but R2-4 is a side effect of letting Mod+Z act on the grid.
- **R1-6:** the window is bounded, but the failure path now loses text (R2-2) and outlives the grid
  (R2-8).
- **R1-7:** fixed for text, number and date only (R2-5).
- **R1-8, R1-9, R1-10:** fixed.

Answers to the brief:
- **Session lifecycle, write order, double-click, a press on another cell, Escape:** correct (see
  R1-2 and R1-3 above). Escape cancels at once and keeps focus on the cell.
- **Timeout and failure:** the `PendingTasks` task ends, the keyboard is free, and the next open or
  paste retries, all verified by the unit tests. But see R2-2 and R2-8.
- **Focus on arrival:** right for text, number and date. Enum and entity: R2-5.
- **RTL:** the draft span takes `cell.dir`, and its padding is logical. I found no window-specific
  RTL problem. The RTL e2e run settles first, so it never sees the window.
- **Touch:** R2-11, plus the coverage gap in R2-9.
- **Entity columns:** R2-3. **Undo:** R2-4.
- **Validation:** a committed draft goes through the same engine commit as editor text: `commitText`
  with the column's parse, or the label ladder. It shows invalid input and cell errors exactly as an
  editor commit does.
- **The `gotoGrid` settle wait:** it hides no bug in the specs it settles, which test the editor.
  - It does mean none of the existing batteries runs in the window, and users can reach it. The
    window opens on the first render of an editable grid, or when Edit flips a readonly grid, until
    the chunk arrives.
  - Typing in the window has R2-1, R2-2 and R2-3.
  - The new window e2e types with `keyboard.type`, which sends no Shift keydown, so it cannot see
    R2-1.
  - The test that failed on the starved runner showed R2-3's other half: a label two entities share
    goes to the resolver.
- **The ɵ loader seam exported from ./grid:** acceptable. The showcase needs a package import, and
  ./grid already exports `ɵTmGridCore` the same way. The golden side effect is R2-10.
- **The listed window deviations:**
  - Enum drafts through the column's parse: acceptable. The result is a visible invalid input, not
    a wrong value.
  - Caret keys commit and move: acceptable, but it breaks spec 0004 §8.4 edit mode ("arrows move
    the caret") for the window.
  - IME not captured: acceptable, but it breaks 0004 §8.4's "type-to-edit must not eat CJK input"
    for the window, for Amharic input methods too.
  - Template editors through the column's parse: acceptable for text-like templates.
  - Entity drafts ask the resolver: not acceptable as it stands (R2-3).
  - Not listed: R2-4, R2-6, R2-7 and R2-11.
  - A plain `<input>` as the draft surface would remove most of these deviations; see R2-1's fix.

### Findings

**R2-1 (blocking): a modifier or dead-key keydown commits the draft, and the next character
replaces it. A capital letter, an AltGr character or an accented character typed in the window
therefore loses everything typed before it.**
- **Where:**
  - `grid/internal/grid-core.ts:1894-1902`, the window branch for "a key an editor would have kept".
  - `onDraftKeydown`'s typed-text gate at `:1937-1942`.
- **What happens:**
  1. In the window, the user types "ab" into the draft.
  2. To type "C" they press Shift. Its keydown (`key: 'Shift'`) is not typed text, and
     `tmResolveEditingKey` returns `null` for it. The branch commits "ab" and closes the session.
  3. "C" arrives with no session. `tmResolveGridKey` resolves type-to-edit, which opens a new draft
     seeded "C" in enter mode, replacing the cell's content.
  4. Enter commits "Cd". "ab" is gone, with no error.
- **Other keys that do the same:**
  - Control then AltGraph, which Windows sends before an AltGr character. On German "@" or Polish
    "ą", "a@b" commits as "@b".
  - CapsLock.
  - A dead key (`key: 'Dead'`): the accents on French, Spanish and Portuguese layouts.
  - Alt or Meta pressed alone.
- **Evidence:**
  - Real browser, trusted input, on the lazy story: `type('ab')`, `press('Shift+C')`, `type('d')`,
    Enter. The description is "Cd".
  - Unit probe P8: "a", Shift, "B", "c" gives "Bc".
  - Unit probe P9: "a", Control, AltGraph, "@", "b" gives "@b".
  - This is why R1-4 is not fixed. Its test (`tm-grid-lazy-editing.spec.ts:444`) dispatches only
    the "@" keydown.
- **Fix:** either change would do.
  - In that branch, ignore keys that never act alone, with no commit and no `preventDefault`:
    `Shift`, `Control`, `Alt`, `AltGraph`, `Meta`, `CapsLock`, `NumLock`, `ScrollLock`, `Fn`,
    `FnLock`, `Hyper`, `Super`, `OS`, `Symbol`, `SymbolLock`, `Dead`, `Compose`, `Process`,
    `Unidentified`. Chromium reports a dead key's composed character on the next keydown, so
    "Dead" then "é" extends the draft.
  - Or make the draft surface a bare `<input>` in the cell, without `tm-input`, focused on open,
    and hand its value and caret to the editor on arrival.
    - It costs ./grid a few hundred bytes.
    - It takes modifiers, dead keys, IME compositions, the caret keys, Delete, native undo, paste
      into the draft and the touch keyboard natively.
    - That removes R2-1, R2-4, R2-7 and R2-11, and the IME and caret deviations, with no key
      table.
- **Tests:**
  - Unit: "a", a Shift keydown, then "B" gives the draft "aB". Control, AltGraph, then "@" (the
    Windows sequence) gives "a@". "Dead" then "é" gives "aé".
  - e2e: `press('Shift+C')` in the window.

**R2-2 (major): a failed or timed-out load commits the draft at whatever moment it fails. While
the code keeps failing, every keystroke replaces the one before it.**
- **Where:** `grid-core.ts:3905-3910`, where the catch commits the draft, with the retry on each
  open at `:4006`.
- **Scenario A: the load keeps failing,** as when the user is offline or a chunk returns 404 after
  a deploy.
  1. The user types "1". A draft opens and asks for the code again, and the import rejects.
  2. The catch commits "1" and closes the session.
  3. "2" arrives with no session. Type-to-edit opens a draft "2" that replaces "1", and the next
     rejection commits it.
  4. The cell ends with the last character only. Probe P1: "1", "2", "3" gives "3", with five
     errors reported.
  - No cell error marks the loss, and the status bar that would show a tally never loads.
- **Scenario B: a slow network.** The 10 s timer starts when the grid becomes editable, not when the
  draft opens. A user typing "Long" as it fires gets "Lo" committed, then a new draft "ng" that
  replaces it (probe P2).
- **Why the commit is unneeded:** the draft is complete without the editing code. Enter, Tab, the
  arrows, a press, blur and Escape all settle it through the engine.
- **Fix:**
  - On a failure or timeout, end the pending task and report the error, but leave the draft open.
    The user's own commit gesture settles it.
  - Keep the retry on the next open or paste.
  - Update the test at `tm-grid-lazy-editing.spec.ts:549`, which asserts that the draft commits.
- **Test:** a loader that always rejects, three keys with a macrotask between each. The draft reads
  "123", and Enter commits "123".

**R2-3 (major): an entity draft on a column without a resolver turns valid text into an invalid
input and clears the cell.**
- **Where:**
  - `grid-core.ts:4034-4037`: `commitEditorLabel(text)`, without `deferToHost`.
  - Rung (3) of `commitLabel` in `grid-engine/tm-grid-edit-state.ts`.
- **What happens:**
  - Spec 0006 §7.4 says a column with `search` but no resolver "still resolves typed text". Take
    such a column, type "Bob" in the window and press Enter.
  - The ladder has no resolver and no host rung, so it writes the cleared value with a parse error.
  - Probe P3: `agentId` goes from 11 to `null`, and the cell shows "Bob" in error. The editor would
    have adopted its own search and written 12.
- **The listed deviation does not cover it.** It says entity drafts go to `resolvePastedLabels`, but
  this column has none, so nothing decides and the verdict is wrong.
- **With a resolver, the draft also skips 0006 §7.4's search rungs:**
  - A label two entities share goes to the resolver, which may return one of them. The search
    would have called it ambiguous.
  - Every entity draft costs a resolver call. The spec's "commonly none" no longer holds.
- **Fix:**
  - Commit an entity draft with `deferToHost: true`, so the cell goes pending under the grid's
    affordance.
  - Answer the request from the column's search, with the decision the controller already makes.
    Either:
    - let `installEditing` hand the pending request to a new `ɵTmGridEditing` method that runs
      `entitySearch(text)` and `decideFromSearch`, with a failed load answering
      `{ failed: true }`; or
    - call `column.entitySearch` from ./grid and move `decideFromSearch` to ./grid-engine.
- **Tests:**
  - A typed unique label on a column with no resolver, committed in the window: the value is
    written, with zero resolver calls.
  - A label two entities share: ambiguous.

**R2-4 (minor): Mod+Z on a draft runs the grid's undo, which can revert an earlier, unrelated
edit.**
- **Where:** `grid-core.ts:1894-1902`: Mod+Z commits the draft, then `onKeydown` runs the undo.
- **What happens:**
  - In the window, F2 on a cell, then Ctrl+Z. The untouched draft cancels, and the grid's undo
    reverts the previous history entry. Probe P5 reverted an "A" committed a moment earlier.
  - After typing, the first Ctrl+Z undoes the draft's own commit and a second undoes earlier edits.
  - Mod+Y and Mod+Shift+Z redo the same way.
  - In an editor, Mod+Z is the input's native undo. Spec 0004 §11 runs the grid's undo only "when
    the grid has focus and no editor is open".
- **Fix:** in the window, treat Mod+Z, Mod+Y and Mod+Shift+Z as draft keys. Mod+Z restores the draft
  to its start, or cancels it when untouched. `preventDefault` them, and never let them reach the
  grid's undo.

**R2-5 (minor): R1-7 is fixed only for text, number and date. An enum resume takes focus, and an
entity resume opens its dropdown, while the user is elsewhere.**
- **Where:**
  - `grid-editing/editing.ts:251`, and `:504`, `:506` and `:514`: `seed` and `openDropdown` run
    whatever `focus` says.
  - `select/tm-select.ts:338-339`: `seed` calls `focus()` and opens the panel.
- **What happens:**
  - Probe P6: an enum draft "p", focus moved to an input in an overlay pane, then the code arrives.
    Focus ends in the select's combobox.
  - An entity resume's `seed` opens the dropdown under a user who is not in the grid.
- **Fix:** either of these.
  - When focus is outside the grid at arrival, commit the draft rather than resuming it. A draft
    owns no overlay, so a focusout into an overlay need not keep it open.
  - Or mount without seeding or opening any panel, and seed on the next focus.

**R2-6 (minor): an enum draft resumes with only its first character.**
- **Where:** `editing.ts:251` calls `tm-select.ts:330-339`. Its `seed` feeds only `text[0]` to the
  typeahead, replacing the mount's own seed.
- **What happens:**
  - The options are kg, pallet and pcs. Type "pc" in the window.
  - On arrival the active option is "pallet" (probe P4), and Enter commits it.
  - Typing "pc" into the editor would have reached "pcs".
- **Fix:** feed the whole draft to the typeahead through an internal select hook. Or commit an enum
  draft longer than one character instead of resuming it. Otherwise, list it as a window deviation.

**R2-7 (minor): an IME-opened draft starts from the cell's text.**
- **Where:** `grid-core.ts:1949-1952`, where `draftBase` applies to any draft whose text is `null`,
  with the IME open at `:1825`.
- **What happens:**
  - A composing keydown opens the draft (edit mode, `ime`), and the composition is lost (a listed
    deviation).
  - The user then types "a", and the draft becomes "Alphaa" (probe P7).
  - Under `ime` the editor opens unseeded, with an empty baseline, so it would hold "a".
- **Fix:** start from `''` when `draftOpts.ime` is set.

**R2-8 (minor): the load's timer and its error outlive the grid, and a persistent failure reports
once per attempt.**
- **Where:** `grid-core.ts:3888-3917`.
- **What happens:**
  - A user leaves the route within 10 s while the chunk is slow. The pending task keeps the app
    unstable until the timer fires.
  - Then the error handler receives "did not load within 10000 ms" for a grid that no longer
    exists.
  - Under R2-2's scenario A, probe P1 logged five errors for three keys.
- **Fix:**
  - Race a destroy signal as well, clear the timer in `onDestroy`, and do not rethrow once
    destroyed.
  - Report a failing load once per grid, or once per run of failures.

**R2-9 (minor): test gaps.**
- No test sends the keydowns a real keyboard sends around a character: Shift, Control, AltGraph,
  Dead. So R2-1 passed every gate.
  - The AltGr unit test (`tm-grid-lazy-editing.spec.ts:444`) dispatches only "@".
  - The e2e uses `keyboard.type`.
- The fix round's focus checks are unpinned. Reverting `requestFocusActive` (`grid-core.ts:4244`)
  and the roving-focus effect (`:5027-5028`) to "any open session" passes all 17 tests
  (mutation C).
- R1-2's guard, which commits an open draft inside `openEditor` (`grid-core.ts:3995-3997`), is
  unpinned. Without it the double-click test passes, because the first press commits.
- The window e2e runs on chromium only. The touch project's `testMatch`
  (`e2e/playwright.config.ts:89`) skips grid-lazy-editing, so no test double-taps in the window.
- The story keeps the 10 s load timeout (`grid-lazy-editing-story.ts:42-50`).
  - A runner that takes more than 10 s before `release()` closes the window.
  - The arrival tests then fail on the abandoned loader.
  - Provide `ɵTM_GRID_EDITING_LOAD_TIMEOUT` with a long value in the story.

**R2-10 (minor): the grid golden now depends on grid-editing's icon imports.**
- **Where:**
  - `client/api/core-ui.grid.api.md:19` (`import * as lucide from 'lucide'`).
  - `grid-core.ts:114-119`: `ɵTmGridEditingCode.ɵTmGridStatusBar` is typed
    `typeof ɵTmGridStatusBar`.
- **What happens:** the exported token's type makes api-extractor follow into the status bar's
  declarations. A change to the status bar's glyphs then drifts ./grid's golden, though no ./grid
  declaration changed.
- **Fix:** type that member as `Type<unknown>`, which is all `NgComponentOutlet` takes. Confirm with
  `api:check` that the lucide line drops.

**R2-11 (minor): touch editing in the window has no keyboard.**
- **Where:** the draft design (`grid-core.ts:3982-4007`), and the arrival focus at
  `editing.ts:477-479`.
- **What happens:**
  - On a phone or tablet, a double-tap in the window opens a draft on a cell with no input, so no
    on-screen keyboard appears, and nothing can be typed.
  - On arrival the editor's `focus()` runs outside a user gesture. iOS Safari does not raise the
    keyboard for that, so the user must tap again.
- **Fix:** list it as a window deviation. The bare-input draft surface in R2-1's fix removes it,
  because the double-tap would focus a real input inside the gesture.

**Spec misalignments for Ahmad** (some are already in the implementer's list):
- Spec 0010 §C.11:
  - Its table has no ./grid-editing row (6.37), and ./grid's lowered ceiling is not recorded.
  - It still says on-demand loading "is not part of this amendment".
- Spec 0010 §C.15:
  - Its budget line puts the popup editors' +1 KB on ./grid, while the new budget notes say they
    belong in ./grid-editing.
  - It makes popup editors "always edit mode" (the arrows never commit and move). A draft in the
    window commits and moves on the arrows and on Enter. When §C.15 lands, the draft needs a rule
    for popup columns.
- Spec 0004 §8.4 (edit-mode caret, IME), spec 0004 §11 (undo only with no editor open), and spec
  0006 §7.4 (typed-commit resolution, including the no-resolver case): if the keyed draft stands,
  each needs a window clause. The bare-input draft surface removes most of that need.

### Responses

Fix round 2. The design first, then each finding.

#### Design: the draft is a real input, and the code preloads when idle

The keyed draft is gone. Both of the orchestrator's directions are in.

**(a) The draft is a bare `<input data-tm-draft>` in the editing cell.**
- `openEditor` opens the engine session, renders the input with `ApplicationRef.tick()` (the same move the editor's mount makes) and fills it:
  - the type-to-edit seed;
  - `''` under an IME composition;
  - otherwise the text the editor would open with (`''` for enum and template columns).
- It focuses the input synchronously, inside the gesture. On a phone, a double-tap therefore raises the keyboard.
- The browser owns the text: modifiers, dead keys, IME, the caret, Delete, local undo and paste. The grid has no key table for it.
- **Keys.** A key in the draft goes through the same path as a key in an open editor:
  - `onEditingKeydown` and `tmResolveEditingKey`, with the session's own mode.
  - A key that resolver does not name stays with the input, untouched.
  - Escape cancels, F2 toggles the mode, and Enter and Tab commit and move.
  - The arrows follow the editor's own rules. In an F2 (edit-mode) draft they move the caret. In a typed (enter-mode) draft they commit and move, as the editor does under spec 0004 §8.4.
  - Mod+Z, Mod+Y and Mod+Shift+Z are the input's own, so they never reach the grid's undo.
- **Commits.** A draft commits only on the user's own gestures: Enter, Tab, an enter-mode arrow, blur, or a press or an open on another cell.
  - A draft owns no overlay, so focus moving into one commits it.
  - Nothing commits a draft because a load failed or timed out.
- **Arrival.**
  - If the draft input has focus, the editor mounts and takes focus while the input is still in place, so focus never drops. It then receives the draft's text (`seed`, only when the text changed from what the mount installs) and its caret or selection (`setSelectionRange` on the focused input).
  - If the input does not have focus, the draft commits as it stands, and the arrival opens nothing.
  - If an IME composition is in progress, the code waits for `compositionend` and installs then.

**(b) Preload on idle.**
- Once an editable grid has rendered, `preloadEditing` waits for `requestIdleCallback` (2 s timeout; Safari falls back to `setTimeout`), then loads.
- A pending task covers the wait, so app stability still includes the editing code.
- An open or a paste in the meantime starts the load at once.
- The lazy chunk keeps the initial bundle as it was. The draft is now the correctness fallback for a slow or failed network.

**Loading.**
- Each attempt has its own timeout and pending task.
- **A failure or a timeout:**
  - ends the attempt;
  - reports the error once per grid;
  - leaves the draft untouched.
- **Retries.** The next gesture asks again quietly. That covers any key in a draft, an open and a paste.
- **Late arrival.** Code that arrives after its attempt timed out is still installed.
- **Destroy:**
  - ends the attempt (its timer and pending task);
  - cancels the preload;
  - drops the pending entity commits;
  - suppresses any report.

**Entity columns (R2-3): the cell stays pending.**
- A built-in-picker entity draft commits with `commitEditorLabel(text, { deferToHost: true })`. The cell goes to the grid's pending state (spinner, cleared value, one open history entry), and the request is queued.
- On arrival, `ɵTmGridEditing.resolveLabel` runs the column's search for the text through `ɵtmEntitySearchOnce`, then answers through `settleLabel`. That is the tail of the editor's own commit, split out and unchanged:
  - `decideFromSearch` decides where it can;
  - the resolver runs with the search's fallback where it cannot;
  - a failed search on a resolver-less column gives a retryable failure.
- **Shared code.** The result normalization (object form, de-duplication by id) moved from the picker into `entity-picker/tm-entity-search.ts`. The picker and the grid now share it.
- **Templates.** A template editor's entity draft takes the resolver at once, as its editor would.
- **Undo, a readonly flip and disposal** abort the queued request, and the drain skips it.

Why I chose pending over waiting with the draft open:
- The user's commit gesture commits immediately, as for every other column.
- The pending state already exists for resolver round trips, with its spinner and undo.
- The verdict comes from the editor's own code and the picker's own normalization, so the two paths cannot drift.
- The cost: while the code never arrives, the cell stays pending. It writes nothing invalid and nothing null beyond the engine's usual pending clear.

**Window-only deviations, replacing the earlier list:**
1. **Enum.** An enum draft committed before the code arrives goes through the column's parse: a full label commits its value, and a prefix becomes an invalid input. On arrival, the editor takes the whole text as typeahead (R2-6).
2. **Entity.** An entity draft committed before the code arrives waits pending in its cell for the code. If the code never arrives, it stays pending.
3. **Number and date.** A number or date draft is a plain text input: no number filtering and no calendar until the code arrives (Alt+ArrowDown is remembered). Its text takes the column's parse on commit, as a typed editor commit does.
4. **iOS.** The handoff's `focus()` runs outside a gesture. iOS should keep the keyboard, because focus moves between two inputs, but this is not verified on iOS (the touch project emulates Chromium).

#### R2-1 (blocking): fixed

The draft is a real input. Shift, Control, AltGraph, CapsLock, a dead key, Alt, Meta and Process keydowns never reach a grid path that acts: the editing resolver names none of them. The browser composes the character.
- **Unit test:** "modifier and dead-key keydowns leave the draft to the browser".
  - It sends eight modifier keydowns, Windows' Ctrl+Alt+AltGraph "@" among them.
  - Each one is not prevented, and the draft stays open.
  - The draft then commits "aB@é".
- **e2e test (trusted):** "Shift, AltGr and accented characters typed while the code loads all land, and the editor takes them over".
  - Input: `down('Shift')`, `press('KeyC')`, `up('Shift')`; then `down('Control')`, `down('AltGraph')`, "@", and both up; then "dé".
  - After a 500 ms slow-load pause and more typing, the editor holds "abC@dé ok" with focus and caret, and the commit includes the next key.

#### R2-2 (major): fixed

A failed or timed-out load never commits or replaces the draft.
- The draft stays usable, and the user's own commit gesture commits through the engine.
- The error is reported once per grid, and the next gesture retries quietly.
- The timeout now starts with each attempt, not when the grid becomes editable.

Tests:
- Unit: "a stalled load leaves the draft open and usable, and reports once". It types "1", then "2" and "3" across a second timeout, gets "123" and one report, and the Enter retry loads the code.
- Unit: "a failed load leaves the draft whole while every key asks again, and reports once". It gets "123" after several rejected retries, one report, and a paste retry that loads.
- e2e: "a failed load leaves the draft whole, reports once, and the next gesture asks again". It runs through the story's fail hook.

#### R2-3 (major): fixed

See the Entity columns section in the design.

Tests:
- Unit: "an entity draft waits in its cell's pending state, then resolves through the column's search". The cell shows the spinner and no error; after arrival the value is 12 with zero resolver calls.
- Unit: "an entity draft on a column with no resolver resolves typed text through the search". "Bob" resolves to 12. "Dana", a label two agents share, is ambiguous.
- e2e: "an entity draft committed before the code arrives resolves through the column's search". The story's agent column has a search and no resolver.

#### R2-4 (minor): fixed

A key the editing resolver does not name stays with the input, so Mod+Z, Mod+Y and Mod+Shift+Z are the input's native undo and redo, as in an editor.
- Unit test: "undo and redo keys in a draft are the input's own and never reach the grid's undo".
- e2e test: "undo in a draft is the input's own: it never reverts an earlier edit".

#### R2-5 (minor): fixed

I took the first option:
- Focus moving into an overlay commits a draft.
- At arrival, a draft whose input lacks focus commits instead of resuming.
- `resume` therefore always runs with focus in the draft, so `seed` and `openDropdown` only run under the user.

Tests:
- "a draft commits when focus moves into an overlay, and the arrival leaves focus there".
- "the arrival commits a draft whose input lost focus within the grid, and moves no focus".

#### R2-6 (minor): fixed

`tm-select`'s `seed` now feeds the whole text to the typeahead. A new protected `ɵholdTypeaheadText` on the select base seeds with the first character and relays the rest in order behind it, through the panel's existing relay queue. The ɵ prefix keeps it out of the public surface: the select golden is unchanged.
- Unit test: "an enum draft hands the editor its whole text, which picks the option typing it reaches". "pc" over kg, pallet and pcs commits "pcs".
- `./select` measures 8.64 KB (was 8.59), under its 9 KB ceiling.

#### R2-7 (minor): fixed

An IME open now starts the draft at `''`. The composition itself happens in the draft input, which takes focus inside the composing keydown, as with the editor. The code waits for `compositionend` before it installs.
- Unit test: "an IME composition opens an empty draft, and the code waits for the composition to end".

#### R2-8 (minor): fixed

- Destroy ends the attempt: it clears the timer, ends the pending task and suppresses the report. It also cancels the idle preload.
- The report is once per grid.
- Unit test: "a grid destroyed while its code loads ends the load: no timer, no report". The app becomes stable at once, and no error arrives after the timeout.

#### R2-9 (minor): fixed, with one remainder

- **Trusted modifier keydowns.** See R2-1.
- **R1-2's guard is pinned.** The new test "an open on another cell commits the draft first, even with no press before it" dispatches only the dblclick. It fails with the guard removed: mutation run, then reverted.
- **Touch.** A new block in `grid-touch.spec.ts`, which the touch project runs: "a double-tap focuses the draft input inside the gesture, so typing lands".
- **Story timeout.** The story provides a 10-minute `ɵTM_GRID_EDITING_LOAD_TIMEOUT`. Its hooks are one-shot, so a test waits for each new request.
- **Remainder (pushed back as unpinnable here):**
  - The roving-focus effect's draft branch reclaims focus into the draft input only after a DOM-move focus drop, as the editor branch does. A mutation that removes it still passes all 25 unit tests: no test in this battery moves the editing row's DOM.
  - `requestFocusActive`'s draft guard is equally defensive: no flow calls it while a draft is open.
  - Both mirror the editor's checks.

#### R2-10 (minor): fixed

`ɵTmGridEditingCode.ɵTmGridStatusBar` is typed `Type<unknown>`. The grid golden no longer imports `lucide`: re-approved from a clean build, its only remaining difference from the pre-PC-F21 golden is the `ApplicationRef` import. The select, grid-editing and entity-picker goldens are unchanged (their new members are ɵ and filtered).

#### R2-11 (minor): fixed

A double-tap opens the draft from the dblclick handler and focuses its input in that handler, inside the gesture. The touch e2e test shows the input focused and the typing landing.

#### Sizes (clean build, cache moved aside)

| Entry point | Before PC-F21 | Fix round 1 | Fix round 2 | Ceiling |
|---|---|---|---|---|
| ./grid | 42.00 | 38.27 | **38.99** (39,924 B) | 39.25 (was 38.75) |
| ./grid-editing | (new) | 6.27 | **6.44** (6,594 B) | 6.5 (was 6.37) |
| ./select | 8.59 | 8.59 | 8.64 | 9 |
| ./entity-picker | 11.28 | 11.28 | 11.37 | 12 |
| ./tree-grid | 0.97 | 0.97 | 0.97 | 8 |

**./grid +0.72 KB:**
- the input, its template and style;
- the idle preload;
- the per-attempt load with one report;
- the pending entity queue;
- the composition and caret handoff.

The keyed draft's handlers went. I took two small trims: one arrival path, and one preload branch. Under the spec's 42 KB, PC-051 and PC-054 have **3.01 KB** between them, which still meets the brief's 3 KB floor.

**./grid-editing +0.17 KB:** `resolveLabel`, the caret handoff and the shared search runner.

The budgetNotes are updated.

#### Gates (clean build)

All on one clean build: `client/.angular/cache` moved aside and `client/dist` removed before `pnpm run build`.
- build: pass.
- lint (eslint + stylelint): pass. Stylelint first caught a hardcoded `1em` in the draft's padding; it now uses the spinner's `--spinner-size` token.
- lint:test, typecheck, tokens:check, docs:build: pass.
- test (all projects): 2,296 passed, 0 failed (core-ui 1,845; core 401; core-ui-tokens 23; core-testing 15; locale-ar 9; others 3).
- api:approve then api:check: 45 ok. The only golden change is the grid's dropped `lucide` import (R2-10).
- size:check: pass, including both static-graph checks (`./grid` and `./tree-grid` reach no editing entry point).
- e2e PR tier (chromium + touch, `--workers 2`, `@nightly|@perf|@quarantine` excluded): 789 passed, 0 flaky, 26.4 min.

After the gates, one TSDoc line in `grid-core.ts` lost a section reference (comment only, eslint re-run on the file).

#### Spec misalignments for Ahmad (as they stand after this change)

- **Spec 0010 §C.11:**
  - Its table has no ./grid-editing row (now 6.5).
  - It does not record ./grid's ceiling of 39.25 (lowered from 42).
  - It still says on-demand loading "is not part of this amendment". The grid now preloads ./grid-editing on idle and keeps a draft input as the fallback.
- **Spec 0010 §C.15:**
  - Its budget line puts the popup editors' +1 KB on ./grid. The budget notes say it belongs in ./grid-editing.
  - On "always edit mode": the draft follows the engine session's mode, so if §C.15 makes popup columns open their sessions in edit mode, their drafts move the caret on the arrows with no further rule. If §C.15 only changes the popup editors themselves, the draft still commits and moves on a typed draft's arrows, and the spec should say which.
- **Spec 0004 §8.4:**
  - Type-to-edit, IME, the edit-mode caret and Delete now behave in the window as in an editor.
  - The remaining window clauses are the deviations above: an enum or entity draft committed before the code arrives, and number and date drafts as plain text inputs.
- **Spec 0004 §11:** no clause needed. A draft is an open editor for undo: the grid's undo never runs while one is open.
- **Spec 0006 §7.4:**
  - Typed-commit resolution now runs exactly the picker's rungs, including the search-only and duplicate-label cases.
  - The only window clause: a commit made before the code arrives waits in the cell's pending state until the search can run.
