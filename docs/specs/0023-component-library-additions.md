# Spec: Component Library Additions

- **Author:** Ahmad Akra
- **Date:** 20 October 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

The front-end specs need about thirty generic components that `@tellma/core-ui` does not have:
icons, badges, avatars, toasts, dialogs, a tree, tiles, a pager and more. This spec adds them as new
entry points of `@tellma/core-ui`, one per component family. Each comes with its harness, budget,
tokens, and English and Arabic strings. No feature spec defines its own generic primitive; each
composes these.

Every component is domain-free. None knows tenants, entities, the BFF or `@tellma/core`, and none
touches storage or the network. Policy belongs to the caller: spec 0028 decides which outcomes show
a toast, spec 0024 what an error dialog says, and spec 0031 when a save blocks the page. The
components own geometry, keyboard, ARIA, direction and stable layout, so pages built from them meet
the UI rules for free.

The spec builds on spec 0002, the library foundation: conventions, tokens, Signal Forms,
`TM_UI_TRANSLATE`, budgets, API goldens and the docs pipeline. It also builds on spec 0010 Parts
B–D. A change to an entry point that already exists is an amendment in spec 0010, never here.

**Other specs that carry parts of this design**

- **Spec 0010 Part B:** §B.1 breakpoints; §B.3 the live root `Directionality`; §B.6 `ɵtmControlSize`;
  §B.8 the semantic `categorical` token group that avatar tones read.
- **Spec 0010 Part C:** the grid's activation payload (§C.1), driver interface (§C.2) and tree
  loading (§C.9), all reused by `tm-tiles`; §C.10 menu entries with icon names or data and
  `TmKeys`; §C.12 the grid's key map matched through `kbd`.
- **Spec 0010 Part D:** §D.1 `overlay` and its Escape stack; §D.2 `TmModalConfig.role`; §D.4 the
  relative-time format, the zone label and `TM_TIME_ZONE`; §D.9 `placeholderOnError`; §D.10
  `tmButton` sizing `tm-icon`.
- **Spec 0010 Part E:** §E.2 the multi-picker's chip row, built from `tm-chip`.
- **Spec 0010 §B.7, §C.13, §D.11, §E.5:** the existing entry points' glyphs drawn through `tm-icon`
  (§3.1).
- **Spec 0024:** §2.2 feature `icons`, `TM_NOW`, `TM_TIME_ZONE` and `provideTellmaToasts`; §11 the
  error dialog's triggers and strings.
- **Spec 0027:** §5.1 the toast offset; Appendix A the toast shortcut.
- **Spec 0028:** §8 the toast policy; §9 the banner host.

## Goals / Non-goals

**Goals**

- Twenty-nine new `@tellma/core-ui` entry points (§3–§8), each with its harness, budget, tokens,
  strings, docs examples and showcase story, under one set of shared rules (§2).
- A runtime icon registry that a distribution extends in one line (§3.1).
- UI rules that hold by construction: no enter animation, reserved space, live direction,
  persistent live regions, a non-drag path for every drag.

**Non-goals (explicitly out of scope)**

- Changes to existing entry points (grid, menu, modal, select, image, button, l10n, picker), the
  drawer (`TmModal` `placement: 'end'`) and the multi-select inputs — spec 0010 Parts B–E.
- An inline banner for dialogs — spec 0005's `tm-alert` (spec 0005 §13).
- An accordion, a free-entry chip input and a signature pad — no consumer needs them; spec 0032
  §4.7 uploads a signature image instead.
- `tm-chart` and `tm-pivot` — spec 0034. Toast policy and banner copy — spec 0028.
- The app bar, the launcher and the shortcut registry — spec 0027.
- Feature components built from these — their owning specs.

## 1. Placement

Every component ships as its own entry point `@tellma/core-ui/<ep>`, in
`client/projects/core/tellma-core-ui/<ep>/`, under spec 0002's conventions. This section lists them
with their exports, dependencies and budgets, and the terms the spec uses.

Contract blocks are TypeScript sketches: names and shapes are normative; imports, documentation
comments, decorators and bodies are omitted. A class's trailing comment names its selector.

- **Conventions (spec 0002).** Every entry point follows them:
  - `public-api.ts` with `@packageDocumentation`; imports through `@tellma/core-ui/<ep>`, never `../`.
  - Standalone, OnPush, signal inputs, `model()`, `output()`; host `display: block` unless a
    section says otherwise; services in `root`.
  - Overlays and Escape come from `@tellma/core-ui/overlay` (spec 0010 §D.1), never from
    `@tellma/core-ui/private`. Responsive rules are container queries (spec 0010 §B.1).
  - No storage, no network: images load through `tm-image` and `TM_BLOB_FETCHER`.
- **One third-party dependency: `lucide`** (ISC licence). It is a `dependencies` entry, allowed by
  ng-packagr's `allowedNonPeerDependencies`, and is imported one icon at a time. The registry also
  accepts `@lucide/icons` v1's `{ node }` objects (Review flag 2).
- **One global stylesheet:** `styles/tm-description-list.css`, listed in
  `"tellma".docs.globalStyles`. `@tellma/core/styles.css` imports it (spec 0024 §7.6).

In the table, `@tmGroup` is the docs group: the library's existing groups plus a new `collection`.
KB is the gzip budget ceiling.

| Entry point | Exports | Depends on (`@tellma/core-ui/…`) | `@tmGroup` | KB | Harnesses |
|---|---|---|---|---|---|
| `icon` | `TmIcon` (`tm-icon`), `TmIconRegistry`, `provideTellmaIcons`, `TmIconNode`, `TmIconSource` | — | media | 2 | — |
| `kbd` | `TmKbd` (`tm-kbd`), `TmKeys`, `tmFormatKeys`, `tmKeysText`, `tmKeysMatch`, `tmAriaKeyShortcuts`, `tmIsApplePlatform` | — | indicator | 1 | — |
| `copy` | `TmCopyButton` (`tm-copy-button`), `TmReference` (`tm-reference`) | `button`, `icon`, `kbd`, `tooltip` | control | 1 | `TmCopyButtonHarness`, `TmReferenceHarness` |
| `badge` | `TmBadge` (`tm-badge`), `TmCounter` (`tm-counter`), `TmBadgeTone` | `icon` | indicator | 1 | `TmCounterHarness` |
| `avatar` | `TmAvatar` (`tm-avatar`), `TmAvatarSize`, `tmInitials`, `tmAvatarTone` | `image`, `icon` | media | 2 | `TmAvatarHarness` |
| `chip` | `TmChip` (`tm-chip`), `TmChipSet` (`tm-chip-set`) | `icon`, `badge`, `tooltip` | indicator | 2 | `TmChipHarness`, `TmChipSetHarness` |
| `progress` | `TmProgressBar` (`tm-progress-bar`), `TmProgressRing` (`tm-progress-ring`) | — | indicator | 1 | `TmProgressBarHarness` |
| `skeleton` | `TmSkeleton` (`tm-skeleton`) | — | indicator | 1 | — |
| `relative-time` | `TmRelativeTime` (`tm-relative-time`), `TM_NOW` | `l10n`, `tooltip` | indicator | 1 | `TmRelativeTimeHarness` |
| `toast` | `TmToasts`, `TmToast`, `TmToastRef`, `TmToastTone`, `TmToastsOptions`, `provideTellmaToasts` | `icon`, `button`, `overlay` | overlay | 4 | `TmToastHarness` (root loader) |
| `banner` | `TmBannerBar` (`tm-banner-bar`) | `icon`, `button` | indicator | 2 | `TmBannerBarHarness` |
| `empty-state` | `TmEmptyState` (`tm-empty-state`) | `icon` | indicator | 1 | `TmEmptyStateHarness` |
| `busy-overlay` | `TmBusyOverlay` | `spinner` | overlay | 1 | `TmBusyOverlayHarness` |
| `confirm` | `TmConfirm`, `TmConfirmOptions`, `TmConfirmChoice` | `modal`, `button`, `icon` | overlay | 2 | `TmConfirmHarness` |
| `error-dialog` | `TmErrorDialog`, `TmErrorDialogOptions`, `TmErrorDialogAction` | `modal`, `button`, `copy`, `icon` | overlay | 2 | `TmErrorDialogHarness` |
| `radio` | `TmRadioGroup` (`tm-radio-group`), `TmRadio` (`tm-radio`) | — | form-control | 2 | `TmRadioGroupHarness`, `TmRadioHarness` |
| `segmented` | `TmSegmented` (`tm-segmented`), `TmSegment` (`tm-segment`) | `icon`, `tooltip` | form-control | 2 | `TmSegmentedHarness` |
| `switch` | `TmSwitch` (`tm-switch`) | — | form-control | 1 | `TmSwitchHarness` |
| `search-field` | `TmSearchField` (`tm-search-field`), `TmSearch` (`input[tmSearch]`) | `form-field`, `input`, `icon` | form-control | 2 | `TmSearchFieldHarness` |
| `date-range` | `TmDateRange` (`tm-date-range`), `TmDateRangeValue`, `tmDateRangeOrder` | `date-picker`, `form-field` | form-control | 2 | `TmDateRangeHarness` |
| `card` | `TmCard` (`tm-card`) | `badge`, `icon` | layout | 1 | `TmCardHarness` |
| `page-header` | `TmPageHeader` (`tm-page-header`) | `icon` | layout | 1 | — |
| `description-list` | `TmDescriptionList` (`dl[tmDescriptionList]`) | — | layout | 1 | — |
| `splitter` | `TmSplitter` (`tm-splitter`), `TmSplitterPane` (`[tmSplitterPane]`) | — | layout | 2 | `TmSplitterHarness` |
| `pager` | `TmPager` (`tm-pager`) | `button`, `icon`, `select` | collection | 2 | `TmPagerHarness` |
| `tree` | `TmTree` (`tm-tree`), `TmTreeNode` (`ng-template[tmTreeNode]`), `TmTreeNodeState` | `icon`, `drag-drop`, `spinner` | collection | 4 | `TmTreeHarness`, `TmTreeNodeHarness` |
| `drag-drop` | `TmDragSource` (`[tmDragSource]`), `TmDropTarget` (`[tmDropTarget]`), `TmDragDrop`, `TmDragPayload`, `TmDrop` | — | collection | 2 | — (through tree and reorder list) |
| `reorder-list` | `TmReorderList` (`tm-reorder-list`), `TmReorderItem`, `TmReorderEmpty`, `TmReorderMove` | `drag-drop`, `icon`, `overlay` | collection | 3 | `TmReorderListHarness` |
| `tiles` | `TmTiles` (`tm-tiles`), `TmTileLayout`, `TmTileText`, `TmTileImage`, `TmTileContent`, `TmTilesEmpty`, `TmTilesLoading` | `image`, `badge`, `icon`, `spinner`, `grid-engine`, `contracts`; `grid` as types only | collection | 7 | `TmTilesHarness`, `TmTileHarness` |

**Vocabulary**

- **Size ladder** — `TmControlSize` `sm | md | lg`. Controls are 30, 38 or 46 px; option rows are
  26, 30 or 34 px (spec 0002 §5).
- **Resolved size** — the size `ɵtmControlSize` answers for a component (spec 0010 §B.6).
- **Registry name** — a kebab-case icon name registered in `TmIconRegistry`, such as `chevron-right`.
- **Definition directive** — a directive that carries inputs and a template only; its parent
  renders the real elements, as with `tm-option`.
- **Mapped** — direction-mapped: Left and Right swap in RTL, read from the live direction at keydown.
- **Row** — one data item of `tm-tiles`, as in the grid. A **lattice line** is one line of tiles.
- **Active row** — the tile or tree node the keyboard acts on.
- **Context node** — a node or tile shown only because a descendant matched a search; it is muted.

## 2. Shared rules

Every component follows one set of rules for size, keyboard, direction, motion and layout
stability. The component sections state only their deltas.

### 2.1 Keyboard and ARIA

One table holds the keyboard of every interactive component; the longer key maps follow it as
bullets. In the pattern column, "own" means the library implements the behaviour itself.

| Component | Pattern (behaviour layer) | Tab stops | Keys |
|---|---|---|---|
| `tree` | APG tree view (`@angular/aria/tree`) | 1 | below |
| `tiles`, flat | APG layout grid, `role="grid"` (own) | 1 | below |
| `tiles`, tree mode | `role="treegrid"`, one cell per row (own) | 1 | below |
| `radio`, `segmented` | APG radio group (native radios, own arrow handler) | 1 | below |
| `switch` | APG switch (native checkbox, `role="switch"`) | 1 | Space toggles |
| `chip`, removable or activatable | list with roving chip buttons (own) | 1 | below |
| `reorder-list` | list with a handle button per row (own, over `drag-drop`) | 1 per handle | below |
| `splitter` | APG window splitter (own) | 1 | below |
| `card`, collapsible | APG disclosure (own) | 1 | Enter or Space toggles |
| `confirm`, `error-dialog` | APG alert dialog (`TmModal`) | trapped | Tab cycles; Escape dismisses |
| `toast` | a region of buttons (own) | per button, entered by `TmToasts.focus()` | Tab and Shift+Tab move between toast buttons; Escape closes the focused toast and restores focus |
| `search-field` | native `input type="search"` | 1, plus the clear button | Enter emits at once; Escape clears when the text is non-empty, else propagates |
| `pager`, `copy`, `banner` | native buttons and `tm-select` | per control | native |
| `progress`, `busy-overlay`, `skeleton`, `badge` | `progressbar`, `status`, or hidden | 0 | — |

- **`tree`.** Up/Down move. Right expands, else moves to the first child; Left collapses, else
  moves to the parent (both mapped). Home/End; typeahead on `label`. Enter emits `activate` and
  selects in `single`; Space toggles in `multiple`.
- **`tiles`, flat.** Arrows move by tile: Left/Right mapped; Up/Down keep the column, clamped on a
  short last line. Home/End go to the line's ends, Ctrl+Home/End to the first and last tile,
  PageUp/Down by visible lines. Enter activates (Mod: new tab). Space toggles selection;
  Ctrl+Shift+Space toggles every loaded row.
- **`tiles`, tree mode.** As flat, in one column; Right and Left act as in `tree` (mapped).
- **`radio`, `segmented`.** Tab enters at the checked radio, else the first enabled one. Up/Down
  and Left/Right (mapped) check and focus the previous or next enabled radio, wrapping. Space checks.
- **`chip`.** Left/Right (mapped) and Home/End move between the chips' buttons, a label button
  before its remove button. Enter or Space on a label button emits `activate`. Delete or Backspace
  removes, then focuses the next chip, else the previous, else emits `exit('end')`. An arrow past
  either end emits `exit`.
- **`reorder-list`.** On a handle, Space or Enter grabs the item. While grabbed, Up/Down move it one
  place and Home/End to the ends; Space, Enter or Tab drops it; Escape cancels and restores it.
  Alt+Up/Down moves one place without grabbing.
- **`splitter`.** Arrows move the bar by `step`; Shift moves it ×4. A horizontal splitter uses
  Left/Right, mapped so the bar moves the way the arrow points; a vertical one uses Up/Down.
  Home/End jump to the ends of the clamp (§7.3).

The same rules hold for every component:

- **Live direction.** A mapped key reads `getComputedStyle(host).direction` at keydown, or the aria
  pattern's live `Directionality` (spec 0010 §B.3). Switching from English to Arabic needs no reload.
- **One Escape stack.** Whatever dismisses on Escape (a focused toast, a reorder grab, the chip
  overflow tooltip) pushes onto the shared stack with `tmPushEscapeDismissal` (spec 0010 §D.1).
- **Focus and targets.** Focus shows `--focus-ring` on `:focus-visible`. Targets are at least 24×24
  px (spec 0002 §6). An icon-only button always has an accessible name.

### 2.2 Sizes, direction, stable layout, motion

These rules keep components correctly sized, right in both directions, and still while users work.

- **Sizes.** On the size ladder, at the resolved size: `segmented`, `search-field`, `date-range`,
  `pager`, `chip` (20, 24, 28 px), `copy` (through `tmButton`), and the rows of `tree`,
  `reorder-list` and `radio`. The rest have fixed geometry from their token group (§9).
- **Direction.** Styles use logical properties only. Directional glyphs mirror (§3.1). Progress
  bars and the tiles lattice flow from inline-start.
- **Numbers.** Numeric runs that read as expressions carry `dir="ltr"`: the counter, the pager
  status, `tm-kbd` and `tm-reference`. Digits follow the locale through `TmL10n`.
- **Overlays never displace content:** toasts, the busy overlay, overlay counters, drop indicators.
- **Reserved space,** so nothing moves when content comes and goes: the search field's clear slot,
  the tree's spinner slot, the pager status (§8.1), the inline counter's two digits, the copy
  button's labels (§3.3), and the header slots (§7.2).
- **Recompute on resize only, never on input:** chip collapse, segmented labels, tiles columns.
- **Tabular digits.** Every changing number uses `font-variant-numeric: tabular-nums`.
- **No enter or exit animation.** The only motion is the switch knob (`--duration-fast`), the
  progress sweep and the ring's spin. All three are static under `prefers-reduced-motion: reduce`.
- **Forced colors.** Every state a tint conveys (selected tile, active segment, switch on, counter
  tone, drop target) also has a border or glyph that survives `forced-colors: active`.

## 3. Foundations: `icon`, `kbd`, `copy`

Other components build on these three: how an icon is named and drawn, how a key combination is
written and matched, and how a value is copied.

### 3.1 `icon`

`tm-icon` draws a Lucide icon by registry name, or from icon data passed directly. The library
itself registers nothing globally. `@tellma/core` registers the platform's names (spec 0024 §2.2),
and a distribution adds its own.

```ts
type TmIconNode = readonly (readonly [tag: string, attrs: Readonly<Record<string, string | number>>])[]; // Lucide's IconNode: SVG children
type TmIconSource = TmIconNode | { readonly node: TmIconNode; readonly mirrorInRtl?: boolean };      // @lucide/icons v1 data fits the object form
declare function provideTellmaIcons(icons: Readonly<Record<string, TmIconSource>>): EnvironmentProviders; // multi; a later name wins
declare class TmIconRegistry {                        // root service
  register(icons: Readonly<Record<string, TmIconSource>>): void;   // throws on a refused attribute
  get(name: string): { readonly node: TmIconNode; readonly mirrorInRtl: boolean } | undefined;
}
declare class TmIcon {                                // tm-icon
  readonly name: InputSignal<string | undefined>;      // registry name: 'chevron-right'
  readonly svg: InputSignal<TmIconSource | undefined>; // direct data, bypassing the registry (the library's own glyphs)
  readonly size: InputSignal<'xs' | 'sm' | 'md' | 'lg' | 'xl' | undefined>; // 12, 14, 16, 20, 24 px; unset: var(--icon-size)
  readonly label: InputSignal<string | undefined>;     // set: role="img" and aria-label; unset: aria-hidden
}
```

- **Rendering.** An `inline-flex` square host of `var(--icon-size)` holds one `<svg viewBox="0 0 24
  24" fill="none" stroke="currentColor">`, with round caps and joins. It is built with
  `createElementNS`, never `innerHTML`, so icons need no Trusted Types policy.
- **Stroke.** `--icon-stroke-width` is 1.75 by default. A host may set its own, as the image edit
  pane's check does at 2.25 (spec 0010 §D.11).
- **First frame.** `tm-icon` draws in the change-detection pass that creates it, so a control
  never paints a frame without its glyph.
- **Guards.** `register` throws on an attribute name starting with `on`, `href` or `xlink:href`. An
  unknown name renders an empty box of the same size and logs one dev-mode error.
- **Mirroring.** `mirrorInRtl` flips the glyph (`scale(-1, 1)`) under an RTL computed direction.
  Directional glyphs carry it: `chevron-left`, `-right`, `-first`, `-last`, `arrow-left`, `-right`,
  `log-in`, `log-out`, `undo`, `redo`, `panel-left-open`, `-close`; never the magnifier, check or x.
- **Containers set the size.** `tmButton` and `tm-menu` point `--icon-size` at their icon-size
  tokens (spec 0010 §D.10, §C.10), so `<button tmButton><tm-icon name="plus" />New</button>` fits.
- **The library's own glyphs** use `<tm-icon [svg]>` with data imported from `lucide`. A
  directional glyph passes the object form with `mirrorInRtl`. Spec 0010 §B.7, §C.13, §D.11 and
  §E.5 move the existing entry points onto this rule. Two exceptions keep their own drawing:
  - `tm-spinner`'s animated arc;
  - the grid's per-cell check and dash masks, which a grid paints thousands of times (spec 0010
    §C.13).

**Example —** Acme's feature (spec 0024 §2.2) adds its icon in one line, `icons: { 'hand-coins':
HandCoins }`, with `HandCoins` imported from `lucide`. Its entries then write `icon: 'hand-coins'`.

### 3.2 `kbd`

`kbd` owns the grammar for writing a key combination, its matching and its rendering. The shortcut
registry (spec 0027 §4), the menu's hints, and the grid's key map, find bar and hints (spec 0010
§C.10, §C.12) use it.

```ts
// Modifiers (Mod | Ctrl | Alt | Shift | Meta), then one KeyboardEvent.key, joined by '+'.
// 'Plus', 'Minus' and 'Space' stand for '+', '-' and ' '. Examples: 'Mod+K', '?', 'F2', 'Mod+Plus'.
type TmKeys = string;
declare function tmIsApplePlatform(): boolean;                                   // userAgentData.platform, else navigator.platform
declare function tmFormatKeys(keys: TmKeys, apple?: boolean): readonly string[]; // ['Ctrl', 'K'] or ['⌘', 'K']: one per <kbd>
declare function tmKeysText(keys: TmKeys, apple?: boolean): string;              // 'Ctrl+V' or '⌘V': one string, for sentences
declare function tmKeysMatch(event: KeyboardEvent, keys: TmKeys): boolean;       // Mod is Meta on Apple, Ctrl elsewhere
declare function tmAriaKeyShortcuts(keys: TmKeys): string;                       // 'Control+K' or 'Meta+K'
declare class TmKbd { readonly keys: InputSignal<TmKeys> }                       // tm-kbd
```

- **Modifiers match exactly, except Shift beside a symbol.** A printable symbol (`?`, `+`, `/`)
  never compares Shift, because layouts differ on whether it needs one. `'?'` matches Shift+/ on
  a US layout. `'Mod+Alt+Plus'` matches both `+` from the numpad and Shift+= from the main keys.
- **Keys compare `event.key`, case-insensitively,** except in three cases, which match by
  `event.code` instead:
  - a letter or digit binding, when `event.key` is not an ASCII letter or digit: "ي" from D, or
    "]" from Shift+D, on an Arabic layout;
  - a US punctuation binding, when `event.key` is one non-ASCII character: "ظ" from the Slash key
    on an Arabic layout;
  - either kind while Alt is held on an Apple platform, where Option changes `event.key`.
- **Punctuation codes** read as the US layout's keys: `Slash` `/`, `Period` `.`, `Comma` `,`,
  `Semicolon` `;`, `Quote` `'`, `BracketLeft` `[`, `BracketRight` `]`, `Backslash` `\`,
  `Backquote` `` ` ``, `Minus` `-`, `Equal` `=`. With Shift held, each reads as its US shifted
  symbol: `Slash` is `?`, `Equal` is `+`.
- **AltGr types.** The `event.code` fallback never applies while `getModifierState('AltGraph')` is
  true. Windows reports AltGr as Ctrl+Alt, so on a Polish layout AltGr+A types "ą" and must not
  match `'Mod+Alt+A'`.
- **One platform check.** `tmIsApplePlatform` is the library's only reader of `navigator.platform`
  and `userAgentData`. The ESLint rule `tm/no-platform-sniffing` refuses both outside `kbd` (spec
  0010 §C.12).
- **Rendering.** One `<kbd>` per key: 18 px caps, `dir="ltr"`, Latin key names in every language.
  Apple uses ⌃ ⌥ ⇧ ⌘, in that order. `tmKeysText` joins the same names with `+`, or with nothing
  on Apple.

**Example —** `tmFormatKeys('Mod+Shift+K')` is `['Ctrl', 'Shift', 'K']` on Windows and
`['⇧', '⌘', 'K']` on a Mac. `tmKeysText('Mod+V')` is "Ctrl+V" or "⌘V". On an Arabic layout,
these bindings match by `event.code`:

| Binding | `event.key` | `event.code` |
|---|---|---|
| `Mod+D` | `'ي'` | `'KeyD'` |
| `Mod+Shift+D` | `']'` | `'KeyD'` |
| `Mod+/` | `'ظ'` | `'Slash'` |

### 3.3 `copy`

`tm-copy-button` copies a value in one click; `tm-reference` shows a trace id or version as a mono
chip with that button. The error dialog (§5.6), error states (spec 0024 §11), the request log (spec
0026 §8) and About (spec 0027 §11.1) use them.

```ts
declare class TmCopyButton {                          // tm-copy-button
  readonly text: InputSignal<string | (() => string | Promise<string>)>; // what is copied
  readonly label: InputSignal<string | undefined>;    // accessible name and visible label; default "Copy"
  readonly showLabel: InputSignal<boolean>;           // false (default): icon-only, with a tooltip
  readonly size: InputSignal<TmControlSize | undefined>;
  readonly copied: OutputEmitterRef<void>;            // after a successful copy
}
declare class TmReference {                           // tm-reference
  readonly value: InputSignal<string>;                // the trace id or version, shown and copied
  readonly label: InputSignal<string | undefined>;    // default "Reference"
}
```

- **Copy.** `navigator.clipboard.writeText` runs inside the click handler.
- **Success.** The icon becomes a check and the label (visible, or the tooltip) reads "Copied" for
  2 s, announced politely. The button reserves the wider label, so nothing moves.
- **Failure** (no secure context, permission denied). The text is selected in a hidden read-only
  input, and the tooltip says "Press Ctrl+C to copy", or "Press ⌘C to copy" on a Mac
  (`tmKeysText('Mod+C')`).
- **The reference chip** shows the label "Reference", the value in a sunken mono chip, then an
  icon-only "Copy reference" button. The chip is `dir="ltr"` and ellipsized when narrow; the button
  copies the full value. "Reference 4bf92f3577b3…" copies all 32 characters.

## 4. Status and identity: `badge`, `avatar`, `chip`, `progress`, `skeleton`, `relative-time`

These render small facts about a thing: a status, a count, a person, chips, progress, a placeholder
and an age. Only removable and activatable chips take input.

### 4.1 `badge`: badges and counters

`tm-badge` labels a status in a colour tone; `tm-counter` shows a number, inline or on its parent's corner.

```ts
type TmBadgeTone = 'neutral' | 'accent' | 'info' | 'success' | 'warning' | 'error';
declare class TmBadge {                               // tm-badge; content: the text ("Active", "Held", "New")
  readonly tone: InputSignal<TmBadgeTone>;            // default 'neutral'
  readonly icon: InputSignal<string | undefined>; readonly dot: InputSignal<boolean>; // dot: a 6 px leading dot
}
declare class TmCounter {                             // tm-counter: app-bar badges, card and tab counts
  readonly value: InputSignal<number | null>;
  readonly max: InputSignal<number>;                  // default 99: 120 renders "99+"
  readonly tone: InputSignal<'neutral' | 'accent' | 'attention'>; // attention: the error fill
  readonly placement: InputSignal<'inline' | 'overlay'>; // overlay: absolute at the parent's block-start, inline-end corner
  readonly showZero: InputSignal<boolean>;            // false (default): 0 and null render nothing
}
```

- **Overlay counters** are a 16 px pill, at least 16 px wide, with a 2 px halo in `--counter-halo`.
  They are `aria-hidden`: the button's name carries the count, as in "Notifications, 3 new" (spec
  0028 §5.2).
- **Inline counters** are read as text and are at least two digits wide, so 9 → 10 moves nothing.
  Digits come from `TmL10n.formatNumber`.

### 4.2 `avatar`

`tm-avatar` shows a person as a round photo, or as their initials on a tinted circle when there is
no photo. A stable key picks the tint, so two people with the same initials usually differ.

```ts
type TmAvatarSize = 'xs' | 'sm' | 'md' | 'lg' | 'xl'; // 20, 24, 28, 40, 72 px
declare class TmAvatar {                              // tm-avatar
  readonly name: InputSignal<string>;                 // the initials' source and the accessible name
  readonly src: InputSignal<string | null>;           // a tm-image URL (TmUrls.blob, spec 0024 §9.4); null: initials
  readonly etag: InputSignal<string | null>;          // passed to tm-image with src
  readonly colorKey: InputSignal<string | null>;      // an opaque stable key that picks the tone; null: the accent tone
  readonly size: InputSignal<TmAvatarSize>;           // default 'sm'
  readonly decorative: InputSignal<boolean>;          // true when the name is printed beside it: aria-hidden
}
declare function tmInitials(name: string, locale: string): string;
declare function tmAvatarTone(key: string): 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8; // the key's hash picks one of 8 tones
```

- **Initials.** `tmInitials` splits graphemes with `Intl.Segmenter`:
  - a cased script gives the first grapheme of the first and last words, upper-cased in the
    locale: "Farah Nasser" → "FN";
  - an uncased script (Arabic, Ethiopic, CJK) gives one grapheme: "فرح ناصر" → "ف";
  - an empty name shows the `user` glyph.
- **Image.** A `tm-image shape="circle"` with `placeholderOnError` (spec 0010 §D.9). The initials
  show while it loads, when `src` is null, and when it fails.
- **Tones.** The circle's colours depend on `colorKey`:
  - with a key, it takes tone `tmAvatarTone(colorKey)` of the semantic `categorical` group (spec
    0010 §B.8): the tone's `tint` fills it, and the same hue's `onTint` draws the initials, at
    least 4.5:1 in both schemes;
  - without one, it takes `--avatar-bg`, the accent teal, with ink initials at 6.1:1.
- **The hash** is 32-bit FNV-1a over the key's UTF-16 code units. Fixed test vectors pin it, so a
  tone never changes between releases.
  1. Start with `h` at 2166136261.
  2. For each unit, `h = (h XOR unit) × 16777619 mod 2³²`.
  3. The tone is `h mod 8 + 1`.
- **Which key.** The platform passes the record key `{entity}:{id}` (spec 0029 §12.2), never an
  email or a `sub`. The user id is per tenant, so one person may have another tone in another
  tenant.
- **Forced colours.** The tint disappears. The circle keeps a 1 px transparent border, which the
  system colours paint, and the initials take the system text colour.

**Example —** `tmAvatarTone('core.User:17')` is 4 and `tmAvatarTone('core.User:18')` is 3, so two
"FN" avatars in a members list get different tints.

### 4.3 `chip`

`tm-chip` is one label, link, button or removable value. `tm-chip-set` lays chips out as a list
with one tab stop. Applied filters (spec 0030 §10.1), drill-through chips (spec 0034 §8.4) and the
multi-picker's chip row (spec 0010 §E.2) use them.

```ts
declare class TmChip {                                // tm-chip
  readonly label: InputSignal<string>; readonly tone: InputSignal<TmBadgeTone>; // tone default 'neutral'
  readonly icon: InputSignal<string | undefined>; readonly href: InputSignal<string | null>; // href set: renders <a>
  readonly removable: InputSignal<boolean>;           // adds a remove button named "Remove {label}"
  readonly size: InputSignal<TmControlSize | undefined>;
  readonly activate: OutputEmitterRef<void>;          // bound without href: the label renders a button that emits it
  readonly remove: OutputEmitterRef<void>;            // the remove button, or Delete or Backspace on the chip
  focus(): void;                                      // focuses the label button, else the remove button, else the link
}
declare class TmChipSet {                             // tm-chip-set: role="list"; each chip is a listitem
  readonly overflow: InputSignal<'wrap' | 'collapse'>; // default 'wrap'; collapse: one line and a trailing "+N" chip
  readonly ariaLabel: InputSignal<string | undefined>;
  readonly exit: OutputEmitterRef<'start' | 'end'>;   // an arrow past the first or last chip: the host refocuses its input
  focusLast(): void;                                  // a host calls it on Backspace in its empty input
}
```

- **Collapse.** With `overflow: 'collapse'`, chips that do not fit one line hide. The "+N" chip's
  accessible name lists them ("3 more: Service, Sale, Operation") and its tooltip shows them. The
  fit is recomputed on resize and when the chips change, never on a keystroke. The `--chip-*`
  tokens are public to read (§9).

### 4.4 `progress` and `skeleton`

`tm-progress-bar` and `tm-progress-ring` show how far a task has got, or that it is running.
`tm-skeleton` holds the place of content that is still loading.

```ts
declare class TmProgressBar {                         // tm-progress-bar: role="progressbar", 4 px tall
  readonly value: InputSignal<number | null>;         // 0–100: aria-valuenow/min/max; null: indeterminate (a 1.4 s sweep)
  readonly label: InputSignal<string | undefined>;    // the accessible name
  readonly labelledBy: InputSignal<string | undefined>; // or the id of an element that names it
  readonly tone: InputSignal<'accent' | 'success' | 'warning' | 'error'>; // default 'accent'
  readonly active: InputSignal<boolean>;              // default true; false: renders nothing once the hold below ends
}
declare class TmProgressRing {                        // tm-progress-ring: the job ring around an icon button (spec 0028 §6.1)
  readonly value: InputSignal<number | null>;         // null: a spinning arc
  readonly diameter: InputSignal<number>;             // px, default 30; stroke 1.5 px
  readonly decorative: InputSignal<boolean>;          // default true: the host's name says "1 job running"
}
declare class TmSkeleton {                            // tm-skeleton: aria-hidden
  readonly shape: InputSignal<'line' | 'block' | 'circle' | 'table'>; // default 'line'
  readonly width: InputSignal<string>; readonly height: InputSignal<string | undefined>; // CSS lengths; width default '100%'
  readonly lines: InputSignal<number>;                // line: how many 8 px bars, the last at 60 % (default 1); table: body rows (default 8)
  readonly columns: InputSignal<number>;              // table: cells per row; default 4
}
```

- **The hold.** Once shown, a bar stays visible for at least `--progress-min-visible` (250 ms), so a
  fast answer never flashes it. Consumers overlay the bar, so it takes no layout space (spec 0030
  §3.3). The hold never delays content:
  - rows render the moment they arrive;
  - the consumer's `aria-busy` follows the real state.
- **The skeleton rule.** A skeleton has the final geometry of what replaces it and is static. The
  page framework sets `aria-busy="true"` and one polite "Loading" status (spec 0030 §5.3, 0031 §3.3).
- **The `table` shape** is a header bar, then `lines` rows of `columns` bars at the grid's row
  height: the List and Summary placeholder (spec 0034 §3.3). A pivot or chart frame uses `block`.

### 4.5 `relative-time`

`tm-relative-time` shows how long ago an instant was ("2 min ago") and keeps the text current.

```ts
declare const TM_NOW: InjectionToken<() => number>;   // epoch ms; default Date.now; @tellma/core-testing's fake clock replaces it
declare class TmRelativeTime {                        // tm-relative-time: <time datetime>, tooltip with the absolute date-time
  readonly value: InputSignal<string>;                // an ISO 8601 instant
  readonly style: InputSignal<'long' | 'short' | 'narrow'>; // default 'short'
}
```

- **Text** is `TmL10n.formatRelativeTime(value, { now, style })` (spec 0010 §D.4), in the display
  zone `TM_TIME_ZONE` and with that section's thresholds. Two minutes ago reads "2 min ago"
  (`short`) or "2 minutes ago" (`long`).
- **Tooltip:** `TmL10n.formatDateTime(value, { dateStyle: 'medium' })`, "20 Oct 2026, 10:40". Its
  default `'auto'` zone label (spec 0010 §D.4) appends the offset when the display zone's offset
  differs from the device's: "20 Oct 2026, 10:40 GMT+3". The relative text itself is never
  labelled.
- **One shared ticker** drives every instance, aligned to minute boundaries. It stops while the
  document is hidden and catches up when it becomes `visible` again.
- **Not for "Updated 10:42".** An absolute time of day is `TmL10n.formatTime` (spec 0034 §3.6).

## 5. Feedback surfaces: `toast`, `banner`, `empty-state`, `busy-overlay`, `confirm`, `error-dialog`

These show outcomes and states. Each is presentational or a thin service; which outcome shows where
is the caller's policy.

### 5.1 `toast`

`TmToasts` is a root service with one top-layer host that shows short, timed messages. Spec 0028 §8
decides which outcomes show a toast; spec 0024 §2.2 configures the host.

```ts
type TmToastTone = 'neutral' | 'accent' | 'success' | 'warning' | 'error';
interface TmToast {                                   // all text arrives localized by the caller
  readonly tone: TmToastTone; readonly title: string; readonly detail?: string;
  readonly icon?: string;                             // registry name; default per tone
  readonly action?: { readonly label: string; readonly run: () => void }; // runs, then closes the toast
  readonly duration?: number | 'sticky';              // ms; default 6000; tone 'error' defaults to 'sticky'
  readonly key?: string;                              // a show() with the key of an open toast replaces it in place
}
interface TmToastRef {
  close(): void; update(patch: Partial<TmToast>): void; // update also restarts the timer
  readonly closed: Promise<'timeout' | 'dismiss' | 'action' | 'api' | 'replaced'>;
}
interface TmToastsOptions {
  readonly maxVisible?: number;                       // default 3
  readonly overflow?: { readonly label: (hidden: number) => string; readonly run: () => void }; // the "N more" pill
}
declare function provideTellmaToasts(options: TmToastsOptions): EnvironmentProviders;
declare class TmToasts {                              // root service; works with no configuration
  show(toast: TmToast): TmToastRef; clear(): void;
  focus(): boolean;                                   // focuses the newest toast; false when none is open
}
```

- **Host.** The first `show` creates a `popover="manual"` host on `<body>`; the top layer keeps it
  above modals. It sits in the block-start, inline-end corner, offset by the shell's
  `--toast-offset-block-start` (spec 0027 §5.1). It is 360 px wide, or full width minus 8 px
  gutters below `sm`.
- **Stack.** The host is a `role="region"` named "Recent messages", with the newest toast on top. At
  most `maxVisible` toasts show; older ones hide and the overflow pill counts them. Without
  `overflow` options the pill is plain text.
- **Card.** Each toast is a card (radius md, `--shadow-lg`) holding the tone icon, the title (13 px,
  600), the detail (12 px), the action and a 28 px close button.
- **Timers.** Only visible toasts run their timers. Every timer pauses while the pointer is over the
  stack, while focus is inside it and while the page is hidden. It then resumes with the time left.
- **Announcements.** Two hidden live regions always exist: `polite`, and `assertive` for the
  `error` tone. They receive "title. detail" when a toast shows or is replaced. The visible toasts
  are not live regions.
- **Focus.** A toast never takes focus by itself. `focus()` moves focus to the newest toast; the
  `tm.toasts.focus` shortcut calls it (spec 0027 Appendix A). Escape then closes that toast and
  returns focus.

**Example —** spec 0028's toast when the server accepts a background job (HTTP 202):

```ts
inject(TmToasts).show({
  tone: 'accent', title: t('Started in the background.'), key: `job:${jobId}`, duration: TM_TOAST_FEEDBACK_MS,
  action: { label: t('Track'), run: () => router.navigateByUrl(urls.target('core.Job', jobId)!) },
});
```

### 5.2 `banner`

`tm-banner-bar` is one presentational row. `TmBanners` and its host (spec 0028 §9) decide which
banner shows, when it enters and what it says.

```ts
declare class TmBannerBar {                           // tm-banner-bar
  readonly tone: InputSignal<'info' | 'warning' | 'error'>; readonly message: InputSignal<string>;
  readonly icon: InputSignal<string | undefined>;     // default per tone
  readonly actionLabel: InputSignal<string | null>;   // "Reload", "Learn more"
  readonly dismissible: InputSignal<boolean>;
  readonly position: InputSignal<{ index: number; count: number } | null>; // "1 of 3", with previous and next, when count > 1
  readonly action: OutputEmitterRef<void>; readonly dismiss: OutputEmitterRef<void>;
  readonly previous: OutputEmitterRef<void>; readonly next: OutputEmitterRef<void>;
}
```

- **Geometry.** The bar is exactly 32 px tall, the height of the shell's banner row (spec 0027
  §5.1). It has 12 px inline padding, 12 px text on one line with an ellipsis, the tone's `-bg`
  fill and `-border` line, and 24 px icon buttons. Spec 0028 §9.1 owns the copy and its length cap.
- **Not a live region.** The bar has no `alert` or `status` role. The banner host announces each
  message once, when it is queued and before the bar shows (spec 0028 §9.4).

### 5.3 `empty-state`

`tm-empty-state` renders every "nothing to show" and inline-failure state with one anatomy.

```ts
declare class TmEmptyState {                          // tm-empty-state; content: the detail; [tmEmptyStateActions]: buttons
  readonly heading: InputSignal<string>;
  readonly icon: InputSignal<string | undefined>;     // registry name: 'inbox', 'search-x', 'circle-x'
  readonly tone: InputSignal<'neutral' | 'error'>;    // error tints the icon
  readonly size: InputSignal<'page' | 'panel'>;       // page: centred in the remaining height; panel: compact
  readonly live: InputSignal<'off' | 'polite'>;       // polite: announced when it replaces content
}
```

The content is centred, in this order: a 22 px icon, the heading (14 px, 600), the detail (12 px,
at most 42 characters wide) and the actions. A `tm-reference` may sit in the detail (spec 0024 §11).

### 5.4 `busy-overlay`

`TmBusyOverlay` blocks the page while a write runs that the user must wait for, such as a save
(spec 0031 §8) or sign-out (spec 0024 §8.4). Feedback starts in the click's frame: the caller's
busy trigger and the progress cursor. The dark scrim follows only after a long wait, or at once
when no trigger is visible.

```ts
declare class TmBusyOverlay {                         // root service
  readonly active: Signal<boolean>;
  run<T>(work: Promise<T>, options?: {               // nested runs share one overlay
    label?: string | Signal<string>;
    immediate?: boolean;                              // true: scrim, spinner and label at once
  }): Promise<T>;
}
```

A call such as `await busy.run(save(), { label: t('Saving…') })` goes through three steps:

1. **At once, in the same task,** `run` makes every child of `<body>` except the overlay host
   `inert`. It shows the host as a transparent blocker with `cursor: progress` and remembers the
   focused element. The host is a `popover="manual"`, re-shown so it sits on top of the top layer.
2. **After `--busy-overlay-delay` (200 ms),** a scrim and a spinner with the label appear. The label
   is a polite `role="status"`, default "Please wait". It is read live as it changes ("Finishing
   uploads…" → "Saving…"). With nested runs, the latest run's label shows.
3. **When the last run settles,** `inert` is removed and focus returns to the remembered element.

- **Callers with no visible trigger** pass `immediate: true`, which shows step 2 at once. They are:
  - sign-out, in every tab (spec 0024 §8.4);
  - a command whose trigger has closed, such as a confirmation, the conflict dialog or a menu item
    (spec 0031 §10.1).
- **Every other caller** must show its trigger busy in the click's frame. Examples are the Save
  button's spinner, and the `pending` action button of a dialog that stays open while its work runs
  (spec 0031 §10.4). A save that ends within 200 ms then shows only that spinner, and the page
  never blinks.

Escape does nothing while the overlay is up. Locking the router and the Back button is the details
host's job (spec 0031 §8.7).

### 5.5 `confirm`

`TmConfirm` asks a question with two or more answers and resolves to the chosen key.

```ts
interface TmConfirmChoice<K extends string> {        // one footer button; ask() resolves to its key
  readonly key: K; readonly label: string; readonly variant: 'primary' | 'secondary' | 'ghost' | 'danger';
}
interface TmConfirmOptions<K extends string> {
  readonly title: string; readonly message: string;
  readonly icon?: 'warning' | 'error' | 'info' | null; // a 26 px tinted disc; default warning when a choice is danger
  readonly choices: readonly TmConfirmChoice<K>[];     // footer order, aligned inline-end
  readonly initialFocus?: K;                           // default: the primary choice, else the first non-danger one
}
declare class TmConfirm {                              // root service
  ask<K extends string>(options: TmConfirmOptions<K>): Promise<K | null>; // null: Escape, the close button or the backdrop
  confirm(options: {                                   // ask() with a confirm and a cancel choice
    title: string; message: string; confirmLabel: string; cancelLabel?: string;
    destructive?: boolean;                             // confirm button 'danger', icon 'warning', focus on Cancel
  }): Promise<boolean>;
}
```

The shell is `TmModal` size `sm`, with `role: 'alertdialog'` and `describedBy` pointing at the
message (spec 0010 §D.2).

**Example —** the leave guard (spec 0031 §11.3) uses `ask()`; a delete uses `confirm()`:

```ts
const choice = await confirm.ask({
  title: t('Leave without saving?'), message: t('This center has unsaved changes.'),
  choices: [{ key: 'discard', label: t('Discard changes'), variant: 'ghost' }, { key: 'stay', label: t('Stay'), variant: 'primary' }],
}); // 'discard', 'stay' or null
const ok = await confirm.confirm({ title: t('Delete this center?'), message, confirmLabel: t('Delete'), destructive: true });
```

### 5.6 `error-dialog`

`TmErrorDialog` shows a failure the user must acknowledge. Spec 0024 §11 decides when it opens and
supplies every string.

```ts
interface TmErrorDialogAction { readonly label: string; readonly icon?: string; readonly run: () => void; readonly closes?: boolean } // stays open unless closes
interface TmErrorDialogOptions {
  readonly title: string; readonly message: string;
  readonly items?: readonly string[];                  // listed: the first 10, then "and N more"
  readonly reference?: string;                         // a trace id, shown with tm-reference
  readonly details?: readonly { readonly labelKey: string; readonly value: string }[]; // inside the Details disclosure
  readonly actions?: readonly TmErrorDialogAction[];   // buttons inside the Details disclosure
  readonly retry?: boolean;                            // adds "Try again" (primary); Close becomes secondary
}
declare class TmErrorDialog { show(options: TmErrorDialogOptions): Promise<'retry' | 'close' | 'replaced'> } // root service
```

- **Shell.** The same alert-dialog `TmModal` as `confirm` (§5.5), with focus on the primary button.
  Escape and the close button resolve `'close'`.
- **Replacement.** A `show` while a dialog is open replaces its content in place. The earlier
  promise resolves `'replaced'`.
- **Body,** in order: the message, the `items` as a `<ul>`, the reference row, a "Details" disclosure.
- **Details disclosure.** Closed by default. It holds the `details` as a description list (labels
  resolved through `TM_UI_TRANSLATE`), then the `actions`.
- **Footer.** "Try again" when `retry` is set, then Close.

**Example —** the 5xx dialog (spec 0024 §11), abbreviated:

```ts
await errorDialog.show({
  title, message, reference: traceId, retry: true,
  details: [{ labelKey: 'tm.errors.status', value: '500' }, { labelKey: 'tm.errors.version', value: '2.14.0+8f2c41a' }],
  actions: [{ label: t('Copy diagnostics'), icon: 'clipboard', run: () => support.copyDiagnostics() }],
});
```

## 6. Form controls: `radio`, `segmented`, `switch`, `search-field`, `date-range`

This section adds five Signal Forms controls. Each binds with `[formField]` and honours the field's
`disabled`, `readonly`, `required`, `invalid` and `touched` state. Each emits `touch` on blur.

### 6.1 `radio`, `segmented` and `switch`

`tm-radio-group` picks one value from a visible list. `tm-segmented` does the same as a compact
track of segments. `tm-switch` turns one setting on or off.

```ts
declare class TmRadioGroup<T> {                       // tm-radio-group: FormValueControl<T | null>
  readonly value: ModelSignal<T | null>; readonly size: InputSignal<TmControlSize | undefined>;
  readonly orientation: InputSignal<'vertical' | 'horizontal'>; // default 'vertical'
  readonly ariaLabel: InputSignal<string | undefined>; // else the enclosing tm-form-field's label
}
declare class TmRadio<T> {                            // tm-radio: definition directive
  readonly value: InputSignal<T>; readonly label: InputSignal<string>;
  readonly hint: InputSignal<string | undefined>; readonly disabled: InputSignal<boolean>;
}
declare class TmSegmented<T> {                        // tm-segmented: FormValueControl<T | null>
  readonly value: ModelSignal<T | null>; readonly ariaLabel: InputSignal<string>;
  readonly labels: InputSignal<'always' | 'never' | TmBreakpoint>; // show labels while the container is at least this wide
  readonly size: InputSignal<TmControlSize | undefined>;
}
declare class TmSegment<T> {                          // tm-segment: definition directive
  readonly value: InputSignal<T>; readonly label: InputSignal<string>;
  readonly icon: InputSignal<string | undefined>; readonly disabled: InputSignal<boolean>;
}
declare class TmSwitch {                              // tm-switch: FormCheckboxControl; content: the label
  readonly checked: ModelSignal<boolean>;
  readonly labelPosition: InputSignal<'end' | 'start'>; // the label's side of the track; default 'end'
}
```

- **Radio.** A `role="radiogroup"` wraps native radios that share one generated `name`. Native
  radios give form semantics, Tab to the checked radio, and Space. The group handles arrow keys
  itself (§2.1), because only Chromium is confirmed to map native radio arrows by direction.
- **Segmented** puts the same native radios in a sunken track at the field height. Segments are
  inset 2 px; the active one has a card fill and a hairline. Below `labels`, a segment with an icon
  shows only the icon, with a tooltip, and keeps its label as its accessible name.
- **Switch.** A native checkbox with `role="switch"`: a 28×16 px track and a 12 px knob, in
  `--color-primary` when on. The whole label row is the target.

**Example —** the search page's view toggle (spec 0030 §3.4) is `<tm-segmented [(value)]="view"
labels="xl">`, holding `<tm-segment value="grid" icon="table-2" [label]="t('Grid')" />` and its
`tiles` twin. Below `xl`, it shows icons only.

### 6.2 `search-field`

`tm-search-field` is the search box of pages and panels; `input[tmSearch]` is the same behaviour
for custom chrome, such as the launcher (spec 0027 §9).

```ts
declare class TmSearch {                              // input[tmSearch]
  readonly value: ModelSignal<string>;                // on every input event, at once
  readonly search: OutputEmitterRef<string>;          // the query, on the leading and trailing edges below
  readonly debounce: InputSignal<number>;             // the coalescing window in ms, default 50; 0 fires on every input
  readonly escapeClears: InputSignal<boolean>;        // default true
  flush(): void;                                      // emits now when the text differs from the last emitted
}
declare class TmSearchField {                         // tm-search-field: tm-form-field chrome, a magnifier, a clear button
  readonly value: ModelSignal<string>; readonly search: OutputEmitterRef<string>;
  readonly ariaLabel: InputSignal<string>;
  readonly placeholder: InputSignal<string | undefined>; // default "Search"
  readonly size: InputSignal<TmControlSize | undefined>; readonly invalid: InputSignal<boolean>;
  focus(options?: { select?: boolean }): void;
}
```

- **When `search` emits** (on both edges, as the picker of spec 0006 §4.2 does):
  - at once on the first input after a pause of `debounce` ms or more (the leading edge);
  - once more `debounce` ms after the last input of the burst that follows (the trailing edge);
  - while a key is held (`KeyboardEvent.repeat`), only after its `keyup`;
  - at once on Enter and on clear;
  - during IME composition, only after `compositionend`;
  - never with the value it last emitted.
- **`value` updates per keystroke** for instant feedback. The request and its cancellation are the
  consumer's.
- **Loading feedback** is the consumer's too, under one rule: a `tm-progress-bar` turns `active`
  when `search` emits, and stays active while the shown result is not for the last emitted text.
  As a result, the bar stays on through continuous typing and never blinks between keys. Its hold
  (§4.4) absorbs fast answers. Spec 0030 §3.3 and spec 0034 §3.3 follow this rule.
- **Clear button.** Its slot is always reserved. The button shows while the text is non-empty; it
  clears the text, emits and refocuses the input.
- **The input** is `type="search"` (native cancel hidden), `dir="auto"`, `enterkeyhint="search"`.

**Example —** the customers search box: `<tm-search-field [(value)]="q" (search)="query.set($event)"
[ariaLabel]="t('Search customers')" />`. Farah types "jed", 30 ms per key:

1. "j" emits at 0 ms, and the bar shows.
2. "e" and "d" fall in the window, so "jed" emits at 110 ms.
3. The bar holds until the "jed" rows paint, and at least until 250 ms.

### 6.3 `date-range`

`tm-date-range` edits a from–to pair of dates as two coupled date pickers.

```ts
interface TmDateRangeValue { readonly from: string | null; readonly to: string | null } // ISO YYYY-MM-DD
declare class TmDateRange {                           // tm-date-range: FormValueControl<TmDateRangeValue>
  readonly value: ModelSignal<TmDateRangeValue>;
  readonly label: InputSignal<string | undefined>;    // the group's label (role="group")
  readonly fromLabel: InputSignal<string | undefined>; readonly toLabel: InputSignal<string | undefined>; // default "From", "To"
  readonly layout: InputSignal<'stacked' | 'inline'>; // stacked: labels above (a filter popover); inline: side by side
  readonly minDate: InputSignal<string | undefined>; readonly maxDate: InputSignal<string | undefined>;
  readonly calendar: InputSignal<TmCalendar | undefined>; readonly size: InputSignal<TmControlSize | undefined>;
}
declare function tmDateRangeOrder(path: SchemaPath<TmDateRangeValue>): void; // validator kind 'dateRangeOrder'
```

- **Structure.** Two pairs of `tm-form-field` and `tm-date-picker` sit in a `role="group"`.
- **Coupling.** The "from" picker's `maxDate` is the current `to`, and the "to" picker's `minDate`
  is the current `from`, so each popup greys out impossible days. The order error shows on the
  "to" field.
- **Placement.** `tm-date-range` is placed directly, never inside a `tm-form-field`. It has no
  range-calendar popup (Review flag 6).

## 7. Layout: `card`, `page-header`, `description-list`, `splitter`

These components own geometry and semantics only. Content, data, and which actions collapse at
which width belong to the page.

### 7.1 `card` and `description-list`

`tm-card` frames a block of content under an optional heading. `dl[tmDescriptionList]` lays out
label–value pairs.

```ts
declare class TmCard {                                // tm-card; slots [tmCardSubtitle], [tmCardActions], the body, [tmCardFooter]
  readonly heading: InputSignal<string | undefined>; readonly headingLevel: InputSignal<2 | 3 | 4 | 5 | 6>; // level default 2
  readonly count: InputSignal<number | null>;          // an inline tm-counter after the heading
  readonly collapsible: InputSignal<boolean>; readonly expanded: ModelSignal<boolean>; // expanded default true
  readonly flush: InputSignal<boolean>;                // no body padding: a grid fills the card
}
declare class TmDescriptionList {                     // dl[tmDescriptionList]
  readonly columns: InputSignal<1 | 2 | 'auto'>;      // auto: 2 while the container is at least sm wide, else 1
  readonly layout: InputSignal<'stacked' | 'inline'>; // stacked: label above value; inline: label beside value
}
```

- **Card.** Card fill, a hairline, radius md, no shadow. The header has 8×11 px padding, 13 px 600
  text, a divider, and `[tmCardActions]` at its inline end.
- **Subtitle.** `[tmCardSubtitle]` is a second header line in 12 px secondary text. Its start
  ellipsizes and its end does not, as on a dashboard tile (spec 0035 §8.4).
- **Collapsible** cards are a disclosure: the heading sits in a `<button aria-expanded
  aria-controls>` with a mirrored chevron. A collapsed body leaves the DOM ("Audit info", spec 0031
  §6.8).
- **Description list.** A directive on a native `<dl>`, so the list roles survive; each pair sits
  in a `<div>`. The global stylesheet (§1) sets labels at 12 px 500 secondary, values at 13 px
  strong, gaps 10×18 px. About (spec 0027 §11.1) writes
  `<dl tmDescriptionList><div><dt>Version</dt><dd>2.14.0</dd></div></dl>`.

### 7.2 `page-header`

`tm-page-header` is a page's two-row header, title row and toolbar row, filled through named slots.

```ts
declare class TmPageHeader {}                         // tm-page-header: no inputs; two rows, each at least the field height
// Row 1 slots: [tmPageBack] (the crumb link), [tmPageTitle] (the h1), [tmPageTitleExtra] (badges),
//              [tmPageHeaderEnd] (the row's end)
// Row 2 slots: [tmPageToolbar] (the row's start), [tmPageToolbarEnd] (the row's end)
```

- **Row 1,** in order: the crumb link, a mirrored chevron separator (`aria-hidden`), the `h1` (16
  px, 600, ellipsis), the extras, flexible space, and the end slot.
- **Row 2** is absent when both toolbar slots are empty. In order: the start slot, space, the end
  slot. It is `nowrap` with overflow hidden; no `role="toolbar"` is imposed.
- **Padding** is 12×14 px, or 10 px below `sm`.
- **Reserved width.** Each slot keeps the widest inline size its content has reached since the
  header last changed width. Hiding an action therefore leaves its space, as when Refresh hides in
  edit mode (spec 0031 §5.2). A resize of the header releases the space.

**Example —** the details header (spec 0031 §5):

```html
<tm-page-header>
  <a tmPageBack [href]="listUrl">Centers</a> <h1 tmPageTitle>Jeddah sales</h1>
  <tm-badge tmPageTitleExtra tone="success" icon="check">Active</tm-badge>
  <tm-pager tmPageToolbarEnd mode="item" [index]="3" [total]="293" (previous)="previous()" (next)="next()" />
</tm-page-header>
```

### 7.3 `splitter`

`tm-splitter` divides its container between two panes with a bar the user drags or moves by
keyboard. The consumer sizes one pane, the primary; the clamp below keeps both panes usable.

```ts
declare class TmSplitter {                            // tm-splitter: exactly two [tmSplitterPane] children
  readonly orientation: InputSignal<'horizontal' | 'vertical'>; // horizontal: panes side by side
  readonly primary: InputSignal<'start' | 'end'>;     // the pane whose size is set
  readonly size: ModelSignal<number | null>; readonly defaultSize: InputSignal<number>; // the primary's px size; null: defaultSize
  readonly min: InputSignal<number>; readonly otherMin: InputSignal<number>; // px minimums: the primary, the other pane
  readonly maxFraction: InputSignal<number>;          // the primary's largest share of the container; default 1
  readonly step: InputSignal<number>;                 // the arrow step in px; default 16
  readonly ariaLabel: InputSignal<string | undefined>; // default "Resize panels"
}
declare class TmSplitterPane { readonly tmSplitterPane: InputSignal<'start' | 'end'> } // [tmSplitterPane]
```

- **Clamp.** With `W` the container's size along the split, the rendered size is
  `max(min, min(size ?? defaultSize, maxFraction × W, W − otherMin − separator))`. It is re-applied
  on container resize. `size` keeps the user's intent, not the clamped value.
- **Separator semantics.** `role="separator"`, focusable, with `aria-orientation` perpendicular to
  the split. `aria-valuenow` is the primary's share (0–100), `aria-valuemin` and `aria-valuemax`
  come from the clamp, and `aria-controls` points at the primary pane.
- **Separator geometry.** A 7 px bar with a 3×28 px grip in a 24 px hit area. Dragging uses pointer
  capture, an RTL-aware delta, `touch-action: none` and a `col-resize` or `row-resize` cursor.

**Example —** the report studio (spec 0034 §12.2): `<tm-splitter [(size)]="panel"
[defaultSize]="wide() ? 560 : 500" [min]="420" [maxFraction]="0.66" [otherMin]="340">`. In a
1,200 px container, a `size` of 800 renders at 792 px (0.66 × 1,200), while `size` stays 800.

## 8. Collections: `pager`, `tree`, `reorder-list`, `drag-drop`, `tiles`

This section adds paging, trees, drag and drop, reorderable lists and tiles. Each keeps one tab
stop, or one per handle in the reorder list. None blocks the keyboard while it loads.

### 8.1 `pager`

`tm-pager` shows paging status and controls. Range mode pages through a list (the search page);
item mode steps through single items (the details page).

```ts
declare class TmPager {                               // tm-pager
  readonly mode: InputSignal<'range' | 'item'>;       // range: "1–20 / 55"; item: "3 / 293"
  readonly page: ModelSignal<number>; readonly pageSize: ModelSignal<number>; // range mode; page is 1-based
  readonly pageSizes: InputSignal<readonly number[]>; // empty: no size select
  readonly index: InputSignal<number>;                // item mode, 1-based
  readonly total: InputSignal<number | null>;         // items, both modes; null: unknown (status hidden, next enabled)
  readonly totalIsLowerBound: InputSignal<boolean>;   // a capped count: "1–20 / 1,000+", "52 / 10,000+"
  readonly disabled: InputSignal<boolean>;            // buttons and size select disabled; geometry kept
  readonly size: InputSignal<TmControlSize | undefined>;
  readonly previous: OutputEmitterRef<void>; readonly next: OutputEmitterRef<void>; // item mode; range mode writes page
}
```

- **Status.** A `<span dir="ltr">` with tabular digits and a hidden sentence ("Items 1 to 20 of 55").
- **Buttons.** Previous and next, with mirrored chevrons, named "Previous page" and "Next page", or
  "Previous item" and "Next item" in item mode. Previous is disabled at the start. Next is disabled
  at the end, unless the total is unknown or a lower bound.
- **Page size.** A `tm-select` reading "20 per page".
- **Width reservation.** The status's `min-inline-size`, in `ch`, is the length of "{total}–{total}
  / {total}", or "{total} / {total}" in item mode. The select reserves its widest option. So paging
  never moves the buttons: with 120 items, "1–20 / 120" already holds the room of "120–120 / 120".
- **Clicks while loading.** A click while a page loads writes `page` again; the consumer's
  `resource()` supersedes the earlier request.

### 8.2 `tree`

`tm-tree` renders a tree whose children load on first expansion. It can be selectable and
draggable. The path picker (spec 0029 §11), the column editor (spec 0030 §11) and the studio's
field list (spec 0034 §12) use it.

```ts
interface TmTreeNodeState {                           // what a row template sees about its node
  readonly level: number; readonly expanded: boolean; readonly selected: boolean;
  readonly loading: boolean; readonly leaf: boolean; readonly context: boolean; // context: a context node (§1)
}
declare class TmTree<T> {                             // tm-tree
  readonly nodes: InputSignal<readonly T[]>;          // the roots
  readonly nodeKey: InputSignal<(node: T) => string>; // unique in the tree: 'Parent.Manager.Code'
  readonly label: InputSignal<(node: T) => string>;   // the default rendering and the typeahead text
  readonly children: InputSignal<((node: T) => readonly T[] | Promise<readonly T[]> | null) | undefined>; // null: a leaf
  readonly selection: InputSignal<'none' | 'single' | 'multiple'>; // default 'none'
  readonly selected: ModelSignal<ReadonlySet<string>>; readonly expanded: ModelSignal<ReadonlySet<string>>; // node keys
  readonly context: InputSignal<((node: T) => boolean) | undefined>; // true: a context node, rendered muted
  readonly dragData: InputSignal<((node: T) => TmDragPayload | null) | undefined>; // set: rows are drag sources
  readonly ariaLabel: InputSignal<string>; readonly size: InputSignal<TmControlSize | undefined>;
  readonly activate: OutputEmitterRef<T>;             // Enter, double-click
}
declare class TmTreeNode {}                           // ng-template[tmTreeNode]: row content after the twisty; let-node, let-state="state"
```

- **Rendering.** Nested `ul[ngTree]`, `li[ngTreeItem]` and `ng-template[ngTreeItemGroup]` from
  `@angular/aria/tree`. A group renders only when expanded, so a deep tree costs only what is open.
- **No virtualization.** Consumers show at most hundreds of open nodes; over 500 warn in dev mode.
- **Rows** use the option-row height. Each has a 14 px indent per level and a twisty: the 13 px
  mirrored chevron that expands and collapses, or an empty slot on a leaf. In `multiple` mode the
  row also shows a decorative checkbox glyph.
- **Selection** uses aria's `selectionMode: 'explicit'`. A click selects in `single` and toggles in
  `multiple`; the keys are in §2.1.
- **Lazy children.** `children(node)` runs on first expansion. A pending promise shows a spinner in
  the reserved slot beside the twisty, then the group. Results are cached per key until `nodes`
  changes identity. A rejection collapses the node and announces "Couldn't load {label}".
- **Drag sources.** With `dragData`, each row's content element is a `tmDragSource` (§8.3), not the
  `li`, so a nested item never fires twice. A drag never changes selection or focus.

**Example —** the column editor's available paths (spec 0030 §11):

```html
<tm-tree [nodes]="roots()" [nodeKey]="pathOf" [label]="labelOf" [children]="expandPath" selection="multiple"
         [(selected)]="shownPaths" [ariaLabel]="t('Available columns')" size="md">
  <ng-template tmTreeNode let-node>
    {{ labelOf(node) }} @if (node.target) { <tm-badge>{{ node.target }}</tm-badge> }
  </ng-template>
</tm-tree>
```

### 8.3 `drag-drop`

`drag-drop` is a typed layer over native HTML5 drag and drop. Every drag has a non-drag path to the
same result.

```ts
interface TmDragPayload {
  readonly kind: string;    // what drop targets match against their accepts, such as 'tm.path'
  readonly data: unknown;   // kept in memory; same document only
  readonly text: string;    // written as text/plain; a text field inserts it at its drop caret
  readonly label: string;   // the name used in announcements
}
interface TmDrop { readonly payload: TmDragPayload; readonly event: DragEvent }
declare class TmDragSource {                          // [tmDragSource]
  readonly tmDragSource: InputSignal<TmDragPayload | null>; // null: not draggable
  readonly dragEnded: OutputEmitterRef<{ dropped: boolean }>;
}
declare class TmDropTarget {                          // [tmDropTarget]; class tm-drop-target--over while an accepted drag hovers
  readonly accepts: InputSignal<readonly string[]>;   // payload kinds
  readonly dropped: OutputEmitterRef<TmDrop>; readonly over: Signal<boolean>; // over: an accepted drag hovers
}
declare class TmDragDrop { readonly current: Signal<TmDragPayload | null> } // root service: the drag in flight
```

- **On `dragstart`,** the source sets `effectAllowed = 'copyMove'` and writes two types:
  `text/plain` holds `text`, and `application/x-tellma-drag` holds a drag id.
- **The payload stays in memory** in `TmDragDrop`: `dataTransfer` cannot be read during `dragover`.
- **On drop,** `tmDropTarget` ignores a drop whose drag id is not the current drag, such as one from
  another window or a file. Text fields take the `text/plain` natively, at the drop caret.
- **The non-drag rule (WCAG 2.5.7).** Each consumer offers and tests a non-drag path: the reorder
  handle (§8.4), the path picker's activation (spec 0029 §11.3), the column editor's checkbox (spec
  0030 §11), the studio's "Add to…" (spec 0034 §12.5).

### 8.4 `reorder-list`

`tm-reorder-list` is an ordered list the user rearranges by pointer or keyboard. It also accepts
drops from other sources.

```ts
interface TmReorderMove<T> { readonly item: T; readonly from: number; readonly to: number }
declare class TmReorderList<T> {                      // tm-reorder-list
  readonly items: ModelSignal<readonly T[]>;          // a move writes a new array
  readonly itemKey: InputSignal<(item: T) => string>;
  readonly itemLabel: InputSignal<(item: T) => string>; // the {label} in the handle's name and announcements
  readonly accepts: InputSignal<readonly string[]>;   // external payload kinds; default none
  readonly ariaLabel: InputSignal<string>; readonly size: InputSignal<TmControlSize | undefined>;
  readonly moved: OutputEmitterRef<TmReorderMove<T>>;
  readonly received: OutputEmitterRef<{ payload: TmDragPayload; index: number }>; // the consumer inserts
}
// <ng-template tmReorderItem let-item let-index="index">: row content after the handle
// <ng-template tmReorderEmpty>: the empty list, which is also the drop target
```

- **DOM.** A named `ul role="list"`. Each row starts with a 24 px handle `button` with a grip icon,
  named "Reorder {label}" and described by the instructions string (§10).
- **Pointer.** The whole row drags and lifts (`--shadow-sm`). A 2 px accent line marks the
  insertion point, so rows never reflow mid-drag, for own and accepted external drags alike.
- **Keyboard.** The handle uses §2.1's grab model and keeps focus as the item moves. Each step
  announces through `LiveAnnouncer`, with the strings in §10.

**Example —** Farah moves "Code" from second to third place in a five-item list:

1. On the "Reorder Code" handle she presses Space: "Code picked up. Position 2 of 5."
2. She presses Down: "Position 3 of 5".
3. She presses Space: "Code dropped at position 3 of 5." Escape instead would announce "Move
   cancelled. Code is back at position 2."

### 8.5 `tiles`

`tm-tiles` shows rows as a virtualized lattice of fixed-size tiles, each an image and four text
corners. Below `sm` the lattice becomes a divided list, and in tree mode a single-column tree. Its
members mirror the data grid's (spec 0010 §C.1–§C.9), so the search page swaps grid and tiles over
one state (spec 0030 §8).

```ts
type TmTileText = string | { readonly text: string; readonly numeric?: boolean; readonly badge?: TmBadgeTone }; // object form: tabular digits or a badge
interface TmTileImage { readonly src: string | null; readonly etag?: string | null; readonly alt: string }
interface TmTileLayout<T> {
  readonly image?: (row: T) => TmTileImage;           // absent: no image column; src null: a placeholder glyph
  readonly title: (row: T) => string;                 // top-start, primary weight
  readonly href?: (row: T) => string | null;          // the title renders as <a href> (new tab, copy link)
  readonly topEnd?: (row: T) => TmTileText | null;
  readonly bottomStart?: (row: T) => TmTileText | null;
  readonly bottomEnd?: (row: T) => TmTileText | null;
}
declare class TmTiles<T> {                            // tm-tiles: implements TmGridDriverTarget; fills its host, scrolls inside
  readonly data: InputSignal<readonly T[]>; readonly rowId: InputSignal<(row: T) => TmRowId>;
  readonly layout: InputSignal<TmTileLayout<T>>; readonly ariaLabel: InputSignal<string>;
  readonly loading: InputSignal<boolean>; readonly selectable: InputSignal<boolean>;
  readonly selectedIds: ModelSignal<ReadonlySet<TmRowId>>; // a fresh Set on every change; ids not in data are kept
  readonly activeRowId: ModelSignal<TmRowId | null>;  // arrows move it; the page may move it while focus stays in its field
  readonly rowClass: InputSignal<((row: T) => string | null) | undefined>;
  readonly parentId: InputSignal<((row: T) => TmRowId | null) | undefined>; // set: tree mode
  readonly hasChildren: InputSignal<((row: T) => boolean) | undefined>;
  readonly expandedIds: ModelSignal<ReadonlySet<TmRowId> | undefined>;
  readonly loadChildren: InputSignal<((rows: readonly T[]) => Promise<void>) | undefined>; // batched; the consumer appends rows
  readonly rowActivate: OutputEmitterRef<TmGridRowActivation<T>>; // the grid's payload (import type)
  moveActiveRow(by: number | 'first' | 'last'): void; // by lattice lines, keeping the column; clamped
  activateRow(rowId?: TmRowId, options?: { newTab?: boolean }): boolean; // emits rowActivate { via: 'keyboard' }
  scrollToRow(rowId: TmRowId, options?: { flash?: boolean; activate?: boolean; align?: 'nearest' | 'center' }): boolean;
  scrollOffset(): number; scrollTo(offset: number): void; focus(): void; // offsets: page-state restore
}
// ng-template[tmTileContent] replaces a tile's body at the same size; [tmTilesEmpty] and [tmTilesLoading] as in the grid
```

- **Tile.** 60 px tall (a 40 px image, 9 px padding, a hairline), radius 0, card fill.
- **Columns.** From a container width `W` of 576 px, columns = `max(1, floor((W + 10) / 230))` with
  a 10 px gap: 1,000 px gives 4. Below 576 px, and always in tree mode, tiles form one column with
  `--divider` lines. The count changes only on resize; `scrollbar-gutter: stable`.
- **Corners.** The title is 13 px 500, ellipsized. Top-end is tabular when `numeric` and stays at
  the physical right in RTL, as grid numbers do. Bottom-start is 12 px secondary; bottom-end is a
  `tm-badge` when `badge` is set. Corners are static DOM; `tmTileContent` costs a view per tile.
- **Virtualization.** Lattice lines are windowed by `tmComputeAxisWindow` from
  `@tellma/core-ui/grid-engine`, the grid's own windowing, at a 70 px pitch with three lines of
  overscan. The active row is always rendered. `@for` tracks tiles by `rowId`.
- **Activation.** A click or tap anywhere but the checkbox emits `rowActivate`, with `newTab` for a
  Mod-click or a middle click. A modified click on the title link follows the link. A repeat
  within 400 ms is ignored (spec 0010 §C.1).
- **Selection.** Selectable tiles reserve a checkbox at inline-start, which toggles selection only.
  Each change announces "{count} of {total} selected".
- **ARIA, flat.** `role="grid"` with `aria-rowcount` and `aria-colcount`; lattice lines are
  `role="row"`; tiles are `role="gridcell"` with `aria-colindex`, plus `aria-selected` when
  selectable. One tab stop; the title link is `tabindex="-1"`.
- **ARIA, tree mode.** `role="treegrid"`, one cell per row with `aria-level`, `aria-expanded`,
  `aria-posinset` and `aria-setsize`. Each row has a 14 px indent and a twisty.
- **Tree loading** follows the tree grid (spec 0010 §C.9). Expanded rows without children load in
  one batched `loadChildren` call. Each loading row has a reserved spinner slot, and a rejection
  collapses the row and announces.
- **The driver.** Through `import type`, `tm-tiles` implements `TmGridDriverTarget` (spec 0010
  §C.2), so `<input [tmGridDriver]="tiles">` moves the active tile from the search field.
- **Loading and empty.** `loading` without data shows skeleton tiles; with data it sets
  `aria-busy`. No data shows `tmTilesEmpty`, else "No items to display".
- **Flash.** `flash` tints the tile (`--tiles-flash-bg`) for 1.2 s, as the grid does.
- **Built-in `rowClass` values:** `tm-tiles__item--context` and `tm-tiles__item--marked`.

**Example —** spec 0030 §2.3 maps the declaration `tile: { image: 'Image', title: 'Name', subtitle:
'Code', badge: 'IsActive' }` to this layout for Acme's customers:

```ts
const layout: TmTileLayout<AcmeCustomerRow> = {
  image: (c) => ({ src: c.imageId ? urls.blob('customer-image', c.imageId, { variant: 'thumb' }) : null, alt: c.name }),
  title: (c) => c.name, href: (c) => urls.details('acme.Customer', c.id), bottomStart: (c) => c.code,
  bottomEnd: (c) => ({ text: c.isActive ? t('Active') : t('Inactive'), badge: c.isActive ? 'success' : 'neutral' }),
};
```

## 9. Tokens

Each component adds one component token group to `presets/tellma-default.ts`, the only home of its
values. Colours reference semantic roles, so dark mode and theme deltas need nothing new.

- **Groups,** each emitting `--<group>-*`: `icon`, `kbd`, `badge`, `counter`, `avatar`, `chip`,
  `progress`, `skeleton`, `toast`, `banner`, `emptyState`, `busyOverlay`, `radio`, `segmented`,
  `switch`, `card`, `pageHeader`, `descriptionList`, `splitter`, `pager`, `tree`, `reorderList`,
  `dropTarget`, `tiles`.
- **Values** snap to the 4 px grid. The sizes stated in §3–§8 are the values the preset holds.
- **The `categorical` semantic group** (spec 0010 §B.8) holds eight tones, each with a value per
  scheme. Avatars read its `tint` and `onTint` (§4.2). Charts read its `mark` (spec 0034 §7.7).
- **Two colour literals,** `busyOverlay.scrim` and `dropTarget.overBg`, are translucent and the
  same in both schemes.
- **Naming.** Names are written as they emit: `paddingXSm` emits `--…-padding-xsm`.
- **What a consumer may set:** `--icon-size` (containers), `--icon-stroke-width` (a host that needs
  a heavier glyph, §3.1), `--counter-halo` (the counter's host), `--toast-offset-block-start` (the
  shell) and `--busy-overlay-delay`.
- **What a consumer may read:** the `--chip-*` group, which the launcher's chip-shaped options use
  (spec 0027 §9). Every other component token is internal.
- **Checks.** `tokens:check` and the axe contrast battery cover the groups in both schemes.

## 10. Strings

Every built-in string is a `tmUi` key resolved through `TM_UI_TRANSLATE`: English in
`TM_UI_STRINGS_EN`, Arabic in `@tellma/locale-ar`. The parity gate fails on a missing twin. Text a
consumer passes in arrives localized. Messages are ICU (`@jsverse/transloco-messageformat`); Arabic
is unvocalized.

| Area | Keys (`tmUi.`) and their English text |
|---|---|
| Copy | `copy.copy` "Copy" · `copy.copied` "Copied" · `copy.manual` "Press {keys} to copy" · `reference.label` "Reference" · `reference.copy` "Copy reference" |
| Counter, chip | `counter.overflow` "{max}+" · `chip.remove` "Remove {label}" · `chip.more` "{count} more: {labels}" |
| Toast, busy overlay | `toast.region` "Recent messages" · `toast.close` "Close" · `busyOverlay.label` "Please wait" |
| Banner | `banner.dismiss` "Dismiss" · `banner.previous` "Previous message" · `banner.next` "Next message" · `banner.position` "{index} of {count}" |
| Dialogs, splitter | `confirm.ok` "OK" · `confirm.cancel` "Cancel" · `splitter.label` "Resize panels" |
| Error dialog | `errorDialog.close` "Close" · `errorDialog.retry` "Try again" · `errorDialog.details` "Details" · `errorDialog.more` "and {count} more" |
| Search, date range | `searchField.placeholder` "Search" · `searchField.clear` "Clear search" · `dateRange.from` "From" · `dateRange.to` "To" |
| Validation | `errors.dateRangeOrder` "The end date must be on or after the start date" |
| Pager status | `pager.range` "{from}–{to} / {total}" · `pager.rangeAtLeast` "{from}–{to} / {total}+" · `pager.item` "{index} / {total}" · `pager.itemAtLeast` "{index} / {total}+" |
| Pager sentences | `pager.rangeSentence` "Items {from} to {to} of {total}" · `pager.rangeSentenceAtLeast` "Items {from} to {to} of {total} or more" · `pager.itemSentence` "Item {index} of {total}" · `pager.itemSentenceAtLeast` "Item {index} of {total} or more" |
| Pager controls | `pager.previousPage` "Previous page" · `pager.nextPage` "Next page" · `pager.previousItem` "Previous item" · `pager.nextItem` "Next item" · `pager.pageSize` "Page size" · `pager.perPage` "{size} per page" |
| Tree | `tree.loading` "Loading" · `tree.loadFailed` "Couldn't load {label}" |
| Reorder handle | `reorderList.handle` "Reorder {label}" · `reorderList.instructions` "Press Space to pick up. Use the arrow keys to move, Space to drop, and Escape to cancel." |
| Reorder announcements | `reorderList.grabbed` "{label} picked up. Position {index} of {count}." · `reorderList.position` "Position {index} of {count}" · `reorderList.dropped` "{label} dropped at position {index} of {count}." · `reorderList.cancelled` "Move cancelled. {label} is back at position {index}." |
| Tiles | `tiles.empty` "No items to display" · `tiles.select` "Select {label}" · `tiles.selected` "{count} of {total} selected" · `tiles.loadFailed` "Couldn't load {label}" |

- **Digits** in parameters are formatted by the ICU transpiler for the formatting locale (`ar-SA`
  gives Arabic-Indic, `ar-SA-u-nu-latn` Latin), so no template carries digits of its own.
- **No plural forms.** Every count sits in a position or ratio phrase ("{count} of {total}"), which
  reads correctly for every Arabic plural category.

## 11. Budgets, harnesses, docs and stories

Each entry point ships its §1 budget and harnesses, co-located `*.examples.ts`, its
`components.json` entry and a showcase story with the entry point's id.

- **Budgets** are §1's ceilings in `"tellma".budgetsInKb` (spec 0002 §8), measured by `size:check`;
  a raise needs a `budgetNotes` entry. API goldens cover every new entry point (`api:check`).
- **Eager weight.** The shell's eager entry points (`icon`, `badge`, `avatar`, `kbd`, `progress`,
  `toast`, `banner`, `skeleton`) total 14 KB of the app's initial budget (spec 0024 §12). The rest
  load with their routes. The icon data `@tellma/core` registers counts in spec 0024's budget.
- **Harnesses** live in `@tellma/core-ui-testing` (TestBed only).
- **Stories** show every state a section names, in both themes and both directions.
- **Docs.** JSDoc carries `@tmGroup` and `@tmA11yNotes` (§2.1's keys). Public text never cites a
  spec, and `docs:build` fails on "§".

## 12. Testing

Seven suites pin this spec, and every one runs on every pull request.

| Suite | Pins |
|---|---|
| Unit (vitest, browser mode) | `tmInitials` across scripts; `tmAvatarTone`'s fixed vectors; `tmFormatKeys`, `tmKeysText`, `tmKeysMatch` and `tmAriaKeyShortcuts` on both platforms, with `tmKeysMatch` vectors for an Arabic layout (`Mod+D`, `Mod+Shift+D` with `key` `']'`, `Mod+/`), Option on Apple, Polish AltGr+A not matching `Mod+Alt+A`, and `Mod+Alt+Plus` from the numpad and the main keys; refused icon attributes; `tm-icon` drawn after the first change detection; the splitter clamp; the tiles lattice (columns per width, line windows, moves on a short last line); search timing with fake timers (leading and trailing edges, held key, Enter, IME, equal values); the progress bar's 250 ms hold; the toast queue (max visible, replace by key, pause and resume, sticky error); chip collapse; pager width reservation; page-header width release on resize |
| Harness (TestBed) | each §1 harness: Signal Forms binding for radio, segmented, switch and date range (order error, bound coupling); tree lazy load, selection modes, `activate`; reorder grab, move, drop, cancel, `moved`; tiles selection, external `activeRowId` moves, tree-mode batched `loadChildren`; `ask` per dismissal path; the error dialog's item cap and in-place replacement; busy-overlay nesting, live label and `immediate` |
| Playwright on the showcase | §2.1's table in LTR and RTL; a live English → Arabic switch; a native drag of a tree node into a textarea (inserted at the drop point) and into a reorder list in Chromium, Firefox and WebKit (`@cross-engine`); reorder announcements; toast timers pausing on hover and focus, `focus()` and Escape; the busy overlay blocking keys and pointer with `cursor: progress` and the trigger busy in the click's frame, the scrim at 200 ms, or at once with `immediate`, then restoring focus; tiles at 10,000 rows with no long task over 50 ms while scrolling; §13's bounding-box specs; the copy fallback with clipboard permission denied |
| axe | every story state in light and dark, LTR and RTL, toasts and dialogs open, trees collapsed and expanded, avatars in every tone |
| Forced colors, reduced motion | `emulateMedia`: selected tile, active segment, switch on, counter tone, drop target and a tinted avatar's circle visible under `forced-colors: active`; sweep, ring and knob static under `reduce` |
| Touch (Pixel 7 project) | a tile tap activates, a checkbox tap selects; splitter drag; segmented and switch targets at least 24 px; resolved sizes at 390 px |
| Gates | `size:check`, `api:check`, `docs:build`, `tokens:check`, the English–Arabic parity gate |

## 13. Definition of done

- **Placement.** Twenty-nine entry points build, lint and fit §1's budgets, with API goldens,
  `components.json`, `llms.txt`, MCP entries, stories, and every §10 key in English and Arabic.
- **Instant warm launch.** Nothing awaits network or storage; the eager set is 14 KB (§11); skeletons
  give final geometry. Pinned by `size:check` and spec 0024's warm-launch e2e (every request held).
- **Instant feedback.** Models update in the event's handler, and nothing animates. The busy
  overlay blocks in the click's task, with a progress cursor beside the caller's busy trigger
  (§5.4). The search field fires on the first key, so a consumer's progress bar shows in that frame
  (§6.2). Pinned by a keydown-to-paint probe per story, the overlay spec and the search-field timing
  spec.
- **Non-blocking.** The search field never awaits; the pager writes `page` mid-load; tree and tiles
  loads never block keys. Pinned by the search-field timing spec and the pager double-click spec.
- **Stable layout** (§2.2). Pinned by bounding-box specs: counter 9 → 10, pager 1 → 6 of 120, first
  keystroke, toast, chip added, copy success, lazy spinner, tiles data, hidden header action, and a
  glyph present in its control's first frame.
- **RTL.** Pinned by every story spec in both directions and a live English → Arabic switch spec
  for radio, segmented, splitter, tree and tiles.
- **WCAG AA.** §2.1's patterns, 24 px targets, non-drag paths, paused timers, persistent live
  regions. Pinned by axe, the keyboard specs, and the forced-colors and reduced-motion specs.
- **Page context.** The library publishes nothing to the page-context registry. Its state lives in
  models (`selectedIds`, `activeRowId`, `expandedIds`, `value`) that the page frameworks publish
  (spec 0026 §9.3).
- **Telemetry and Acme.** No telemetry; dev-mode warnings only. Acme adds one line, the `hand-coins`
  icon (§3.1).

## Decisions record

1. **One domain-free entry point per component family; one new semantic token group,
   `categorical`** — per-route budgets stay precise; the brand roles cover every surface except
   per-item tones, which avatars and charts share (§1, §9).
2. **Lucide data in a runtime registry, drawn with `createElementNS`, sized by `--icon-size`** — no
   Trusted Types policy, no size per call site; a sprite downloads the whole set (§3.1).
3. **`kbd` owns the grammar and matches letters, digits and US punctuation physically when needed,
   never under AltGr** — one grammar and one platform check for registry, menus and grid;
   `event.key` alone fails on Arabic layouts, and AltGr types characters (§3.2).
4. **Chips are `tm-chip` and `tm-chip-set` only** — `tm-multi-select` renders joined text and the
   multi-picker composes `tm-chip` in its own scrolling row (spec 0010 §B.4, §E.2); a free-entry
   chip input has no consumer (§4.3).
5. **Relative time renders `TmL10n.formatRelativeTime` with a `style` input** — one format owner;
   "now" is Intl's text; the component owns only the ticker (§4.5).
6. **Static skeletons with a `table` shape** — nothing moves while the user waits (§4.4).
7. **Toasts: a top-layer host, two persistent live regions, no focus, no animation** — above modals
   and reliably announced; a `status` role per toast double-announces (§5.1).
8. **The banner bar is not a live region** — the host announces at queue time, once and early (§5.2).
9. **The busy overlay inerts at once, with a progress cursor beside the caller's busy trigger, and
   shows its scrim after 200 ms unless `immediate`** — feedback and blocked input from the first
   frame, and no full-page blink on a fast save; a caller with no visible trigger gets the scrim at
   once; a pointer-only blocker lets keys through (§5.4).
10. **Confirm and error dialogs are alert-dialog `TmModal` contents behind promise services** — one
    modal stack; a second error replaces the open dialog's content (§5.5, §5.6).
11. **Native radios with an own arrow handler; a native checkbox switch** — native form semantics,
    arrows right in RTL in every engine; aria has no radio group (§6.1).
12. **The description list is a directive on a native `dl`** — plain HTML keeps the list roles;
    projected items break the content model (§7.1).
13. **Reserved widths: copy labels, page-header slots until a resize, the pager's widest status** —
    nothing moves on click; one pager `total` serves ranges and items (§3.3, §7.2, §8.1).
14. **The tree is `@angular/aria/tree`, DOM-rendered, not virtualized** — aria covers the APG
    keyboard, typeahead and RTL; consumers show at most hundreds of open nodes (§8.2).
15. **Native HTML5 drag and drop with a typed payload and a non-drag path for every drag** — a
    native `text/plain` drop inserts at the caret, which CDK drag-drop cannot (§8.3).
16. **The reorder list grabs, moves and drops on a handle** — the established accessible model;
    Alt+Arrow alone is undiscoverable, so it stays a shortcut (§8.4).
17. **Tiles are a virtualized layout grid with the grid's names, payload and driver** — one tab stop
    survives virtualization, and grid and tiles swap behind one handler (§8.5).
18. **Motion only in the switch knob and progress; toasts at the top inline-end, newest first,
    three visible, paused on hover or focus, errors sticky** — instant feedback (§2.2, §5.1).
19. **Every library glyph draws through `tm-icon`, at stroke 1.75 by default, in the pass that
    creates it** — one stroke, one size token and one RTL rule instead of hand copies;
    `tm-spinner` and the grid's per-cell masks keep their own drawing (§3.1).
20. **Avatar tones hash a record key into eight `categorical` tints** — people with the same
    initials become distinguishable; a record key is stable, private and on every row, unlike an
    email or a `sub`; a person's tone may differ per tenant (§4.2).
21. **The search field fires on the leading edge and again 50 ms after a burst; the progress bar
    follows a stale result and holds 250 ms** — the first key searches at once, as the picker does,
    and the bar neither waits nor blinks between keys (§4.4, §6.2).

## Review flags

1. **Native HTML5 drag and drop** (§8.3). Alternative: CDK drag-drop for lists. Flip: a need for
   touch drag on Samsung Internet or Firefox for Android, which lack native drag.
2. **`lucide`, the v0 vanilla package, as icon data** (§1). Alternative: pin `@lucide/icons` v1.
   Flip: v1 shipping stable before implementation starts.
3. **The busy overlay's scrim after 200 ms when the caller shows a busy trigger** (§5.4).
   Alternative: a faint veil with the spinner and label at once, and the full scrim at 200 ms.
   Flip: users missing a button's spinner as the only signal of a short wait.
4. **Toasts at the top inline-end, under the shell bars** (§5.1). Alternative: bottom inline-end, as
   Material and Sonner. Flip: users missing toasts, or toasts covering page toolbars.
5. **Tiles as a layout grid with one tab stop** (§8.5). Alternative: a list of links. Flip:
   screen-reader testing that shows grid semantics confuse tile browsing.
6. **`tm-date-range` as two coupled pickers** (§6.3). Alternative: a dual-month range popup. Flip:
   report parameter bars needing fast range selection.
7. **Static skeletons** (§4.4). Alternative: a subtle pulse. Flip: users reading static bars as
   broken content.
8. **The pager status forced LTR in both languages** (§8.1). Alternative: an Arabic RTL template
   ("١–٢٠ من ٥٥"). Flip: Saudi users finding the LTR fraction unnatural.
9. **The description list as a `dl` directive** (§7.1). Alternative: a component with an `items`
   input. Flip: distribution code misusing raw `dl` markup.
10. **Tiles take the grid's member names** (§8.5). Alternative: item names (`itemId`, `activeId`,
    `scrollToItem`). Flip: a preference for local naming over one swap contract with the grid.
11. **The search field's leading edge** (§6.2). Alternative: trailing only, which saves one
    cancelled read per burst of keys. Flip: server load from the extra reads.
