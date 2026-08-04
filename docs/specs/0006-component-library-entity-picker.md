# Spec: UI Component Library — Entity Picker

- **Author:** Ahmad Akra
- **Date:** 30 July 2026

**Status:** Frozen **historical** record of the design and its reasoning, revised once as the
implementation landed so that it describes what shipped. It is not updated thereafter as the code or
its dependencies evolve.

## Context

Phase 4 of the Tellma component library delivers **`tm-entity-picker`** — the server-searched
foreign-key selector every ERP screen leans on (Supplier on a purchase invoice, Account on a journal
line). It is the component spec 0002 §3.4 explicitly carved out of `tm-select` ("server-side search
on the typed string, an inline create-new affordance, a launch-advanced-search-modal escape") and the
one spec 0004 §6.2 promised as the built-in editor for `entity` grid columns. Both promissory notes
come due here.

All foundation decisions apply unchanged: Angular v22+, zoneless, signal-first, Signal Forms only,
CSS logical properties + CDK `Directionality` for RTL, Transloco behind `TM_UI_TRANSLATE`,
token-driven styling with the `@layer tm.base, tm.theme` cascade, static inline-SVG glyphs, the
showcase + vitest + Playwright pipeline, per-entry-point budgets and API goldens, worktree-isolated
port-free tooling. The picker embeds in the grid through the hardened `TmCellEditor` contract and
`TM_CELL_EDITOR_HOST` self-registration of spec 0004, launches its modals through `tm-modal`
(spec 0005 §10), and composes its dropdown from the shared anchored-overlay helper (spec 0005 §2.1).
Implementers must use the Angular CLI MCP (`get_best_practices`, `search_documentation`) rather than
memory for framework conventions, and verify `@angular/aria`/CDK APIs against the installed types
(angular.dev docs are known to run ahead of npm).

The component is the library's first **editable** `@angular/aria` combobox — `ngCombobox` on an
`<input>` — where `tm-select` proved the non-editable mode. The overlay wiring, the
activedescendant/explicit listbox configuration, the activation-commit rule, and the portaled ARIA
id chain all carry over from spec 0002 §3.4 unchanged; what is new is the async search lifecycle,
the text↔value resolution semantics, and the two consumer-supplied modal pages.

## Goals / Non-goals

**Goals**

- Ship `tm-entity-picker` to production quality: a11y-complete (WCAG 2.1 AA, APG editable-combobox
  conformant), RTL-complete, brand-themed, Signal-Forms-native, harness-tested, budgeted.
- Define the consumer contract: the search function, the id/label accessors, the committed-value
  display resolver, and the advanced-search / create modal page contracts.
- Make the picker the grid's **built-in editor for `entity` columns**, configured on the column,
  with typed-commit text resolving through the same async pipeline pasted labels already use.
- Extend `@tellma/locale-ar` with every new built-in string.

**Non-goals (explicitly out of scope)**

- Multi-select (tag/chip input) — a future component; the picker stays a scalar FK selector.
- Rich option templates — options render the consumer's label string, one line, ellipsized. A
  content-template slot can be added later without breaking the string path.
- Match highlighting (bolding the typed substring inside labels) — the consumer's search may match
  on code, synonym, or fuzzy rules the picker cannot see; bolding would guess wrong.
- Built-in recents/MRU, a result cache ACROSS queries, or client-side filtering/re-ranking — the
  picker renders exactly what `search` returns, in the returned order. Recents are expressible by
  the consumer inside `search('')`. It does hold the answer to the text currently in the box, so
  re-opening the dropdown on unchanged text costs nothing (§4.2) — one entry, retired by any edit.
- Minimum-query-length gating — the consumer's search decides what an empty or short query returns.
- Virtual scroll in the dropdown — the search contract imposes a result limit (§2); a dev-mode
  warning fires above 200 results.
- A built-in clear button — select-all + Delete clears the text (committing `null`); grid Delete
  clears the cell.
- A standalone Delete affordance — deletion (confirmation, lifecycle, server refusal) belongs to
  the consumer's **edit page**, which reports it through its result (§6); without an edit page the
  loop closes on the entity's master page.
- The advanced-search, create, and edit **pages themselves** — consumer-supplied; distributions
  will grow standard ones.
- A `label`/`label2`/`label3` multilingual-field convention — see the Decisions record (#1).

## 1. Package & entry points

| Entry point | Contents |
|---|---|
| `@tellma/core-ui/entity-picker` | `tm-entity-picker`, the consumer contract types (§2), the internal panel. Imports `@tellma/core-ui/modal` (page launching) and `@tellma/core-ui/private` (overlay helper). |
| `@tellma/core-ui/grid` | Entity-column picker configuration + the built-in entity editor mount (§7); imports the entry point above, the same one-directional dependency it has on the select and date-picker editors. |
| `@tellma/core-ui` (primary) | The new built-in strings (English). No new services or contracts — `TmCellEditor`, `TM_CELL_EDITOR_HOST`, and `TmLabelResolution` are reused as-is. |
| `@tellma/core-ui-tokens` | The `entityPicker` component token group (§10). |
| `@tellma/core-ui-testing` | `TmEntityPickerHarness` (§11). |
| `@tellma/locale-ar` | Arabic translations for the new strings. |

No addition to `@tellma/core-ui/contracts`: the picker's contract types need `Type<unknown>`
(Angular) and are consumed only by the picker and the grid, which already depends on the picker's
entry point.

## 2. The consumer contract

The picker owns interaction; the consumer owns data. Four inputs carry the whole data seam:

```ts
export type TmEntityId = string | number;

/**
 * A result set. The bare-array form is the common case; the object form additionally flags a
 * server-truncated set (`hasMore`), which renders the §4.1 truncation hint and switches the
 * results announcement to its open-ended variant ("10+ results").
 */
export type TmEntitySearchResult<T> =
  | readonly T[]
  | { readonly items: readonly T[]; readonly hasMore?: boolean };

/**
 * The search facility. Called with the query text and an AbortSignal that fires when the
 * request is superseded, the dropdown closes, or the picker is destroyed. May return the
 * results synchronously (in-memory/cache source — renders instantly, no spinner) or as a
 * Promise. The implementation is expected to impose a result limit; the picker renders what
 * it gets, in order, without filtering or re-ranking.
 */
export type TmEntitySearchFn<T> =
  (query: string, signal: AbortSignal) => TmEntitySearchResult<T> | Promise<TmEntitySearchResult<T>>;

/** A selection returned by a modal page. */
export interface TmEntityPick<Id extends TmEntityId = TmEntityId, T = unknown> {
  readonly id: Id;
  readonly label: string;   // display text at pick time; §5's memo fallback
  readonly item?: T;        // the full entity, when the page has it — relayed through `picked`
}

/** A consumer page the picker launches in a tm-modal. */
export type TmEntityPickerPage =
  | Type<unknown>
  | { component: Type<unknown>; size?: 'sm' | 'md' | 'lg'; title?: string };

/** Injected into the page component via TM_MODAL_DATA. */
export interface TmEntityPickerPageData<Id extends TmEntityId = TmEntityId> {
  readonly query: string;   // the picker's text at launch — prefills the page's own search/name field
  readonly id?: Id;         // edit launches only: the committed value being edited
}
```

Component API (`tm-entity-picker<T, Id extends TmEntityId>`):

```ts
value = model<Id | null>(null);                       // FormValueControl value — the FK id
search = input.required<TmEntitySearchFn<T>>();
itemId = input.required<(item: T) => Id>();           // result → id
itemLabel = input.required<(item: T) => string>();    // result → display text (reactive, see below)
displayWith = input<((id: Id) => string | null) | undefined>();  // committed id → display text (reactive)
advancedSearch = input<TmEntityPickerPage | undefined>();  // absent ⇒ no magnifier, no footer row
create = input<TmEntityPickerPage | undefined>();          // absent ⇒ no Create row
edit = input<TmEntityPickerPage | undefined>();            // absent ⇒ no Edit row (§6)
createLabel = input<string | undefined>();            // overrides the localized "Create…" row label
editLabel = input<string | undefined>();              // overrides the localized "Edit…" row label
placeholder = input('');
searchDebounce = input(50);                           // ms; see §4.2
picked = output<TmEntityPicked<T, Id>>();
// + the standard optional Signal Forms state inputs (disabled, readonly, required, errors,
//   touched, dirty, invalid, pending, name, …) and the `touch` output on blur, per spec 0002 §5.

export interface TmEntityPicked<T, Id extends TmEntityId> {
  readonly id: Id;
  readonly label: string;
  readonly item?: T;        // the search result for 'list'/'auto'; the page-returned entity for modal sources
  readonly source: 'list' | 'auto' | 'advanced' | 'create' | 'edit';
}
```

**Locale-reactive labels — functions, not field conventions.** `itemLabel` and `displayWith` are
called in a **reactive context**: an implementation that reads signals — the ambient locale, a
workspace entity cache — re-renders every visible label in place when those signals change. This is
how "the display updates on the fly with the ambient locale" is delivered without the library
knowing anything about multilingual entity shapes: a Tellma distribution writes one
`getMultilingualLabel(entity)` against its own `Name`/`Name2`/`Name3` convention and passes it
everywhere. The core stays domain-free, exactly as `tm-select.displayWith` and the grid's column
`format` already are (Decisions #1).

**`displayWith` resolves the committed value; a memo covers the gap.** A form arrives with
`value` set before any search ran (the edit-screen case), so the input's text for a committed id
resolves in order: (1) **`displayWith(id)`** when provided and non-null — the reactive,
recommended path, typically backed by the consumer's entity cache; (2) else the **memoized label**
of the pick that produced the value in this component's lifetime (`itemLabel(item)` at list-pick
time; the returned `label` for modal picks) — correct at pick time but frozen, so it does not
re-render on locale switch; (3) else `String(id)` with a dev-mode warning naming `displayWith` —
visible and honest, never a silently empty field that claims to hold a value. `picked` gives
cache-less consumers the full item at pick time — list picks carry the search result, and modal
pages include the entity in their pick (§6) — which is also how a distribution keeps its cache
(and therefore `displayWith`) warm across every source.

## 3. Anatomy & form-field integration

The component renders an internal native `<input>` plus an optional trailing **magnifier button**
(present only when `advancedSearch` is configured), and reports `ownsChrome: false` — inside
`tm-form-field` the field supplies the bordered box, focus ring, label (`<label for>` targets the
internal input via `controlId`), hint/error wiring; standalone or in a grid cell the bare input +
button fill the host. This is the `tm-date-picker` anatomy (spec 0005 §6.2) with a magnifier in
place of the calendar button.

- The input carries `role="combobox"` semantics via `ngCombobox` (`aria-expanded`,
  `aria-controls`, `aria-activedescendant` into the portaled listbox), `aria-autocomplete="list"`,
  `dir="auto"` (foundation bidi rule), and `autocomplete="off"`/`spellcheck="false"` so browser
  autofill and spellcheck never fight the dropdown. `aria-autocomplete` is asserted by the picker
  after render: aria derives it from the LIVE popup, so a closed picker would otherwise announce a
  combobox with no autocomplete — the opposite of what typing here does.
- **The magnifier is not a tab stop** (`tabindex="-1"`), the calendar-button precedent: one Tab per
  field is the ERP data-entry contract, in forms and grid cells alike. It stays pointer- and
  AT-activatable (localized `aria-label`); the keyboard path to the same modal is the
  **Advanced search… footer row** in the dropdown (§4.4), so WCAG 2.1.1 holds without a bespoke
  shortcut (Decisions #7).
- While the picker is resolving text (§5.2) it surfaces `pending` to the field —
  `pending() = fieldPending() || resolving()` — so the field shows the standard trailing
  `tm-spinner` and the error-display policy holds errors until resolution lands (spec 0002 §5).
  The control sets `aria-busy` while resolving.
- **Size stability:** the control never changes size across states — the spinner renders in the
  field's existing pending slot, the magnifier is always present when configured (disabled state
  included), and the dropdown is top-layer overlay content outside the page flow.
- `disabled`/`readonly` (field-authoritative when bound) suppress the dropdown, the search, and
  the magnifier. Only `disabled` reaches aria's own `disabled` input, which host-binds
  `aria-disabled` unconditionally: a read-only control must still convey its value, so the native
  read-only state is the picker's own (asserted after render, since aria owns that attribute too)
  and the arrow-opens-the-dropdown behavior is suppressed in the keyboard layer (§4.3).
- **Errors surface even with no `[formField]`.** Text that names no entity must look wrong wherever
  the control is used, and standalone there is no field message machinery to read it: the control
  merges its own parse errors into `localizedErrors` and aliases `invalid`/`touched`/`pending` so a
  bound field still drives them. What it never shows is the in-progress error — see §5.1.

## 4. The dropdown

### 4.1 Composition

The proven spec 0002 §3.4 stack, in editable mode: `ngCombobox` on the **input**,
`cdkConnectedOverlay` + `ngComboboxPopup` nested per the official pattern, and inside the popup a
status area plus an `ngListbox ngComboboxWidget` with `focusMode="activedescendant"` (DOM focus
never leaves the input) and `selectionMode="explicit"`. Commit is **activation-driven** —
`(click)`/`(keydown.enter)` on the listbox — never `valueChange` (the auto-prune lesson). The
overlay is created lazily on first open through the shared anchored-overlay helper (spec 0005
§2.1): `disableClose` (the control owns Esc), `matchWidth`, macrotask re-measure. The known
upstream aria-in-overlay **mouse** bug guard of spec 0002 §3.4 applies verbatim: the suite pins
option-click commit, outside-click close, and magnifier click with real mouse events.

The panel opens on **one** logical position, not a flip list. This panel's content arrives in
stages — the overlay attaches, aria's deferred content renders the listbox a pass later, results
replace the spinner later still — and a position list lets the CDK re-pick the side on each
measurement, which reads as opening downward and then jumping up. The side is decided once from
the anchor's own geometry, with a max-height clamp that makes the chosen side fit by construction.

**Anchor = the chrome the user reads as the field**, not the bare input: the `tm-form-field`
bordered box when wrapped, the **cell box** when grid-hosted, the host element standalone — the
date-picker's rule, which is what "anchored to the cell border rather than the input" requires
in the grid. `matchWidth` therefore matches the box/cell; a `--entity-picker-panel-min-width`
token keeps the panel readable when a narrow grid column would make matched width unusable
(the panel may exceed the cell width, never undershoot the token).

The popup contains, in order:

1. **The listbox** — one option per search result, single line, ellipsized; the option whose id
   equals the committed `value` renders `aria-selected` + the check glyph (the listbox mirrors the
   committed id; aria's unmatched-value prune is harmless under activation-commit).
2. **The truncation hint** — when the result set carries `hasMore` (§2), a presentational,
   `aria-hidden` row after the results: localized *"Showing top N matches. Keep typing to
   refine."* It pre-empts the "why isn't X showing up" confusion a silently capped list creates,
   and it sits directly above Advanced search…, the affordance that answers it. Centered, in the
   smallest type, so nothing about it reads as a clickable row. AT hears it through the open-ended
   count announcement (§8).
3. **The status area**: the `tm-spinner` while an async search is outstanding, the localized
   **"No results"** when a fresh search returned empty, or the localized **search-failed message**
   when it rejected. Presentational; announcements go through the live region (§8).
4. **The footer rows**, when configured: a visual separator, then **Advanced search…**,
   **Create…**, and — only while the field NAMES an entity — **Edit…** (§6) — real
   `role="option"` rows *inside* the listbox (ARIA listbox children must be options), reachable by
   arrow keys, never auto-highlighted, activating their modal instead of committing a value.

The hint, the status area and the separator are `aria-hidden` `<li>`s inside the same `<ul>`. That
is the only arrangement honoring both the visual order above and the options-only ARIA contract,
since the footer commands must be real options in that listbox: `<li>` children of `role="listbox"`
carry no implicit role, and `aria-hidden` removes them from the tree, so the owned-options set and
`setsize`/`posinset` are unaffected. The axe gate runs over every popup state in both themes.

Two rules govern when the footer rows are there at all. They are **hidden while a search is
loading** — while the spinner is up the panel says one thing only, and commands that would arrive,
shift and re-order under the user's cursor a moment later stay out of it; they return the instant
the state settles, so "no results → Create…" is one arrow + Enter away. And **Edit…** follows what
the field NAMES, not what the model holds: emptying the text does not write `null` until the commit
gesture (§5.1), so a cleared box would otherwise still offer to edit the entity it has stopped
showing, with a tick beside a row the user just removed. The `aria-selected` check follows the same
rule.

### 4.2 Search lifecycle

- **Open triggers:** typing (any text change, including clearing to empty); pointer click/tap on
  the input (the touch path — no keyboard chord exists there); `Alt+ArrowDown` (the platform
  dropdown chord, grid and form alike); plain `ArrowDown`/`ArrowUp` **standalone only** (APG
  editable-combobox behavior — in a grid, plain arrows belong to the grid's commit-and-move model,
  the spec 0005 §6.2 branching rule, detected via the optional `TM_CELL_EDITOR_HOST` injection).
  Keyboard focus alone never opens it.
- **Re-opening on unchanged text costs no request.** Closing the dropdown resets the search state,
  but the fetch happened: the picker keeps the answer to the text currently in the box and
  re-applies it on the next open — rows immediately, no spinner, and the same count announcement
  and highlight the re-fetch would have produced. Exactly one answer is held, and it is retired by
  ANY write to the content — a keystroke, a type-to-edit seed, a quiet cell install — so `Adam` →
  `Ada` → `Adam` asks again, whether or not the intermediate search ever came back. It is retired
  by a change of `search` too: a different closure is a different question. Within those rules the
  answer has no expiry; a consumer whose data turns over faster than a user's visit should say so
  in `search`, which is the only thing that knows.
- **The query:** the input's current text — except when that text is **pristine** (empty, or
  exactly the committed value's display text), where the query is `''`: opening a populated field
  is a browse-alternatives intent, not a search for the label already chosen. First keystroke into
  a pristine field replaces per type-to-edit (grid) or edits in place (form) and searches the
  edited text.
- **Debounce — leading + trailing, default 50 ms.** The first change after idle fires
  immediately (single keystrokes pay zero added latency); subsequent changes inside the window
  coalesce and fire once on the trailing edge. The window exists solely to absorb key-autorepeat
  and paste bursts: Windows' fastest repeat is ~33 ms/char, which is why a 10 ms window would
  coalesce nothing (Decisions #5). `searchDebounce` tunes it; `0` disables coalescing.
- **Supersession:** every text change aborts the in-flight request (`AbortSignal`) and clears the
  stale results + highlight immediately — the spinner shows at once and holds until a fresh,
  uninterrupted result set returns; results on screen always correspond to the text in the field,
  and Enter/Tab meanwhile fall back to resolution (§5.2), so a stale row can never be committed.
  A stale response that arrives anyway (a consumer ignoring the signal) is discarded by sequence
  token. Honoring the signal is an optimization; discard-on-arrival is the correctness guarantee
  (the §9.4 posture of spec 0004).
- **Height stability.** The status area (spinner / no results / failure) reserves a fixed
  block-size — a small multiple of the option-height token — so the list↔spinner↔list churn of
  fast typing over a fast server moves the panel edge minimally, and the empty/error states never
  collapse the panel (Decisions #15).
- **Synchronous fast path:** when `search` returns synchronously (either §2 result form — an
  array or the `{ items, hasMore }` object — rather than a thenable), results render in the same
  turn: no spinner, no flicker. The debounce window is skipped while the source is
  known-synchronous (the previous invocation returned synchronously), so in-memory sources are
  keystroke-instant; the coalescing exists for server traffic, which an in-memory source has none
  of.
- **Search failure** (rejected promise / thrown sync): the status area shows the localized
  failure message where results would render; footer rows stay; the next text change retries.
  Abort-caused rejections are not failures.
- **Close triggers:** selection commit, Esc (§4.3), outside pointer press, opening a modal
  (§6), blur (§5.2), `disabled`/`readonly` becoming true, destroy. Closing aborts any in-flight
  search except the blur-resolution case (§5.2). Destroy also closes any modal the picker opened.

### 4.3 Keyboard model

The aria directives own the combobox/listbox keyboard model and the ARIA wiring; the picker adds:

- **After every fresh result render for a typed (non-pristine) query, the first result is
  auto-highlighted** (`aria-activedescendant`; the public listbox first-item navigation API) — so
  type-then-Enter is the zero-arrow happy path. A **pristine browse list highlights nothing**:
  clicking a populated field to look at it and tabbing on must never change the selection —
  there, arrows highlight explicitly. Footer rows are never the auto-highlight target; a fresh
  empty result set highlights nothing.
- **Enter** — with a highlighted option: commits it (§5.1) and closes; on a footer row: opens its
  modal; with the popup open, Enter is always consumed (never submits the form). With the popup
  open but results still loading, Enter requests **resolution** of the current text (§5.2) with
  focus retained; on a fresh **empty** set for a typed query, that resolution fails fast to the
  no-match error (§5.2), the popup staying open with its footer rows as the recovery path. With
  pristine text and no highlight, Enter simply closes the popup. With the popup closed, Enter is
  not consumed (native form submit applies).
- **Tab** — with a highlighted option: commits it, closes the popup, and does **not** consume the
  key — focus proceeds to the next field (form) or the grid handles commit-and-move (§7.3). A
  documented, deliberate strengthening of the APG baseline — the Excel/ERP data-entry convention
  (Decisions #2). With no highlight, Tab closes the popup and leaves; §5.2 blur resolution covers
  any typed text.
- **Space** types into the input like any printable character — it **never** activates or selects
  an option (the bare listbox's Space-select does not apply inside an editable combobox;
  activation is Enter, Tab, or a pointer press).
- **Esc** — closes the popup, text intact; innermost-first against modals and the grid's
  two-stage Esc. Standalone, a second Esc does nothing (`tm-select` posture: no revert state
  exists outside a grid).
- **Arrow Up/Down** — navigate options (wrapping per aria defaults) through results and footer
  rows; the input's caret is untouched while browsing (APG manual/highlight model — the text never
  mutates during navigation). From no highlight, ArrowUp reaches the LAST option. The list scrolls
  to follow the highlight: DOM focus never leaves the input, so nothing else would, and arrowing
  past the fold would walk the highlight out of sight.
- **Home/End** move the caret (editable-combobox APG), not the highlight.
- Printable typing always returns to filtering — the highlight resets with the next result set.

Three of these override `@angular/aria`'s own bindings, through one capture-phase listener on the
host, each with a guard test that fails loudly if the upstream behavior moves:

- **Home/End**, which aria relays to the listbox — freezing the caret and moving the highlight.
- **Plain vertical arrows in a grid cell**, where aria expands the collapsed popup unconditionally
  with no opt-out input. The original is stopped and an identical clone is dispatched above the
  host, so the grid receives it unconsumed and its commit-and-move model runs (§7.3). The
  implementation carries a `TODO` naming the upstream gap, to be swapped for a plain pass-through
  once aria offers the input.
- **The same arrows while `readonly`**, for the same reason: a read-only picker offers no choices,
  and it does not use aria's `disabled` input to say so (§3).

### 4.4 Touch

Tap opens the browse list (`search('')`) alongside the soft keyboard; option rows and the
magnifier meet the WCAG 2.2 AA 24 px floor with the ≈44 px comfortable target on touch-primary
forms (`--entity-picker-option-height`, matching the select's row sizing); the panel's max-height
token keeps it inside small viewports (scrollable list). No hover-only affordances exist.

## 5. Text, value & resolution semantics

### 5.1 The value channel

`value` is the FK id (`FormValueControl<Id | null>`); the text is a **query surface, not the
value** — typing never writes the model. Writes happen at exactly these points:

- **Pick** (option activation, Tab-commit, modal selection, blur auto-resolution): writes the id,
  sets the input to the canonical display text (§2's resolution order), memoizes the label, emits
  `picked`, clears any resolution error.
- **Empty commit** (blur/Enter with empty text): writes `null` — clearing text clears the value;
  `required` is the field's concern.
- **Failed resolution** (§5.2): writes `null` and raises the error — the model and the display
  are never silently out of sync (the grid's invalid-input principle applied to forms: a value
  the form could save must never sit behind text that claims something else).

That list is exhaustive, so **clearing the text writes nothing until the commit gesture**: an
emptied box carries the unresolved-text error and the model keeps the old id until blur or Enter.
Everything that tells the user "this is your selection" follows the text rather than the model for
exactly that interval (§4.1).

The text channel rides the same machinery as the date picker (spec 0005 §6.7): a raw-text channel
over the value model whose parse reports kind `parse` with an inline localized message, and whose
canonical writes (picks, locale reformat) pin their known value so a reformat can never corrupt
the model through a lossy re-parse. The pin is retired whenever the display context changes, so
re-typed old text can never bind to a dead context's value. External model writes reformat the
text immediately while unfocused; while focused the user's text wins until their next commit
point — and the deferred reformat runs the moment focus leaves, so a display context that moved
mid-visit (a warmed entity cache, a rename, a locale switch) does not leave the older rendering on
screen. A visit that typed nothing never resolves on the way out: the text on screen is the
picker's own, and it is owed a refresh, not a search.

Switching locale re-renders the display text via `displayWith` with the model unchanged, and
re-renders a kept error message in the new locale — the date picker keeps its stale-locale message
until the next keystroke; this control does not.

**The in-progress error is invisible by construction.** Every keystroke on the way to a valid pick
passes through "this text names no entity", which is true and useless: half-typed text is a query,
not a mistake. That error is marked transient — filtered out of both message channels, kept in the
VALIDITY channel so a racing submit is still blocked. What the user eventually sees is the
resolution outcome (§5.2), recorded only once they try to commit. Validity and displayability are
separate questions. (Material and PrimeNG land in the same place: state on the control, message in
a container, never mid-edit.)

### 5.2 Resolution — blur and Enter on unresolved text

*Resolution* maps the current text to at most one entity, reusing the search already in flight:

1. Take the freshest results for the current text — awaiting the in-flight request if one is
   outstanding (blur does **not** abort it; it is the resolution input). If no search is current
   (e.g. Esc closed the popup earlier), one is issued.
2. **Exactly one result → auto-pick it** (`picked` with `source: 'auto'`).
3. **Zero → error** *"No match for ‹text›"*; **two or more → error** *"‹text› matches more than
   one item"*; **search failure → error** *"Search failed"*. In every error case the **text is
   kept** for correction, the model holds `null`, and the field is invalid (kind `parse`, inline
   localized message).

Triggers: **blur** with non-pristine, non-empty text (the popup closes immediately; resolution
continues behind it) — where blur means a **real departure**: pointer presses inside the popup or
on the magnifier keep focus in the input and are their own interactions (a pick, a modal launch),
never a resolution trigger; and **Enter** while results are loading or on a fresh empty set for a
typed query (focus retained; `touch` is reported so a failure displays without waiting for blur).
While resolution is
pending the control reports `pending` and the raw-text channel already carries the unresolved
error — so the field is **invalid from the moment of blur** (a submit racing the resolution is
blocked, never saved with a phantom value), while the standard display policy
(`!pending && invalid && (touched || dirty)`) keeps the message visually held until the outcome
lands: a fast unique match paints the label with no error flash at all.

Interleaving guards: refocusing and editing the text, an external `value` write, or destroy
supersedes/aborts the pending resolution (sequence token; late results discarded). A pristine or
empty blur resolves trivially (no-op / `null`) with no request.

**Why this pair of behaviors** (Decisions #3, #4): unique-match auto-resolve on blur is the
long-standing ERP lookup convention (heads-down entry: type a fragment, Tab, keep typing) and is
exactly the grid's pasted-label semantics — `TmLabelResolution`'s value/notFound/ambiguous — so
forms and grids agree on what unresolved text means. Keep-text-plus-error (rather than
silently clearing the field) matches the platform's invalid-input posture everywhere else
(`tmNumber`, `tm-date-picker`, grid cells keep unparseable text for correction); discarding the
user's query on a stray click is the classic lookup-field frustration this rule exists to avoid.

## 6. The modals — advanced search, create & edit

All three pages follow one launch contract. The picker opens the consumer's component via the
`TmModal` service — advanced search defaults to size `lg`, create and edit to `md`; `title` from
the page config, else the localized defaults — passing `TmEntityPickerPageData` (`query` = the
current text, so the page can prefill its own filter or the new entity's name; `id` = the
committed value, edit launches only). `query` is **pristine-mapped**: a field showing its
committed label sends `''`, since prefilling a create page's name with the OLD entity's label
would be wrong. The picker focuses its input before opening, so the modal's focus restore returns
there even when the launch came from an unfocused magnifier press. The page closes itself through
its
`TmModalRef<TmEntityPick<Id> | null>`:

- `close({ id, label, item? })` → the picker applies the pick (§5.1, `source: 'advanced' |
  'create' | 'edit'`), focuses the input, and clears any error. The label seeds the memo;
  `displayWith` wins over it wherever it resolves (§2). `item`, when the page supplies it, rides
  through `picked` so cache-warming (§2) works from modal picks too. An edit that only renamed
  re-applies the same id with the fresh label.
- `close(null)` — meaningful from the **edit page only** — the entity no longer exists (the page
  deleted or deactivated it): the picker clears to `null` with empty text and focuses the input.
- `close()` / close-button / backdrop / Esc → no change: text, value, and error state are exactly
  as they were; focus returns to the picker (the modal's focus restore).

Launch paths: the magnifier (pointer/AT) and the **Advanced search…** footer row open the
advanced-search page; the **Create…** footer row opens the create page (`createLabel` overrides
its caption — *"Create supplier…"* reads better than a generic *"Create…"*); the **Edit…** footer
row (`editLabel` likewise), shown only while a value is committed, opens the edit page on that
value. **Edit closes the create-typo loop in place**: a user who just created an entity with a
misspelled name — or shouldn't have created it at all — fixes or removes it without leaving the
form; deletion itself (confirmation, referential-integrity refusals, soft-delete policy) lives
inside the consumer's page, which knows the entity's lifecycle, and reaches the picker only as
the `close(null)` outcome. `edit` is optional: when a screen doesn't wire it, the loop closes on
the entity's master page — the documented, acceptable fallback.

Opening a modal closes the dropdown and cancels any in-flight search; the input's text survives
for `TmEntityPickerPageData` and for the user's return. Modal stacking, focus trapping, Esc
ordering, and top-layer interplay are `tm-modal`'s (spec 0005 §10) — a picker **inside** a modal
works, and its own pages stack above per the CDK dialog stack.

## 7. Grid integration

### 7.1 Column configuration — the built-in `entity` editor

`tm-grid-column` gains the picker's data seam, mirroring how `enum` columns carry
`options`/`optionLabel`/`optionValue`:

| New column input | Meaning |
|---|---|
| `search?: TmEntitySearchFn<unknown>` | Enables the built-in `tm-entity-picker` editor for this `entity` column. |
| `itemId?`, `itemLabel?` | The result accessors (§2), required alongside `search`. |
| `advancedSearch?`, `create?`, `edit?: TmEntityPickerPage` | Optional modal pages; absent ⇒ no magnifier / no Create row / no Edit row in this column's editor. |

The column's existing **`format` doubles as the editor's `displayWith`** (the id→label duty it
already owns for display cells) — no duplicate configuration; `resolvePastedLabels` keeps its
paste duty unchanged. Precedence: a consumer `*tmGridEditor` template still wins over the
built-in; an `entity` column with neither `search` nor a template remains the spec 0004 dev-mode
error unless readonly. Display cells stay static DOM formatted by `format` — the picker
contributes no `TmCellDisplay` because the column already owns that text.

### 7.2 Mounting — no chrome, cell-anchored

The grid mounts the picker bare (no `tm-form-field`): the input fills the cell box exactly like
the text editor, with only the magnifier at the inline end (when configured) — no bordered box in
either display or edit mode. The dropdown anchors to the **cell box** (§4.1's anchor rule) with
`matchWidth` against the cell and the min-width token as the floor. In the grid's editor session
this is a new built-in **`entity` mount kind** alongside the text, number, date, and enum
editors, its mount-config variant carrying the column's §7.1 picker configuration. The picker
registers itself via `TM_CELL_EDITOR_HOST` on construction and implements the mounted-editor
dropdown hooks the enum and date editors established: `isDropdownOpen()`/`openDropdown()` wired
to the grid keymap's `Alt+ArrowDown` and dropdown gate, and the `activated`-style output for
pick-commits.

### 7.3 Editing semantics

- **Opening:** `Enter`/`F2`/double-click open the editor on the cell's display text (edit mode,
  caret at end, dropdown closed); typing opens it seeded (enter mode) and immediately searches
  the seed; `Alt+ArrowDown` opens editor **and** dropdown in one press (the pristine rule ⇒
  browse list). IME composition follows spec 0004 §8.4 (open unseeded, compose inside).
- **While the dropdown is open the grid stays out** (the established dropdown gate): arrows
  navigate results, Esc №1 closes the dropdown, Esc №2 is the grid's cancel — the two-stage Esc
  composes unchanged.
- **Enter on a highlighted option = the pick IS the edit** (the enum/date contract): the picker
  consumes Enter, commits the cell, and the editor closes with **no move**.
- **Tab with a highlighted option** selects it, closes the dropdown, and lets the key bubble —
  the dropdown gate sees a closed dropdown and the grid performs its normal commit-and-move.
  Excel's dropdown-cell behavior, one keystroke.
- **Commit with unresolved text** (blur-commit, click-elsewhere, Enter with no highlight): the
  editor's own SEARCH is consulted first, then the column's resolver — §7.4.
- **Modals from a cell:** the magnifier and footer rows work mid-edit. The activating press is
  inside the editor, so the grid's commit-on-blur — which reads the press that precedes a real
  departure, the spec 0005 §6.6 rule — holds the edit session open while the modal traps focus.
  A pick applies through the still-open editor and commits the cell (no move); a dismissal
  returns focus to the editor input with the session intact.
- **A session neither channel moved commits nothing.** An entity editor owns both channels, and on
  a clean cell the text channel stands down (below), so a VALUE baseline is what recognizes an
  untouched session. On a real row the write would be elided anyway; on the new-row placeholder any
  commit at all materializes the row, and F2 + Enter on the `*` row must not append a blank one. A
  cell holding an invalid input is never "untouched": its value is already `null`, so clearing the
  bad text moves no value — and clearing that text is the only way to clear the annotation.
- `cancel()` restores the value present at open; `seed()` replaces content and searches; `text`
  reports `null` whenever the VALUE channel is authoritative — picker-authored canonical text, or
  the pristine display of the set value — and the raw string only for user-edited unresolved text.
  Committing a label as text would send it back through the resolution ladder and cost a second
  round trip; the label the user sees is the input's text, and copy exports `displayText` (the raw
  text for an errored cell, spec 0004 §10, unchanged).

### 7.4 Typed-commit resolution

Unresolved editor text on an `entity` column takes the **§9.3 conversion chain**, which this spec
extends from paste to editor commits — the same pending-cell affordance, sequence tokens, undo
entry and §9.4 messages, so typed and pasted text share one pipeline and one UX (Decisions #8).
An empty commit clears the cell; a consumer `parse` that succeeds commits synchronously; then the
two async rungs below.

**The editor's own search answers first.** It is the question the user was already asking, it is
already paid for, and its answer outlives the editor: at commit the grid ADOPTS it — the set
already settled, the request still in flight, or the query still inside the coalescing window,
which is flushed rather than cancelled. The adopted request detaches from the editor, so it
survives the teardown that follows; the grid holds the only abort handle from then on, and undo
while pending fires it. An aborted search decides nothing at all — a cancellation must not start
the round trip it was cancelling.

**The column's resolver answers what the search cannot.** The two ask different questions:
`search` asks *what matches this query?* — ranked, capped, typically scoped to what a user may
pick today; `resolvePastedLabels` asks *which entity IS this label?* — an identity lookup that may
reach codes, aliases, or records the type-ahead never offers. So the search is authoritative about
MULTIPLICITY and the resolver about IDENTITY OF SOMETHING THE SEARCH CANNOT SEE, and the split
follows which kind of fact the search produced:

| the search returns | who decides | why |
|---|---|---|
| one result, or exactly one whose label IS the text | the search | identity, at no further cost |
| several rows all carrying the text AS their label | the search — ambiguous | two entities really are named this; no lookup can undo it |
| several rows, the text is none of their labels | the resolver, with ambiguous as the **fallback** | a dead end for a ranked query, still an open identity question — a type-ahead that matches codes returns several rows for a code, and that code is exactly what the resolver knows. The fallback keeps the better message when the resolver comes back empty: "matches more than one Agent" is true and useful where "no match" is neither |
| a TRUNCATED page, unless already ambiguous by label | the resolver | a capped page can be trusted about what it contains and never about what it does not: the duplicate that would make a lone exact match ambiguous may sit past the cap |
| nothing, or a failure | the resolver | a search miss is not proof of no match; a failure is an independent path, possibly transient |

So **at most one** resolver call performs the authoritative resolution — commonly none — and an
`entity` column carrying `search` but no resolver still resolves typed text. Consumers implementing
`resolvePastedLabels` should know it receives typed partial queries, not only pasted labels.

The picker's own async resolution (§5.2) still **never runs in a cell**: what crosses the boundary
is the SEARCH, not the resolution — the grid decides, using data the picker fetched. Grid commit
stays synchronous, and pending state belongs to the **grid** — the same `pendingCount` + in-cell
spinner the paste pipeline uses, showing the text being resolved for the duration rather than an
empty cell — never the picker's `pending` surface. §5.2's held-error interplay has no grid
counterpart: the picker's Signal-Forms text channel is inert in a cell (the `tmNumber` precedent,
spec 0005 §5), so the status-bar tally reflects only field-invalid cells, the invalid-input map,
and the resolver's pending count.

## 8. Accessibility

Target WCAG 2.1 AA — axe static floor + behavioral Playwright specs, per the foundation posture.
The design was audited against the APG editable-combobox pattern; conformance and the two
deliberate strengthenings:

- **Pattern:** combobox (input) + listbox popup, `aria-autocomplete="list"`,
  `aria-activedescendant` with DOM focus pinned to the input, `aria-expanded`, `aria-controls`
  across the overlay portal — the id chain is asserted across the portal exactly as for
  `tm-select` (spec 0002 §6). Browsing never mutates the input text (APG list-autocomplete
  manual/highlight model); there is no inline completion.
- **Auto-highlighted first result + Enter commits** is the APG's automatic-selection variant of
  list autocomplete — conformant, and the announced active option tracks it. It applies to typed
  queries only; pristine browse lists highlight nothing (§4.3), so opening a populated field and
  tabbing away can never mutate the value.
- **Tab commits the highlighted option** (APG baseline: Tab merely closes). Deliberate,
  documented: the Excel/ERP data-entry convention this control exists to serve; the popup is
  closed either way and Tab always leaves the field, so no keyboard trap arises.
- **Create…/Advanced search… are options** (`role="option"` inside the listbox — the only
  children ARIA permits), with accessible names from their captions; the separator above them is
  `aria-hidden` decoration. Activating them opens a `role="dialog"` modal; APG has no rule
  against command options, and arrow-reachability is what makes the magnifier's `tabindex="-1"`
  legitimate under WCAG 2.1.1.
- **Async status is announced, not rendered as fake options:** the spinner/no-results/failure
  area and the truncation hint are presentational inside the popup; a visually-hidden
  `aria-live="polite"` region (the official aria-autocomplete example's mechanism) announces
  result counts (*"5 results"* — ICU plural; the open-ended *"10+ results — more available"*
  variant when `hasMore`), *"No results"*, search failure while the popup is open, and the
  auto-resolution outcome on blur. Announcements fire on fetch completion, never per keystroke.
  The popup carries `aria-busy` while loading.
- Resolution FAILURES surface through the standard field error machinery (persistent polite live
  region in `tm-form-field`; cell error overlay + `aria-describedby` in the grid) — one message in
  one place. The picker's own region adds nothing on a blur, whose popup is already leaving; it
  announces the failure only where the popup STAYS (Enter while results are loading, §4.3), so the
  status the user is still looking at is spoken rather than silently swapped. Success
  auto-resolution announces there too, having no other channel.
- Focus chains: modal focus trap/restore per `tm-modal`; the dropdown never takes DOM focus, so
  there is nothing to restore on close; Esc dismisses innermost-first (dropdown → modal → grid
  edit).
- Forced-colors and reduced-motion honored and Playwright-gated (active-option ring, selected
  check, separator, spinner all survive `forced-colors: active`; fades collapse under reduced
  motion).
- Touch targets per §4.4; the magnifier ≥ 24 px with the ≈44 px comfortable posture on
  touch-primary forms.

## 9. RTL & i18n

- All geometry is logical: the magnifier sits at the inline end, the panel positions flip via
  `Directionality`, option padding/ellipsis are logical, the check glyph mirrors position with
  the layout. The input is `dir="auto"` (foundation bidi rule); option rows inherit the ambient
  direction and rely on the Unicode bidi algorithm for mixed-script labels.
- New built-in strings, resolved through `TM_UI_TRANSLATE` with English in-package and Arabic in
  `@tellma/locale-ar`: the Create…, Edit…, and Advanced search… captions and default modal
  titles, the magnifier `aria-label`, "No results", "Search failed", the truncation hint, the ICU
  results-count announcement (plain and `hasMore` variants), and the resolution errors (*no
  match* / *more than one match* / *unresolved — select an item*).
  Live locale switch re-renders every visible string and label (reactive `itemLabel`/
  `displayWith`, §2) with the model untouched; grid cell messages reuse the spec 0004 §9.4
  strings.

## 10. Performance budget

- **Budget:** `entity-picker` ships at ~10.5 KB gzipped self-weight against a ratchet of 11 —
  above the 10 KB this spec aimed for (between `select`'s 8 and `date-picker`'s 14: the same
  overlay/combobox wiring plus resolution logic and modal glue, minus select's projection
  machinery). The remaining lever is the `@defer` popup split below, which moves the mounting
  sequence this control's overlay placement and highlight protocol are demonstrably sensitive to;
  not worth reopening for half a kilobyte. `grid` 35 → 37 (column inputs, editor mount, and the
  §7.4 commit-resolution chain) — and worth recording that the grid ceiling is a ratchet that has
  never met the 24 KB spec 0004 set for it, which wants a pass of its own. Primary entry point
  unchanged (strings only). Every raise carries its reason in `"tellma".budgetNotes`: a ratchet
  moved silently is a bug.
- **Lazy everything that floats:** the overlay and panel are created on first open and torn down
  on close; a closed picker costs its input + button DOM only. The modal pages are consumer
  components instantiated by `tm-modal` on open.
- **No per-keystroke layout thrash:** typing mutates only the panel's content (overlay layer);
  the field never resizes (§3); the spinner is transform-animated; results replace a single list;
  the §4.2 reserved status-area height bounds panel-edge movement across fast re-searches.
- **Network discipline:** leading+trailing coalescing (§4.2), abort on supersession, close, and
  destroy (the §2 signal contract — destroy also aborts a pending §5.2 resolution), one request
  per settled query, zero requests for pristine/empty blurs and for the synchronous fast path.
- Component tokens: `--entity-picker-panel-max-height`, `--entity-picker-option-height`,
  `--entity-picker-panel-min-width` (validated by the schema + missing-ref gate, both schemes).

## 11. Testing

- **Unit (vitest, zoneless):** search lifecycle — leading/trailing coalescing, autorepeat burst ⇒
  ≤ 2 requests, abort-on-supersede, stale-response discard, immediate spinner + immediate
  stale-result clearing on every async change, the reserved status-area height, sync fast path
  (no spinner, no debounce), failure + retry; resolution — unique auto-pick (including results arriving after
  blur), zero/ambiguous/failed errors with kept text and `null` model, pending suppression (no
  error flash on a fast unique match), Enter-while-loading, pristine/empty blur no-ops,
  supersession by refocus-edit/external write/destroy; display — `displayWith` > memo >
  `String(id)` + dev warning, live locale re-label, `hasMore` hint render + announcement variant;
  Signal Forms — `[formField]` binding, invalid blocks submit during resolution, required-on-null,
  touch on blur; modal contract — pick applies and focuses, edit re-applies a renamed label,
  `close(null)` from edit clears to `null`, every dismissal path is a no-op, `query`/`id`
  payloads; footer rows — presence by config, Edit… only while a value is committed, never
  auto-highlighted, error-state persistence.
- **Harness:** `TmEntityPickerHarness` (+ option sub-harness): read/type query, open state,
  option labels, active option, select by label, spinner/status text, magnifier click, committed
  text. Composes the aria combobox/listbox harnesses; the panel is portaled (document-root
  locator, the select-harness precedent).
- **Playwright (showcase stories):** keyboard matrix (type→auto-highlight→Enter; pristine browse
  highlights nothing — open-then-Tab changes nothing; Space types; Enter fails fast on an empty
  typed set; arrows through results and footer rows; Tab-commit; Esc;
  Enter-never-submits-while-open); **real-mouse specs**
  (option click commits, outside click closes, magnifier opens the modal) guarding the upstream
  aria-in-overlay mouse bug; blur auto-resolution and error states with live-region assertions;
  modal round-trips (pick and dismiss, focus restore, picker-inside-a-modal stacking); grid story
  — entity column on the built-in editor: type-to-edit, `Alt+ArrowDown` cell-anchored dropdown,
  pick-commits-no-move, Tab-commit-move, two-stage Esc, typed-commit resolution (each row of
  §7.4's table: what the adopted search decides, what reaches the resolver, and that an aborted
  one starts nothing), the grid's pending affordance showing the text being resolved, then value
  or invalid — paste unchanged, mid-edit modal holding the session; RTL mirroring; axe on every
  state (open popup, error, loading) in light/dark;
  forced-colors + reduced-motion gates. The showcase story offers sync and artificial-latency
  async search modes, both modals, and an `ar` locale switch.
- API golden + `api:approve`, `components.json`/`llms.txt`/MCP docs, co-located examples, budget
  and boundary lints — the standard gates.

## 12. Definition of done

1. `@tellma/core-ui/entity-picker` builds, lints, and ships under a declared ratchet with its API
   golden approved — see §10 for the entry point's own overshoot and why it stands; docs pipeline
   and showcase story updated; no `contracts` changes.
2. Standalone and `tm-form-field`-wrapped rendering per §3: `ownsChrome: false`, label/hint/error
   wiring, magnifier only when configured and never a tab stop, size stability across every state.
3. Search lifecycle per §4.2 green: immediate spinner + dropdown on typing with stale results
   cleared on change and the reserved status-area height, leading+trailing coalescing
   (autorepeat ⇒ no request barrage), cancel-on-change, sync fast path with zero spinner,
   failure state showing footer rows, the `hasMore` truncation hint + open-ended announcement,
   dev-mode warning above 200 results.
4. Keyboard model per §4.3 green, including non-pristine first-result auto-highlight (pristine
   browse highlights nothing), Space-always-types, Enter/Tab commit, Enter failing fast on an
   empty typed set, Enter consumed only while open, standalone ArrowDown vs grid branching, Esc
   ordering.
5. Resolution per §5 green: unique auto-pick with no error flash, notFound/ambiguous/failed keep
   the text with `null` model and localized kind-`parse` errors, submits blocked while resolving,
   all supersession races pinned.
6. Committed-value display per §2: `displayWith` reactive path, memo fallback, `String(id)` +
   dev warning; live locale switch re-renders labels, options, and kept errors.
7. Modal contract per §6 for all three pages: `TM_MODAL_DATA` payloads (`query`; `id` on edit),
   typed `TmModalRef` result, pick applies + memoizes + focuses, edit's `close(null)` clears the
   field, Edit… row present only while a value is committed, every dismissal is a no-op,
   size/title defaults and overrides.
8. Grid: entity columns configured with `search` mount the picker as the built-in editor —
   bare-input chrome, magnifier only, cell-anchored `matchWidth` panel with min-width floor,
   `Alt+ArrowDown`, dropdown gate, pick-IS-the-edit, Tab-commit-move, two-stage Esc, `seed`/
   `cancel`/`text` per the `TmCellEditor` contract; consumer `*tmGridEditor` still wins; the
   no-config dev error remains.
9. Grid typed-commit resolution per §7.4: unresolved commit text flows through parse → the
   editor's adopted search → `resolvePastedLabels`, with the single-cell pending affordance and
   §9.4 messages, splitting by the table there; at most one resolver call runs, after editor
   teardown; grid commit stays synchronous; pending state is the grid's `pendingCount`; the
   picker's form-path resolution and text channel are inert in a cell; the grid suite is green.
10. Mid-edit modals hold the grid edit session open and commit on pick (Playwright-pinned).
11. A11y per §8: axe clean in every state; the portaled ARIA id chain resolves; live-region
    announcements (counts, no-results, failure, auto-resolution) fire on completion only;
    real-mouse specs green; forced-colors/reduced-motion gates green.
12. RTL per §9 verified under `dir="rtl"` (panel mirroring, magnifier at inline end, bidi text);
    every new string resolves through `TM_UI_TRANSLATE`, ships English in-package, and lands in
    `@tellma/locale-ar`.
13. `TmEntityPickerHarness` shipped; unit + Playwright suites of §11 green; budgets and goldens
    enforced.

## Decisions record

The load-bearing decisions, where not already evident above:

1. **Locale-dependent labels** — caller-supplied functions evaluated in a reactive context
   (`itemLabel`, `displayWith`), not a `label`/`label2`/`label3` convention with global language
   mapping: the core stays domain-free, the same seam `tm-select.displayWith` and the grid's
   column `format` already established, and one distribution-level helper closure delivers the
   on-the-fly ambient-locale behavior everywhere (§2).
2. **APG conformance** — no contradictions found; two deliberate strengthenings documented in §8:
   Tab commits the highlighted option (APG baseline only closes), and command options
   (Create…/Advanced search…) live inside the listbox as `role="option"` rows. Auto-highlighting
   the first result is APG's automatic-selection variant (typed queries only, §4.3); async
   status is announced via a polite live region rather than rendered as options.
3. **Blur before results return** — the search continues as the resolution input; exactly one
   result auto-selects, zero or many becomes an error (§5.2). Rationale: the
   ERP unique-match auto-resolve convention, and it is precisely the grid's pasted-label
   semantics (`value`/`notFound`/`ambiguous`), so forms and grids agree. The pending +
   display-policy interplay eliminates the error-flash cost that usually argues for option A, and
   the field is invalid throughout the window so a racing submit can never save a phantom value.
4. **Blur without selecting** — keep the text, enter the error state (never silently clear).
   Consistent with `tmNumber`, `tm-date-picker`, and grid invalid inputs, which
   all preserve unparseable text for correction; clearing discards user work on a stray click
   (§5.2). The auto-resolve rule above means this state is reached only for genuinely
   zero/ambiguous text.
5. **Debounce** — 50 ms leading + trailing: anything below the fastest OS key-repeat interval
   (~33 ms) coalesces nothing; leading-edge firing keeps single keystrokes latency-free, and the
   first-open spinner keeps the UX responsive either way (§4.2). Tunable via `searchDebounce`.
6. **Synchronous sources** — supported first-class: array returns render in the same turn with no
   spinner, and the debounce window is skipped while the source is known-synchronous (§4.2).
7. **Advanced-search keyboard path** — the magnifier follows the calendar-button precedent
   (`tabindex="-1"`, one Tab per field); its keyboard/touch equivalent is the Advanced search…
   footer option, reachable by arrows in every SETTLED popup state — no new shortcut, no second
   tab stop on every FK field (§3, §4.1). §4.1's loading exclusion narrows this: while a search is
   outstanding there is no keyboard route to the page. Accepted, because the window is transient
   and self-clearing — it ends when the response lands, and the user reaches the row by waiting
   rather than by discovering anything. WCAG 2.1.1 asks that the functionality be operable from
   the keyboard, not that it be reachable at every instant of a load.
8. **Grid typed-commit resolution** — unresolved editor text on `entity` columns flows through
   the same §9.3/§9.4 pipeline as pasted labels (pending affordance, sequence tokens, localized
   notFound/ambiguous messages), extending spec 0004's chain to editor commits. Grid commits stay
   synchronous under the grid's own pending state (§7.4). What answers the question is the
   **editor's own search first, the column's resolver second**: the search is the one the user was
   already waiting for, and handing a partial query to a label resolver instead spends a second
   round trip to get a worse answer — "Alice" resolves to one supplier by search and to nothing by
   an identity lookup. The picker's form-path resolution still never runs in a cell; what crosses
   the boundary is the SEARCH, which the grid adopts and decides from. At most one resolver call
   is authoritative, commonly none.
9. **Grid keyboard divergence from forms** — pick-IS-the-edit on Enter (no move, the enum/date
   contract) and Tab select-close-bubble so the grid's own commit-and-move runs; plain arrows
   belong to the grid when the dropdown is closed, per the spec 0005 branching rule (§7.3).
10. **Modals mid-cell-edit** — the edit session stays open while a picker-launched modal is up
    (the commit-on-blur press rule already reads the activating press as internal); a pick
    commits through the still-open editor, a dismissal resumes editing (§7.3).
11. **Dropdown anchor** — the chrome the user reads as the field (form-field box / grid cell /
    bare host), the date-picker's rule, with `matchWidth` plus a min-width token floor for
    narrow cells (§4.1).
12. **Open-on-click with a pristine browse query** — pointer opening a populated field searches
    `''` (browse alternatives) rather than the committed label (which would only find the current
    entity); an edited text always searches itself (§4.2). Click-to-open is also the touch path.
    Browse lists auto-highlight nothing (§4.3), so open-then-Tab can never change the selection.
13. **The create-typo loop** — an optional **Edit… footer row** opens a consumer edit page on the
    committed value, and deletion is that page's business, reported back as `close(null)` (§6) —
    not a separate Delete affordance, whose confirmation and lifecycle semantics the picker
    cannot own. Industry practice splits between in-place record editing (Odoo's linked-record
    dialog) and routing to the record page (Dynamics, Salesforce); the optional input supports
    the first and documents the second as the acceptable fallback when a screen doesn't wire it.
14. **Truncated results are flagged, not silent** — the search result's object form carries
    `hasMore`, rendering the refine-your-search hint above the Advanced search… row and switching
    the count announcement to its open-ended variant (§2, §4.1). An explicit consumer flag was
    chosen over inferring truncation from a result-count input, which guesses wrong exactly at
    the boundary; showing "top N + refine/search-more" is the prevailing lookup convention.
15. **Immediate spinner, reserved status height** — every async (re-)search clears the stale rows
    and shows the spinner at once: what is on screen always corresponds to the text in the field,
    and the instant spinner reads as a responsive system. The height churn fast typing over a
    fast server invites is damped structurally instead: the status area reserves a fixed
    block-size, so the panel edge barely moves across list↔spinner swaps (§4.2). The
    deferred-spinner alternative (a grace period with stale rows kept visible) was rejected — it
    reads as a slow system, and stale rows invite stale picks.
