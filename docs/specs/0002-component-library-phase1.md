# Spec: UI Component Library — Phase 1 (Forms Walking Skeleton)

**Status:** Draft — for discussion. Nothing here is locked until we agree to it and the
[ARCHITECTURE.md](../../ARCHITECTURE.md) Frontend section is updated to match.

**Architecture deltas** (applied to ARCHITECTURE.md's Frontend section alongside this spec):
- **D9 → Signal Forms only.** Drop the `ControlValueAccessor` dual-compat requirement. Signal Forms
  is stable in Angular v22 and every consumer is greenfield v22+, so the CVA fallback buys nothing.
- **D6 → CSS-variable theming, no builder.** No theme-builder UI is planned, ever. Themes are the
  emitted CSS custom properties; overrides are authored as CSS (or set at runtime on a scope). Drop
  the `dt()`/typed-passthrough framing.
- **`@angular/aria` is stable in v22** (graduated from developer preview) — the open "aria maturity"
  question in ARCHITECTURE.md is resolved; we build on it.
- **Inline templates for small components.** The Angular CLI MCP's `get_best_practices` is the
  source of truth for framework conventions and takes precedence over the research doc, so D5's
  external-template preference is superseded: small components (all of Phase 1) use inline templates;
  external `.html` is reserved for larger components with rich named slots.

## Context

[ARCHITECTURE.md → Frontend → UI component library](../../ARCHITECTURE.md) commits the platform
to a greenfield Angular component library, shipped as a `core-*` package family every
distribution references, built on `@angular/cdk` + `@angular/aria`, signal-first, `tm-`-prefixed.
The rationale and the Material/PrimeNG comparison behind every decision live in
[`docs/research/angular-component-library-analysis.md`](../research/angular-component-library-analysis.md)
(decisions **D1–D13**). The default look — colors, type, spacing, the shared form-field token
group, focus ring, dark mode, RTL/Arabic posture — is fixed by the Tellma design system
(`tellma-brand/design-system`, especially `tokens/*.css` and the `forms/` reference components).

This spec covers **Phase 1**: a *walking skeleton* — the thinnest end-to-end slice that stands
up the whole architecture (all five packages, the headless/styled split, the token emitter, the
forms contract, harnesses, the a11y/RTL/perf gates, the docs/MCP pipeline) while shipping only
**three production components**:

1. **Text input** — single-line text field.
2. **Checkbox** — boolean / tri-state choice.
3. **Select** — single-select dropdown (a listbox in an overlay panel).

Plus the one piece of scaffolding all three need to be usable: **`tm-form-field`** (label /
required marker / hint / error). The architecture analysis names `tm-form-field` the single most
underweighted-yet-essential component, so it is in scope as supporting infrastructure, not as a
fourth headline component.

### Why these three (the de-risking rationale)

A walking skeleton exists to **force every load-bearing architectural seam to be built once**, on
the thinnest possible slice, so the risky integrations are proven before 40 later components are
templated on top of them. The set is chosen to maximize *distinct hard-seam coverage*, not feature
breadth. Each of the three pierces a seam the others do not:

- **Text input** — the foundational seam: the `tm-form-field` contract, the Signal Forms
  custom-control binding, the base field. Irreducible; everything text-like descends from it.
- **Checkbox** — a *custom-rendered binary control* (hidden native input + styled box), distinct in
  DOM shape from text, owning tri-state ARIA (`aria-checked="mixed"`). It is the template for a
  whole family — radio, toggle/switch — at near-zero marginal cost.
- **Select** — the high-leverage seam the flat controls miss entirely: **CDK Overlay + portal +
  flexible RTL-aware positioning**, **`@angular/aria` listbox/combobox** (which validates the central
  build-on-aria decision of D1/D4), **keyboard navigation + typeahead + active-descendant a11y**, and
  a *collection* harness rather than a single-value one. This infra is reused by autocomplete, date
  picker, details picker, menu, popover, and **every dropdown editor in the future data grid**.
  Select also stress-tests the grid-embedding contract (rule 6) harder than any flat control: an
  overlay anchored to a cell, with Esc/commit/Tab interplay against grid navigation, is the case
  that actually shapes the cell-editor design.

The point of Phase 1 is **not** breadth. It is to prove the spine — pattern class → styled adapter
→ tokens → Signal Forms → harness → axe/RTL/bundle gates → generated docs — works once, across both
a flat control *and* an overlay/collection control, so every later component is a fill-in-the-blank
exercise against an established template.

### Guiding rules (from the task brief)

These are acceptance constraints, not aspirations. Every component below is checked against them:

1. **Fast and smooth to render, especially on low-cost mobile.** No unnecessary deep object
   hierarchy; minimal DOM per control; zoneless + OnPush (the v22 default); overlays created lazily;
   no per-keystroke layout thrash.
2. **Accessibility to WCAG 2.1 AA.** Verified by axe-core in CI, not by inspection.
3. **Fluent on mobile and touch.** ≥44×44 CSS px touch targets (with a visually smaller control
   where the brand calls for it), no hover-only affordances, no `:hover` traps.
4. **Native LTR and RTL.** CSS logical properties only; direction from CDK `Directionality`;
   overlay positioning mirrors automatically; no per-component `rtl` flag.
5. **Unit and e2e testable.** Component harnesses shipped from day one; deterministic, framework-
   independent automation surface.
6. **Forward-compatible with an Excel-like editable data grid.** The behavior layer must be
   embeddable in a grid cell (external value ownership, delegated keyboard, commit/cancel, no
   self-owned focus trap; overlay anchored to a cell) and must expose a cheap **readonly
   presentation** so the grid can paint thousands of non-edited cells as plain DOM. The grid itself
   is out of scope. See [§9](#9-data-grid-forward-compatibility-contract).

### Simplifying assumptions (and what they let us cut)

Two facts about the consumer set let us delete complexity the general-purpose libraries carry:

- **All consumers are greenfield Tellma apps on Angular v22+.** ⇒ Signal Forms only (no CVA), no
  NgModule support, no legacy-browser polyfills, zoneless + OnPush assumed, signal APIs throughout,
  a single narrow Angular peer-dependency range. The library tracks the platform's single pinned
  Angular version rather than supporting a matrix.
- **The first 4–6 distributions live inside `tellma-platform`** (split to their own repos later).
  ⇒ Phase 1 consumes the UI packages through Angular **workspace path mappings** (project
  references), not a published-package / local-feed flow — a much faster inner loop. The
  prerelease-versioning + local-feed + cross-repo dependabot machinery is **postponed** until the
  distribution split, *without* compromising package boundaries: the packages stay independently
  buildable and publishable so the split is a mechanical change, not a redesign.

## Goals / Non-goals

**Goals**

- Stand up all five `@tellma/core-ui*` packages with real (if small) contents and working build,
  test, lint, and docs pipelines.
- Ship `tmInput`, `tm-checkbox`, `tm-select`, and `tm-form-field` to production quality:
  a11y-complete, RTL-complete, themed from the brand tokens, Signal-Forms-native.
- Prove the shared overlay/positioning + aria-listbox + keyboard-navigation infrastructure once,
  via Select, so later overlay/collection components reuse it.
- Establish the canonical `Tm*Pattern` headless template, the styled-adapter template, the harness
  template, and the `*.stories.ts` → `components.json` docs template that every later component
  copies.
- Encode the brand design tokens into the typed `TmTokens` contract + emitter, with one default
  preset that reproduces `tellma-brand/design-system` and a build-time WCAG-contrast gate.

**Non-goals (explicitly deferred)**

- All other components (numeric, currency, textarea, **date picker**, details picker, data grid,
  radio, toggle, multi-select, autocomplete, buttons, layouts, nav, modal, menu, popover, etc.).
- Multi-select, option groups, and virtual scroll for long option lists. Phase-1 Select is
  single-select with a flat option list; the pattern is shaped not to preclude these.
- The `TmNumberAdapter`/`TmDateAdapter` and the components that need them (numeric, date picker). A
  **date picker is its own future component** — a dropdown-calendar overlay built on the same
  CDK-Overlay + aria infra Select establishes; the `TmDateAdapter` is the multi-calendar
  (Gregorian/Hijri/Ethiopian) abstraction that picker *depends on*, not a text-field substitute for
  it. See [§7](#7-rtl-i18n--l10n).
- The full `provideTellmaForms()` cross-field policy engine beyond what these three controls need.
- The federated `dotnet tellma mcp` umbrella (D13). Phase 1 ships the *scoped* `@tellma/core-ui-mcp`
  as a thin, generated server.
- Density and typography as **runtime-switchable axes** (a compact/comfortable density knob and a
  swap-the-type-scale-at-runtime axis, à la Material's density system). Phase 1 ships the tokens and
  a single default density/type scale; the *system that toggles them at runtime* is a later, larger
  piece of work and is not needed to prove the spine. **Design requirement: they must be addable
  later without a major refactor.** Both are modelled as token sets switched by CSS variables (the
  same runtime-override mechanism as themes, [§4](#4-tokens--theming-tellmacore-ui-tokens)), every
  component sizes itself from density/type tokens rather than hardcoded values, and the per-control
  `size` input ([§3.2](#32-text-input--tminput)) already exercises the static variant path — so the
  runtime axes drop in as additional token sets without touching component internals.

There is **no theme-builder UI**, now or later. Theming is done by authoring CSS custom properties
(the emitted token variables) — see [§4](#4-tokens--theming-tellmacore-ui-tokens).

## 1. Package & build skeleton

All five packages are created under `client/projects/core/` exactly as ARCHITECTURE.md lays out.
Phase 1 puts real contents in four and a stub-but-wired version in the fifth (`-mcp`).

| Package | Phase-1 contents |
|---|---|
| `@tellma/core-ui-primitives` | `SignalLike`; base pattern utilities; `TmTextFieldPattern`, `TmCheckboxPattern`, `TmSelectPattern`; the `TmFormFieldControl`, `TmCellEditor`, and `TmCellDisplay` contract interfaces. No `@angular/core` import. |
| `@tellma/core-ui` | `tmInput` directive; `tm-checkbox`; `tm-select` + `tm-option` (overlay panel via CDK Overlay, listbox via `@angular/aria`); `tm-form-field`; `provideTellmaForms()`; the static base CSS; the self-hosted default fonts + `@font-face` ([§7.1](#71-fonts--web-font-loading)). The primary import. |
| `@tellma/core-ui-tokens` | `TmTokens` TS contract; the brand default preset; the `tokens → CSS variables` emitter; generated JSON Schema; build-time schema + WCAG-contrast validation. |
| `@tellma/core-ui-testing` | `TmInputHarness`, `TmCheckboxHarness`, `TmSelectHarness` (+ `TmOptionHarness`), `TmFormFieldHarness`. |
| `@tellma/core-ui-mcp` | Generated `components.json` for the four; a minimal MCP server exposing `list/describe/example` tools over it. Wired into the build; tool breadth is later. |

**Build & tooling (shared, established once):**

- pnpm workspace + **ng-packagr** per package; per-component secondary entry points;
  `"sideEffects": false`. (D1/D2 — no Bazel.)
- Angular **v22**, standalone, **zoneless**, signal-first public API (`input()`/`model()`/`output()`).
  Follow the v22 best practices: do **not** set `standalone` or `OnPush` explicitly (both default in
  v22); host bindings live in the `host` object (never `@HostBinding`/`@HostListener`); `computed()`
  for derived state; `inject()` over constructor injection; `@Service` for new singletons; native
  control flow; no `ngClass`/`ngStyle`.
- Depends on `@angular/cdk` (Overlay, Portal, a11y, Directionality), `@angular/aria` (listbox/
  combobox + harnesses, stable in v22), and `@angular/forms/signals` (Signal Forms) as the shared
  foundation.
- ESLint flat config + Prettier + commitlint; a custom ESLint selector rule enforcing the `tm-` /
  `Tm…` prefix (D3).
- **API goldens** per entry point via Microsoft API Extractor + an `approve-api` CI gate (D11) — see
  [§10](#10-testing-tellmacore-ui-testing).
- CI gates: unit + harness tests, **axe-core**, **bundle-size budget**, API golden, lint. Tests
  always on. (No SSR gate — distributions are client-rendered, per ARCHITECTURE.md Frontend.)

> **Note — inline templates.** Per the v22 best-practices guide (the authoritative source for
> framework conventions, which takes precedence over the research doc), small components use
> **inline templates**. All three Phase-1 components are small, so all use inline templates;
> D5's external-template preference is superseded here and reserved for larger future components
> with rich named slots.

### 1.1 Use the Angular CLI MCP during implementation

Implementers (human or agent) **must** use the Angular CLI MCP server throughout: call
`get_best_practices` (with the workspace path) before writing or changing Angular code, prefer
`ng generate` via the CLI for scaffolding, and use `search_documentation` / examples to confirm v22
APIs (Signal Forms, `@angular/aria`, `transformedValue`, etc.) rather than relying on memory. The
best-practices output is the source of truth for framework conventions; this spec defers to it.

### 1.2 Build tooling — pnpm + Angular CLI (nx deferred)

Phase 1 uses **pnpm workspaces + the Angular CLI + ng-packagr** — no nx. Nx's real wins (computation
caching, an `affected` task graph, distributed cache) matter most once the repo holds many packs and
in-repo distributions; with five packages they do not yet pay for their onboarding and tooling-sprawl
cost (the same low-onboarding argument that rejected Bazel in D1, and the Angular side already sits
beside the .NET MSBuild build). We **revisit nx** when either (a) the in-repo distribution count
grows, or (b) changed-test selection ([§10](#10-testing-tellmacore-ui-testing)) outgrows the test
runner's own `--changed`-style filtering. The package boundaries are nx-ready regardless, so adoption
later is additive.

### 1.3 Worktree-isolated, port-free tooling

Per ARCHITECTURE.md *Parallel Local Development*, every build/test/run path and any hosted tool must
run in parallel across isolated git worktrees with **no hardcoded localhost ports** and no shared
mutable global state:

- Storybook, the test runner, the MCP server, and any dev/preview server bind to an
  OS-assigned free port (or read the worktree's `.dev-ports.local`), never a literal port.
- Test artifacts, caches, and any emitted files are written under the worktree (or a per-worktree
  namespaced path), so two agents testing two worktrees never collide.
- The MCP server and Storybook are launched by scripts that follow the same free-port discovery the
  platform's `dotnet tellma setup-worktree` flow uses; nothing assumes a singleton instance.

## 2. The headless pattern layer (`@tellma/core-ui-primitives`)

Per **D4**, each component's behavior lives in a framework-decoupled `Tm*Pattern` class. The
defining rule: **the pattern has no `@angular/core` dependency.** Its inputs are `SignalLike`
(plain zero-arg getters), so it is trivially unit-testable without `TestBed` and reusable from
any host — including, later, a data-grid cell that drives it from grid state.

```ts
// An Angular signal IS a SignalLike (callable, returns its value), so the styled adapter
// passes signals straight in.
export type SignalLike<T> = () => T;
```

Each pattern exposes derived state as `SignalLike` computeds plus imperative event entry points the
host forwards DOM events to (`onKeydown`, `onInput`, `onFocus`, `onBlur`, …). It never touches the
DOM and never assumes it owns focus.

### 2.1 Shared contracts

```ts
// Lets tm-form-field wire any control generically (label[for], aria-describedby, required/invalid).
export interface TmFormFieldControl {
  readonly controlId: SignalLike<string>;
  readonly empty: SignalLike<boolean>;
  readonly required: SignalLike<boolean>;
  readonly disabled: SignalLike<boolean>;
  readonly invalid: SignalLike<boolean>;
  readonly describedByIds: SignalLike<string[]>;
  setDescribedByIds(ids: string[]): void;
  onContainerClick?(): void;
}

// Every grid-embeddable control's pattern implements this, so the grid drives them uniformly.
export interface TmCellEditor<T> {
  readonly value: SignalLike<T>;   // host-owned edit value
  commit(): void;                  // accept the edit (Enter/Tab in a grid; blur standalone)
  cancel(): void;                  // revert to last committed (Esc)
  focus(): void;
  onKeydown(e: KeyboardEvent): void; // host forwards; the editor consumes only its own keys
}

// Pure display path, no Angular instance required — lets the grid paint thousands of
// non-edited cells as plain readonly DOM (see §9).
export interface TmCellDisplay<T> {
  formatValue(value: T): string;   // e.g. select → selected option's label; text → the string
  readonlyClass?(value: T): string; // optional token-driven class for non-text glyphs (checkbox box)
}
```

### 2.2 The three patterns

- **`TmTextFieldPattern`** — value (`model`-backed), empty/dirty/touched, disabled, readonly, a
  `validate()` hook; implements `TmCellEditor<string>` and `TmCellDisplay<string>`. Keyboard is
  mostly native; `onKeydown`/`onInput` passthroughs let a grid host intercept Enter/Esc/Tab/arrows.
- **`TmCheckboxPattern`** — `checked` (`model`-backed boolean), `indeterminate`, derived
  `aria-checked` (`true`/`false`/`mixed`), `toggle()`, disabled, required; toggling clears
  indeterminate. Implements `TmCellEditor<boolean>` and `TmCellDisplay<boolean>` (readonly = a
  styled box glyph, no input).
- **`TmSelectPattern`** — the load-bearing one. State: `value` (`model`-backed, single selection),
  `open`, active-option index, the option collection (`SignalLike`, so a grid can feed per-cell
  options), and derived `empty`/`disabled`/`invalid`. Behavior: open/close, selection, keyboard
  navigation (Up/Down/Home/End/typeahead, Enter/Space select, Esc close, Alt+Down open), and
  active-descendant tracking. Built on **`@angular/aria`'s listbox/combobox** behavior (stable in
  v22). Implements `TmCellEditor<T>` (Esc closes the panel first, then cancels the edit — the Excel
  dropdown-cell behavior) and `TmCellDisplay<T>` (`formatValue` = the selected option's label).

## 3. The styled layer (`@tellma/core-ui`)

The styled component/directive is a thin adapter: it declares the Angular public API, constructs the
pattern (passing its signals straight in), binds ARIA + classes out via the `host` object, renders an
**inline template** with `@if`/`@for`/`@let` (small components, per the v22 best practice), and
implements the relevant **Signal Forms custom-control interface**
([§5](#5-forms-integration-signal-forms)). No business logic here.

### Host shape — directive on native input vs. component

Chosen per control by what the native element gives us. **Confirmed: directive-on-native for the
text input** (best a11y + native mobile/IME behavior + minimal DOM + grid-embeddable), **components
for checkbox and select**:

- **Text input → `tmInput`, a directive on the native `<input>`** (`<input tmInput>`, the `matInput`
  model). The native element *is* the control.
- **Checkbox → `tm-checkbox` component** — renders custom box + check/indeterminate glyph chrome with
  no stylable native equivalent, wrapping a visually-hidden native `<input type="checkbox">` for
  semantics.
- **Select → `tm-select` + `tm-option` component** — native `<select>` cannot host a custom overlay
  panel, rich options, or the brand styling, so Select is a custom trigger + a CDK-Overlay-mounted
  `@angular/aria` listbox.

**"Adornment chrome" and where it lives.** *Adornment chrome* = the visual furniture around the
editable element: the bordered box, the focus-ring container, the leading/trailing slots, the size
variants. Because `tmInput` is a **bare directive that adds nothing around the `<input>`** (so the
input drops into a grid cell with nothing to strip), that chrome lives in **`tm-form-field`** (or a
lighter `tm-input-shell` for a field-less adorned input). This "chrome lives in the field" pattern is
**specific to the input directive**. `tm-checkbox` and `tm-select` are components that render their
own structure, so they **own their chrome** (the checkbox box; the select trigger + overlay panel).
The throughline is the same — a *bare* behavior host with chrome supplied around it — but for the
directive the chrome is a sibling wrapper, while for the components it is internal.

### 3.1 `tm-form-field`

The shared label / required-marker / hint / error scaffold (brand `FormField`), reading the
`--field-*` token group. Generates and wires ids (`<label for>` ↔ `controlId`; hint/error ids fed
back via `setDescribedByIds`). Reads the bound control's **Signal Forms field state** to render
error (when invalid per the display policy) **or** hint, never both. Logical-property layout mirrors
in RTL. Hosts all three controls uniformly via `TmFormFieldControl`. Inputs: `label`, `hint`,
`error` (or derived from field state), `required` (mirrors the control), `size` (`sm | md | lg`).

### 3.2 Text input — `tmInput`

- **Selector:** `input[tmInput]` (`textarea[tmInput]` reserved for later).
- **API:** `value = model<string>()`; `disabled`, `readonly`, `required`, `placeholder`; `size`
  resolved from the enclosing `tm-form-field` or set directly. Implements `FormValueControl<string>`
  + `TmFormFieldControl`.
- **`size`** = the control's height/density variant, mapping to the brand field-height tokens:
  `sm` → `--field-height-sm` (30px), `md` → `--field-height` (38px, default), `lg` →
  `--field-height-lg` (46px). It is the static, per-instance density knob (distinct from a global
  density *system*, [Non-goals](#goals--non-goals)); it also adjusts padding and font-size tokens.
- **Host bindings** (in the `host` object): `--field-*` styling, the focus ring, `aria-invalid`,
  `aria-required`, `aria-describedby`, `disabled`.
- **Leading/trailing slots** = *adornments* placed before (leading) / after (trailing) the text —
  e.g. a search icon, a currency code, a clear button — supplied by **content projection** on
  `tm-form-field` / `tm-input-shell` via attribute-selector `ng-content` (`[tmPrefix]` / `[tmSuffix]`),
  not baked into the bare input. Example: `<tm-form-field><i tmPrefix data-lucide="search"></i><input
  tmInput></tm-form-field>`.

### 3.3 Checkbox — `tm-checkbox`

- **Selector:** `tm-checkbox`.
- **API:** `checked = model<boolean>()`; `indeterminate`; `disabled`, `required`; projected label;
  `value` (for groups later). Implements `FormCheckboxControl` + `TmFormFieldControl`.
- **Rendering:** visually-hidden native checkbox for semantics + the styled box (teal when
  checked/indeterminate, `--radius-xs`, check polyline / indeterminate bar), `aria-checked="mixed"`
  for indeterminate, space-to-toggle, focus ring.
- **Touch:** the clickable label+box hit area meets ≥44px (padding on the label, not a bigger box).

### 3.4 Select — `tm-select`

- **Selectors:** `tm-select` (trigger + value display) with projected `tm-option` children.
- **API:** `value = model<T>()` (single-select); `placeholder`, `disabled`, `required`,
  `compareWith` (identity fn for object values), `size`. `tm-option`: `value` + projected label
  content; outputs `selectionChange`/`opened`/`closed`. Implements `FormValueControl<T>` +
  `TmFormFieldControl`.
- **Display one property, capture another — yes.** This is the default shape: `tm-option`'s **`value`**
  is what lands in the model, its **projected content** is what the user sees. So
  `<tm-option [value]="record.id">{{ record.label }}</tm-option>` captures the id while displaying the
  label. The collapsed trigger shows the selected option's label via `TmCellDisplay.formatValue`
  (the pattern caches the selected option's label so the trigger renders correctly even before the
  panel has opened); an optional `displayWith` input covers data-bound lists where the selected label
  must be derived without a materialized option.
- **Overlay:** the panel mounts through **CDK Overlay + Portal** with a flexible connected position
  strategy anchored to the trigger — opens below, flips above when tight, repositions on scroll, and
  **mirrors automatically in RTL**. Created lazily on first open; backdrop/outside-click and Esc
  close it; focus returns to the trigger.
- **Keyboard & a11y:** `@angular/aria` combobox/listbox roles + `aria-expanded`/`aria-selected`/
  `aria-activedescendant`; full keyboard model + typeahead from `TmSelectPattern`.
- **Forward-compat (not in Phase 1, not precluded):** multi-select (value → array, option
  checkboxes), option groups, and **virtual scroll** (`cdk/scrolling` replaces the static `@for`
  without an API change).
- **Touch:** ≥44px trigger and option rows; full-width-friendly panel on narrow viewports.

## 4. Tokens & theming (`@tellma/core-ui-tokens`)

Per **D6** (as amended above), theming is a typed TS/JSON token model in three tiers (primitive →
semantic → component), emitted to CSS variables. Phase 1 builds the contract and the emitter and
ships **one default preset reproducing `tellma-brand/design-system`** — same hexes, same `--field-*`
/ `--focus-ring` / spacing / type tokens, same `[data-theme=dark]` inversion.

**Why TS/JSON tokens rather than hand-written CSS** (your OQ4): the CSS variables are still the
runtime currency — the TS layer sits *above* them and buys what raw CSS cannot:

- **Type safety** — autocomplete, and a reference to a missing token won't compile.
- **Build-time validation** — generate a JSON Schema from `TmTokens`, validate every preset against
  it **and** run a WCAG-contrast check (both light and dark) so a preset that breaks AA contrast or
  references a missing token **fails the build**. This encodes the brand's own accessibility rules
  (action-teal = teal-600 for text-on-fill; focus-ring = teal-500 for 3:1).
- **One source, many outputs** — the same contract emits the CSS variables, the JSON Schema, the
  docs/MCP metadata, and (later) a Figma sync.
- **Safe composition** — presets extend a base by typed merge, not copy-paste.
- **Agent-authorability** — an agent emits a typed object that is validated at build, not free CSS.

**Runtime theme switching (your OQ4) — yes, supported, with no rebuild.** Because tokens emit to CSS
custom properties, a distribution's settings screen (e.g. a color picker) sets the relevant
variable(s) on a scope at runtime — `document.documentElement.style.setProperty('--color-primary', …)`
or a scoped `<style>` — and every component restyles instantly. The TS contract is the *build-time
authoring/validation* layer; runtime overrides operate directly on the emitted variables. The same
contrast check can run client-side before applying a user-picked color. Dark mode is exactly this
(`[data-theme=dark]` swaps a variable set).

**Emission — static, build-time CSS (what "precompiled" means).** "Precompiled"/"static" = the
emitter runs at **library/distribution build time** and writes plain `.css` files (base component
styles + the token variables) that ship in the package and load as ordinary stylesheets. This is the
opposite of PrimeNG's runtime CSS-in-JS, which generates and injects styles in the browser on first
render (runtime cost + FOUC/SSR risk). Ours has zero runtime style-generation cost: the browser just
fetches a static sheet. A distribution's override deltas are likewise emitted at build into a static
sheet baked into its `index.html`. Runtime overrides ([above](#4-tokens--theming-tellmacore-ui-tokens))
are the one exception and are a few CSS-variable writes, not style generation.

**Brand source of truth (your OQ4, decided):** the **TS `TmTokens` contract is canonical** for the
platform; the brand CSS is a starting import. A conformance test asserting the emitted CSS matches
`tellma-brand` anchors is **deferred** (the brand is still in flux — keep it flexible for now). The
schema + WCAG-contrast gates ship in Phase 1 regardless (they don't depend on the brand).

## 5. Forms integration (Signal Forms)

Signal Forms is **stable in Angular v22** and is the only forms mechanism the library supports — no
`ControlValueAccessor`, no dual path (every consumer is greenfield v22+). Concretely:

- Each control implements the matching **Signal Forms custom-control interface** from
  `@angular/forms/signals`: `tmInput` and `tm-select` → `FormValueControl<T>`; `tm-checkbox` →
  `FormCheckboxControl`. Each exposes `value = model<T>()` (or `model.required<T>()`) as the bound
  field value; disabled/required/validation state surface through the interface's signals.
- `tm-form-field` reads the bound field's **state** (touched/dirty/valid/errors) to decide error vs
  hint and to mirror `required`/`invalid`.
- Numeric input (Phase 2) will use the stable **`transformedValue`** utility (`@angular/forms/signals`)
  for the string↔number parse/format with automatic parse-error reporting to the field — which is
  precisely why numeric is a cheap follow-up rather than skeleton-worthy.
- **`provideTellmaForms()`** (your Q10) is a root provider function that centralizes the
  cross-cutting form policy so distributions don't re-wire it per field:
  1. **Error-display policy** — *when* `tm-form-field` surfaces an error (default: field invalid and
     touched, or after a submit attempt). One place to change it app-wide.
  2. **Validation-message resolver** — maps a validator key (`required`, `minlength`, …) to a
     **localized** message, resolved through the i18n runtime ([§7](#7-rtl-i18n--l10n)), so error
     text is translated and consistent without per-control wiring.
  3. **Field defaults** — default `size`, required-marker behavior, etc.
  Phase 1 implements only what these three controls need; the cross-field engine is deferred.

## 6. Accessibility

Target **WCAG 2.1 AA**, verified by **axe-core in CI** (rule 2 / D7), not by review.

- Text input: native semantics, `aria-invalid`/`aria-required`/`aria-describedby`, label association.
  Error region referenced by `aria-describedby` (consider `aria-live="polite"`).
- Checkbox: native checkbox semantics, `aria-checked="mixed"`, space-to-toggle, clickable label.
- Select: `@angular/aria` combobox/listbox roles, `aria-expanded`/`aria-selected`/
  `aria-activedescendant`, full keyboard model, focus returned to the trigger on close. No focus
  trap (the combobox+activedescendant model keeps focus on the trigger).
- **Focus ring — "the brand teal halo, never removed without replacement"** (your Q18): the focus
  ring is the visible indicator shown when an element holds keyboard focus — here the brand's teal
  halo with a white gap (`--focus-ring`), applied on `:focus-visible`. "Never removed without
  replacement" means we never write `outline: none` (the common a11y regression that makes keyboard
  navigation invisible) unless we provide an equally-visible substitute indicator. This satisfies
  **WCAG 2.4.7 Focus Visible** and is enforced by the axe gate plus a lint check against bare
  `outline: none`.
- Forced-colors / high-contrast respected (`@media (forced-colors: active)`); `prefers-reduced-motion`
  respected (transitions are 120–280ms fades, no transforms/bounce).
- Touch targets ≥44×44 CSS px. CDK a11y utilities (`FocusMonitor`, `LiveAnnouncer`, `Directionality`)
  reused, not reinvented.

## 7. RTL, i18n & l10n

- **RTL (rule 4 / D7):** CSS **logical properties only**; direction from CDK **`Directionality`**
  (auto-detected), never a per-component `rtl` flag. Adornment order, checkbox box side, label
  alignment, and the **Select overlay's connected position** all mirror automatically. Arabic type
  uses `--font-arabic` and the larger Arabic leading from the brand tokens.
- **Runtime i18n/l10n via Transloco (your Q15/Q23).** The library's own labels (required-field
  announcement, select placeholder default, validation messages) are translated through a **runtime**
  i18n library. **Decision: standardize on Transloco** as the platform i18n runtime, consumed behind
  a *thin* one-function seam rather than the full multi-backend adapter of D8 — see the pros/cons in
  the chat discussion. Concretely: an injection token `TM_UI_TRANSLATE` resolving to
  `(key: string, params?) => Signal<string>`, with the default implementation in `@tellma/core-ui`
  backed by Transloco (scoped/lazy-loaded library strings). **A distribution on the default Transloco
  path writes zero config code** — `provideTellmaForms()` / the library's root provider wires the
  Transloco-backed default itself; the token only needs supplying to override it. The headless
  primitives never import Transloco (they stay framework/library-decoupled); only the styled layer's
  default provider does. This keeps one mechanism for the whole platform while leaving a clean swap
  point if ever needed. English + Arabic library-string presets ship in-package.
- **Adapters declared, none implemented in Phase 1.** `TmNumberAdapter` / `TmCurrencyAdapter` /
  `TmDateAdapter` (D8) are the locale/calendar seams for *later* components (numeric, currency, date
  picker — e.g. a Hijri calendar from a Locale pack). None is needed by text/checkbox/select.

### 7.1 Fonts & web-font loading

Fonts are shared by the components (via `--font-*` tokens) and the distribution shell, and the app
must run on an **isolated intranet (no font CDN)**. Strategy, optimized for low latency / fast first
text paint, and scalable to many scripts (Amharic, Japanese, Hindi, Russian, …) **without eagerly
loading all of them**:

- **Self-hosted, content-hashed `.woff2`** served from the app origin (works offline/intranet). No
  Google Fonts CDN in production. `@tellma/core-ui` ships the default bilingual families — **Noto
  Sans** (Latin) + **Noto Sans Arabic**, plus **Noto Sans Mono** for code — and the `@font-face`
  rules; the components reference only the `--font-*` tokens.
- **`unicode-range` subsetting per `@font-face`** is the key to not eagerly loading every script: the
  browser downloads a face **only when the page actually contains glyphs in that range**. Additional
  scripts are declared as separate `@font-face` blocks (or shipped by **Locale packs** —
  `@tellma/locale-am` ships Amharic, etc.) and fetched on demand; nothing for an unused script is
  ever downloaded.
- **`font-display: swap`** so text paints immediately in a fallback and swaps when the web font
  arrives (fast TTI; no invisible-text delay).
- **Preload is resolved at runtime from per-tenant locale config**, not fixed at build. A
  distribution may support any number of locales, and each tenant configures up to three and switches
  between them at runtime — so two tenants in the *same* distribution may run English+Arabic vs.
  English+Amharic. **Latin is always preloaded** (the universal fallback); once the tenant
  configuration resolves on entry, the app injects `<link rel="preload" as="font" crossorigin>` for
  exactly the script subsets that tenant's configured locales need (Arabic, Amharic, …). Other
  scripts are never preloaded and only fetch on demand via `unicode-range` if their glyphs appear.
  A tenant entering an English+Amharic workspace preloads Amharic; an English+Arabic tenant in the
  same distribution preloads Arabic; neither pays for the other. The font assets a distribution can
  preload from are the union of its installed Locale packs.
- **Variable fonts** where available, to cut file count/weight (one file spans weights).
- **Long-cache immutable** (content-hashed filenames, `Cache-Control: immutable, max-age=1y`) plus
  the PWA service-worker cache, so repeat loads are instant (ARCHITECTURE.md *Performance*).

## 8. Performance budget

- **Zoneless + OnPush** (the v22 default; not set explicitly). Signal-driven, so only the changed
  control re-renders.
- **Minimal DOM:** text = one `<input>` + the field wrapper only when labelled; checkbox = label +
  hidden input + one box; select trigger = one element, and the **overlay panel is created lazily on
  first open** and torn down on close — closed selects cost nothing.
- **Long option lists:** `@for` + `track` now; `cdk/scrolling` virtual scroll drops in later without
  an API change.
- **Bundle budget** per entry point in CI; `sideEffects:false` + per-component entry points keep
  tree-shaking honest; CDK Overlay is pulled in only by the `select` entry point, so text/checkbox-only
  apps don't pay for it. Budgets are set when the first build exists (recorded in the package README).
- Static, build-time token/base CSS — no runtime style generation ([§4](#4-tokens--theming-tellmacore-ui-tokens)).

## 9. Data-grid forward-compatibility contract

The editable Excel-like data grid is out of scope, but Phase 1 must not foreclose it (rule 6). Two
codified contracts make every Phase-1 control grid-ready:

- **`TmCellEditor<T>`** ([§2.1](#21-shared-contracts)) — the *edit* path. Defined as a TS interface
  (your Q25) so every grid-able control's pattern implements commit/cancel/focus/keydown **uniformly**.
  Guarantees: external value ownership (the grid owns the model), **no self-owned focus trap or
  document-level listeners** (the grid owns Tab/Enter/Esc/arrow navigation and forwards only what the
  cell editor consumes), and explicit `commit()`/`cancel()` (Enter/Tab commit, Esc cancels; for
  Select, Esc closes the panel first, then cancels — the Excel dropdown-cell behavior). The Select
  overlay anchors to an arbitrary element (a cell rect) via the same connected-position strategy a
  grid dropdown editor needs.
- **`TmCellDisplay<T>`** ([§2.1](#21-shared-contracts)) — the *readonly* path, enabling the
  optimization you describe (your Q26): a virtualized grid renders **every non-edited cell as plain,
  non-interactive DOM** (a formatted value in a `<span>`, a token-styled checkbox-glyph instead of a
  real checkbox) and instantiates the full interactive control **only for the one cell being
  edited**. This is a standard, very worthwhile technique (ag-Grid/Excel), and it is **cleanly
  supportable** because each control already separates a *pure display formatter* (`formatValue`, and
  an optional token-driven `readonlyClass` for non-text glyphs) from its interactive behavior. The
  grid calls `formatValue` to paint thousands of cells with zero component instances, then swaps in
  the live editor on entering edit mode. Phase 1 ships and tests these interfaces on all three
  controls; no grid-specific code ships.

A short "embedding a control in a cell" note goes in each component's docs to keep the contract
visible.

## 10. Testing (`@tellma/core-ui-testing`)

- **Component harnesses** (D11/D16) for all four: `TmInputHarness`, `TmCheckboxHarness`,
  `TmSelectHarness` (+ `TmOptionHarness` — a *collection* harness: open the panel, list/select
  options, read the active option) and `TmFormFieldHarness`. Built on the CDK harness infrastructure
  (and `@angular/aria`'s shipped harnesses for the listbox). This is the template every later
  component copies.
- **API goldens (your Q14)** — for each entry point, **API Extractor** emits a `*.api.md` "golden": a
  human-readable, diff-able snapshot of the complete public API surface (every export, signature, and
  type), committed to the repo. A PR that changes the public API shows up as a golden diff in review,
  so drift is never silent — which matters when agent-generated code depends on a stable surface.
- **`approve-api` CI gate (your Q13)** — CI re-extracts the API and compares it to the committed
  golden; **if they differ, CI fails**. To land an intended API change, a maintainer runs the
  `approve-api` script to regenerate and commit the golden, making every public-API change an
  explicit, reviewed act rather than an accident.
- **Unit tests** per component (zoneless test env): value flow via Signal Forms, validity/touched,
  indeterminate, and — for Select — open/close, keyboard nav, typeahead, selection, `compareWith`,
  Esc/outside-click close.
- **axe-core** specs per component (including the open Select panel); **RTL specs** (mirrored layout,
  checkbox side, and Select overlay position).
- **e2e:** Storybook stories driven by Playwright for keyboard + touch + overlay flows on a real
  browser (Storybook is the only showcase surface — [§11](#11-docs--mcp-pipeline-tellmacore-ui-mcp)).
- **Changed-test selection (your Q27).** CI runs only the tests whose code changed: on PRs, the test
  runner's `--changed`-against-merge-base filtering (per package), **plus** the direct consumers of
  any changed package (so a primitives change re-tests the styled layer); on `main`/release, the full
  suite always runs (changed-only can miss cross-package breakage). This is the pnpm + Angular CLI
  path; if it proves insufficient as the repo grows, nx `affected` is the upgrade
  ([§1.2](#12-build-tooling--pnpm--angular-cli-nx-deferred)).
- Tests are **on in CI** (D16).

## 11. Docs & MCP pipeline (`@tellma/core-ui-mcp`)

Per **D12/D13**, docs are generated from source as a single source of truth. The Phase-1 showcase is
**Storybook only** (your OQ5) — no dedicated showcase app, and the sample distribution is out of
scope for now.

- Co-located `*.stories.ts` per component (runnable demos + interaction tests) + a short narrative `*.md`.
- API Extractor (`.api.json`) + a thin extractor → **`components.json`** (single source of truth).
- `components.json` feeds: the Storybook showcase, `llms.txt`, the scoped **`@tellma/core-ui-mcp`**
  server (Phase 1: `list` / `describe` / `example` tools), and the API goldens.
- The federated `dotnet tellma mcp` umbrella is not in Phase 1.

## 12. Phase-1 directory layout

```
client/projects/core/
├── tellma-core-ui-primitives/
│   └── src/lib/
│       ├── signal-like.ts
│       ├── contracts/         # TmFormFieldControl, TmCellEditor, TmCellDisplay
│       ├── text-field/tm-text-field.pattern.ts
│       ├── checkbox/tm-checkbox.pattern.ts
│       └── select/tm-select.pattern.ts        # on @angular/aria listbox/combobox
├── tellma-core-ui/
│   └── src/lib/
│       ├── form-field/        # tm-form-field (inline template; .css if styles externalized)
│       ├── input/             # tmInput directive (+ tm-input-shell)
│       ├── checkbox/          # tm-checkbox (inline template)
│       ├── select/            # tm-select + tm-option (inline template; CDK Overlay panel)
│       ├── forms/             # provideTellmaForms(), field-state helpers, message resolver
│       ├── i18n/              # TM_UI_TRANSLATE token + Transloco-backed default
│       └── fonts/             # @font-face + self-hosted woff2 (default Latin + Arabic + Mono)
├── tellma-core-ui-tokens/
│   └── src/lib/
│       ├── contract/          # TmTokens types
│       ├── presets/tellma-default.ts
│       ├── emit/              # tokens → CSS emitter
│       └── schema/            # generated JSON Schema + validators (contrast, missing-ref)
├── tellma-core-ui-testing/
│   └── src/lib/               # the harnesses (incl. TmSelectHarness + TmOptionHarness)
└── tellma-core-ui-mcp/
    └── src/                   # generated components.json + minimal MCP server
```

Storybook config lives in the workspace (free-port launch per [§1.3](#13-worktree-isolated-port-free-tooling)).

## 13. Definition of done

1. All five packages build, lint (incl. the `tm-` selector rule), and are consumable by an in-repo
   app via workspace path mappings.
2. `tmInput`, `tm-checkbox`, `tm-select`, `tm-form-field` work in a **Signal Form**, themed from the
   brand default preset, in light and dark, LTR and RTL.
3. Each component: unit tests green, harness shipped, **axe clean** (incl. the open Select panel),
   **RTL spec green** (incl. mirrored overlay position), within the bundle budget.
4. Select proves the shared infra: CDK Overlay panel opens/flips/repositions and mirrors in RTL;
   `@angular/aria` listbox keyboard model + typeahead + `aria-activedescendant`; lazy overlay.
5. `tm-select` captures `tm-option.value` (e.g. a record id) while displaying projected label content.
6. The token preset passes the schema + WCAG-contrast gate in both color schemes; runtime CSS-variable
   override demonstrated (a setProperty changes `--color-primary` live).
7. `components.json` is generated; the scoped MCP server answers `list`/`describe`/`example`;
   `llms.txt` and a Storybook showcase render.
8. API goldens committed; `approve-api` gate active.
9. The grid contracts are demonstrated by tests: a bare `<input tmInput>` mounts with no
   `tm-form-field`, driven via `TmCellEditor.commit()`/`cancel()` with no document-level listeners; a
   `tm-select` panel anchors to an arbitrary element with two-stage Esc; and `TmCellDisplay.formatValue`
   renders a readonly cell for each control with no component instance.
10. Default fonts are self-hosted woff2 with `unicode-range` subsetting + `font-display: swap`; no CDN
    reference; an unused script downloads nothing.
11. All tooling (Storybook, tests, MCP server) runs on OS-assigned free ports — two worktrees in
    parallel, no collision.

## Decisions confirmed

The earlier open questions are now settled:

1. **Repo home** — the UI family lives in `client/projects/core/` in `tellma-platform`.
2. **Build tooling** — pnpm + Angular CLI for Phase 1; nx revisited later if the in-repo project
   count or changed-test needs grow ([§1.2](#12-build-tooling--pnpm--angular-cli-nx-deferred)).
3. **i18n** — standardize on Transloco behind the thin `TM_UI_TRANSLATE` escape-hatch token; the
   default Transloco path is **zero-config** for distributions ([§7](#7-rtl-i18n--l10n)).
4. **Density/typography runtime axes** — deferred, but a design requirement that they be addable
   later without a major refactor (token-set switching; no component-internal changes).
5. **Showcase** — Storybook only; sample distribution and any showcase app are out of scope for now.
6. **Templates** — inline for these small components (v22 best practice supersedes D5 here).

No open questions remain for Phase 1. Implementation can proceed against this spec.
```
