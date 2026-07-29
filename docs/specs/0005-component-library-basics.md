# Spec: UI Component Library — Basics

- **Author:** Ahmad Akra
- **Date:** 23 July 2026

**Status:** Frozen **historical** record of the design and its reasoning at authoring time. It is not
updated as the code or its dependencies evolve.

## Context

Phase 3 of the Tellma component library delivers the everyday building blocks around the forms
foundation ([spec 0002](0002-component-library-foundation.md)) and the data grid
([spec 0004](0004-component-library-datagrid.md)):

| Group | Components |
|---|---|
| Control | `tmButton` |
| Forms | textarea (via `tmInput`), `tmNumber`, `tm-date-picker`, `tm-image`, `tmFilePicker` + `tm-dropzone`, `tm-file-preview` |
| Layout & feedback | `tm-tab-group`/`tm-tab`, `tm-modal`, `tm-popover`, `tmTooltip`, `tm-alert` |

All foundation decisions apply unchanged: Angular v22+, zoneless, signal-first, Signal Forms only,
CSS logical properties + CDK `Directionality` for RTL, Transloco behind `TM_UI_TRANSLATE`,
token-driven styling with the `@layer tm.base, tm.theme` cascade, static inline-SVG glyphs, the
showcase app + vitest + Playwright pipeline, per-entry-point budgets and API goldens, worktree-
isolated port-free tooling. Components embed in the grid through the hardened
`TmCellEditor`/`TmCellDisplay` contracts and `TM_CELL_EDITOR_HOST` self-registration of spec 0004.
Implementers must use the Angular CLI MCP (`get_best_practices`, `search_documentation`) rather
than memory for framework conventions, and verify `@angular/aria`/CDK APIs against the installed
types (angular.dev docs are known to run ahead of npm).

Two spec-0004 promissory notes come due here: the grid's internal number codec becomes the shared
number formatter/parser (no grid API change), and `tm-date-picker` becomes the grid's built-in
`date` column editor with built-in format/parse defaults.

## Goals / Non-goals

**Goals**

- Ship the twelve components above to production quality: a11y-complete (WCAG 2.1 AA),
  RTL-complete, brand-themed, Signal-Forms-native where form-bound, harness-tested, budgeted.
- Establish the shared machinery they need once: a private overlay-composition helper, an `l10n`
  entry point (number + date formatting/parsing, the calendar seam), pluggable calendar systems
  (Gregorian built-in; Umm al-Qura and Ethiopic as opt-in entry points), and a client-cache
  registry with a global clear hook.
- Migrate the three existing overlay wirings — `tm-select`, `tm-menu`, and the grid's error
  overlay — onto the shared overlay helper: a mechanical consolidation of already-proven wiring,
  gated on the existing suites passing unchanged.
- Upgrade the grid's `date` column type from consumer-supplied `format`/`parse` to built-in
  defaults, with `tm-date-picker` as its editor; add a date column to the grid showcase.
- Extend `@tellma/locale-ar` with every new built-in string.

**Non-goals (explicitly out of scope)**

- Currency input (`TmCurrencyAdapter`), entity picker, time / date-time / date-range pickers, and
  date values coarser than a day — a month or a year does not map 1:1 onto the months of a
  non-Gregorian calendar, so the value stays `YYYY-MM-DD` throughout.
- Toast/snackbar notifications, drawer/side panel, accordion, wizard/stepper, segmented
  (per-part) date editing, week numbers, in-picker calendar-system switcher UI.
- Anchor tags styled as buttons (`a[tmButton]`) — deferred until a consumer needs one.
- Textarea autosize — it conflicts with the size-stability rule (components never change size
  over their lifetime); rows are fixed per instance.
- Upload transport, progress UI, retry/queue management: the file components emit `File`/`Blob`
  objects and metadata; sending them (multipart per §8.3) is the distribution's concern.
- Server-side image processing (format standardization, crop to rect, renditions) — the
  component's output contract (§7.5) is designed for it, but the backend is out of scope.
- Image rotation/annotation editing; multi-image galleries.

## 1. Packages & entry points

Everything lives in `@tellma/core-ui` as secondary entry points (per-component entry points and
budgets, as before):

| Entry point | Contents |
|---|---|
| `@tellma/core-ui/button` | `tmButton` directive. |
| `@tellma/core-ui/input` | Gains `textarea[tmInput]` (§4); no new entry point. |
| `@tellma/core-ui/number` | `tmNumber` directive (uses `l10n`). |
| `@tellma/core-ui/date-picker` | `tm-date-picker` + the calendar popup views (uses `l10n`, `private`). |
| `@tellma/core-ui/calendar-umalqura` | The Umm al-Qura `TmCalendar` provider (§6.5). |
| `@tellma/core-ui/calendar-ethiopic` | The Ethiopic `TmCalendar` provider (§6.5). |
| `@tellma/core-ui/l10n` | Number codec (hoisted from the grid), date format/parse engine, the `TmCalendar` seam + Gregorian implementation, week-info helper (§2.2). |
| `@tellma/core-ui/image` | `tm-image` viewer/editor + the image blob cache (§7). |
| `@tellma/core-ui/files` | `tmFilePicker`, `tm-dropzone` (§8.1–8.2). |
| `@tellma/core-ui/file-preview` | `tm-file-preview` (§8.4; uses `modal`). |
| `@tellma/core-ui/tabs` | `tm-tab-group` + `tm-tab` on `@angular/aria/tabs`. |
| `@tellma/core-ui/modal` | `TmModal` service + `TmModalRef` on `@angular/cdk/dialog`. |
| `@tellma/core-ui/popover` | `tm-popover` + `tmPopoverTriggerFor` (uses `private`). |
| `@tellma/core-ui/tooltip` | `tmTooltip` directive (uses `private`). |
| `@tellma/core-ui/alert` | `tm-alert`. |
| `@tellma/core-ui/private` | Shared overlay-composition helper (§2.1). Importable but carries no stability guarantees, and excluded from the API goldens through a new per-package `"tellma".apiGoldens.exclude` list the extractor script honors — the `@angular/aria` `private`-entry-point pattern spec 0002 reserved. |
| `@tellma/core-ui` (primary) | Gains `TmClientCache` (§2.3), the `TmL10n` reactive formatting facade and the `TM_CALENDAR` token + `provideTmCalendar()` (§2.2), and the new validator/message kinds' English strings. Stays component-free. |
| `@tellma/core-ui-testing` | New harnesses (§17). |

**New third-party dependency:** `@internationalized/date` (Adobe; Apache-2.0; `sideEffects: false`;
one runtime helper dep) supplies calendar arithmetic and the Gregorian↔Umm al-Qura/Ethiopic
conversions behind the `TmCalendar` seam (§2.2). Rationale in §6.5. It is a regular `dependencies`
entry of the library package (declared in ng-packagr's `allowedNonPeerDependencies`); calendar
classes are imported directly (never via its `createCalendar` factory, which defeats tree-shaking).

## 2. Shared infrastructure

### 2.1 `private/` — the overlay-composition helper

`tm-select`, `tm-menu`, and the grid's error overlay each wire `cdkConnectedOverlay` + the Popover
API independently today. This phase adds three more anchored overlays (popover, tooltip, date
picker), so the proven composition is extracted once into `@tellma/core-ui/private`:

- A `tmCreateAnchoredOverlay(config)` helper owning the settled wiring from spec 0002 §3.4: lazy
  creation, `usePopover: 'inline'` (top-layer, clipping-escape) with a `{ type: 'parent', element }`
  override for hosts where sibling insertion violates ARIA structure (the spec-0004 grid lesson),
  `disableClose: true` (the consumer owns Esc), logical `[bottom-start, top-start]`-style position
  sets resolved through `Directionality`, `matchWidth` opt-in, an optional keep-on-screen push for
  surfaces whose alignment is a preference rather than a meaning, and a re-measure strategy for
  flip measurement: the `updatePosition()`-on-attach macrotask fix, or `afterNextRender` where the
  anchor itself is written from a render effect and reaches CDK only on the next pass (`tm-menu`
  and the grid's error overlay need that one). Each consumer keeps its own
  `<ng-template cdkConnectedOverlay>` and open state — the helper centralizes the config and the
  measurement, not the template.
- Consumers in this phase: `tm-popover`, `tmTooltip`, `tm-date-picker` — and the three existing
  wirings (**`tm-select`, `tm-menu`, and the grid's error overlay**) migrate onto it,
  consolidating identical behavior behind one function; every existing overlay spec passes
  unchanged as the migration gate.

### 2.2 `l10n/` — number & date codecs, the calendar seam

A new runtime entry point for locale-sensitive formatting/parsing. Everything here is pure
TypeScript + `Intl` (+ `@internationalized/date` for calendar math): no DOM, no DI, no components
— enforced by the same boundary lint that guards `contracts` and `grid-engine`.

**Number codec — hoisted, not rewritten.** The grid's `tm-number-codec.ts` (`tmFormatNumber`,
`tmParseNumber`: `Intl.NumberFormat` formatting; parsing with symbols derived via `formatToParts`,
so localized separators, non-Latin numerals, and format-control marks round-trip) moves here
behavior-intact; the one signature change is folding the fraction-digit arguments into an options
bag and adding `percent`:

```ts
export interface TmNumberFormatOptions {
  minDecimals?: number;   // default 0
  maxDecimals?: number;   // default 20 ("as many as needed"); floored at minDecimals
  percent?: boolean;      // default false — format fraction as percentage, parse percentage to fraction
}
export function tmFormatNumber(value: unknown, locale: string, options?: TmNumberFormatOptions): string;
export function tmParseNumber(text: string, locale: string,
  options?: TmNumberFormatOptions & { sourceLocale?: string }): number | null | TmParseError;
```

The grid deletes its internal copy and imports these — the spec-0004 "replaced internally when the
adapter ships, no API change" note, honored: identical behavior, same `TM_PARSE_ERROR` sentinel
from `contracts`. In percent mode, formatting uses `Intl.NumberFormat` `style: 'percent'`
(`minDecimals`/`maxDecimals` bound the *displayed percent* digits), and parsing strips one
percent sign (`%`, `٪` U+066A, `％` U+FF05) at **either end** — several locales (Turkish,
Basque) format with a leading percent sign, and formatted output must round-trip — then parses
the remainder as a plain number and divides by 100: typed `75`, `75%`, and `%75` all parse to
`0.75`; a bare number is never treated as an already-scaled fraction.

**Date engine.** ISO 8601 date strings are the value currency (§6.1). The engine formats and
parses them per locale × calendar:

```ts
export type TmDateStyle = 'numeric' | 'medium' | 'long';
export function tmFormatDate(iso: string, locale: string,
  options?: { calendar?: TmCalendar; dateStyle?: TmDateStyle }): string;
export function tmParseDate(text: string, locale: string,
  options?: { calendar?: TmCalendar; today?: string }): string | null | TmParseError;
export function tmDatePlaceholder(locale: string,
  options?: { calendar?: TmCalendar }): string;  // e.g. "dd/mm/yyyy"
```

Formatting delegates to `Intl.DateTimeFormat` with the calendar's Intl identifier; month names
come from Intl/CLDR at runtime, so **no name tables ship in any package**, and every calendar
renders localized in every UI language. `dateStyle` (one name, shared with the `tm-date-picker`
input) maps to a fixed CLDR skeleton — `yMd` / `yMMMd` / `yMMMMd` — fixed in code, not
configurable.

**The era suffix is stripped.** Intl appends one to non-Gregorian calendars whether or not it is
asked for (`9/16/1447 AH`), but inside a field whose calendar the user chose there is only ever
one era in play, so the suffix is noise that costs width in every cell and input. It is removed
from the formatted parts along with the literal that joined it, keeping one separator where a
locale places the era *between* two fields.

**Cross-platform formatting contract.** Formatted values also originate server-side (validation
messages, documents), so the option surface is deliberately a small closed set — number:
locale × `minDecimals`/`maxDecimals`/`percent`; date: locale × calendar × style —
that a C# implementation can reproduce: the date styles are the CLDR skeletons above, and number
formatting is CLDR-locale digits/separators with fraction-digit bounds and half-away-from-zero
rounding (Intl `halfExpand` ↔ .NET `MidpointRounding.AwayFromZero`; .NET has been ICU-backed
since .NET 5, so both ends draw on the same CLDR data). The contract is *consistent output for
the same parameters*, not byte equality (platforms differ in invisible spacing/bidi marks).
Two rules make numeric parity hold despite the client's IEEE-754 `number` values: the
**single-rounding rule** — a value is rounded once, in one engine, at the scale it will be
shown (the client's commit rounding for user input, §5; a `decimal` rounding on the server for
derived values, *before* they are embedded in a message or sent), and is thereafter only
formatted at the scale it carries, so no midpoint is ever adjudicated twice (the same `x.xx5`
rounds differently as a binary double than as an exact decimal); and the
**15-significant-digit envelope** (§5), inside which every client number is exactly the decimal
the server parses.
It is pinned by a **committed golden** — `format-golden.json` in `l10n`, rows of
(locale, calendar, dateStyle, iso → expected) and (locale, minDecimals, maxDecimals,
percent, value → expected) — asserted by the client suite and consumed verbatim by the future
C# implementation's tests; CLDR-driven drift on engine updates is an explicit, reviewed
regeneration, like an API golden.
Documented backend caveats: .NET ships no Ethiopic calendar (backend Ethiopic formatting needs an
ICU-based implementation), and .NET's `UmAlQuraCalendar` covers a narrower range (1900–2077 CE)
than the ICU tables the client uses.

**The calendar seam.** Display calendars are pluggable; the backing value is always ISO (proleptic
Gregorian):

```ts
export interface TmCalendarParts { year: number; month: number; day: number; }  // 1-based, in-calendar
export interface TmCalendar {
  readonly id: string;                    // Intl calendar identifier: 'gregory', 'islamic-umalqura', 'ethiopic'
  toParts(iso: string): TmCalendarParts;
  fromParts(parts: TmCalendarParts): string | null;   // null = not a real date in this calendar
  monthsInYear(year: number): number;                 // 13 for ethiopic
  daysInMonth(year: number, month: number): number;
  today(): string;                                    // ISO, local time zone
}
```

`l10n` ships the Gregorian implementation; the **DI seam lives in the primary entry point**,
keeping `l10n` genuinely DI-free: `TM_CALENDAR: InjectionToken<Signal<TmCalendar>>` (the
app-ambient display calendar, defaulting to Gregorian) and
`provideTmCalendar(calendar: TmCalendar | Signal<TmCalendar>)`. The interface exists so the
library's public surface never leaks its backing arithmetic (§6.5), and so native `Temporal` can
replace it later without an API change. Dates are pure calendar dates — no time-of-day, no time
zone; `today()` is the user's local date.

**Week info.** `tmFirstDayOfWeek(locale)` resolves via `Intl.Locale.prototype.getWeekInfo()` where
available, with a small region fallback table — the day-grid's column order (§6.4).

**Reactive facade.** The `l10n` functions are pure and public — the standalone path for any
readonly display of a number or date (a `computed()` feeding a span). For template ergonomics the
**primary entry point** adds a root `TmL10n` service exposing `formatNumber`/`formatDate` bound
to the active locale and `TM_CALENDAR` signals, so consumer templates re-render on locale or
calendar switch. No pipes: a pure pipe would not re-evaluate on locale change and an impure one
re-runs every change-detection pass — the signal-bound facade gives the reactivity without the
cost.

### 2.3 `TmClientCache` — the global cleanup hook

Persistent client-side state follows one rule instead of a registration protocol: **every
browser store a library feature creates is named with a `tm-` prefix** (this phase: the
`tm-images-v1` CacheStorage cache; the same convention will bind any future IndexedDB database
or web-storage key). A root `TmClientCache` service in the primary entry point exposes the one
operation distributions need:

```ts
@Injectable({ providedIn: 'root' }) class TmClientCache {
  clearAll(): Promise<void>;   // best-effort, parallel, never throws
}
```

`clearAll()` enumerates and deletes every `tm-`-prefixed store (`caches.keys()` today;
`indexedDB.databases()` and prefixed web-storage keys the day a feature first uses them).
Convention replaces registration deliberately: there is nothing to register, no init-order
dependency, and state left by features that never loaded this session — or by prior sessions —
is swept all the same. In-memory state needs no hook (it dies with the session), and a
feature needing custom cleanup logic beyond store deletion would be the moment to add one.

A distribution calls `clearAll()` on logout. **Tenant switching clears nothing:** a
distribution hosts multiple tenants on one origin, so every cache key must carry tenant
identity — for the image cache the full request URL does, since the tenant id is part of every
API URL (§7.2) — making entries tenant-scoped by construction; a signed-in user switching
tenants keeps only caches they are authorized for anyway, and logout sweeps all of them.
Features must treat their caches as re-populatable accelerators — `clearAll()` mid-flight is
always safe.

## 3. Button — `tmButton`

- **Selector:** `button[tmButton]` — a directive on the native `<button>`; the native element is
  the control (semantics, focus, activation, forms participation for free).
- **API:** `variant: 'primary' | 'secondary' | 'ghost' | 'danger'` (default `secondary`);
  `size: 'sm' | 'md' | 'lg'` (default `md` — heights map to the `--field-height*` tokens so
  buttons align with form fields in toolbars); `pending: boolean` (default `false`).
- **`type` default.** A native button inside a `<form>` defaults to `type="submit"` — a recurring
  footgun on data-entry screens. The directive host-binds `type` to the authored attribute when
  present, else `"button"`; submit buttons opt in explicitly with `type="submit"`.
- **Pending state** (async actions — Save, Post): `pending` sets `aria-busy="true"`, suppresses
  activation (click/Enter/Space swallowed at the host; the button is **not** `disabled`, so focus
  is retained and the state change is announced), hides the label content (`visibility: hidden`,
  so the box **keeps its exact size**) and overlays a centered `tm-spinner`.
- **Icons:** leading/trailing inline SVGs projected as content, `aria-hidden`. Icon-only buttons
  must carry an accessible name; a dev-mode warning fires when the button has no text content and
  no `aria-label`/`aria-labelledby`.
- **Styling:** a `--button-*` component token group (per-variant background/text/border ramps,
  hover/active/disabled states, radius) in both schemes; focus ring from `--focus-ring`;
  forced-colors keeps variant boundaries via system colors; touch target ≥ 24px per the
  foundation's sizing posture.

## 4. Text area — `tmInput` on `<textarea>`

A textarea is the same control as a text input at every layer that matters (string value channel,
field chrome, bidi, Signal Forms), so the existing directive's selector extends to
`textarea[tmInput]` — **no new directive, no new entry point, no new API**. Deltas:

- The element type generalizes (`HTMLInputElement | HTMLTextAreaElement`); `value` stays
  `model<string>`; `dir="auto"` and all state/aria host bindings apply unchanged.
- **Fixed size:** height comes from the authored `rows` (native attribute; consumer-set) and the
  field tokens; the directive's stylesheet sets `resize: none` (the size-stability rule — a
  user-draggable resize handle shifts the layout below), as an ordinary class rule a consumer
  stylesheet can override where a resizable comment box is genuinely wanted. `tm-form-field`'s bordered box grows to the textarea's
  block size; the focus ring wraps it as usual; label/hint/error wiring is unchanged.
- Multi-line specifics: the box aligns adornments and the label to the first line
  (`align-items: start` in the field box when the control is a textarea).
- Grid: not a cell editor (single-line cells per spec 0004); no registration.
- `TmInputHarness` gains a `hostTagName()` accessor and works for both hosts.

## 5. Numeric input — `tmNumber`

- **Selector:** `input[tmNumber]` — a separate directive, not a `tmInput` mode: the Signal Forms
  value type differs (`FormValueControl<number | null>` vs `<string>`), and a mode flag cannot
  change a control's model type. Like `tmInput` it is a bare directive on the native input
  (`ownsChrome: false`) — it drops into `tm-form-field` for chrome and into a grid cell bare.
- **API:** `value = model<number | null>()`; `minDecimals` (default 0), `maxDecimals` (default 20,
  floored at `minDecimals`), `percent: boolean` (default `false`); `placeholder`; the standard
  optional Signal Forms state inputs, including `min`/`max` (typed `number | undefined`), whose
  errors surface with the framework's `min`/`max` kinds through the spec-0002 message resolver.
- **Host:** `type="text"` guarded (never `type="number"` — its spinner, scroll-to-increment, and
  locale quirks are the reason this control exists) + `inputmode="decimal"` for the numeric mobile
  keypad (known limitation: the iOS decimal keypad has no minus key, so negative amounts there
  need the standard keyboard — accepted and documented); `text-align: right` — physical, not
  logical, so numerals stay right-aligned in RTL locales too (the grid's `number`-column
  default); the same `aria-*`/state host bindings as `tmInput`.
- **Format/parse loop.** Built on Signal Forms' `transformedValue` (stable in v22): the typed
  `value` model is the source of truth; the raw text signal parses through `tmParseNumber` and
  formats through `tmFormatNumber` (§2.2) with the active locale (+ `percent`). **Gaining focus
  never rewrites the text** (no flicker, stable caret): the user edits the formatted string in
  place — the parser accepts group separators anyway. On **blur/commit** the text reformats to
  the canonical display form (group separators, `minDecimals`/`maxDecimals` bounds, percent sign
  in percent mode); external model changes reformat immediately while unfocused. The model
  follows the text live — `transformedValue`'s own semantics, the same as `tmInput` — so a
  keystroke writes the parsed (unrounded) value straight through and commit is where
  normalization happens, not where the write happens. Text and rounding still commit only when
  the text actually changed: focusing and leaving a field never rewrites its model.
- **Rounding — the model equals the display.** On commit, the parsed value is rounded to
  `maxDecimals` (the display formatter's own rounding), so the persisted value can never silently
  differ from what the field shows. The grid's `number` columns follow the same rule (superseding
  spec 0004's display-only rounding): editor commits and pasted values round to the column's
  `maxDecimals` before the field write — one platform-wide invariant, grid cells and form fields
  agreeing. The invariant governs **user commits only — a programmatic write to `value` is never
  mutated by the control**: `maxDecimals` is an entry/display policy, not the storage scale, so an
  externally written value carrying more precision (a server-computed figure, or a column scale
  above the display policy) displays rounded while the model keeps the written value — until the
  user edits it, at which point the commit normalizes to display scale. The server
  remains the final authority on storage scale (it re-rounds to the column's scale on write);
  the docs advise setting `maxDecimals` to the column scale on entry fields.
- **Precision envelope — exact by construction.** `value` is an IEEE-754 double while the backend
  stores exact decimals; any decimal of at most **15 significant digits** round-trips
  string → double → string exactly, so the control guards that boundary per committed value:
  after parse and rounding, a value whose decimal digit count (integer digits + fraction digits
  present) exceeds 15 is rejected as a parse-level error with a localized message — never
  silently corrupted. The rule is per-value, so it composes with any `maxDecimals`, including
  the unbounded default (`0.12345678901234` passes on a default field; a 17-digit amount fails).
  The grid's `number` columns enforce the same guard at editor commit and paste, through a
  dedicated `precision` invalid-input reason (its own cell message, so an overflowing cell does
  not claim to be unreadable) added to spec 0004's error machinery — an additive union member in
  **grid-engine**'s cell annotations, where the other reasons live. Values that genuinely
  need more than 15 significant digits — hyperinflated-currency amounts — are the future
  string-backed currency control's territory (Non-goals).
- **Invalid input:** unparseable text reports through `transformedValue`'s parse-error channel
  (kind `parse`, message localized via `TM_UI_TRANSLATE`); the control shows the standard invalid
  state, keeps the user's text for correction, and the model holds `null`. Empty text parses to
  `null` (not an error; `required` is the field's concern).
- **Percent mode:** display `0.75` → `75%`; parse `75`, `75%`, `٧٥٪` → `0.75`. `min`/`max`
  validate the model fraction (documented; a `0..1` range bounds a percentage field).
- **Grid:** `tmNumber` registers with `TM_CELL_EDITOR_HOST` (text channel = its raw text;
  `seed()` replaces content) and becomes the **built-in editor for `number` columns**. When
  grid-hosted the grid owns the value channel and the parse (the column's `parse` — the same
  `l10n` codec), so the control's own `transformedValue` loop is inert in a cell; what the
  editor contributes there is `inputmode="decimal"`, the physical right alignment, and one
  control to theme. Display cells stay static DOM formatted by the codec, unchanged.

## 6. Date picker — `tm-date-picker`

### 6.1 Value, bounds

- **Value:** `value = model<string | null>()` — an ISO 8601 `YYYY-MM-DD` date string. Implements
  `FormValueControl<string | null>` + `TmFormFieldControl`. The value is always Gregorian ISO
  regardless of the display calendar; no `Date` objects cross the API (no time zone ambiguity).
- **Bounds:** the control rejects (as parse/validation errors) anything outside
  **`0001-01-01`–`9999-12-31`** — the intersection of ISO 8601 with .NET `DateOnly`/`DateTime`
  and SQL Server `date`, all proleptic Gregorian. (ISO 8601 also admits year `0000`; .NET and SQL
  Server do not, so `0001` is the floor.) The popup cannot navigate outside the bounds.
- **`minDate`/`maxDate` validation:** the primary entry point's `forms/` ships `tmMinDate`/
  `tmMaxDate` schema validators — component-free schema code — operating on the ISO strings
  (lexicographic comparison is correct for this shape) and reporting the framework's
  `minDate`/`maxDate` kinds so the spec-0002 resolver supplies localized defaults. (The
  framework's own `minDate`/`maxDate` validators are typed for `Date | null` and don't apply to
  string-valued fields.) The control declares the matching `minDate`/`maxDate` inputs (ISO
  strings), which the popup renders as disabled cells (§6.4). They are spelled in full because
  `[min]`/`[max]` are forbidden bindings on a `[formField]` host (NG8022) — the validator kinds
  keep the framework's names.

### 6.2 Anatomy & form-field integration

The component renders an internal native `<input>` (free-text entry) plus a trailing calendar
button, and reports `ownsChrome: false` — inside `tm-form-field` the field supplies the bordered
box, focus ring, label (`<label for>` targets the internal input via `controlId`), hint/error
wiring; standalone or in a grid cell the bare input + button fill the host. The input carries
`aria-haspopup="dialog"` + `aria-expanded`, `dir="auto"`, and a placeholder defaulting to
`tmDatePlaceholder()` (the locale's field order, e.g. `dd/mm/yyyy`) unless overridden.

**The calendar button is not a tab stop** (`tabindex="-1"`), following the APG combobox-datepicker
precedent: one Tab per field is the ERP data-entry contract, in forms and grid cells alike. The
button remains pointer/AT-activatable (localized `aria-label`); the keyboard path to the popup is
`Alt+ArrowDown` (and plain `ArrowDown` standalone — inside a grid, plain arrows belong to the
grid's commit-and-move model, matching the spec-0004 `Alt+ArrowDown` convention for dropdown
cells). `Alt+ArrowUp` and `Esc` close.

**Display formatting:** `dateStyle: 'numeric' | 'medium' | 'long'` (default `numeric`) via
`tmFormatDate` with the active locale and calendar. The displayed text re-renders reactively on
locale or calendar change; the model never changes.

### 6.3 Free-text parsing

Typed text commits on blur and on Enter, through `transformedValue` with `tmParseDate` (§2.2).
The algorithm is deterministic — forgiving on separators and completion, strict on ambiguity:

1. **ISO fast path:** input in the value's own shape (`2026-03-05`) is accepted in any locale —
   except where that shape is also the locale's own numeric rendering in a *different* field
   order, and there the locale's reading wins. Two cases exist: Kyrgyz writes numeric dates as
   `yyyy-dd-MM`, and several locales render a Hijri date as `1406-03-07`. Taking those as ISO
   would mean the engine could not read back the text it had just written.
2. **Field order** comes from `formatToParts` on the active locale + calendar (numeric reference
   date) — `5/3` is day-month in en-GB and ar-SA, month-day in en-US. Any of `/ . - ٫` and spaces
   separate segments.
3. **Segments are interpreted in the active calendar**, then converted to ISO: with an Umm
   al-Qura display calendar, `15/2/1448` is 15 Safar 1448 AH.
4. **Month names** (long and short forms from Intl for the active locale + calendar,
   case/diacritic-insensitively) may replace the numeric month: `5 mar 2026`, `١٥ صفر`.
5. **Omitted trailing parts complete from today** (in the display calendar): one segment = that
   day of the current month; two segments = day and month, read in the locale's field order
   (en-US `3/15` is March 15), of the current year — the fast-entry accelerator. Extra segments
   are an error, not ignored.
6. **Two-digit years** resolve in the sliding window [today − 80, today + 19] years — the
   documented convention, applied in the display calendar's year numbering.
7. Anything else — a segment out of range for its slot (month 14, day 31 in a 30-day month), a
   name matching no month, contradictory input — is a **parse error** (kind `parse`, localized
   message that includes the expected pattern). The control never reorders segments to force a
   match: if the locale reading is invalid and exactly one other reading would be valid, it is
   still rejected — predictability over cleverness.

Valid input reformats to the display form on commit. Empty input commits `null`.

### 6.4 The popup

An anchored, non-modal `role="dialog"` popup (via the §2.1 helper; lazily created), implementing
the APG date-picker-dialog pattern with the APG combobox-datepicker's focus/opening model:

- **Opening** (button click, `Alt+ArrowDown`/`ArrowDown` per §6.2) first commits any pending
  typed text through the §6.3 parse, then moves focus into the active view — onto the day
  matching the field's (just-committed) value, else today. **Esc** closes without committing
  and returns focus to the input; selection commits, closes, and returns focus to the input.
- **Views:** day grid → month grid → year grid, cycled by the header button exactly as the mode
  ladder requires: the header shows "month year" in day view (clicking it switches to month
  view and the label becomes the year), "year" in month view (clicking switches to year view,
  label becomes the 24-year range), and clicking again returns to day view. Two header arrow
  buttons page the active view (month / year / 24-year block); all three buttons are in the
  popup's Tab cycle (`tabindex` normal inside the dialog). The popup always opens on the day
  view; the coarser views are drill-down navigation (year → month → day), and only a day commits.
- **Day view:** a `role="grid"` of the display-calendar month — 7 columns, weekday headers from
  Intl (narrow/short names), first day of week from `tmFirstDayOfWeek` (§2.2); no out-of-month
  days — cells before the month's first day and after its last are empty and non-interactive,
  inside the fixed six-row grid. Roving `tabindex` (one tabbable
  cell); keyboard per APG: arrows ±1 day/week (direction-mapped: inline-start/end), `Home`/`End` week
  edges, `PageUp`/`PageDown` ±1 month, `Shift+PageUp`/`Shift+PageDown` ±1 year, `Enter`/`Space`
  select. The month/year header text is `aria-live="polite"`. Today is visibly marked
  (`aria-current="date"`); the selected day carries `aria-selected`.
- **Month view:** a fixed five-row grid of the year's months, three per row (12 cells — or **13
  for Ethiopic**, whose Pagume is a real, selectable month; the layout holds the extra cell with
  no size change between years or calendars). **Year view:** a 24-year grid, paged in 24-year
  blocks. Arrows move by cell/row; the same select-to-drill semantics.
- **Footer:** localized **Today** and **Clear** buttons (Today commits today; Clear commits
  `null`). Today is disabled when today itself lies outside the bounds.
- Navigation ranges over the whole calendar within the §6.1 bounds; `minDate`/`maxDate` appear as
  disabled cells (`aria-disabled`), never as a navigation stop — an arrow that swallows input
  reads as a broken control, while a month you can see is unavailable explains itself.
- **The popup never resizes** across months or views (the fixed six-row day grid above, the fixed
  month/year grids). A calendar that resized under the pointer would move the controls the user
  is aiming at — opened upward it keeps its bottom edge, so its header arrows would climb the
  screen with every page — and one that fitted the viewport when it opened could grow past it.
  Blank rows on a short month are the cheaper compromise.

### 6.5 Calendar systems

- Display/formatting only; the model stays ISO (§6.1). The active calendar is the app-ambient
  `TM_CALENDAR` signal (default Gregorian), overridable per instance via a `calendar` input;
  switching re-renders text and popup in place.
- **Umm al-Qura and Ethiopic ship as their own entry points** (`/calendar-umalqura`,
  `/calendar-ethiopic`), not in `l10n` and not in locale packs: a calendar is not a language
  (an English UI can display Umm al-Qura; Arabic UIs routinely show Gregorian), and per-entry-
  point packaging keeps the tables out of apps that don't need them while month/era names come
  from Intl at no bundle cost.
- **Implementation:** Gregorian and Umm al-Qura are adapters over `@internationalized/date`
  (`GregorianCalendar`, and `IslamicUmalquraCalendar` — the ICU-ported Umm al-Qura tables),
  chosen over the alternatives because native `Temporal` is still absent from stable Safari
  (mid-2026), Temporal polyfills cost ~20–45 KB gzipped against ~8 KB for the whole library
  (~3 KB Gregorian-only), and hand-porting the Umm al-Qura almanac tables is exactly the risk a
  maintained port removes. The `TmCalendar` seam (§2.2) keeps it swappable for native Temporal
  later; the adapter source carries a `TODO` marker to re-evaluate replacing the dependency with
  native `Temporal` once it ships in stable Safari.
- **Umm al-Qura accuracy window:** the underlying tables cover AH 1300–1599 (≈ 1882–2174 CE);
  outside that window the implementation degrades to the arithmetic Islamic calendar, silently
  and continuously (the ICU behavior). Documented in the component docs; no guard is added —
  ERP dates live comfortably inside the window, and the ISO model value is exact regardless. The
  window ends at 1599 because the library's table has a one-day discontinuity at the AH 1600
  boundary (an upstream defect, worth reporting); the drift gate pins that boundary.
- **Ethiopic ships its own arithmetic** rather than an adapter: `@internationalized/date`'s
  `EthiopicCalendar` starts the Ethiopic year one Gregorian day late in every non-leap year — two
  Gregorian days map to Pagume 5, roughly 150 wrong days per century — which the
  adapter↔Intl agreement gate catches. The pack implements the conversion directly (13 months,
  Pagume of 5–6 days, Amete Mihret era numbering); month and era NAMES still come from Intl, and
  the month grid and `daysInMonth` need no special-casing at call sites.

### 6.6 Grid integration

- `tm-date-picker` registers with `TM_CELL_EDITOR_HOST` (`TmCellEditor`: `text` = the raw input
  text; `seed()` replaces it; `commit()` parses; `cancel()` restores) and becomes the **built-in
  editor for `date` columns** — mounted in the cell box, input filling the cell, calendar button
  at the inline end, popup anchored to the cell rect via the §2.1 helper. `Alt+ArrowDown` opens
  the popup (extending the spec-0004 keymap's dropdown row to date cells) — from an unedited
  cell it opens the editor AND the calendar in ONE press, the way it reaches an enum cell's
  panel; `F2` opens the editor alone, since typing is a date cell's primary path. While the popup is
  open the editor consumes navigation keys (the spec-0004 dropdown gate), and its Esc closes the
  popup first — the grid's two-stage Esc composes unchanged. Drilling through the popup's views
  keeps the session open: swapping views destroys the button that was clicked, and the resulting
  focus drop to nowhere is a re-render artifact, not a blur (§8.4's commit-on-blur reads the
  press that precedes a real departure, not the focus event alone). **Picking a day IS the edit:** the
  pick commits the cell and closes the editor without a move, the same contract an enum option
  activation has, because a user who reached for the calendar has no reason to press Enter
  afterwards. Typed text still commits the grid's way.
- **`date` columns gain built-in defaults** (superseding spec 0004's "consumer `format`/`parse`
  required"): display = `tmFormatDate`, `numeric` style, active locale + ambient `TM_CALENDAR`;
  parse = `tmParseDate`. Column-level `format`/`parse` still override.
- **The grid's display locale and calendar are reactive**: date cells re-render in place when the
  ambient locale or `TM_CALENDAR` changes, and because display flows through a single internal
  locale seam, number cells follow (spec 0004's was static). Internal only — the public grid API
  is untouched.
- **Foreign pastes state their provenance.** `TmParseContext` gains `foreignSource` (additive):
  the paste path marks text that came from outside this grid, and the column's parse decides what
  to do with it. A built-in `date` column reads such text as Gregorian when the clipboard carries
  no calendar id — the spreadsheet case is the common one. Documented limitation: text both
  calendars can read (`9/23/2024`, or this grid's own display text round-tripped through
  something that dropped the metadata) therefore resolves in Gregorian's favour. Grid-to-grid
  pastes are unaffected — the clipboard metadata carries the display calendar id.
- The grid showcase gains a date column in the editable story (typed edit, popup edit, paste,
  invalid-input state) — the DoD covers it.

### 6.7 Signal Forms & error surfacing

`transformedValue` carries the text↔ISO channel; parse errors surface as kind `parse` with a
localized message naming the expected pattern (e.g. *"Enter a date like 15/03/2026"*). `required`,
`minDate`, `maxDate` resolve through the standard message resolver with new English defaults (and
`@tellma/locale-ar` translations). Pending/disabled/readonly behave per the foundation contract;
`readonly`/`disabled` also disable the calendar button and popup.

## 7. Image — `tm-image`

One component, two modes (`mode: 'view' | 'edit'`, default `view`): renders a record image inside
a fixed box; in edit mode adds replace / re-fit / delete.

### 7.1 Contract

```ts
// Inputs
src: string;                       // base URL of the image resource (no size param)
etag: string | null;               // the record's current image version stamp (null = unknown)
alt: string;                       // required; '' only for decorative images
shape: 'rect' | 'circle';          // default 'rect'
width/height: number;              // CSS px of the box — fixed for the component's lifetime
srcForSize?: (src: string, size: number) => string;  // default: appends ?size=<bucket>
defer: boolean;                    // default true — IntersectionObserver-deferred fetch
editSrc?: string;                  // URL of the stored ORIGINAL — enables re-fitting an existing
                                   //   image; absent = an existing image can only be replaced/deleted
// Content
<ng-template tmImagePlaceholder>   // no-image state; default: a generic image glyph
// Edit-mode output
imageChange: OutputRef<TmImageEdit | null>;   // null = user deleted the image
interface TmImageEdit {
  blob: Blob | null;               // a newly picked file, or null = re-fit of the existing image
  fit: TmImageFit;
}
interface TmImageFit {              // normalized to the ORIGINAL image, all 0..1
  rect: { x: number; y: number; width: number; height: number };  // aspect = box aspect
  focal: { x: number; y: number };  // rect center — survives future aspect changes
}
```

The component talks to the network through a `TM_BLOB_FETCHER` token (default implementation:
`HttpClient` with the app's interceptors — so auth headers, and later BFF cookies, apply without
any component knowledge). The consumer sends `TmImageEdit` to the server (multipart, §8.3); the
backend standardizes format, crops to `rect`, and generates renditions. **Re-fitting an existing
image emits `blob: null`** — what the component holds for display is a rendition (already
cropped/downscaled), so re-emitting it would silently downgrade the stored original; the server
re-crops from the original it holds, and the bytes never round-trip. `fit` is always normalized
against the image being fitted: the picked file, or the original fetched from `editSrc`. `fit` carries both the
exact rect (lossless re-edit) and the focal point (automatic recrop if the product later changes
the box aspect) — the DAM-industry convention. Whether the backend retains the original for later
re-fitting is a backend policy decision; the component docs note that **cropping is presentation,
not redaction** — users who cropped something out must use delete/replace.

### 7.2 The blob cache

A `CacheStorage` cache (`tm-images-v1`), origin-scoped, with entries tenant-scoped through
their keys — the cache key is the full request URL, and the tenant id is part of every API URL
by platform convention (a distribution hosts multiple tenants on one origin, §2.3):

- **Why Cache API** (not IndexedDB, and never localStorage): it stores `Request`/`Response`
  pairs with headers — the ETag rides inside the cached entry, no parallel metadata store; it
  streams; it shares the origin quota with LRU whole-origin eviction; localStorage is
  synchronous, string-only, and ~10 MB.
- **Lookup:** cache key = the full sized URL. On hit, compare the entry's stored version stamp
  (the `etag` input, persisted on the cached response as a header at put-time) against the
  current `etag` input: equal → serve from cache with **no network**; different → delete every
  size variant of that `src` and refetch. Variants are matched by the URL with its `size`
  parameter removed, not by the Cache API's `ignoreSearch` (which drops the whole query and
  would purge unrelated images on a query-keyed endpoint). When `etag` is `null`, serve the
  cached entry immediately and revalidate in the background with `If-None-Match` (304 → keep;
  200 → replace and swap in, stamped with the response's own ETag — the record stamp is
  unknowable on that path, and the next stamped read heals it).
- **No serial ETags needed:** entries are written as matched `(blob, etag)` pairs from one
  response, so any interleaving of writes leaves a coherent entry; the worst race outcome is a
  stale-but-valid entry that the next mismatch/revalidation heals. Compare-and-replace on opaque
  tags is sufficient.
- **Request coalescing:** a per-tab in-flight `Map<url, Promise<Blob>>` — N concurrent
  `tm-image`s for one URL issue one request. No cross-tab locking (writes are idempotent; a
  duplicate fetch across tabs is a bandwidth footnote, not a correctness issue).
- **Failure & quota:** fetch/decode failure renders a small error glyph (fixed box, no layout
  shift) with a localized tooltip; `QuotaExceededError` on `put` deletes the whole cache and
  continues uncached (the cache is an accelerator, never required). Where the Cache API itself is
  unavailable (insecure context, restrictive browser profile), the pipeline runs cache-less from
  the start — direct fetches, with the in-flight map still coalescing. The cache carries the
  `tm-` prefix and is swept by `TmClientCache.clearAll()` on logout (§2.3). Browser eviction
  (LRU, Safari's 7-day script-storage cap) is tolerated by design — every entry is re-fetchable.

### 7.3 Size hints

`size` buckets: **64, 128, 256, 512, 1024, 2048** — the smallest bucket ≥
`max(width, height) × min(devicePixelRatio, 2)`. Bucketing (vs exact px) keeps server rendition
counts bounded and cache keys stable across near-identical layouts. The default `srcForSize`
appends `?size=<bucket>`; the server may serve any size ≥ the hint (and negotiates AVIF/WebP via
the `Accept` header server-side — no client code).

### 7.4 View-mode rendering

Fixed `width`/`height` box (`aspect-ratio` reserved up front — zero CLS), `shape: 'circle'`
clips via `border-radius: 50%`. Pipeline: deferred until near-viewport (`defer`, IO with a
200px root margin) → cache/fetch (§7.2) → `img.decode()` off-DOM → swap in (no
flash-of-partial-image) → `URL.revokeObjectURL` of any replaced object URL; all object URLs are
revoked on destroy. Placeholder template shows until the swap (and permanently when `src` is
empty).

### 7.5 Edit mode

- **Replace:** an overlay affordance opens the OS file dialog (accept: `image/png`, `image/jpeg`,
  `image/webp`, `image/gif`, `image/avif`); a file that fails to decode is rejected with a
  localized message. Guardrail: files over **20 MB** are rejected before any processing (a limit
  only realistically hit by abuse); after fitting, output larger than **4096 px** on its longest
  edge is downscaled via `createImageBitmap` + canvas (EXIF orientation applies automatically),
  re-encoded (JPEG 0.9; PNG when the source has alpha). **GIFs are exempt from the downscale** —
  a canvas pass keeps only the first frame, silently de-animating them — and pass through at
  original resolution, with the byte-size limit still applying. Both limits are inputs with
  these defaults.
- **Fit:** the image being fitted — the newly picked file, or for an existing image the
  original fetched from `editSrc` (§7.1; without `editSrc` the re-fit affordance is absent) —
  renders under a fixed viewport
  of the box's aspect; the user pans (pointer drag / touch drag) and zooms (wheel, pinch, and an
  always-visible zoom slider — the accessible path; arrow keys pan when the crop surface is
  focused, `+`/`-` zoom). The fit state is the `TmImageFit` rect (clamped so the rect never
  leaves the image). Output emits on every committed adjustment (`imageChange`), carrying the
  fit plus the blob only when a new file was picked (`blob: null` on re-fits, §7.1) — the
  component never crops pixels client-side beyond the downscale guardrail.
- **Delete:** a labeled control emits `imageChange(null)`; the box returns to the placeholder.
- Edit affordances live inside the fixed box (overlay chrome) — the component's size never
  changes between modes or states.

## 8. Files

### 8.1 `tmFilePicker` + `tm-dropzone` — two surfaces, one engine

Selection logic (accept filter, size/count guardrails, the output contract) is one shared
internal engine; it ships behind two surfaces because ERP screens need both a plain toolbar
button ("Attach") and a visual drop target, and neither wants the other's DOM:

- **`tmFilePicker`** — a directive for `button[tmFilePicker]`: opens the OS dialog via an
  internally managed hidden `<input type="file">`. Inputs: `accept` (native accept string),
  `multiple` (default `false`), `maxFileSize` (bytes, default **100 MB**), `maxFiles`
  (default unbounded). Output: `filesSelected: OutputRef<TmFileSelection>`.
- **`tm-dropzone`** — a component rendering a bordered drop region (dashed border, icon,
  localized hint text showing the accepted types/size limit, and a browse affordance —
  presentational styled text, never a nested interactive element): the whole region is a single
  focusable control (`role="button"`, Enter/Space opens the dialog — keyboard users'
  full-fidelity path, since drag-and-drop has no keyboard equivalent). It applies `tmFilePicker`
  as a **host directive** — the browse path, the guardrail inputs, and `filesSelected` are
  literally the directive's, re-exposed — and adds only the drop-target handling and visuals.
  Drag-over highlights via tokens; drops of folders/directories are rejected with a localized
  reason (flat file lists only); dropping when `multiple` is false takes the first file and
  rejects the rest. The focused zone also accepts **paste**: `Ctrl+V` with files on the
  clipboard (a screenshot) runs the same guardrail pipeline, and the hint mentions it. Because
  a missed drop navigates the browser away from the SPA, the first connected `tm-dropzone`
  installs a document-level guard canceling `dragover`/`drop` defaults outside designated drop
  targets; the guard is removed when the last dropzone disconnects.

```ts
interface TmFileSelection {
  accepted: File[];                       // native File objects: name, size, type ride along
  rejected: { file: File; reason: 'size' | 'type' | 'count' | 'folder' }[];
}
```

The output is native `File`s — fully decoupled from the backend; upload is the consumer's job.
Client-side checks are UX guardrails only (fail fast, honest error copy); `File.type` is
extension-derived and spoofable, so real validation (magic bytes, AV, limits) is the server's —
stated in the component docs.

### 8.2 A11y & i18n

The dropzone's hint and every rejection reason resolve through `TM_UI_TRANSLATE`; rejections are
announced via the CDK `LiveAnnouncer` and surfaced in the output for the consumer's own UI. The
hidden file input never receives focus; the visible control owns the keyboard interaction.

### 8.3 Transport guidance (documented, not implemented)

The components stop at `File`/`Blob` outputs, which feed either sanctioned transport unchanged:

- **Inline multipart save** — `multipart/form-data` with a JSON DTO part plus one binary part
  per blob (native `FormData`, streaming, real progress events, no base64 +33%/memory penalty).
  The default for record images and small attachment sets: the save stays a single atomic
  request and an abandoned draft needs no server cleanup. Base64-in-JSON is reserved for
  reliably-tiny payloads.
- **Staged upload** — files upload on selection to a staging area and the record save references
  staged ids (per-file progress and retry, early server-side validation, and a lean, fast final
  save). The cost is a staged→committed lifecycle: staged blobs carry a TTL comfortably above
  any in-memory draft's realistic lifetime, a server job garbage-collects expired ones, and the
  client best-effort deletes its staged uploads on explicit draft abandonment. The sanctioned
  path where file sizes or counts make inline saves slow.

A distribution picks per screen; this guidance lives in the library docs so distributions
converge on the same two shapes.

### 8.4 File preview — `tm-file-preview`

A modal viewer (hosted on `tm-modal`, size `lg`) opened through a service:

```ts
@Injectable({ providedIn: 'root' }) class TmFilePreview {
  open(file: TmPreviewFile): TmModalRef<void>;
}
interface TmPreviewFile {
  name: string;                     // drives kind detection (extension) and the title bar
  type?: string;                    // MIME when known
  size?: number;                    // bytes, for the header
  source: Blob | (() => Promise<Blob>) | { url: string };
}
```

`source` decouples the viewer from the backend: a `Blob` (already in memory — the §8.1 output),
a lazy loader (the consumer fetches with its own auth — interceptors/BFF cookies apply there),
or `{ url }` for **streamable media** (video/audio use the URL directly so the browser range-
requests instead of buffering whole blobs; URL sources require ambient auth — cookies under the
BFF model, or presigned URLs under bearer tokens — documented). Streaming is the whole of a
`{ url }` source's job: every other kind renders from bytes this component holds.

**Rendering by kind** (detected from MIME, else extension; no client-side content sniffing —
anything undetected is unsupported):

| Kind | Rendering |
|---|---|
| Raster image | `<img>` from an object URL; `decode()` before display; error → unsupported card |
| SVG | `<img>` only (no inline/iframe SVG — scripts never execute in `<img>`) |
| PDF | `navigator.pdfViewerEnabled` → a **non-sandboxed** `<iframe>` with the blob URL (the browser's own viewer is trusted UI and supplies print/download chrome; sandboxed iframes never render PDFs, per the HTML spec's plugins rule); else the unsupported card. The viewer chrome varies per browser and is not brand-themed — accepted for full fidelity at zero maintenance. **Only for bytes this component fetched and re-typed itself**: a `{ url }` PDF is download-only, because its endpoint's real content type is unknowable here and one echoing a stored `text/html` would run script in the app's origin |
| Video / audio | native element with `controls`, `preload="metadata"`, no autoplay — playback starts only on the user's play action; `canPlayType()` gates; unplayable → unsupported card |
| Plain text (MIME allowlist: `text/plain`, `text/csv`, JSON, XML) | escaped `<pre>` (capped at 1 MB, tail truncated with a notice) |
| HTML and everything else | **never rendered** — the unsupported card ("Preview not available") with the download button. Blob URLs inherit the app origin, so user-authored active content is a same-origin XSS vector; download-only is the policy, not a limitation to engineer around. |

Toolbar: file name + size, **Download** (always; `<a download>` with the object URL), **Print**
for images only (PDF printing is the built-in viewer's toolbar; other kinds have none), Close.
Object URLs are revoked on close. A loading state (spinner, `aria-busy`) shows while a lazy
`source` resolves; load failure shows a localized error state inside the modal.

## 9. Tabs — `tm-tab-group` + `tm-tab`

- **Composition:** built on `@angular/aria/tabs` (stable in v22): `ngTabs`/`ngTabList`/`ngTab`/
  `ngTabPanel` own roles, roving focus, arrow-key navigation (direction-aware), `Home`/`End`,
  and selection state. Following the proven `tm-select`/`tm-option` pattern, `tm-tab` is a
  **definition directive** (label, id, disabled, content template) — `tm-tab-group` renders the
  real `ngTab`/`ngTabPanel` elements itself via `@for` + `ngTemplateOutlet`, because aria
  directives cannot be content-projected across DI boundaries (the phase-1 NG0201 lesson).
- **API:** `tm-tab-group`: `selectedId = model<string | undefined>()` (defaults to the first
  enabled tab), `selectionMode: 'follow' | 'explicit'` (default `follow` — arrowing through the
  tab list selects as focus moves, the APG automatic-activation model; `explicit` moves focus
  only and selects on Enter/Space/click — the choice for expensive panels, since keyboard
  scanning then instantiates nothing), `orientation` (default `horizontal`). `tm-tab`: `id`
  (required), `label` (string; a `*tmTabLabel` template for rich labels), `disabled`,
  `preserveContent` (default `false`).
- **Only the active tab is in the DOM** — the headline requirement, delivered by aria's
  `ngTabContent` deferred-content mechanism: a panel's content instantiates on first activation
  and is **destroyed on deactivation** by default. A tab's content is therefore an
  `<ng-template tmTabContent>`, never projected elements — projected content belongs to the
  consumer's view and cannot be destroyed by the panel, which would defeat both active-only DOM
  and lazy instantiation. A missing template warns in dev mode; `preserveContent` (per tab) keeps an
  activated panel's DOM alive (hidden + `inert`) for expensive tabs whose state must survive
  switching. Grid state inside tabs survives destroy/recreate via spec 0004's `TmGridStateStore`
  regardless.
- **Overflow:** the tab strip scrolls on overflow (momentum scroll, no pagination buttons);
  keyboard navigation scrolls the focused tab into view. Router-driven tabs are out of scope
  (the group is a pure UI container).
- Panels are the standard `role="tabpanel"` with `tabindex="0"` (aria's wiring); tab sizing,
  ink/active indicators, and hover states come from a `--tabs-*` token group; the active
  indicator never shifts sibling layout.

## 10. Modal — `tm-modal`

- **Composition:** built on `@angular/cdk/dialog` (`Dialog`, `DialogRef`, `DIALOG_DATA` — real
  `FocusTrap`, `autoFocus`/`restoreFocus`, backdrop, `aria-modal`), reused rather than rebuilt;
  `@angular/aria` ships no dialog pattern in v22. Top-layer interplay is correct by
  construction: the library's dropdowns/popovers render as native `[popover]` top-layer
  elements, which always paint above the overlay-container-hosted modal — a `tm-select` inside
  a modal works with no z-index management.
- **API — service-first** (the result-channel requirement):

```ts
@Injectable({ providedIn: 'root' }) class TmModal {
  open<R = void>(content: Type<unknown> | TemplateRef<unknown>, config?: TmModalConfig): TmModalRef<R>;
}
interface TmModalConfig {
  size?: 'sm' | 'md' | 'lg';        // default 'md'; lg = viewport minus a constant token margin
  panelClass?: string;               // custom-dimension/styling escape hatch (see Sizes below)
  title?: string;                    // rendered in the header; also the dialog's accessible name
  data?: unknown;                    // injected via TM_MODAL_DATA
  showClose?: boolean;               // default true — the X button
  backdropDismiss?: boolean;         // default true — click on the backdrop closes
  escapeDismiss?: boolean;           // default true
  canDismiss?: () => boolean | Promise<boolean>;  // guards user-initiated dismissals
}
class TmModalRef<R> {
  close(value?: R): void;                          // programmatic dismissal ('api')
  readonly closed: Promise<TmModalResult<R>>;
}
type TmModalResult<R> =
  | { via: 'api'; value: R | undefined }
  | { via: 'close-button' | 'backdrop' | 'escape' };   // user dismissals carry no value
```

- **Shell:** the service wraps the content in a standard shell — header (title + optional X),
  scrollable body, and a footer slot (`[tmModalFooter]` content projection for
  action buttons) — so every Tellma modal reads alike. Content components inject `TmModalRef`
  to close themselves with a result and `TM_MODAL_DATA` for input. The footer holds the bottom
  of the scrollport even when the content is short. A built-in **`--flush` panel variant** drops
  the body's padding (header and footer keep theirs) for content that must own the whole
  scrollport — the file preview's viewing area.
- **Sizes:** `sm`/`md` are fixed token widths with `max-height` + body scroll; `lg` fills the
  viewport minus a constant margin on all sides. Semantic buckets are the norm so Tellma modals
  read alike; for the rare modal they genuinely don't fit, `panelClass` styles the panel
  (dimensions included) without forking the shell. A modal never resizes itself while open
  (size-stability rule); on small viewports all sizes converge to near-full-screen.
- **Dismissal guard:** `canDismiss` (sync or async) is consulted before every user-initiated
  dismissal — close button, backdrop, Esc; returning or resolving `false` keeps the modal open.
  The unsaved-changes pattern: the guard opens a confirm modal (see **Stacking**) and resolves
  with the user's answer. Programmatic `close()` is not guarded — consumer code owns its own
  calls.
- **Stacking:** a modal may open another (the CDK dialog stack): each layer gets its own
  backdrop and focus trap; Esc and backdrop-click dismiss the **topmost** layer only; closing a
  layer restores focus into the layer beneath, ultimately back to the original opener. The
  machinery is CDK's, not bespoke; more than two levels is discouraged in the docs.
- **Focus:** trap inside the dialog; initial focus per CDK `autoFocus` (first tabbable, else the
  container); focus restored to the opener on close. Backdrop uses the `--modal-*` token group
  (scrim color/opacity); `prefers-reduced-motion` collapses the open/close fades.
- Dismissal via Esc respects `escapeDismiss`; an open dropdown inside the modal consumes its own
  Esc first (aria's handling), so Esc closes innermost-first — no special casing.

## 11. Popover — `tm-popover`

- **Shape:** `tm-popover` + a `button[tmPopoverTriggerFor]` trigger directive. The panel's
  content is an `<ng-template tmPopoverContent>` (like `tm-menu`), for the reason tab content is
  one (§9): only a template can be instantiated on first open and destroyed on close. A missing
  template warns in dev mode. Click toggles; `Esc` and outside-click
  close; `Tab` past the popover's content closes (non-modal). The trigger carries
  `aria-expanded` + `aria-haspopup="dialog"`; the panel is `role="dialog"` (non-modal, no focus
  trap). On open, focus moves to the first tabbable element inside (else the panel itself,
  `tabindex="-1"`); on close it returns to the trigger.
- **Positioning:** via the §2.1 helper — `position: 'block-end' | 'block-start' |
  'inline-start' | 'inline-end'` (default `block-end`) + `align: 'start' | 'center' | 'end'`
  (default `start`), logical (RTL mirrors via `Directionality`), flipping to the opposite side
  on overflow. Lazy overlay; top-layer popover host (clipping-escape).
- **Programmatic use:** `open(anchor: Element | DOMRect)` / `close()` on the component, for
  hosts that position against non-trigger anchors — the same anchor flexibility `tm-menu`
  exposes.
- **Grid error overlays stay grid-internal.** They are non-interactive `role="tooltip"` chrome
  with grid-specific parent hosting and lifecycle; `tm-popover` is an interactive consumer
  surface. The shared §2.1 helper is the DRY boundary between them, not component reuse.

## 12. Tooltip — `tmTooltip`

- **Directive** on any element: `tmTooltip: string` (plain text only — interactive or rich
  content belongs in `tm-popover`; a tooltip must never contain focusables).
- **Show:** pointer hover (after a ~500 ms token-driven delay) and `:focus-visible` (immediate).
  **Hide:** pointer leave / blur / `Esc` (WCAG 1.4.13 dismissable) — and the tooltip surface
  itself is hoverable (moving the pointer onto it does not dismiss; 1.4.13 hoverable/
  persistent). **Touch:** long-press shows it (reusing the menu's long-press utility) until the
  next tap; tooltips remain a progressive enhancement — content required for operation may not
  live only in a tooltip (documented).
- **A11y:** the panel is `role="tooltip"`; the host is described via the CDK `AriaDescriber`
  (the text is available to AT even when the tooltip has never opened — the Material-proven
  mechanism). Never focus-stealing; no arrow-key interaction. Known limitation, stated in the
  docs: natively `disabled` controls fire no pointer events in most engines, so a tooltip on a
  disabled button never shows on hover — use suppressed-activation states instead (what
  `tmButton`'s `pending` does) or place the tooltip on a wrapper element.
- **Positioning:** §2.1 helper, `block-start` preferred with flip, small token offset;
  `--tooltip-*` token group; `prefers-reduced-motion` removes the fade. One tooltip visible at
  a time (a module-level coordinator closes the previous).

## 13. Alert — `tm-alert`

- **API:** `kind: 'info' | 'success' | 'warning' | 'error'` (required); `live: 'off' | 'polite' |
  'assertive'` (default `'off'`). Content is projected; an optional `heading` input renders a
  bold first line.
- **Rendering:** a static inline-SVG kind glyph (aria-hidden) + content in a tinted, bordered
  wrapper from the `--alert-*` token group (per-kind accent border/background/icon color in both
  schemes, forced-colors-safe). A visually-hidden localized kind prefix ("Error:", "Warning:", …)
  precedes the content so the severity survives screen-reader linearization; color is never the
  only signal.
- **Announcement:** `live: 'polite'` renders `role="status"`, `'assertive'` renders
  `role="alert"` — for alerts inserted dynamically (a failed save). Because an element that
  arrives in the DOM *with* its content is unreliably announced across screen-reader/browser
  pairs, the live region is inserted **empty** and its text populated in a follow-up
  microtask — a content change inside an existing region is the dependable trigger. The default
  `'off'` keeps statically-present alerts silent. No dismiss affordance (visibility is the
  consumer's state).
- **Field and grid errors stay as they are.** Form-field errors remain the compact
  `aria-describedby` + live-region text of spec 0002, and grid cell errors remain the spec-0004
  overlay: wrapping every field error in an icon box would add visual bulk to dense forms and
  re-plumb a proven a11y announcement path for zero information gain. `tm-alert` is for page-
  and section-level messaging (form-top error summaries, empty-state warnings, banner notices).

## 14. Accessibility

Target WCAG 2.1 AA — axe-core static floor + behavioral Playwright specs, per the foundation
posture (Playwright is the standardized runner; real screen-reader verification is a manual pass
outside the DoD). Per-component semantics live in §3–§13; the cross-cutting points:

- **Focus chains:** every overlay in this phase restores focus to its opener on close (modal via
  CDK `restoreFocus`; date-picker popup, popover, preview modal explicitly). Stacked dismissal
  is innermost-first on Esc (dropdown inside modal, date popup inside grid edit — each layer
  consumes its own Esc).
- **Keyboard completeness:** every pointer affordance has a keyboard path — the date button's
  `Alt+ArrowDown`, the dropzone's Enter/Space browse, the image editor's arrow-pan + slider
  zoom, tab-strip arrows. The single deliberate exception remains pointer-only column resize
  (spec 0004, unchanged).
- **Live announcements** route through the CDK `LiveAnnouncer`/existing live regions and
  `TM_UI_TRANSLATE`: file rejections, image load failure, preview load state, pending buttons
  (`aria-busy`), the date popup's month/year heading (`aria-live="polite"`).
- Forced-colors and reduced-motion are honored and Playwright-gated for every new surface
  (calendar selection states, tab indicators, alert kinds, button variants must survive
  `forced-colors: active` via system colors, not tint alone).
- Touch targets follow the WCAG 2.2 AA 24px posture of spec 0002 (calendar day cells, tab
  buttons, image/file affordances ≥ 24px; comfortable ≈ 44px on touch-primary standalone
  controls).

## 15. RTL & i18n

- Direction from CDK `Directionality`; all new geometry is logical (calendar grid column order,
  tab strip, popover/tooltip positions, dropzone layout, modal header). The date popup's arrow
  keys are direction-mapped (inline-start/end) like the grid's. Numerals in `tmNumber` and date
  displays render via `Intl` with the locale's numbering system; numeric fields align
  physically right (§5), so numerals stay right-aligned under RTL too. The date placeholder
  keeps the directional marks ICU puts in the pattern: without them an ASCII hint (`dd/mm/yyyy`)
  lays out left-to-right inside an RTL field and its fields read in the opposite order to the
  value that replaces them.
- Every built-in string resolves through `TM_UI_TRANSLATE` with English in-package:
  button/modal/preview/image/file affordance labels, dropzone hints and rejection reasons,
  date-picker labels (choose date, previous/next month, view-switch buttons, Today, Clear),
  parse-error messages (number, percent, date — with the expected-pattern parameter),
  `minDate`/`maxDate` defaults, alert kind prefixes, tooltip-dismiss documentation strings. A
  date carried in a message is formatted in the field's own locale and calendar, never printed
  as raw ISO — the resolver formats every date-shaped parameter, so this holds for any message,
  not just the two bounds.
  **`@tellma/locale-ar` is extended with all of them** (DoD).
- Calendar names, month names, weekday names, and era labels come from Intl at runtime in the
  active UI language — locale packs ship no calendar data, and calendar entry points ship no
  language data.

## 16. Performance budget

- **Budgets** (gzipped self-weight ratchets in each package's `"tellma".budgetsInKb`, the
  established mechanism): `button` ≤ 2, `input` ≤ 4 (textarea addition), `number` ≤ 4,
  `date-picker` ≤ 14 (three views + parse + overlay wiring), `calendar-umalqura` ≤ 5 (tables +
  the re-bundled dependency core), `calendar-ethiopic` ≤ 2, `l10n` ≤ 8 (codecs + Gregorian
  engine), `image` ≤ 10, `files` ≤ 5, `file-preview` ≤ 8, `tabs` ≤ 4, `modal` ≤ 6, `popover` ≤ 4,
  `tooltip` ≤ 3, `alert` ≤ 2, `private` ≤ 4. Two existing ceilings rise with what this phase adds
  to them: the primary entry point 4 → 5 (`TmL10n`, `TM_CALENDAR`, the date validators, the
  active-locale signal) and `grid` 34 → 35 (the date-editor machinery). The codec hoist itself
  leaves the grid budget alone — imports move, and the weight moves to `l10n`. **What the numbers count:** third-party code outside the measurement's externalized
  set (`@angular/*`, `rxjs`, `tslib`, `@jsverse/*`, `@tellma/*`) is bundled into the importing
  entry point's measured weight, and every entry point measures in isolation — so
  `@internationalized/date`'s core is counted inside `l10n` *and again* inside each calendar
  entry point. The ceilings above are sized with that in mind: `l10n` absorbs the dep core +
  Gregorian on top of both codecs; each calendar entry point carries its calendar class plus the
  shared core.
- **Lazy everything that floats:** date popup, popover, tooltip, modal content, and preview
  renderers are created on first open and torn down on close; closed instances cost their
  trigger DOM only.
- **`@defer` for heavy cold branches:** the date popup's views (`@defer` until first open) and
  `tm-image`'s edit-mode chrome (view mode is the hot path in lists) sit behind Angular defer
  blocks, so consuming apps keep their common-path chunks lean; budgets still bind per entry
  point. The preview's and edit pane's per-kind renderers do NOT — the heavy step is the content
  component itself, which already instantiates lazily on open, and the renderers under it are
  native `img`/`video`/`pre` elements with no dependency graph to split.
- **Image pipeline:** deferred fetch (IO), one in-flight request per URL per tab, decode
  off-DOM, fixed boxes (zero CLS), bucketed renditions (bounded server/cache cardinality),
  object-URL hygiene (no blob leaks in long SPA sessions).
- **No layout shift** invariants (Playwright-pinned where cheap): pending buttons keep their
  size; the date popup never resizes across months or views (§6.4); image/preview error and
  loading states render inside the reserved box; alert/tooltip/popover never displace
  surrounding content (overlay or reserved space).
- Zoneless + OnPush throughout; signal-driven re-render only on the changed control.

## 17. Testing

- **Unit (vitest, zoneless):** the `l10n` codecs get the bulk — number round-trips across
  locales/numbering systems (reusing the grid codec's suite, moved with it), percent mode,
  date format/parse across locale × calendar (field order, month names, completion from today,
  two-digit pivot, ISO fast path and the locales it yields to, every §6.3 rejection rule) —
  including a sweep proving the engine reads back its own formatted output for every month of
  every calendar across a wide locale set; calendar implementations
  (Ethiopic month 13 / leap Pagume, Umm al-Qura conversions inside the table window, bounds),
  `tmFirstDayOfWeek` fallback; a **cross-implementation agreement gate** asserting each
  implementation's `toParts` matches `Intl.DateTimeFormat.formatToParts` numeric year/month/day
  over sampled dates per calendar (the arithmetic and the browser's ICU are separate codebases
  that must not drift at table-window edges or in era numbering — this is the gate that caught
  the Ethiopic new-year defect of §6.5); and the committed `format-golden.json` (§2.2)
  asserted row by row. Component units: Signal Forms binding + `transformedValue` error
  flow for `tmNumber`/`tm-date-picker`, modal result channel per dismissal path, tabs
  destroy/preserve semantics, file selection guardrails, image cache logic against a mocked
  `CacheStorage` (hit/mismatch/size-variant purge/revalidate/coalescing/quota fallback).
- **Harnesses** (`@tellma/core-ui-testing`): `TmButtonHarness`, `TmNumberHarness`,
  `TmDatePickerHarness` (read/type text, open popup, navigate/select in each view),
  `TmImageHarness`, `TmFilePickerHarness`, `TmDropzoneHarness`, `TmFilePreviewHarness`,
  `TmTabGroupHarness`/`TmTabHarness` (composing `@angular/aria/tabs/testing`),
  `TmModalHarness`, `TmPopoverHarness`, `TmTooltipHarness`, `TmAlertHarness`; `TmInputHarness`
  extended for textarea hosts.
- **Playwright (showcase story pages):** the date-picker keyboard matrix (§6.4 grid keys, view
  ladder, focus in/out, Esc, Today/Clear, out-of-range cells) in LTR and RTL and per calendar;
  grid date-column story (type-to-edit, `Alt+ArrowDown` popup anchored to the cell, pick-commits-
  the-cell by pointer and by keyboard, two-stage Esc, paste); modal focus trap/restore + stacked-Esc order + result-by-dismissal; tabs
  active-only-DOM assertion + `preserveContent`; tooltip 1.4.13 behaviors (Esc dismiss,
  hoverable surface) + `AriaDescriber` wiring; popover flip/RTL mirror; image caching behaviors
  (single network request for N instances, etag-mismatch refetch, error glyph), edit-mode
  pan/zoom via pointer and keyboard; dropzone drag-over/drop/reject announcements (synthetic
  `DataTransfer`, with the known Firefox/WebKit shims); preview per-kind rendering including
  the PDF iframe path and the HTML-is-never-rendered policy; pending-button size stability;
  axe on every new component in every state (open popups included), light/dark, LTR/RTL;
  forced-colors + reduced-motion gates. The showcase maps its UI languages to regional
  formatting locales (`en → en-US`, `ar → ar-SA`): bare `ar` resolves to Latin digits in ICU,
  which would leave the live locale-switch assertions proving nothing. Library behavior is
  unaffected — distributions nominate their own regional locales.
- **Fixtures are committed and offline** — tiny generated files (1×1 PNGs, a minimal valid PDF,
  a beep WAV, text/CSV samples) live in the repo; no test fetches the network.
- API goldens + `api:approve` cover every new entry point; co-located `*.examples.ts` feed
  `components.json`/`llms.txt`/MCP; the boundary lint covers `l10n` (no DOM/DI) and `private`
  (excluded from goldens).

## 18. Definition of done

1. All new entry points build, lint, pass boundary lints, ship within their §16 budgets, with
   API goldens approved and `components.json`/`llms.txt`/MCP/showcase story pages updated;
   `tm-select`, `tm-menu`, and the grid's error overlay run on the §2.1 overlay helper with
   their existing suites passing unchanged.
2. `tmButton`: variants/sizes/pending render per §3 in both schemes; `type` defaults to
   `button`; pending keeps size and focus (Playwright-pinned); icon-only warning fires in dev
   mode; axe clean.
3. `textarea[tmInput]` works bound via `[formField]` inside `tm-form-field` (label, hint,
   error, focus ring, fixed rows, `resize: none`); existing `input[tmInput]` behavior is
   regression-free.
4. `tmNumber`: locale round-trip (focus-stable text, blur reformat, external writes), percent
   mode, commit rounding (model = display), the 15-significant-digit envelope rejection,
   commit-only-when-dirty (pristine focus+blur never rewrites the model), parse-error state
   with localized message, `min`/`max` kinds resolve, `inputmode="decimal"`, physical right
   alignment; it is the grid's built-in `number` editor via `TM_CELL_EDITOR_HOST` with the
   grid-owned parse path (§5), and grid number commits **and pastes** round to the column's
   `maxDecimals` and enforce the envelope — the grid suite is updated for the editor swap and
   both invariants, and green.
5. `tm-date-picker` value integrity: the value is always ISO `YYYY-MM-DD`; bounds
   `0001-01-01`–`9999-12-31` enforced; `tmMinDate`/`tmMaxDate` validate and localize per §15;
   no `Date` objects in the public API.
6. Date parsing per §6.3: locale field order, active-calendar interpretation, month names,
   completion-from-today, two-digit pivot, ISO fast path, and every rejection rule — unit-
   covered per locale × calendar; parse errors show the expected-pattern message.
7. Date popup per §6.4: APG keyboard matrix green; view ladder (day↔month↔year, opening on the
   day view and drilling down); focus moves in on open and back on close; fixed six-row grid with
   empty out-of-month cells and no resize across months or views; Today/Clear; navigation reaches
   out-of-range months and renders their cells disabled; month/year heading announces politely.
8. Calendars: Gregorian default via `TM_CALENDAR`; Umm al-Qura and Ethiopic entry points
   register via `provideTmCalendar`; runtime calendar switch re-renders text and popup with the
   model unchanged; Ethiopic shows 13 selectable months incl. leap Pagume; Umm al-Qura window
   documented; month/era names verified to come from Intl (no bundled tables); the
   implementation↔Intl agreement gate and the committed formatting golden (§2.2, §17) pass.
9. Grid `date` columns: built-in format/parse defaults active (consumer overrides still win);
   `tm-date-picker` is the built-in editor (cell-anchored popup, `Alt+ArrowDown`, two-stage
   Esc, type-to-edit seeding, pick-commits-the-cell); cells re-render in place on an ambient
   locale or calendar switch; the grid showcase's editable story includes a date column
   exercising typing, popup, paste, and invalid input.
10. `tm-image` view mode: fixed-box rendering (rect + circle), deferred fetch, decode-before-
    swap, placeholder and error states, `alt` enforced; the cache serves a matching etag with
    zero network, purges all size variants on mismatch, revalidates when `etag` is null,
    coalesces N concurrent instances into one request (Playwright-verified), survives quota
    failure uncached, and is swept by `TmClientCache.clearAll()`.
11. `tm-image` edit mode: replace (format/size guardrails, decode-failure rejection,
    downscale + EXIF, GIFs exempt from the downscale), pan/zoom fitting with pointer and
    keyboard+slider paths (re-fit of an existing image sourced from `editSrc`), delete →
    `imageChange(null)`; a new pick emits the blob + normalized `rect`+`focal` fit, a re-fit
    emits `blob: null` + fit; box size constant across modes.
12. Files: `tmFilePicker` and `tm-dropzone` share the engine and emit identical
    `TmFileSelection`; guardrails (`size`/`type`/`count`/`folder`) reject with localized,
    announced reasons; dropzone keyboard path complete; clipboard paste of files into the
    focused zone works; the document-level missed-drop guard installs with the first dropzone
    and uninstalls with the last; transport guidance in docs.
13. `tm-file-preview`: per-kind rendering per §8.4 — PDF via non-sandboxed iframe gated on
    `navigator.pdfViewerEnabled`, media via `{url}` streaming or blob with explicit play,
    SVG via `<img>` only, HTML/unknown always download-only (spec-pinned by a test); download
    always available; object URLs revoked on close.
14. Tabs: only the active panel's content is in the DOM (destroyed on deactivate;
    `preserveContent` keeps it inert-hidden); aria keyboard model + `selectionMode` both work;
    definition-directive pattern renders correctly (no NG0201); strip scrolls on overflow.
15. `tm-modal`: service-opened component/template content with `TM_MODAL_DATA` and typed
    `TmModalRef`; `closed` resolves with the correct `via` for all four dismissal paths;
    `showClose`/`backdropDismiss`/`escapeDismiss` honored; the `canDismiss` guard (sync and
    async) blocks user dismissals; sizes incl. `lg` margins and the `panelClass` hatch; focus
    trap + restore; stacked modals dismiss topmost-first with chained focus restore
    (Playwright-pinned); `tm-select` inside a modal renders above it (top-layer,
    Playwright-pinned).
16. `tm-popover` and `tmTooltip`: positioning + RTL mirroring via the shared helper; popover
    focus in/out and non-modal Tab-out close; tooltip hover/focus/long-press show, Esc
    dismiss, hoverable surface, `AriaDescriber` description present without opening; single
    open tooltip invariant.
17. `tm-alert`: four kinds themed in both schemes with visually-hidden kind prefixes;
    `live` renders `role="status"`/`role="alert"` and announces on dynamic insertion via the
    empty-then-populate live region; axe clean; forced-colors keeps kind distinction.
18. Cross-cutting: every new built-in string resolves through `TM_UI_TRANSLATE`, ships English
    in-package, and lands translated in `@tellma/locale-ar` (live locale switch re-renders);
    axe + behavioral Playwright + RTL + forced-colors/reduced-motion gates green across new
    components; all fixtures offline; worktree port isolation holds.

## Decisions record

Answers to the design brief's open questions, where not already evident above:

1. **Textarea** — extend `tmInput` to `textarea[tmInput]`; no new directive (§4). The value
   channel and chrome are identical; only sizing rules differ.
2. **Numeric input** — a new `tmNumber` directive, not a `tmInput` parameter (the Signal Forms
   value type is different) and not a component (bare-native-input grid embedding, §5).
3. **Number codec** — hoisted out of the grid into `@tellma/core-ui/l10n` unchanged (§2.2); the
   grid re-imports it, and `tmNumber` becomes the grid's built-in `number` editor with the grid
   still owning the value channel and the parse (§5).
4. **Date picker in a grid** — the calendar button is never a tab stop (`tabindex="-1"`, the
   APG combobox-datepicker precedent); the keyboard path is `Alt+ArrowDown`, extending the
   spec-0004 dropdown-cell convention (§6.2, §6.6).
5. **Arrow-key intent (input text vs calendar)** — resolved by focus location, not heuristics:
   the popup is a dialog that takes real focus on open (APG pattern), so arrows navigate days
   only while focus is in the calendar; the input keeps caret movement otherwise (§6.4).
6. **Calendar packaging** — calendars are neither baked into the component nor shipped in
   locale packs (a calendar is not a language): each non-Gregorian calendar is its own opt-in
   entry point; names come from Intl at runtime (§6.5).
7. **Calendar math** — `@internationalized/date` behind the `TmCalendar` seam for Gregorian and
   Umm al-Qura (Ethiopic is implemented directly, §6.5); native Temporal is not yet in stable
   Safari and polyfills are 3–6× heavier; the seam keeps Temporal adoption an internal swap.
8. **ISO bounds** — `0001-01-01`–`9999-12-31`, not `0000-01-01`: ISO 8601 admits year 0000 but
   .NET and SQL Server `date` do not (§6.1).
9. **Image crop privacy** — non-destructive, the industry norm: a new upload emits the original
   blob + fit metadata; a re-fit of an existing image emits fit metadata alone (`blob: null`),
   so the client never regenerates bytes and a rendition can never overwrite the original.
   Whether the server retains originals is backend policy; crop is documented as presentation,
   not redaction (§7.1, §7.5).
10. **Fit metadata vs future dimension changes** — normalized `rect` (exact re-edit) plus
    `focal` (automatic recrop at any future aspect), both 0..1 relative to the original (§7.1).
11. **Image cache storage** — the Cache API, not IndexedDB/localStorage: Response+headers
    (ETag rides in the entry), streaming, origin-scoped, shared-quota eviction (§7.2).
12. **ETag races** — no serial ETags needed: entries are coherent `(blob, etag)` pairs and
    compare-and-replace is idempotent; a per-tab single-flight map dedupes concurrent fetches;
    cross-tab locking is deliberately omitted (§7.2).
13. **Global cache cleanup** — convention over registration: every library store is
    `tm-`-prefixed and `TmClientCache.clearAll()` (primary entry point) sweeps them on logout;
    cache keys carry tenant identity (tenants share an origin), so tenant switching clears
    nothing (§2.3).
14. **Image/file transport** — multipart/form-data with a JSON DTO part + binary parts is the
    documented platform standard for atomic record-with-attachments saves; base64-in-JSON only
    for tiny payloads (§8.3). The components stop at `File`/`Blob` outputs.
15. **File upload vs drop zone** — two surfaces (`tmFilePicker` directive, `tm-dropzone`
    component) over one selection engine with one output contract (§8.1).
16. **Preview embedding** — no sandboxed-iframe strategy: PDFs use the browser's trusted viewer
    in a non-sandboxed iframe (sandboxed iframes cannot render PDFs, per the HTML spec); SVG
    previews via `<img>`; HTML/unknown are never rendered because blob URLs inherit the app
    origin (§8.4). Auth never touches the iframe: all bytes flow through app code (blob URLs),
    and `{url}` streaming sources are reserved for ambient-auth setups (BFF cookies/presigned).
17. **Preview print/download** — built-in PDF viewer chrome is used as-is (no duplicate
    controls); the component adds Download always and Print for images only (§8.4).
18. **Test fixtures** — committed, generated, offline; CI never downloads samples (§17).
19. **Grid error popover** — not converted to `tm-popover` (it stays non-interactive grid
    chrome), but its overlay wiring migrates onto the shared `private/` helper along with
    `tm-select` and `tm-menu` (§2.1, §11).
20. **Form/grid error messages in alert wrappers** — not migrated; `tm-alert` is page/section-
    level messaging, field errors keep the proven compact pattern (§13).
21. **Modal foundation** — `@angular/cdk/dialog` (aria has no dialog pattern); dropdowns inside
    modals paint above via the top-layer popover mechanism (§10).
22. **Tabs foundation** — `@angular/aria/tabs` with the definition-directive rendering pattern;
    active-only DOM via aria's deferred content, `preserveContent` opt-out per tab (§9).
23. **Formatting is a cross-platform contract** — a closed option set mapping to fixed CLDR
    skeletons and fraction-digit bounds so a C# backend reproduces the client's output from the
    same parameters; consistency, not byte equality (§2.2).
24. **Rounding invariant** — model = display everywhere: `tmNumber` and grid `number` columns
    both round commits and pastes to `maxDecimals` (§5).
25. **Attachment transport** — two sanctioned paths, chosen per screen: inline multipart save
    (atomic, no cleanup) and staged upload (fast saves, TTL + GC lifecycle) (§8.3).
26. **Modal sizing** — semantic buckets plus a `panelClass` escape hatch, the prevailing
    component-library pattern; buckets keep Tellma modals uniform (§10).
27. **Decimal↔double boundary** — `value` stays `number | null`; exactness against the
    backend's `decimal` storage is guaranteed by the per-value 15-significant-digit envelope
    (§5) plus the single-rounding rule (§2.2); the server owns storage scale and authoritative
    aggregates; an exact string-backed channel is deferred to the future currency control.
