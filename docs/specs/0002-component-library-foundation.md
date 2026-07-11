# Spec: UI Component Library — Forms Foundation

- **Author:** Ahmad Akra
- **Date:** 29 June 2026

**Status:** Foundation specification — a frozen, **historical** record of the design and its reasoning
at implementation time. It is not updated as the code or its dependencies evolve; it captures the
original intent, not the current state. The research analysis that preceded it is superseded here.

**Departures from the research analysis's locked decisions** (superseded where they conflict):
- **D12 → no Storybook.** `@storybook/angular` has no Angular 22 support, and running it on
  peer-override workarounds adds fragility without value over an in-repo host. The showcase surface is
  the internal **showcase app** (`client/projects/internal/showcase`, dev-only, never published), which
  doubles as the Playwright/axe target; co-located `*.examples.ts` files feed the docs pipeline
  ([§11](#11-docs--mcp-pipeline-tellmacore-ui-mcp)). Re-evaluate Storybook when it ships official
  Angular 22 support.
- **D9 → Signal Forms only.** Drop the `ControlValueAccessor` dual-compat requirement: Signal Forms is
  stable in v22 and every consumer is greenfield v22+, so the CVA fallback buys nothing.
- **D6 → CSS-variable theming, no builder.** No theme-builder UI, ever. Themes are the emitted CSS
  custom properties; overrides are authored as CSS (or set at runtime on a scope). Drop the
  `dt()`/typed-passthrough framing.
- **`@angular/aria` is stable in v22** (graduated from developer preview) — resolves the analysis's open
  "aria maturity" question.
- **Inline templates for small components.** The Angular CLI MCP's `get_best_practices` is the source of
  truth for framework conventions and supersedes D5: all Phase-1 components are small and use inline
  templates; external `.html` is reserved for larger components with rich named slots.

## Context

The platform builds a greenfield Angular component library, shipped as a `core-*` package family every
distribution references — built on `@angular/cdk` + `@angular/aria`, signal-first, `tm-`-prefixed. The
rationale and the Material/PrimeNG comparison live in the research analysis at
[`docs/research/angular-component-library-analysis.md`](../research/angular-component-library-analysis.md)
(its locked decisions cited below as **D1–D13**). The default look — colors, type, spacing, the
form-field token group, focus ring, dark mode, RTL/Arabic posture — is fixed by the Tellma design system
(`tellma-brand/design-system`, especially `tokens/*.css` and the `forms/` reference components).

Phase 1 is the **foundation**: the thinnest end-to-end slice that stands up the whole architecture (the
four core packages plus the reference locale pack, the `@angular/aria` behavior layer, the token emitter,
the forms contract, harnesses, the a11y/RTL/perf gates, the docs/MCP pipeline) while shipping only
**three production components**:

1. **Text input** — single-line text field.
2. **Checkbox** — boolean / tri-state choice.
3. **Select** — single-select dropdown (a listbox in an overlay panel).

Plus the scaffolding all three need: **`tm-form-field`** (label / required marker / hint / error). Every
form control depends on it for labelling, the required marker, and error display, so it is in scope as
supporting infrastructure, not a fourth headline component.

### Why these three (the de-risking rationale)

The foundation forces every load-bearing architectural seam to be built once, on the thinnest slice, so
the risky integrations are proven before 40 later components are templated on them. The set maximizes
*distinct hard-seam coverage*, not breadth — each pierces a seam the others do not:

- **Text input** — the foundational seam: the `tm-form-field` contract, the Signal Forms custom-control
  binding, the base field. Everything text-like descends from it.
- **Checkbox** — a custom-rendered binary control (hidden native input + styled box), distinct in DOM
  shape from text, owning the tri-state (the native `.indeterminate` IDL property, which the browser
  surfaces as `checked="mixed"`). Template for radio and toggle/switch at near-zero marginal cost.
- **Select** — the high-leverage seam the flat controls miss: **CDK-Overlay connected positioning
  composed with aria's inline-deferred popup** (the riskiest seam, validated by a spike — see
  [§3.4](#34-select--tm-select)), the **`@angular/aria` listbox/combobox** (validating the build-on-aria
  decision, D1/D4), keyboard nav + typeahead + active-descendant a11y, and a *collection* harness. This
  infra is reused by autocomplete, date picker, entity picker, menu, popover, and every dropdown editor
  in the future data grid — which is why proving it once, now, matters. The reuse is concrete code, not
  just shared concepts: the overlay/aria wiring of [§3.4](#34-select--tm-select) and the pure
  value→key / label helpers ([§2.1](#21-shared-contracts)) are extracted as shared functions/directives
  later components import directly; each component's own API, template, and chrome are written per
  component against the established pattern. Select also stress-tests grid embedding (rule 6) harder than
  any flat control: an overlay anchored to a cell, with Esc/commit/Tab against grid navigation, is the
  case that shapes the cell-editor design.

The point is to prove the spine — component (+ `@angular/aria` where needed) → tokens → Signal Forms →
harness → axe/RTL/bundle gates → generated docs — once, across a flat control *and* an overlay/collection
control, so every later component is a fill-in-the-blank exercise against an established template.

### Guiding rules (from the task brief)

Acceptance constraints, not aspirations; every component is checked against them:

1. **Fast and smooth, especially on low-cost mobile.** Minimal DOM per control; zoneless + OnPush (the
   v22 default); overlays created lazily; no per-keystroke layout thrash.
2. **Accessibility to WCAG 2.1 AA.** Verified by axe-core in CI, not by inspection.
3. **Fluent on mobile and touch.** Adequately sized touch targets (the conformance rule is the WCAG-2.2
   AA **24×24** minimum, *not* 44px — see [§6](#6-accessibility)); no hover-only affordances.
4. **Native LTR and RTL.** CSS logical properties only; direction from CDK `Directionality`; overlay
   positions mirror via `Directionality` and are tested under RTL (not assumed); no per-component `rtl`
   flag.
5. **Unit and e2e testable.** Component harnesses from day one; deterministic, framework-independent
   automation surface.
6. **Forward-compatible with an Excel-like editable data grid.** The behavior layer must be embeddable in
   a grid cell (external value ownership, delegated keyboard, commit/cancel, no self-owned focus trap;
   overlay anchored to a cell) and expose a cheap **readonly presentation** so the grid paints thousands
   of non-edited cells as plain DOM. The grid is out of scope. See
   [§9](#9-data-grid-forward-compatibility-contract).

### Simplifying assumptions (and what they let us cut)

Two facts about the consumer set delete complexity the general-purpose libraries carry:

- **All consumers are greenfield Tellma apps on Angular v22+.** ⇒ Signal Forms only (no CVA), no
  NgModules, no legacy-browser polyfills, zoneless + OnPush, signal APIs throughout, a single narrow
  Angular peer range. The library tracks the platform's single pinned Angular version, not a matrix.
- **The first 4–6 distributions live inside `tellma-platform`** (split to their own repos later). ⇒
  Phase 1 consumes the UI packages through Angular **workspace path mappings** (project references), not
  a published-package / local-feed flow — a much faster inner loop. The prerelease-versioning +
  local-feed + cross-repo dependabot machinery is **postponed** to the split, *without* compromising
  package boundaries: the packages stay independently buildable and publishable, so the split is
  mechanical, not a redesign.

## Goals / Non-goals

**Goals**

- Stand up all four `@tellma/core-ui*` packages plus the reference `@tellma/locale-ar` pack, with real
  (if small) contents and working build, test, lint, and docs pipelines.
- Ship `tmInput`, `tm-checkbox`, `tm-select`, and `tm-form-field` to production quality: a11y-complete,
  RTL-complete, brand-themed, Signal-Forms-native.
- Ship **`@tellma/locale-ar`** as the **reference locale pack**, proving the locale-pack seam end-to-end
  rather than leaving it on paper (a locale's library strings + self-hosted font subset +
  `@font-face` stylesheet, installed per distribution — [§7](#7-rtl-i18n--l10n)/[§7.1](#71-fonts--web-font-loading)).
  Arabic is the natural first pack: the brand is RTL-first and Tellma's primary markets (KSA/UAE) need it
  on day one. **The core stays English-only** ([§7](#7-rtl-i18n--l10n)); Arabic ships as a separate,
  installable package — that separation is the thing being proven.
- Prove the shared overlay/positioning + aria-listbox + keyboard-navigation infrastructure once, via
  Select, so later overlay/collection components reuse it.
- Establish the canonical component template, the harness template, and the
  `*.examples.ts` → `components.json` docs template every later component copies.
- Encode the brand tokens into the typed `TmTokens` contract + emitter, with one default preset
  reproducing `tellma-brand/design-system` and a build-time schema + missing-ref gate.

**Non-goals (explicitly deferred)**

- All other components (numeric, currency, textarea, **date picker**, entity picker, data grid, radio,
  toggle, multi-select, autocomplete, buttons, layouts, nav, modal, menu, popover, etc.).
- Multi-select, option groups, and virtual scroll for long option lists. Phase-1 Select is single-select
  with a flat option list, shaped not to preclude these.
- The `TmNumberAdapter`/`TmDateAdapter` and the components needing them (numeric, date picker). The date
  picker is its own future component — a dropdown-calendar overlay on the same CDK-Overlay + aria infra
  Select establishes; `TmDateAdapter` is the multi-calendar (Gregorian/Hijri/Ethiopian) abstraction it
  depends on, not a text-field substitute. See [§7](#7-rtl-i18n--l10n).
- **Locale packs beyond Arabic** (Amharic, any other script/language) — mechanical follow-ons against the
  reference `@tellma/locale-ar` template, not foundation work. (`@tellma/locale-ar` itself **is** in
  scope — see Goals.)
- The full `provideTellmaForms()` cross-field policy engine beyond what these three controls need.
- The federated `dotnet tellma mcp` umbrella (D13). Phase 1 ships the scoped `@tellma/core-ui-mcp` as a
  thin, generated server.
- Density and typography as **runtime-switchable axes** (a compact/comfortable density knob and a
  swap-the-type-scale axis, à la Material's density system). Phase 1 ships a single default density/type
  scale. **Design requirement: they must be addable later without a major refactor** — both are token
  sets switched by CSS variables (the same mechanism as themes, [§4](#4-tokens--theming-tellmacore-ui-tokens)),
  every component sizes itself from density/type tokens (enforced by the stylelint rule below), and the
  per-control `size` input ([§3.2](#32-text-input--tminput)) already exercises the static variant path —
  so the runtime axes drop in as additional token sets without touching component internals.

There is **no theme-builder UI**, now or later — theming is authored CSS custom properties (the emitted
token variables), see [§4](#4-tokens--theming-tellmacore-ui-tokens).

## 1. Package & build foundation

The four `@tellma/core-ui*` packages live under `client/projects/core/`, plus the reference
`@tellma/locale-ar` pack. Phase 1 puts real contents in three core packages, a stub-but-wired version in
the fourth (`-mcp`), and a real (if small) Arabic locale pack.

| Package | Phase-1 contents |
|---|---|
| `@tellma/core-ui` | The components — `tmInput` directive; `tm-checkbox`; `tm-select` + `tm-option` (overlay panel via CDK Overlay, listbox via `@angular/aria`); `tm-form-field`; `provideTellmaForms()`/`provideTellmaUi()`; the static base CSS; the self-hosted default fonts + `@font-face` ([§7.1](#71-fonts--web-font-loading)). Plus a **`@tellma/core-ui/contracts`** secondary entry point holding the `SignalLike`/`WritableSignalLike` boundary types and the `TmFormFieldControl`/`TmCellEditor`/`TmCellDisplay` interfaces ([§2.1](#21-shared-contracts)). Each component is its own secondary entry point (`@tellma/core-ui/input`, `/checkbox`, `/select`, `/form-field`); the primary `@tellma/core-ui` entry point carries the providers, i18n, fonts, and forms infrastructure ([§12](#12-directory-layout)). |
| `@tellma/core-ui-tokens` | `TmTokens` TS contract; the brand default preset; the `tokens → CSS variables` emitter; generated JSON Schema; build-time schema + missing-ref validation. |
| `@tellma/core-ui-testing` | `TmInputHarness`, `TmCheckboxHarness`, `TmSelectHarness` (+ `TmOptionHarness`), `TmFormFieldHarness`. |
| `@tellma/core-ui-mcp` | Generated `components.json` for the components; a minimal MCP server exposing `list/describe/example` tools over it. Wired into the build; tool breadth is later. |
| `@tellma/locale-ar` | The **reference locale pack**: Arabic translations for the library's built-in strings (validator-key messages, placeholders, the required-field announcement) as a Transloco scope, **plus** the self-hosted **Noto Sans Arabic** woff2 + `@font-face` (`unicode-range`) stylesheet — the strings wired by a single **`provideTellmaLocaleAr()`** in the app config, the stylesheet added to the build's `styles` ([§7.1](#71-fonts--web-font-loading)). Installing it adds Arabic to a distribution; not installing it leaves the core English-only. It is the template every later locale pack (`@tellma/locale-am`, …) copies. |

**Build & tooling (shared, established once):**

- pnpm workspace + **ng-packagr** per package; per-component secondary entry points;
  `"sideEffects": false`. (D1/D2 — no Bazel.)
- Angular **v22**, standalone, **zoneless**, signal-first public API (`input()`/`model()`/`output()`).
  Per the v22 best practices: do **not** set `standalone` or `OnPush` (both default in v22); host
  bindings live in the `host` object (never `@HostBinding`/`@HostListener`); `computed()` for derived
  state; `inject()` over constructor injection; `@Service` for new singletons; native control flow; no
  `ngClass`/`ngStyle`.
- Depends on `@angular/cdk` (Overlay, Portal, a11y, Directionality), `@angular/aria` (listbox/combobox +
  harnesses), and `@angular/forms/signals` (Signal Forms). **`@angular/aria` and Signal Forms are stable
  as of v22** (graduated from developer preview), per the
  [v22 announcement](https://blog.angular.dev/announcing-angular-v22-c52bb83a4664), the
  [`@angular/aria` npm package](https://www.npmjs.com/package/@angular/aria), the v22 docs
  ([aria](https://angular.dev/guide/aria/listbox),
  [signal forms](https://angular.dev/guide/forms/signals/custom-controls)), and the CLI MCP's
  `get_best_practices`. **Version pinning:** `@angular/aria` moves in lockstep with the framework, so it
  is pinned to a single Angular minor (the **22.x** line at authoring), with `@angular/aria` and
  `@angular/core` on the *same* minor. "Single pinned version" and "tracks the latest stable" don't
  conflict: the platform holds **one** minor across every package and bumps it deliberately, as one
  platform-wide step, to the latest **stable** release (never `next`/preview, never an automatic
  per-package float). At any moment there is exactly one Angular version in the repo.
- ESLint flat config + Prettier. Custom rules: an ESLint selector rule enforcing the `tm-`/`Tm…` prefix
  on selectors and exported symbols (D3); a stylelint rule enforcing a `tm-` prefix on every library CSS
  class (so no library class collides with a distribution's styles); and a stylelint rule **banning
  hardcoded sizing values in component CSS** — numeric lengths (`px`/`rem`/`em`) on size/space/typography
  properties (height, padding, margin, gap, font-size, border-radius, inline/block sizing) must come from
  a token variable (`var(--field-*)`, `var(--space-*)`, …). That rule is what keeps the deferred
  density/typography runtime axes ([Non-goals](#goals--non-goals), [§4](#4-tokens--theming-tellmacore-ui-tokens))
  addable without a refactor: if every component already sizes from tokens, switching the token set is the
  only change. (Allowed literals: `0`, hairline `1px` borders, explicit allowlist entries; the rule
  targets sizing, not e.g. `1px solid`.) (Commit-message linting — `commitlint` — is a repo-wide concern
  configured at the platform root, out of scope here.)
- **API goldens** per entry point via Microsoft API Extractor + an `api:approve` CI gate (D11) — see
  [§10](#10-testing-tellmacore-ui-testing).
- CI gates: unit + harness tests, **axe-core**, **bundle-size budget**, API golden, lint — always on.
  (No SSR gate — distributions are client-rendered. This is a deliberate **one-way door**: the top-layer
  `[popover]` overlay, `font-display: swap`, and zoneless assumptions are CSR-shaped, so a future surface
  that needs SSR/SSG would have to revisit them.)

> **Note — inline templates.** Per the v22 best-practices guide (authoritative for framework conventions,
> taking precedence over the research doc), small components use inline templates. All three Phase-1
> components are small, so all use inline templates; D5's external-template preference is reserved for
> larger future components with rich named slots.

### 1.1 Use the Angular CLI MCP during implementation

Implementers (human or agent) **must** use the Angular CLI MCP throughout: call `get_best_practices`
(with the workspace path) before writing or changing Angular code, prefer `ng generate` for scaffolding,
and use `search_documentation` / examples to confirm v22 APIs (Signal Forms, `@angular/aria`,
`transformedValue`, etc.) rather than relying on memory. The best-practices output is the source of truth
for framework conventions; this spec defers to it.

### 1.2 Build tooling — pnpm + Angular CLI (nx deferred)

Phase 1 uses **pnpm workspaces + the Angular CLI + ng-packagr** — no nx. The reason is structural: nx's
headline wins (project-graph caching, `affected`, distributed cache) scale with the number of *projects*,
which here is essentially fixed — four core packages plus a slowly-growing set of locale packs (one per
language, a low-tens ceiling). What grows with each new component lives *inside* those packages (more
components, tokens, harnesses, tests, examples, `components.json` entries) and is served by the test
runner itself, not a cross-project graph. So nx would optimize a
near-constant axis while adding onboarding and tooling-sprawl cost (the same low-onboarding argument that
rejected Bazel in D1). **Revisit nx** only if the *project* count climbs — many in-repo distributions, or
the UI family splitting into many packages. Package boundaries are nx-ready regardless, so adoption later
is additive.

### 1.3 Worktree-isolated, port-free tooling

The platform's parallel-local-development rule applies: every build/test/run path and hosted tool runs in
parallel across isolated git worktrees with **no hardcoded localhost ports** and no shared mutable global
state:

- The showcase app, the test runner, the MCP server, and any dev/preview server bind to an OS-assigned
  free port (or read the worktree's `.dev-ports.local`), never a literal port.
- Test artifacts, caches, and emitted files are written under the worktree (or a per-worktree namespaced
  path), so two worktrees never collide.
- The MCP server and the showcase app are launched by scripts following the same free-port discovery as
  the platform's `dotnet tellma setup-worktree` flow; nothing assumes a singleton instance.

## 2. Behavior layer and shared contracts

**There is no per-component headless "pattern" layer.** `@angular/aria` *is* the headless behavior layer
for everything with a non-trivial keyboard/selection model — listbox, combobox, menu, grid, tree — and
each styled `tm-*` control owns the rest of its logic directly, as an ordinary Angular
component/directive (signals, `effect()`, DI, lifecycle, all used normally). Genuinely-shared,
framework-agnostic helpers (value→key mapping, value formatters) are plain exported functions, not a
class per control.

The earlier `Tm*Pattern`-class-per-control split (D4) is **dropped**: with aria providing the behavior
layer, the leftover per-control logic is too thin to justify a second layer plus the `SignalLike`
indirection and no-`effect()` constraint a non-DI class would impose. The headless-engine approach is
**reserved for the future editable data grid** — a substantial, aria-uncovered state machine
(tab/enter/arrow nav, range selection, virtual scroll, clipboard, undo/redo) where an isolated,
separately-tested core earns its keep, shipped in its own package when a real second consumer drives it.

### 2.1 Shared contracts

The cross-cutting contracts live in a secondary entry point of `@tellma/core-ui`
(`@tellma/core-ui/contracts`), **not a separate package** — zero-/low-runtime types plus a couple of pure
helpers. The only consumer besides the components is the future grid, which depends one-directionally on
`@tellma/core-ui` anyway (no cycle to break). A lint keeps this entry point free of component/DI imports
so the grid can import the contracts without pulling in the components.

`SignalLike`/`WritableSignalLike` are the **boundary types a host uses to drive a control it owns** — in
particular the grid, which owns a cell's value and passes it to the editor through the write channel:

```ts
export type SignalLike<T> = () => T;                       // read channel; an Angular signal is one
export interface WritableSignalLike<T> extends SignalLike<T> {  // read + write channel
  set(value: T): void;
  update(fn: (prev: T) => T): void;
}

// What tm-form-field needs to do its job. The control re-surfaces the Signal Forms field state it
// receives via [formField] (see §5) so the wrapper can apply the display policy and render the
// localized error text — it carries the FULL state set, not just `invalid`.
export interface TmFormFieldControl {
  readonly controlId: SignalLike<string>;        // id of the actual control element (the <input>),
                                                 //   so <label for> targets it and aria wiring resolves
  readonly ownsChrome: boolean;                  // true = control renders its own adornment chrome
                                                 //   (tm-checkbox, tm-select); false = the field wraps the
                                                 //   control in the shared bordered box (tmInput) — see §3
  readonly describedByIds: SignalLike<string[]>; // ids the control currently exposes via aria-describedby
                                                 //   (read so the field can merge, not clobber, existing ones)
  setDescribedByIds(ids: string[]): void;        // field pushes its hint/error element ids; control writes
                                                 //   them into aria-describedby (the MatFormFieldControl seam)
  setLabelId?(id: string | null): void;          // for controls whose focusable host is NOT labelable
                                                 //   (tm-select's <div> trigger): field passes its <label>
                                                 //   id, control binds aria-labelledby; native-input hosts
                                                 //   omit this — <label for> does the job (§3.1)
  // Field state, mirrored from the bound Field (all read-only to the wrapper):
  readonly required: SignalLike<boolean>;
  readonly disabled: SignalLike<boolean>;
  readonly readonly: SignalLike<boolean>;
  readonly touched: SignalLike<boolean>;
  readonly dirty: SignalLike<boolean>;
  readonly invalid: SignalLike<boolean>;
  readonly pending: SignalLike<boolean>;                    // async validation in progress
  readonly errors: SignalLike<readonly TmFieldError[]>;     // already-localized messages
  onContainerClick?(): void;  // optional: field calls this when the user clicks the container chrome
                              //   (padding/border, not the input itself) so the control focuses itself
}
// `kind` mirrors the framework ValidationError's `kind` — 'required', 'minLength', 'email', …
// (camelCase, per Signal Forms; NOT reactive forms' 'minlength') — the same kind §5's message resolver
// maps to a localized default. It is the machine-readable category, distinct from `message` (the
// human-readable, already-localized text); consumers branch styling/logic on `kind`. Named `kind`, not
// `key`, to match the framework error shape one-for-one.
export interface TmFieldError { readonly kind: string; readonly message: string; }

// DRAFT / STUB — TmCellEditor and TmCellDisplay below are forward-compat placeholders, not a
// finished design. They exist only to keep rule 6 (grid-embeddability) from being foreclosed and to
// shape the controls' internal separation of edit-path vs. display-path. They are *properly designed
// and hardened when the actual data grid is built* (its real requirements will reshape them); the
// implementation carries the same note as a code comment on each interface. Phase 1 does not
// test-harden them (see §9).
//
// Every grid-embeddable control implements this, so the grid drives them uniformly. The control
// itself implements it (no separate pattern class); commit/cancel mutate through the write channel.
export interface TmCellEditor<T> {
  readonly value: WritableSignalLike<T>;  // host (grid) owns this; commit/cancel write through it
  commit(): void;                  // accept the edit (Enter/Tab in a grid; blur standalone)
  cancel(): void;                  // revert to last committed (Esc)
  focus(): void;
  onKeydown(e: KeyboardEvent): void; // host forwards; the editor consumes only its own keys
}

// Pure display path, no Angular instance required — lets the grid paint thousands of
// non-edited cells as plain readonly DOM (see §9). A grid-facing capability, NOT what the
// standalone control uses to render its own trigger (see §3.4).
export interface TmCellDisplay<T> {
  formatValue(value: T): string;   // e.g. select → resolved label; text → the string
  readonlyClass?(value: T): string; // optional token-driven class for non-text glyphs (checkbox box)
}
```

The control populates `errors` with **already-localized** messages (resolved through the message
resolver, [§5](#5-forms-integration-signal-forms)) so `tm-form-field` only decides *whether* to show
them, never how to translate them. **Value ownership:** Signal Forms requires the bound `value`/`checked`
`model()` on the control ([§5](#5-forms-integration-signal-forms)); in a form the control owns it, in a
grid the host owns it and passes it via `TmCellEditor.value` (the `WritableSignalLike` write channel)
while the control keeps a private `lastCommitted` for revert. Per-control specifics (what each owns vs.
delegates to aria) are in [§3](#3-the-components-tellmacore-ui).

## 3. The components (`@tellma/core-ui`)

Each `tm-*` control is an ordinary Angular component/directive that owns its logic: it declares the
public API (`input()`/`model()`/`output()`), holds state in signals, binds ARIA + classes out via the
`host` object, renders an inline template with `@if`/`@for`/`@let`, implements the relevant Signal Forms
custom-control interface ([§5](#5-forms-integration-signal-forms)), and — where the behavior is
non-trivial — composes `@angular/aria` directives in its template. There is no separate pattern object to
wire to.

### Host shape — directive on native input vs. component

Chosen per control by what the native element gives us — directive-on-native for the text input (best
a11y + native mobile/IME behavior + minimal DOM + grid-embeddable), components for checkbox and select:

- **Text input → `tmInput`, a directive on the native `<input>`** (`<input tmInput>`, the `matInput`
  model). The native element *is* the control.
- **Checkbox → `tm-checkbox` component** — renders custom box + check/indeterminate glyph chrome with no
  stylable native equivalent, wrapping a visually-hidden native `<input type="checkbox">` for semantics.
- **Select → `tm-select` + `tm-option` component** — native `<select>` cannot host a custom overlay
  panel, rich options, or the brand styling, so Select is a custom trigger + a CDK-Overlay-mounted
  `@angular/aria` listbox.

**"Adornment chrome" and where it lives.** *Adornment chrome* = the visual furniture around the editable
element: the bordered box, the focus-ring container, the leading/trailing slots, the size variants.
Because `tmInput` is a **bare directive that adds nothing around the `<input>`** (so the input drops into
a grid cell with nothing to strip), that chrome lives in **`tm-form-field`** — specific to the input
directive. `tm-checkbox` and `tm-select` are components that render their own structure, so they own
their chrome internally (the checkbox box; the select trigger + overlay panel). The throughline is the
same — a *bare* behavior host with chrome supplied around it — but for the directive the chrome is a
sibling wrapper, for the components it is internal. (A field-less adorned input is just `tm-form-field`
used without a `label`; there is no separate `tm-input-shell`.)

Two consequences of that split are made explicit in the contract. **How the field knows:** all three
controls project into `tm-form-field` as `TmFormFieldControl`, so the contract carries an
**`ownsChrome`** flag ([§2.1](#21-shared-contracts)) — `false` for `tmInput` (the field renders the
bordered box around control + adornments), `true` for `tm-checkbox`/`tm-select` (the field renders only
the label/hint/error scaffold, never a second box around the control's own). **Why the box is not just
CSS the `tmInput` directive puts on the `<input>`:** the box must visually *contain* the
`[tmPrefix]`/`[tmSuffix]` adornments, and the focus ring must wrap box-plus-adornments
(`:focus-within` on the wrapper) — an `<input>` is an empty element that cannot contain siblings, so a
border on the input itself would strand the search icon outside the box. Border-on-input would also mean
a second chrome implementation to keep in sync for the adornment-less case, and it would have to be
stripped in grid cells; one wrapper-owned chrome keeps the bare input truly bare.

### 3.1 `tm-form-field`

The shared label / required-marker / hint / error scaffold (brand `FormField`), reading the `--field-*`
token group. It queries its **projected control** (content child) through the `TmFormFieldControl`
contract ([§2.1](#21-shared-contracts)) and reads the field state the control surfaces from `[formField]`
([§5](#5-forms-integration-signal-forms)) — `errors`/`touched`/`dirty`/`invalid`/`pending`/`required`. It
generates and wires ids, two-path because **`<label for>` only associates with *labelable* elements**
(`input`, `button`, `select`, …), not `tm-select`'s `<div>` trigger: for a native-input control
(`tmInput`, `tm-checkbox`'s hidden input) the field renders `<label for>` ↔ `controlId`; a control with a
non-labelable host implements the optional `setLabelId()` ([§2.1](#21-shared-contracts)) — the field
passes its label's id, the control binds `aria-labelledby` on the trigger, and the field forwards label
clicks to `onContainerClick()`/focus so click-to-focus still works. Hint and error ids are fed back via
`setDescribedByIds` → the control's `aria-describedby`, and `required` is mirrored. The hint and error are **separate persistent
elements** (the error element is the persistent `aria-live="polite"` region per [§6](#6-accessibility));
the display policy toggles their *visibility* (at most one shown — error when invalid-and-displayed, else
hint) rather than swapping text in a single node, so announcements are clean. Logical-property layout
mirrors in RTL. Inputs: `label`, `hint`, `size` (`sm | md | lg`); it does **not** take an `error` string
for form-bound controls (errors come from the field), though a plain `error` input remains for non-form
usage.

### 3.2 Text input — `tmInput`

- **Selector:** `input[tmInput]` (`textarea[tmInput]` reserved for later).
- **API:** `value = model<string>()` (the FormValueControl value); `placeholder`; sizing comes from
  the enclosing `tm-form-field`'s `size` (the bare directive carries no size input of its own);
  **`disabled`/`readonly`/`required` apply only in non-form
  (unbound) usage** — when bound via `[formField]` the field is authoritative
  ([§5](#5-forms-integration-signal-forms)). Implements `FormValueControl<string>` + `TmFormFieldControl`,
  and declares the optional Signal Forms state inputs (`disabled`, `readonly`, `invalid`, `errors`,
  `touched`, `pending`, `required`, …) that `[formField]` binds.
- **`size`** (on the field) = the control's height/density variant, mapping to the brand field-height tokens: `sm` →
  `--field-height-sm` (30px), `md` → `--field-height` (38px, default), `lg` → `--field-height-lg` (46px).
  It is the static, per-instance density knob (distinct from a global density *system*,
  [Non-goals](#goals--non-goals)); it also adjusts padding and font-size tokens.
- **Host bindings** (in the `host` object): `--field-*` styling, the focus ring, `aria-invalid`,
  `aria-required`, `aria-describedby`, `disabled`.
- **Leading/trailing slots** = *adornments* placed before (leading) / after (trailing) the text — a
  search icon, a currency code, a clear button — supplied by **content projection** on `tm-form-field`
  via attribute-selector `ng-content` (`[tmPrefix]` / `[tmSuffix]`), not baked into the bare input.
  Example: `<tm-form-field><svg tmPrefix …></svg><input tmInput></tm-form-field>` (icons are inline SVG —
  [§3.5](#35-built-in-glyphs--icons)).

### 3.3 Checkbox — `tm-checkbox`

- **Selector:** `tm-checkbox`.
- **API:** `checked = model<boolean>()`; `indeterminate`; projected label; `disabled`/`required`
  (non-form usage only — field-authoritative when bound). Implements `FormCheckboxControl` +
  `TmFormFieldControl`, plus the optional Signal Forms state inputs.
- **No `value` property.** Signal Forms is explicit: *a `FormCheckboxControl` must not have a `value`
  property* — the value channel is `checked`. Multi-checkbox selection is a future **`tm-checkbox-group`**
  that owns the array value and maps each child's identity; the individual `tm-checkbox` stays a pure
  boolean control.
- **Rendering:** visually-hidden native `<input type="checkbox">` for semantics + the styled box (teal
  when checked/indeterminate, `--radius-xs`, check polyline / indeterminate bar), space-to-toggle, focus
  ring.
- **Indeterminate is the native `.indeterminate` IDL property, *not* a manual `aria-checked="mixed"`.**
  Because the control wraps a *real* `<input type="checkbox">`, the tri-state is driven by host-binding
  the input's `.indeterminate` DOM property (`[indeterminate]="indeterminate()"`) — an IDL property only,
  with no HTML attribute and no value channel, set by host binding rather than as an attribute. The
  browser then exposes `checked="mixed"` in the accessibility tree automatically; we do **not** add
  `aria-checked="mixed"` ourselves (redundant on a native input, and it can conflict with the browser's
  computed value). `aria-checked="mixed"` would only be needed for a custom `role="checkbox"` element with
  no real input. The visible "mixed" glyph (the indeterminate bar) is pure CSS keyed off the same
  `indeterminate()` signal. `indeterminate` is independent of `checked`: setting it doesn't change
  `checked`, and a user toggle clears it (matching native behavior).
- **Touch-target mechanism:** the visible box stays at the brand 18px, but the **clickable region is the
  whole `<label>`**, padded so its hit box clears the target-size rule; where a bare checkbox has no
  adjacent label, a transparent `::before` pseudo-element expands the pointer target while the box renders
  at 18px. Pointer/click events bind to the enlarged region, not the glyph. The hit-box target is the
  [§6](#6-accessibility) sizing rule (≥24px to conform; ≈44px on standalone touch-primary controls where
  layout allows), so the same control is conformant in a dense grid and comfortable on a touch form.

### 3.4 Select — `tm-select`

This section is the home of the Select architecture; later sections ([§6](#6-accessibility),
[§9](#9-data-grid-forward-compatibility-contract), [§10](#10-testing-tellmacore-ui-testing)) reference it
rather than restate it.

- **Selectors:** `tm-select` (trigger + value display) with projected `tm-option` children.
- **API:** `value = model<T>()` (single-select); `placeholder`, `disabled`, `required`, `valueKey`,
  `size`. `tm-option`: `value`, optional `label` (see typeahead below) + projected label content; outputs
  `selectionChange`/`opened`/`closed`. Implements `FormValueControl<T>` + `TmFormFieldControl`.
- **`valueKey` is ours, not aria's.** Signal equality is referential by default, so two option objects
  describing the same entity (the model's `{id:7,…}` vs. a freshly-fetched `{id:7,…}`) are unequal and
  selection would fail to highlight. `@angular/aria` provides no equality hook — its listbox selection
  is strict `===` on whatever is bound to `ngOption [value]`. So `tm-select` takes
  **`valueKey: (value: T) => string | number`**, mapping each domain value to a **stable primitive key**
  before handing it to aria (and back for display). (Primitive-id values, the common ERP shape, need
  nothing.) Deliberately **not** named `compareWith`: Material's `compareWith` is a two-argument
  comparator `(a, b) => boolean`, and reusing the name for a one-argument key extractor would mislead
  humans and agents alike.
- **Built on the aria Select directives.** The template composes v22's `@angular/aria` Select: `ngCombobox`
  (the trigger, `[(expanded)]`) on a **non-`<input>` host**, the `ngComboboxPopup` widget, and `ngListbox`
  + `ngComboboxWidget` + `ngOption` with `focusMode="activedescendant"`, `selectionMode="explicit"`,
  `[(value)]` (an **array** model), and `[activeDescendant]="listbox.activeDescendant()"`. These directives
  — not hand-written code — own keyboard navigation, typeahead, `activeDescendant()`,
  `scrollActiveItemIntoView()`, single-Escape, and all `aria-*`. **Editable vs select mode is chosen by
  the host element tag, not a config flag:** aria derives `isEditable` from the host tag being `input`
  **or `textarea`**, so `tm-select` (non-editable) puts `ngCombobox` on a `<div>`/`<button>`; the future
  editable details-picker puts it on an `<input>`. `tm-select` itself owns the brand chrome, the form-control glue, the
  scalar↔array bridge, label resolution, and the grid commit/cancel.
- **Value source of truth, and aria's auto-prune (load-bearing direction of the bridge).** `@angular/aria`'s
  listbox runs an `afterRenderEffect` that **drops any selected value not matching a currently-rendered
  option** (`value.set(value.filter(v => options.some(o => o.value() === v)))`). That bites the
  prepopulated/async case: if the bridge wrote a prepopulated domain key into aria's listbox `value`
  before its `ngOption` existed, aria would silently discard it. So the bridge is **one-directional**:
  `tm-select`'s own `FormValueControl<T>` `value = model<T>()` is the source of truth; it is mirrored into
  aria's listbox value (mapped to the stable key) and **re-applied when options arrive**, and aria's
  listbox value is never treated as authoritative for a value whose option may not be materialized yet.
  (This is the selected *value* surviving; the trigger *label* path below is separate, covered by
  `displayWith`.) The prune has a second consequence: it **writes the listbox's `value` model**, so at the
  `valueChange` level a prune during async option turnover is indistinguishable from a user deselection —
  which is why the commit trigger is activation events, never `valueChange` (below). The DoD tests that a
  prepopulated value survives until its option renders and that a prune never commits.
- **Display one property, capture another.** `tm-option`'s `value` is what lands in the model, its
  projected content is what the user sees:
  `<tm-option [value]="record.id">{{ record.label }}</tm-option>` captures the id, displays the label.
- **Typeahead needs an explicit string label.** aria's typeahead reads **only** `ngOption`'s `label` input
  (its `searchTerm` is `label() ?? ''` — there is **no `textContent` fallback**), so content-projected
  option text is invisible to type-to-search. `tm-option` therefore takes an optional **`label` string
  input** forwarded to `ngOption`; when absent, `tm-option` derives it from its projected text
  (`textContent.trim()` after render). This is the *search* string; the *trigger* label resolution below
  is a separate path.
- **Trigger label resolution — and the prepopulated-value problem.** Caching the projected option's label
  only works once that option has rendered, but a form frequently arrives with `value` set before any
  `tm-option` exists (an edit screen; an async/virtualized list). So the trigger resolves its label in
  order: (1) a **`displayWith: (value) => string`** input if provided — it needs no materialized option,
  so it is the robust path for prepopulated and async/virtualized lists; (2) else the projected option
  matching `value` (via `valueKey`) once present; (3) else the placeholder. `displayWith` is not
  mandatory for static lists (the option is in the DOM immediately) but is **required in practice** for
  async/virtualized or prepopulated-without-static-options cases; the DoD tests the prepopulated path.
  `TmCellDisplay.formatValue` ([§2.1](#21-shared-contracts)) delegates to the same resolver, but the
  standalone trigger does not depend on the grid-facing interface.
- **Popup positioning — settled by a spike (Angular 22 + Playwright, 36/36).** aria's popup does no
  positioning: `ngComboboxPopup` is a structural directive (`ng-template[ngComboboxPopup]`,
  `hostDirectives: [DeferredContent]`) that renders its content inline via `createEmbeddedView` when
  `expanded` flips true. We add CDK-Overlay positioning + clipping-escape on top, via the **official
  nested pattern**: a `cdkConnectedOverlay` wraps `<ng-template ngComboboxPopup [combobox]="cb">` wraps the
  `ngListbox ngComboboxWidget` panel, with CDK-open and aria's `DeferredContent` both gated on the same
  `expanded` signal. The single-renderer alternative (drop `ngComboboxPopup`, drive the overlay alone) is
  **rejected**: the combobox's keyboard relay, `aria-controls`, and `aria-activedescendant` all derive
  from the popup that `ngComboboxPopup` registers (`combobox._registerPopup` / `popup._registerWidget`),
  so removing it kills keyboard navigation and the ARIA id chain unless you hand-rebuild that plumbing.
  Required wiring (from the spike):
  - overlay config `{ origin, usePopover: 'inline', matchWidth: true }` — `usePopover: 'inline'` renders
    the panel into a native top-layer `[popover]` host, which escapes `overflow:hidden` clipping;
    `matchWidth` sizes it to the trigger.
  - a `[bottom-start, top-start]` position set for flip; and — because `DeferredContent` inserts the panel
    one render pass *after* CDK attaches and measures — the component **must call `overlayRef.updatePosition()`
    on `(attach)` via a macrotask** (`afterNextRender`/microtask fire too early), or flip measures a
    zero-height panel and never flips up.
  - listbox **`focusMode="activedescendant"`** (mandatory — the `roving` default moves real DOM focus into
    the panel and nulls `aria-activedescendant`) and **`selectionMode="explicit"`** (the `follow` default
    commits on every arrow); bind `ngComboboxWidget [activeDescendant]` from the listbox.
  - the combobox `value` (a string model for editable comboboxes) is left **unbound** — the listbox owns
    the value; only the scalar↔array bridge above touches it.
  - **`disableClose` on the overlay — aria owns Esc.** By default `cdkConnectedOverlay` detaches itself
    **and calls `preventDefault()`** on Escape (its `keydownEvents()` handler, gated only by
    `disableClose`/modifier keys) — that would double-handle Esc against aria's own Escape handling *and*
    desync state (overlay detached while `expanded` is still true). So the overlay is created with
    **`disableClose: true`** — exactly what the official aria `combobox-select` example passes
    (`[cdkConnectedOverlayDisableClose]="true"`); Esc is handled by aria alone, which collapses
    `expanded`, and every close path (Esc, outside-click via the overlay's outside-pointer events,
    selection commit) routes through that one shared `expanded` signal — never a bare `detach()`.
  The overlay is created lazily on first open. **RTL — re-diagnose, don't author (residual, in the DoD):**
  CDK's `FlexibleConnectedPositionStrategy` resolves `originX`/`overlayX` `start`/`end` against
  `Directionality` (`_isRtl()` in the strategy source), so a `[bottom-start, top-start]` set **mirrors for
  free** and no hand-authored RTL position set is needed. The spike's observed RTL symptom (panel
  left-aligned, `matchWidth` ignored) therefore has a different cause — most likely the overlay not
  receiving the `dir` context, or the `usePopover: 'inline'` path — to be diagnosed and verified under
  `dir="rtl"`. (One genuine non-mirroring caveat: a raw `offsetX` is **not** direction-aware; avoid or
  negate it per direction if ever used.)
- **Known upstream bug to guard — mouse input in the overlay ([angular/components#32504](https://github.com/angular/components/issues/32504), P3).**
  An aria combobox/listbox composed via `hostDirectives` **inside a `cdkConnectedOverlay`** — exactly this
  composition — currently misbehaves **on mouse input only**: a click on an option may not register the
  selection, and an outside click may not close the panel (keyboard paths are fine). It is **open** and
  could affect our build. (The positioning spike's 36/36 covered flip/clipping/keyboard and the ARIA
  id-chain — not this mouse-in-overlay path, which is proven separately by the mouse specs in
  [§10](#10-testing-tellmacore-ui-testing).) Mitigation: `tm-select` carries an explicit mouse path
  (pointerdown-driven option selection + an outside-pointer close) ready to switch on if the bug bites,
  and the test suite pins the behavior with real mouse-event specs (DoD item 6) so a regression — ours or
  the upstream bug surfacing in our composition — fails CI rather than shipping a select that ignores
  clicks.
- **Commit and close are activation-driven; focus and Esc are aria's.** aria does not auto-close on
  selection, and the listbox's `valueChange` is **not a safe commit trigger** — the auto-prune above writes
  the same value model, so a prune during async option turnover would read as a user deselection, wiping
  the form value and slamming the panel shut. Following the official aria Select example, `tm-select`
  commits and closes on the listbox's **activation events** — `(click)`, `(keydown.enter)`,
  `(keydown.space)` — reading the listbox value in the handler, mapping it back through `valueKey`, writing
  the `FormValueControl` model, and collapsing `expanded`. Activation fires whether or not the value
  changed, so same-value reselection closes the panel with no special case. **Focus never leaves the
  trigger** (the `activedescendant` model), so no focus-restore is needed. Esc is aria's (the overlay's own
  Escape handling is disabled — `disableClose` above): **stage 1 — Esc closes the open panel — is the only
  Esc behavior in a standalone `tm-select`** (it has nothing to revert to and no edit-mode to exit, so it
  does not listen for a second Esc). **Stage 2 — a second Esc that reverts and exits edit mode — is purely
  a grid-host concern:** the grid owns edit-mode and, on the second Esc (panel already closed), calls the
  control's `TmCellEditor.cancel()`. So `tm-select` *implements* `cancel()` for a host to call
  ([§9](#9-data-grid-forward-compatibility-contract)), but the two-stage sequencing lives in the grid.
  Tab is not relayed to the listbox, so "commit on Tab" (if wanted) is wired explicitly.
- **RTL positioning mirrors automatically — and is still tested.** CDK resolves connected-position
  `start`/`end` against `Directionality` (see the wiring note above), so the position set is authored once
  and mirrors for free; the RTL spec in the DoD verifies the rendered result under `dir="rtl"` (and closes
  the spike's RTL residual) rather than trusting the mechanism blindly.
- **Keyboard & a11y:** the aria directives supply the combobox/listbox roles +
  `aria-expanded`/`aria-selected`/`aria-activedescendant`, the keyboard model, and typeahead. Because the
  listbox is portaled outside the trigger's subtree, the trigger references it with **`aria-controls`**
  (and `aria-activedescendant` points at option ids inside the portaled panel); a Playwright test asserts
  the trigger→listbox→active-option id chain resolves ([§6](#6-accessibility)/[§10](#10-testing-tellmacore-ui-testing)).
- **Not the entity picker.** `tm-select` is for in-memory/simple option lists. Selecting a related entity
  on an ERP screen — e.g. **Supplier** on a purchase invoice — needs server-side search on the typed
  string, complex filtering, an inline "create new" affordance, and a "launch advanced-search modal"
  escape. That is a **distinct future component** (`tm-entity-picker`) on aria's **editable combobox** mode
  (`ngCombobox` on an `<input>`) + the same overlay infra, **not** bolted onto `tm-select`. Keeping them
  separate avoids overloading the simple control; the shared aria/overlay foundation is the reuse.
- **Forward-compat (not Phase 1, not precluded):** multi-select (aria multiselect mode, value → array),
  option groups, and **virtual scroll** (`cdk/scrolling` replaces the static `@for` without an API change).
- **Touch:** option rows sized for comfortable pointer/touch use; full-width-friendly panel on narrow
  viewports (target sizing per [§6](#6-accessibility)).

### 3.5 Built-in glyphs — icons

The analysis (D10) fixes the icon *direction* — SVG only, no icon fonts, a future `tm-icon` +
`TmIconRegistry` (default set Lucide, sanitized/Trusted-Types) keeping the set swappable per distribution
— but the registry earns its keep only when consumers supply icons, and no Phase-1 API takes one. The
foundation needs exactly three glyphs: the select **caret** and the checkbox **check / indeterminate
marks** ship as **static inline SVGs in the owning component's template** — private DOM, `tm-`-classed,
colored via `currentColor`/tokens (so themes and forced-colors restyle them), `aria-hidden="true"`. The
pending **spinner** is shared by several controls, so it is the one glyph with a public face:
`tm-spinner` (`@tellma/core-ui/spinner`), decorative (`aria-hidden` — the busy control carries
`aria-busy`), animated on its host with `prefers-reduced-motion` honored. No icon font, no runtime icon
processing, no registry dependency. `tm-icon`/`TmIconRegistry` arrive with the first component that
accepts consumer-supplied icons; the built-in glyphs can migrate to it then without any public-API change.

## 4. Tokens & theming (`@tellma/core-ui-tokens`)

Theming is a typed TS/JSON token model in three tiers (primitive → semantic → component), emitted to CSS
variables. Phase 1 builds the contract and the emitter and ships **one default preset reproducing
`tellma-brand/design-system`** — same hexes, same `--field-*` / `--focus-ring` / spacing / type tokens,
same `[data-theme=dark]` inversion.

**Why TS/JSON tokens rather than hand-written CSS** — the CSS variables stay the runtime currency; the TS
layer sits above them and buys what raw CSS cannot:

- **Type safety** — autocomplete, and a reference to a missing token won't compile.
- **Build-time validation** — generate a JSON Schema from `TmTokens`, validate every preset against it,
  and run a **missing-ref gate**: every emitted `var()` reference must resolve within its scheme (the
  `:lang()` leading map included), so a preset that references a missing token **fails the build**.
  Color contrast is checked where it is observable — the axe browser battery runs over the rendered
  components in light and dark ([§10](#10-testing-tellmacore-ui-testing)) — not by arithmetic over declared token
  pairs. (The brand routes *text* through teal-600 — 5.67:1 on white; the canonical teal-400 is a
  decorative fill that never carries text.)
- **One source, many outputs** — the same contract emits the CSS variables, the JSON Schema, the docs/MCP
  metadata, and (later) a Figma sync.
- **Safe composition** — presets extend a base by typed merge, not copy-paste.
- **Agent-authorability** — an agent emits a typed object validated at build, not free CSS.

**Runtime theme switching — supported, no rebuild.** Because tokens emit to CSS custom properties, a
distribution's settings screen (e.g. a color picker) sets the relevant variable(s) on a scope at runtime
(`document.documentElement.style.setProperty('--color-primary', …)` or a scoped `<style>`) and every
component restyles instantly. The TS contract is the build-time authoring/validation layer; runtime
overrides operate directly on the emitted variables (the same schema + missing-ref validation can run
client-side over an admin-authored token document). Dark mode is exactly this (`[data-theme=dark]`
swaps a variable set).

**Emission — static, build-time CSS.** "Precompiled"/"static" = the emitter runs at library/distribution
build time and writes plain `.css` files (base component styles + token variables) that ship in the
package and load as ordinary stylesheets — the opposite of PrimeNG's runtime CSS-in-JS (runtime cost +
FOUC/SSR risk). Zero runtime style-generation cost: the browser fetches a static sheet. A distribution's
override deltas are likewise emitted at build into a static sheet in its `index.html`. Runtime overrides
(above) are the one exception — a few CSS-variable writes, not style generation.

**Cascade ordering — three override sources.** A `@layer` strategy makes the three sources compose
deterministically regardless of stylesheet load order:

1. **Library base** — the default preset, emitted into `@layer tm.base`.
2. **Distribution build-time delta** — a distribution's overrides, emitted into `@layer tm.theme`.
3. **Runtime override** — a settings-screen `setProperty` on a scope, written as an **inline style**.

Precedence is **runtime > distribution > base**. Layer order is fixed by the *first* `@layer` statement
the browser encounters (later declarations of the same layers are no-ops), so declaring it "once" in one
sheet is **not** load-order-proof — if the theme sheet loaded first, `tm.theme` would register before
`tm.base` and lose. The emitter therefore writes the canonical **`@layer tm.base, tm.theme;`** statement
at the **top of every emitted sheet**: whichever sheet loads first establishes the same order, `tm.theme`
always wins over `tm.base`, and inline-style runtime writes beat any layered stylesheet by the normal
cascade.

**Dark mode is a second base scheme, not a fourth mechanism.** A scheme is a variable set scoped by a
selector (`[data-theme=dark]`); the layer it lives in is orthogonal to that selector. The library's
default **light and dark** schemes both ship in `@tellma/core-ui-tokens` and both belong to **`tm.base`**
(one scoped to `:root`/`[data-theme=light]`, the other to `[data-theme=dark]`). Only a distribution's
overrides ride `tm.theme`, and runtime `setProperty` writes ride inline. Because layer order is
independent of selector specificity, a distribution/runtime override wins **within whichever scheme is
active** — in light and dark alike. Component CSS consumes the variables from outside these layers (or a
later `tm.components` layer) so it never accidentally out-ranks a theme override.

**A slice of `TmTokens`** (the most-reused artifact, so concrete here; Phase-1 subset, full lists
design-in-progress). Primitive ramps → semantic roles via typed refs → the shared `formField` group every
input inherits:

```ts
export type TmRef = `{${string}}`;               // a typed reference to another token, e.g. '{teal.600}'
export type TmColorRamp = Record<50|100|200|300|400|500|600|700|800|900, string>;

export interface TmTokens {
  primitive: {
    color: { ink: TmColorRamp; teal: TmColorRamp; grey: TmColorRamp; white: string };
    radius: { xs: string; sm: string; md: string; lg: string; full: string };
    space:  Record<0|1|2|3|4|6|8, string>;
    font:   { sans: string; arabic: string; mono: string; size: Record<'xs'|'sm'|'base'|'lg', string> };
  };
  semantic: {
    colorScheme: { light: TmSchemeColors; dark: TmSchemeColors };  // two instances of one scheme shape
    focusRing: { width: string; color: TmRef; offset: string };   // e.g. color: '{teal.500}'
    motion:   { durationFast: string; easeStandard: string };
    formField: {                       // one override restyles every input (the ERP runs on dense forms)
      bg: TmRef; bgDisabled: TmRef; border: TmRef; borderHover: TmRef; borderFocus: TmRef; borderInvalid: TmRef;
      text: TmRef; placeholder: TmRef; icon: TmRef; radius: TmRef;
      height: string; heightSm: string; heightLg: string; paddingX: string; fontSize: TmRef;
    };
  };
  component: Record<string, Record<string, TmRef | string>>;  // tm-checkbox, tm-select … ref semantic
}
interface TmSchemeColors { textStrong: TmRef; textBody: TmRef; surfacePage: TmRef; surfaceCard: TmRef; border: TmRef; /* … */ }
```

**Brand source of truth:** the **TS `TmTokens` contract is canonical**; the brand CSS is a starting
import. A conformance test asserting the emitted CSS matches `tellma-brand` anchors is **deferred** (the
brand is still in flux). The schema + missing-ref gates ship in Phase 1 regardless (they don't depend on
the brand).

## 5. Forms integration (Signal Forms)

Signal Forms is **stable in Angular v22** and is the only forms mechanism the library supports — no
`ControlValueAccessor`, no dual path (every consumer is greenfield v22+).

**How field state reaches the control and the wrapper.** `[formField]` is applied to the **control
element**, and the authoritative state lives in the `Field` (`myForm.email`). The directive detects which
interface the control implements and binds:

```html
<tm-form-field label="Email">
  <input tmInput [formField]="form.email" />
</tm-form-field>
```

- The control implements `FormValueControl<T>` (`tmInput`, `tm-select`) or `FormCheckboxControl`
  (`tm-checkbox`) and exposes `value = model<T>()` / `checked = model<boolean>()`. `[formField]` binds
  that, **and** sets the control's declared optional state inputs — per the `FormUiControl<TValue>`
  source (`@angular/forms@22.0.5`), the full set is `disabled`, `disabledReasons`, `dirty`, `errors`,
  `hidden`, `invalid`, `max`, `maxLength`, `min`, `minLength`, `name`, `pattern`, `pending`, `readonly`,
  `required`, `touched` (there is **no `valid`** input — only `invalid`; `valid` exists solely on
  `FieldState`). `touched` is a plain read input; the control reports touch by emitting the separate
  **`touch: OutputRef<void>` output on native blur**, which the directive listens to. `pattern` is
  declared `readonly RegExp[]` (an **array**); `min`/`max` are `NonNullable<TValue> | undefined` (typed
  by the control's value, not plain `number`). The contract also carries optional `focus?()`/`reset?()`
  methods the framework calls when asked to focus/reset the field. The control therefore *is* the thing
  that holds field state.
- **`tm-form-field` reads that state off the control via `TmFormFieldControl`** (it does **not** get the
  `Field` reference — the control does, and re-surfaces it, [§2.1](#21-shared-contracts)). The wrapper
  queries the projected control and reads `errors`/`touched`/`dirty`/`invalid`/`pending`/`required`. This
  is the Material `MatFormFieldControl` shape adapted to Signal Forms.

**Error-display policy (field-scoped) — and why there is no `[tmForm]` directive.** Default policy: show
errors when `invalid() && (touched() || dirty())` — every signal it needs is on the field-control
contract, so no form-level plumbing is required. *"Show errors after a submit attempt"* needs none either:
Signal Forms' `submit()` marks the submitted field **and every descendant** as touched (the field's
recursive `markAsTouched()`) **before** evaluating validity or running the action (verified in
`@angular/forms@22.0.5` source), so a submit attempt flips every field's `touched` and the field-scoped
default policy surfaces every error. Even the form-element wiring is the framework's: v22 ships
**`form[formRoot]`**, which sets `novalidate` and calls `submit()` on the bound field tree — so a library
form directive would duplicate the framework twice over. Richer cross-field policy is deferred.

**`disabled` / `readonly` / `required` precedence — what is contractual vs. what is not.** The rule is
**field wins when bound; the control's own input applies when unbound**. Which half is a documented Angular
contract:

- **Documented (a public contract):** `[formField]` binds the field's state into the control's optional
  state inputs (`disabled`, `readonly`, `required`, `disabledReasons`, `invalid`, `errors`, `touched`,
  `pending`, …). Per the [Signal Forms custom-controls guide](https://angular.dev/guide/forms/signals/custom-controls),
  a custom control *declares* these optional inputs and the framework *writes field state into them*, so a
  field-bound control reflecting the field's `disabled()`/`readonly()`/`required()` is guaranteed. When
  **unbound**, nothing writes those inputs and they keep the author-provided value or default — also
  well-defined. We declare exactly **one** `disabled` input (`disabled = input(false)`, likewise
  `readonly`/`required`/`disabledReasons`) and read it directly — no `computed` merge in the normal
  (single-writer) case.
- **NOT documented (do not rely on it):** the **tie-break ordering** if an author *also* template-binds the
  same input on a field-bound control. Tracing `@angular/forms@22` shows the control-directive host
  protocol (`ɵɵControlFeature` → `setInputOnDirectives` → `writeToDirectiveInput`) writes the field value
  into the input *after* ordinary element bindings each change-detection pass, so the field happens to win
  — but those are private `ɵ`-prefixed internals with no stability guarantee. So we **do not treat the
  ordering as a contract**; we **forbid the conflict**: authors must not template-bind
  `disabled`/`readonly`/`required` on a field-bound control (lint + a doc note), removing the ambiguity. A
  single regression test pins the observed behavior so a framework change surfaces loudly.

The `FormField` directive provides an injectable **`FORM_FIELD`** token, so a control **may**
`inject(FORM_FIELD, { optional: true })` — but only to branch *other* behavior on bound-vs-unbound (e.g.
dropping a standalone-search default), never to choose between two disabled values. (`disabled()` also
populates `disabledReasons`, which feeds tooltips.)

**Async / pending validation — and who debounces.** The control exposes the field's `pending` signal;
while `pending()` is true it sets `aria-busy="true"` and shows an inline spinner, `tm-form-field`
suppresses a stale "valid" affirmation, and the display policy holds errors until validation resolves (DoD
covers this). **Debouncing the server call is the consumer's concern:** the async validator and its
cadence live in the consumer's form schema, where Signal Forms' `debounce()` (and `debounce('blur')`)
controls how often the model — and therefore the validator — fires. The library only **cooperates**: the
control emits its `touch` output on native blur (which `debounce('blur')` relies on, per the
`FormUiControl` docs) and doesn't push value updates faster than the user types. It never bakes in a
hardcoded server-call debounce.

**Numeric (a later phase)** will use the stable `transformedValue` utility (`@angular/forms/signals`) for
the string↔number parse/format with automatic parse-error reporting — which is why numeric is a cheap
follow-up rather than foundation-worthy.

**Providers — split, not bundled** (so i18n/fonts don't hide inside a *forms* provider):

- **`provideTellmaForms()`** — forms only: the error-display policy, the validation-message resolver, and
  form-field defaults (`size`, required-marker). **Message precedence:** a schema-inline message (the
  `{message: …}` passed to a validator in the form schema, surfaced as the framework error's own
  `message`) wins when present; otherwise the resolver maps the error's **`kind`** (`required`,
  `minLength`, `email`, … — Signal Forms kinds are **camelCase**, unlike reactive forms' `minlength`) to a
  localized default via the i18n runtime ([§7](#7-rtl-i18n--l10n)). The control surfaces the resolved
  string through `errors` ([§2.1](#21-shared-contracts)) as `TmFieldError { kind, message }`.
  **Live locale switching:** `TM_UI_TRANSLATE` returns `Signal<string>`, and the control derives `errors`
  in a reactive context that *reads* those signals — so switching the active locale recomputes the
  resolved messages and every visible error re-renders in the new locale; no translated string is ever
  cached outside the reactive graph.
  **Missing-translation fallback.** A distribution can run a locale before it has every library string —
  e.g. an Amharic tenant hits `required` before `am` translations exist. The resolution is a defined
  fallback chain, never a blank or raw key: (1) the **active locale's** string; (2) else the **English**
  string — **English is the only library-string locale that ships in-package**; every other locale
  (Arabic and Amharic included) ships as an optional per-distribution locale pack
  ([§7](#7-rtl-i18n--l10n)). Two failure modes reach English and need **two Transloco settings**, both set
  by the `TM_UI_TRANSLATE` default (because `fallbackLang` alone doesn't cover the second):
  - **Whole pack absent** (the language never loads — an Amharic tenant before `@tellma/locale-am`, or an
    Arabic one before `@tellma/locale-ar`): handled by **`fallbackLang: 'en'`**, which loads English when
    the active language fails to load.
  - **Pack installed but a key is missing** (incomplete translation): `fallbackLang` does **not** fire (it
    triggers on a failed *language* load, not a missing *key* in a loaded one). So the default also sets
    **`useFallbackTranslation: true`** (Transloco's per-key fall-through to the fallback language), or
    equivalently a custom **`MissingHandler`** resolving the key against English — so a missing key in a
    present locale still renders English, not the raw key.

  (3) The raw kind (`required`) is only a last-resort guard if even English lacks it — which can't happen
  for built-in kinds (English always ships them) and signals a missing *custom* kind, surfaced by a
  dev-mode `console.warn` from the `TM_UI_TRANSLATE` default and (optionally) a CI check that every error
  kind used has an English entry. So built-in kinds always resolve to at least English; a not-yet-installed
  or incomplete pack degrades to English, never to a blank or broken UI.
  **Param interpolation + ICU:** the typed error objects carry their params (`minLength` → the required
  length, `min`/`max` → the bound, etc.); the resolver passes them to the translate call so the string
  interpolates (`"At least {minLength} characters"`), and **plurals/gender use ICU MessageFormat** via
  Transloco's MessageFormat plugin (`@jsverse/transloco-messageformat`) — ICU lives in the translation
  layer, not the resolver. The built-in English preset ships ICU strings for the built-in kinds; other
  locales' ICU strings arrive with their locale pack.
- **`provideTellmaUi()`** — the umbrella a distribution calls: composes `provideTellmaForms()` + the
  default Transloco-backed `TM_UI_TRANSLATE` + any UI-wide defaults. A distribution on the defaults calls
  it once and writes **zero** other config. (Font preloading is a post-build step, not wired here —
  [§7.1](#71-fonts--web-font-loading).)

## 6. Accessibility

Target **WCAG 2.1 AA**. **axe-core is necessary but not sufficient:** it catches static violations
(missing roles, contrast, names) but **cannot** verify keyboard navigation, focus return on close,
`aria-activedescendant` tracking, the two-stage Esc, or screen-reader announcements — which is where
Select's compliance is hard. Those are gated by **behavioral Playwright tests**
([§10](#10-testing-tellmacore-ui-testing)), with axe as the static floor. **Playwright is the standardized
runner — not the implementer's choice:** these assertions depend on real focus semantics, `:focus-visible`,
layout/measurement, the CDK overlay portal's positioning, real mouse-vs-keyboard input, and `emulateMedia`
(forced-colors/reduced-motion), so a real browser engine is mandatory (`jsdom`/`happy-dom` cannot cover
them) and the whole library uses one tool. **Caveat:** Playwright verifies the DOM/ARIA *mechanism* (roles,
`aria-live` updates, focus moves, id-relationship chains), **not** that a screen reader speaks the right
thing; real AT verification (NVDA/JAWS/VoiceOver) is a manual pass, out of DoD scope.

- Text input: native semantics, `aria-invalid`/`aria-required`/`aria-describedby`, label association.
- **Live-region decisions (error/hint announcements).** The **hint and error are separate elements**, not
  swapped content in one node. The **error element is a persistent live region** (`aria-live="polite"`,
  `aria-atomic="true"`) that exists whether or not it holds text, so empty→message (or message→message) is
  announced once, and it is never reused for the hint. The hint is **not** a live region (referenced by
  `aria-describedby` for on-demand reading). Politeness: `polite` for inline field validation; on a blocked
  submit the form may escalate its *summary* to `assertive`/`role="alert"`, but per-field errors stay
  `polite`. Both ids are wired into the control's `aria-describedby`.
- Checkbox: native checkbox semantics; the tri-state is the native `.indeterminate` IDL property, so the
  browser exposes `checked="mixed"` automatically (no manual `aria-checked` — [§3.3](#33-checkbox--tm-checkbox));
  space-to-toggle, clickable label.
- Select: `@angular/aria` combobox/listbox roles, `aria-expanded`/`aria-selected`/`aria-activedescendant`,
  full keyboard model, focus returned to the trigger on close, no focus trap. **Portaled-overlay sharp
  edge:** because the listbox renders in a CDK overlay outside the trigger's subtree, the trigger must
  reference it with **`aria-controls`** for the active-descendant relationship to reach assistive tech; a
  Playwright test asserts the trigger→listbox→active-option id chain resolves across the portal (`aria-owns`
  is the fallback if a tested AT needs implicit containment). Details in [§3.4](#34-select--tm-select).
- **Focus ring — the brand teal halo, never removed without replacement.** The teal halo with a white gap
  (`--focus-ring`) on `:focus-visible`; we never write `outline: none` (the common a11y regression) unless
  we provide an equally-visible substitute. Satisfies **WCAG 2.4.7 Focus Visible**; enforced by the axe
  gate plus a lint against bare `outline: none`.
- **Forced-colors and reduced-motion are gated, not just asserted.** `@media (forced-colors: active)` and
  `prefers-reduced-motion` are honored (the latter disables the 120–280ms fades), and both are tested in
  Playwright via `page.emulateMedia({ forcedColors: 'active' })` and `{ reducedMotion: 'reduce' }`,
  asserting the computed result (borders/focus ring remain visible under forced-colors; durations collapse
  under reduced-motion).
- **Target size — WCAG 2.2 AA, not 44px.** The conformance criterion is **2.5.8 Target Size (Minimum) =
  24×24 CSS px**, with its standard exceptions (sufficient **spacing**, an **equivalent** control elsewhere,
  **inline** targets, **essential** presentation). 44×44 is **2.5.5 (Enhanced), AAA** — a target we aim for
  on standalone, touch-primary controls where layout allows, not a floor. **Dense ERP contexts are fine:** a
  32px grid row or a compact `sm` field conforms via the 24px minimum and the spacing/essential exceptions
  (dense tabular data is essential presentation, and the grid is keyboard/pointer-driven). So "≈44px
  comfortable touch targets" (forms) and "32px dense rows" (grid) are different contexts under the same 24px
  rule, not a conflict.
- CDK a11y utilities (`FocusMonitor`, `LiveAnnouncer`, `Directionality`) reused, not reinvented.

## 7. RTL, i18n & l10n

- **RTL (rule 4 / D7):** CSS **logical properties only**; direction from CDK **`Directionality`**
  (auto-detected), never a per-component `rtl` flag. Adornment order, checkbox box side, and label
  alignment mirror via logical properties; the Select overlay's connected position mirrors via
  `Directionality` and is tested under RTL ([§3.4](#34-select--tm-select)).
- **Type is script-adaptive, never direction-keyed** (direction is a layout signal; script is a
  typography signal — a page can be trilingual). Components read `--font-ui`, a **single multi-script
  stack** (brand faces first, generics last): each glyph resolves to its script's face via the faces'
  `unicode-range`s ([§7.1](#71-fonts--web-font-loading)), so mixed Arabic/Latin lines render every brand
  face at once, and family names whose faces no installed pack registers are skipped harmlessly. Leading
  is a line-box property that cannot follow scripts per glyph, so **`:lang()` rules** emitted from the
  token contract's language→leading map both re-point `--leading-ui` and apply
  `line-height: var(--leading-ui)` (line-height inherits by computed value, so re-pointing alone would
  never reach below the root); every listed language both sets and resets, so an explicitly marked
  island (`lang="ar"` in an English page, or the reverse) gets its own leading at any depth. The rules
  live in `@layer tm.base` — unlayered component/app line-heights still win. Distributions set the root
  `lang` attribute when switching locale — required for assistive technology anyway.
- **Bidi text inside fields (mixed Arabic/English).** Form values routinely mix scripts (an Arabic name
  with a Latin code, a phone number in an RTL paragraph). The browser's Unicode Bidi Algorithm handles the
  *display* ordering, but the field's base direction must be right or punctuation and Latin runs land
  wrong. So text inputs set **`dir="auto"`**, picking each field's base direction from its own content's
  first strong character — independent of page/app direction, no per-field JS. **Alignment follows that
  base direction** (`text-align: start` resolves against the field's own `dir`): a field holding only
  English is left-aligned with an LTR caret even inside an RTL (Arabic) form, while an Arabic-first field in
  the same form is right-aligned; the surrounding label, required marker, and chrome still mirror to RTL via
  logical properties. Known rough edges (caret jumps, neutral-character placement at run boundaries) are
  covered by mixed-content tests in both LTR and RTL roots; we do not hand-roll a bidi algorithm.
- **Runtime i18n/l10n via Transloco.** The library's own labels (required-field announcement, select
  placeholder default, validation messages) are translated through a runtime i18n library. **Decision:
  standardize on Transloco**, consumed behind a *thin* one-function seam rather than the full multi-backend
  adapter of D8: an injection token `TM_UI_TRANSLATE` resolving to `(key, params?) => Signal<string>`, with
  the default implementation in `@tellma/core-ui` backed by Transloco (scoped/lazy-loaded library strings).
  **A distribution on the default path writes zero config** — `provideTellmaUi()`
  ([§5](#5-forms-integration-signal-forms)) wires the Transloco-backed default; the token only needs
  supplying to override it. The `contracts` entry point never imports Transloco (it stays dependency-free);
  only the components' default provider does. **Only English** ships in the core; **Arabic, Amharic, and
  every other locale ship as optional per-distribution locale packs** — the same mechanism that ships their
  fonts ([§7.1](#71-fonts--web-font-loading)). A pack bundles a locale's library strings, wired through a
  single **`provideTellmaLocale*()`** provider (Transloco scope), plus a static `@font-face` stylesheet
  the distribution adds to its build's `styles` ([§7.1](#71-fonts--web-font-loading)). A distribution
  installs the packs its tenants need; the core stays
  English-only. **`@tellma/locale-ar` ships in this phase** as the reference pack proving the mechanism
  end-to-end ([§1](#1-package--build-foundation), Goals).
- **Adapters named as future seams, not shipped in Phase 1.** `TmNumberAdapter` / `TmCurrencyAdapter` /
  `TmDateAdapter` (D8) are the locale/calendar seams later components will need (numeric, currency, date
  picker — e.g. a Hijri calendar from a locale pack). None of the three Phase-1 controls needs any, so
  Phase 1 neither declares nor implements them — they are roadmap context, recording where the seam lands.

### 7.1 Fonts & web-font loading

Fonts are shared by the components (via `--font-*` tokens) and the distribution shell, and the app must
run on an **isolated intranet (no font CDN)**. Strategy — low latency / fast first text paint, scalable to
many scripts (Amharic, Japanese, Hindi, Russian, …) without eagerly loading all of them:

- **Self-hosted, content-hashed `.woff2`** served from the app origin (works offline/intranet). No Google
  Fonts CDN in production. `@tellma/core-ui` ships only the **Latin** family (**Noto Sans**) plus **Noto
  Sans Mono** for code, and the `@font-face` machinery; components reference only `--font-*` tokens.
  **Arabic (Noto Sans Arabic) and every other script ship with their locale pack**, not the core — the same
  English-only baseline as the library strings ([§7](#7-rtl-i18n--l10n)).
- **`unicode-range` subsetting per `@font-face`** is the key to not eagerly loading every script: the
  browser downloads a face only when the page contains glyphs in that range. Non-Latin scripts ship as
  locale packs (`@tellma/locale-ar` → Arabic, `@tellma/locale-am` → Amharic, …), each contributing its
  `@font-face` blocks and that locale's strings; nothing for an uninstalled or unused script is downloaded.
- **`font-display: swap`** so text paints immediately in a fallback and swaps when the web font arrives.
- **Fonts flow through the regular application build pipeline.** Each package's `@font-face` stylesheet
  is added to the app's `styles` array; the builder rewrites and fingerprints the woff2 URLs into
  `media/…` outputs like any other CSS-referenced asset. Responsibility boundary:
  - **The core library ships** the Latin/Mono `@font-face` rules (with `unicode-range`) and their woff2.
  - **A locale pack contributes** (a) the locale's **library strings** as a Transloco scope wired by its
    provider (e.g. **`provideTellmaLocaleAr()`**), and (b) its **`@font-face` rules** as a stylesheet the
    distribution adds to its build's `styles` (faces fetch on demand via `unicode-range`). Installing the
    pack, calling its provider, and adding the stylesheet is the whole wiring — no build-time scan, no
    central registry.
  - **Preloading is a post-build step**, not runtime machinery: a small script scans the emitted
    stylesheets for fingerprinted `media/*.woff2` URLs and injects the matching
    `<link rel="preload" as="font" crossorigin>` tags into the built `index.html` (Latin by default;
    additional scripts opt in per distribution). Because it reads what the build emitted, preload hrefs
    can never drift from the real URLs. Unconfigured scripts are never preloaded and only fetch on demand
    via `unicode-range`.
- **Variable fonts** where available, to cut file count/weight (one file spans weights).
- **Long-cache immutable** (the builder's fingerprinted filenames, `Cache-Control: immutable, max-age=1y`)
  plus the PWA service-worker cache, so repeat loads are instant.

## 8. Performance budget

- **Zoneless + OnPush** (the v22 default; not set explicitly). Signal-driven, so only the changed control
  re-renders.
- **Minimal DOM:** text = one `<input>` + the field wrapper only when labelled; checkbox = label + hidden
  input + one box; select trigger = one element, and the **overlay panel is created lazily on first open**
  and torn down on close — closed selects cost nothing.
- **Long option lists:** `@for` + `track` now; `cdk/scrolling` virtual scroll drops in later without an API
  change.
- **Bundle budget** per entry point in CI, with **concrete initial ceilings, not "TBD"**, so the DoD's
  "within budget" isn't circular. Ceilings (gzipped, self-weight excluding shared Angular/CDK
  already in the app): `tmInput` ≤ 3 KB, `tm-checkbox` ≤ 4 KB, `tm-form-field` ≤ 4 KB, `tm-select` ≤ 8 KB
  (it carries the Overlay/listbox wiring), `@tellma/core-ui-tokens` runtime ≤ 8 KB (it ships the
  emitter + missing-ref gate as runtime code — the client-side validation of admin-authored token
  documents). These are **ratchets**:
  set to catch regressions now, inspected and tightened once real builds land, never loosened silently. The
  ceilings measure each component's own weight on top of an assumed Angular + CDK baseline — that baseline
  is a given (any real distribution ships components that pull in CDK), so counting CDK against `tm-select`
  would double-count. `sideEffects:false` + per-component entry points keep tree-shaking honest; CDK Overlay
  entering only via the `select` entry point (so a text/checkbox-only app avoids it) is a genuine but
  secondary nicety, not the basis for the budgets.
- Static, build-time token/base CSS — no runtime style generation ([§4](#4-tokens--theming-tellmacore-ui-tokens)).

## 9. Data-grid forward-compatibility contract

The editable Excel-like data grid is out of scope, but Phase 1 must not foreclose it (rule 6). Two
**draft** contracts shape every Phase-1 control to be grid-ready. **They are stubs** — `TmCellEditor` and
`TmCellDisplay` are minimal placeholders, properly designed and hardened when the grid is built (its real
requirements — range selection, clipboard, virtual scroll — will reshape them). Phase 1 declares them and
shapes the controls around them, but does **not** test-harden them or treat them as frozen.

- **`TmCellEditor<T>`** ([§2.1](#21-shared-contracts)) — the *edit* path, a TS interface every grid-able
  control implements so the grid drives commit/cancel/focus/keydown uniformly. Guarantees: external value
  ownership (the grid owns the model), **no self-owned focus trap or document-level listeners** (the grid
  owns Tab/Enter/Esc/arrow nav and forwards only what the cell editor consumes), and explicit
  `commit()`/`cancel()` (Enter/Tab commit, Esc cancels; for Select, Esc closes the panel first, then
  cancels — the Excel dropdown-cell behavior, [§3.4](#34-select--tm-select)). The Select overlay anchors to
  an arbitrary element (a cell rect) via the same `cdkConnectedOverlay` + `usePopover:'inline'` composition,
  which the grid inherits.
- **`TmCellDisplay<T>`** ([§2.1](#21-shared-contracts)) — the *readonly* path: a virtualized grid renders
  every non-edited cell as plain, non-interactive DOM (a formatted value in a `<span>`, a token-styled
  checkbox-glyph instead of a real checkbox) and instantiates the full interactive control **only for the
  cell being edited**. This standard technique (ag-Grid/Excel) is cleanly supportable because each control
  already separates a *pure display formatter* (`formatValue`, plus an optional token-driven `readonlyClass`
  for non-text glyphs) from its interactive behavior: the grid calls `formatValue` to paint thousands of
  cells with zero component instances, then swaps in the live editor on entering edit mode. Phase 1 shapes
  all three controls around this edit-path/display-path split so the draft interfaces *can* be implemented
  later; it ships no grid-specific code and — because the interfaces are stubs — does not lock them in with
  tests.

**What the edit-cell hosts (differs by control).** The host always owns the writable value channel
([§2.1](#21-shared-contracts)) and drives the editor through `TmCellEditor<T>`. For **text and checkbox**
the behavior is simple and DOM-native, so a grid edit-cell mounts the bare `<input tmInput>` / `tm-checkbox`
directly. For **select**, the behavior is delivered as `@angular/aria` *directives* that require an Angular
injection/template context — there is no way to instantiate the combobox/listbox behavior outside a
component — so a grid edit-cell **mounts the full `tm-select` component** and listens for its
`commit()`/`cancel()`. A short "embedding a control in a cell" note goes in each component's docs.

## 10. Testing (`@tellma/core-ui-testing`)

- **Component harnesses** (D11/D16) for all four: `TmInputHarness`, `TmCheckboxHarness`, `TmSelectHarness`
  (+ `TmOptionHarness` — a *collection* harness: open the panel, list/select options, read the active
  option), `TmFormFieldHarness`. Built on the CDK harness infrastructure, composing `@angular/aria`'s own
  harnesses where useful — those ship as **per-pattern secondary entry points**
  (`@angular/aria/listbox/testing` → `ListboxHarness`/`ListboxOptionHarness`,
  `@angular/aria/combobox/testing` → `ComboboxHarness`; there is **no** root `@angular/aria/testing`).
  This is the template every later component copies.
- **Where harnesses run — TestBed only; Playwright uses raw locators.** The CDK ships a
  `TestbedHarnessEnvironment` but no Playwright environment, and building/maintaining a custom
  `HarnessEnvironment` is not Phase-1 work. So harnesses drive the unit/TestBed layer (and remain the
  typed automation surface for agents), while the Playwright behavioral/e2e specs use **raw locators**
  (role/label-first, `data-testid` where semantics don't identify an element) against the showcase
  app's story pages.
- **API goldens** — for each entry point, **API Extractor** emits a diff-able `*.api.md` snapshot of the
  complete public API surface, committed to the repo, so a public-API change shows up as a golden diff in
  review (it matters when agent-generated code depends on a stable surface).
- **`api:approve` CI gate** — CI re-extracts the API and compares it to the committed golden; if they
  differ, CI fails. To land an intended change, a maintainer runs `api:approve` to regenerate and commit the
  golden, making every public-API change an explicit, reviewed act.
- **Unit tests** per component (zoneless test env), each building the live Signal Form its behavioral
  assertions need (`[formField]` binding, disabled/required precedence, pending state, message
  resolution): value flow via Signal Forms,
  validity/touched, **pending/async-validation state**, **prepopulated-value trigger label via
  `displayWith`**, **disabled/required field-vs-input precedence**, **message precedence + ICU/param
  interpolation**, indeterminate, and — for Select — open/close, keyboard nav, typeahead (explicit
  `label` and `textContent` fallback), selection, `valueKey`, the **prune guard** (async option turnover
  never commits or closes), Esc/outside-click close.
- **axe-core** specs per component (including the open Select panel) as the static floor.
- **Behavioral a11y specs (Playwright, the real gate):** keyboard navigation (arrows/Home/End/typeahead),
  focus return to the trigger on close, `aria-activedescendant` tracking across the portal, the **Esc
  behavior** (standalone: Esc closes the panel; grid: the host's second Esc cancels — [§3.4](#34-select--tm-select)),
  the trigger→listbox `aria-controls` AT-relationship chain, and the announcement *mechanism* (error
  `aria-live` updates, `aria-busy` while pending). Playwright is the standardized runner
  ([§6](#6-accessibility)). They cover what axe cannot, asserting the DOM/ARIA mechanism — not that a screen
  reader speaks it (manual pass, outside the DoD).
- **Select mouse-interaction specs (real mouse events).** Guarding the open aria-in-overlay bug
  ([angular/components#32504](https://github.com/angular/components/issues/32504), [§3.4](#34-select--tm-select)):
  the suite **must include mouse-driven specs** — driving selection with **real input events**
  (`locator.click()` / `page.mouse.click()`, which Playwright delivers as trusted mouse input), **not**
  synthetic `dispatchEvent('click')` and **not** the keyboard path — for: clicking an option commits and
  closes, clicking outside closes, clicking the trigger toggles. If the bug bites, the `tm-select` workaround
  (explicit pointerdown/selection handling + outside-pointer close) is switched on and these specs verify it
  holds. Tracked as a DoD item.
- **RTL specs:** mirrored layout, checkbox side, and the authored Select overlay positions under `dir="rtl"`
  (tested, not assumed — [§3.4](#34-select--tm-select)).
- **Contracts entry-point boundary + lint hygiene.** A lint **fails CI if `@tellma/core-ui/contracts`
  imports anything from `@angular/core` or the component modules** — it is types plus pure helpers only.
  This is a few lines of ESLint config (a path-scoped `no-restricted-imports`, or `eslint-plugin-boundaries`,
  on the `contracts/` folder), not test code. The same lint job also fails on cross-package leakage and bare
  `outline: none` ([§6](#6-accessibility)).
- **e2e:** the behavioral specs run against the showcase app's story pages on a real browser
  ([§11](#11-docs--mcp-pipeline-tellmacore-ui-mcp)).
- **The full suite always runs** — on PRs and on `main`. There is no changed-test selection: the suite
  is small enough that selection buys little, and the machinery it needs (a dependency graph, a
  merge-base diff, full-history checkouts) is complexity without payoff at this scale. nx `affected`
  is the upgrade if suite runtime ever demands selection ([§1.2](#12-build-tooling--pnpm--angular-cli-nx-deferred)).
- Tests are **on in CI** (D16).

## 11. Docs & MCP pipeline (`@tellma/core-ui-mcp`)

Per **D12/D13**, docs are generated from source as a single source of truth. The Phase-1 showcase is
the **internal showcase app** (`client/projects/internal/showcase`) — a dev-only host that serves each
component's story pages (addressable per dir × theme via query params) and doubles as the Playwright/axe
target; it is never published. The sample distribution is out of scope for now.

- Co-located `*.examples.ts` per component (canonical, copy-pasteable usage templates) + a short
  narrative `*.md`.
- API Extractor (`.api.json`) + a thin extractor → **`components.json`** (single source of truth).
- `components.json` feeds the showcase, `llms.txt`, the scoped **`@tellma/core-ui-mcp`** server
  (Phase 1: `list` / `describe` / `example` tools), and the API goldens.
  - **`llms.txt`** is a single, flat, agent-readable Markdown digest of the library's surface (components,
    selectors, inputs/outputs, tokens, a canonical example each) at a conventional path — the *static,
    no-server* path for a coding agent (including ones building distributions) to load the whole library in
    one fetch, where the MCP server is the *interactive* path (query a single component on demand). Both are
    generated from `components.json`, so they never diverge from the code.
- The federated `dotnet tellma mcp` umbrella is not in Phase 1.

**`components.json` schema** (defined here because it feeds everything else). A generated, versioned JSON
document validated against its own JSON Schema in CI, so the MCP server, `llms.txt`, the showcase, and
goldens consume a stable shape. Phase-1 shape:

```ts
interface ComponentsJson {
  schemaVersion: string;            // semver of THIS schema; consumers pin/validate it
  libraryVersion: string;           // @tellma/core-ui version it was generated from
  components: ComponentDoc[];
}
interface ComponentDoc {
  name: string;                     // 'tm-select'
  kind: 'component' | 'directive';
  group: string;                    // taxonomy as metadata, not a folder: 'form-control' | 'layout' | … (§12)
  selector: string;                 // 'tm-select' | 'input[tmInput]'
  entryPoint: string;               // '@tellma/core-ui/select'
  formControl?: 'FormValueControl' | 'FormCheckboxControl' | null;
  description: string;              // the component's JSDoc text (consistent with PropDoc/SlotDoc below)
  inputs:  PropDoc[];               // name, type, default, required, description, signal: 'input'|'model'
  outputs: PropDoc[];
  slots:   SlotDoc[];               // ng-content / typed ng-template contexts
  tokens:  string[];                // CSS custom properties the component reads
  a11y:    { roles: string[]; keyboard: string[]; notes: string };
  examples: ExampleDoc[];           // title, code, from the examples files
  harness: string;                  // 'TmSelectHarness'
  status:  'stable' | 'experimental' | 'deprecated';
  deprecation?: { since: string; replacement?: string };  // pairs with @breaking-change
}
interface PropDoc { name: string; type: string; default?: string; required: boolean; description: string; signal?: 'input'|'model'|'output'; }
interface SlotDoc { name: string; selector: string; contextType?: string; description: string; }
interface ExampleDoc { title: string; code: string; }
```

The extractor derives every field from typed source (signal `input()`/`model()`/`output()`, JSDoc, the
harness, co-located examples); nothing is hand-authored, so docs can't drift from code, and the schema is
the contract the MCP/goldens/showcase build against.

## 12. Directory layout

Each **component is its own secondary entry point** — a flat sibling folder with its own `ng-package.json`
+ `public-api.ts`, importable as `@tellma/core-ui/select`. The **package root is the code root**: the
primary `@tellma/core-ui` entry point's `public-api.ts` sits at the root, its cross-cutting internals
(providers, i18n, fonts, forms infrastructure — no components) are plain folders without an
`ng-package.json`, and there is no `src/` wrapper. Every folder in a library either *is* an entry point
or belongs to the primary; entry points consume shared code only via its import path
(`@tellma/core-ui`), never via `../` relative paths — each entry point is its own compilation unit.

```
client/projects/core/
├── tellma-core-ui/
│   ├── ng-package.json        # primary entry point @tellma/core-ui
│   ├── public-api.ts          #   re-exports the providers/i18n/fonts/forms surface (no components)
│   ├── forms/                 #   provideTellmaForms(), field-state helpers, message resolver
│   ├── providers/             #   provideTellmaUi() umbrella (composes forms + i18n + fonts defaults)
│   ├── i18n/                  #   TM_UI_TRANSLATE token + Transloco-backed default
│   ├── fonts/                 #   self-hosted woff2 + @font-face stylesheet (added to app styles)
│   ├── contracts/             # secondary EP @tellma/core-ui/contracts — ng-package.json + public-api.ts:
│   │                          #   SignalLike/WritableSignalLike, TmFormFieldControl, TmFieldError, draft TmCellEditor/TmCellDisplay
│   ├── input/                 # secondary EP @tellma/core-ui/input    — tmInput directive
│   ├── checkbox/              # secondary EP @tellma/core-ui/checkbox — tm-checkbox (inline template)
│   ├── form-field/            # secondary EP @tellma/core-ui/form-field — tm-form-field (inline template)
│   ├── select/                # secondary EP @tellma/core-ui/select   — tm-select + tm-option (@angular/aria + CDK Overlay)
│   └── spinner/               # secondary EP @tellma/core-ui/spinner  — tm-spinner, the shared pending glyph
├── tellma-core-ui-tokens/
│   ├── public-api.ts
│   ├── contract/              # TmTokens types
│   ├── presets/tellma-default.ts
│   ├── emit/                  # tokens → CSS emitter
│   └── schema/                # the missing-ref validator feeding the generated JSON Schema
├── tellma-core-ui-testing/
│   ├── public-api.ts          # the harnesses (incl. TmSelectHarness + TmOptionHarness)
│   └── *-harness.ts
└── tellma-core-ui-mcp/
    └── src/                   # plain Node package (tsc): generated components.json + minimal MCP server
```

The reference locale pack lives in a sibling `locale/` family (locale packs are not `core-ui` packages),
proving the structure later packs (`@tellma/locale-am`, …) copy:

```
client/projects/locale/
└── tellma-locale-ar/                 # @tellma/locale-ar — the reference Arabic locale pack
    ├── ng-package.json
    ├── public-api.ts                 # provideTellmaLocaleAr(): the pack's Transloco strings
    ├── strings-ar.ts                 # Arabic translations for the built-in library strings
    └── fonts/                        # self-hosted Noto Sans Arabic woff2 + @font-face (unicode-range) stylesheet
```

If entry points later grow shared machinery that should not be public API, it becomes a `private/`
entry point (importable — so it compiles once — but carrying no stability guarantees), the pattern
`@angular/aria` and the CDK use.

**Why this shape (and how it scales to ~40 components later).**

- **Flat per-component folders, *not* category-nested folders.** Material, the CDK, and PrimeNG all keep
  components as a flat list of sibling folders, never a `forms/`/`layout/`/`feedback/` tree. Category is
  **metadata, not a folder** — it lives in the `group` field on `ComponentDoc`
  ([§11](#11-docs--mcp-pipeline-tellmacore-ui-mcp)), re-groupable freely. So adding later components is
  append-a-folder with no reorganization: the taxonomy was never encoded in the directory tree.
- **Per-component secondary entry points** give three things a single barrel file cannot: an import
  path decoupled from disk location (`@tellma/core-ui/select` is stable even if the folder moves), a hard
  tree-shaking boundary (a text/checkbox-only app importing `@tellma/core-ui/input` never pulls in Select's
  CDK-Overlay/aria weight — the basis for the per-entry-point [§8](#8-performance-budget) budgets), and a
  natural unit for the API golden per surface. Cost: one small `ng-package.json` per component, which
  `ng generate` scaffolds.
- **`provideTellmaForms()` and `provideTellmaUi()` live in different folders** because the former is a
  forms-domain artifact (it configures the error-display policy and message resolver the rest of `forms/`
  implements) and lives with its domain in `forms/`, while the latter is the app-composition umbrella
  (composing forms + i18n + font defaults across domains) and lives in the neutral `providers/` folder.
  (Minor call; co-locating both in `providers/` would be fine too.)

The showcase app lives in the workspace at `projects/internal/showcase` (free-port launch per
[§1.3](#13-worktree-isolated-port-free-tooling)).

## 13. Definition of done

1. All four core packages **and `@tellma/locale-ar`** build, lint (incl. the `tm-` selector rule), and are
   consumable by an in-repo app via workspace path mappings; `@tellma/core-ui/contracts` resolves as a
   secondary entry point.
2. `tmInput`, `tm-checkbox`, `tm-select`, `tm-form-field` work bound via `[formField]` in a **Signal Form**
   (each implementing the correct interface — `tm-checkbox` via `FormCheckboxControl` with **no `value`
   property**, enforced by lint/API golden), themed from the brand preset, in light and dark, LTR and RTL.
3. `tm-form-field` renders the localized **error/hint** by reading field state off the control
   (`errors`/`touched`/`dirty`/`invalid`/`pending`), and the **disabled/required precedence** holds (field
   wins when bound; component inputs apply only unbound).
4. Each component: unit tests green, harness shipped, **axe clean** (static floor), and **behavioral
   Playwright a11y specs green** — keyboard nav, focus return, `aria-activedescendant` + `aria-controls`
   across the portal, the standalone Esc-closes-panel behavior, `aria-live`/`aria-busy` announcements.
   **Playwright is the standardized a11y runner ([§6](#6-accessibility)) — not implementer's choice.**
5. **RTL spec green** — the Select overlay's rendered position verified under `dir="rtl"` (mirroring is
   `Directionality`-automatic per [§3.4](#34-select--tm-select), but the result is tested, not assumed).
6. Select: the settled **nested `cdkConnectedOverlay` + `ngComboboxPopup` composition**
   ([§3.4](#34-select--tm-select)) works — `usePopover:'inline'` panel escapes an `overflow:hidden`
   clipping ancestor; flip-up works (with the `updatePosition()`-on-`(attach)` macrotask fix); aria's popup
   registration and the `aria-controls`/`aria-activedescendant` id chain resolve across the overlay
   relocation; `focusMode="activedescendant"` + `selectionMode="explicit"`; **`disableClose` on the overlay
   so aria alone owns Esc**, every close path routing through the shared `expanded` signal; **commit on
   activation events, never `valueChange`**; lazy overlay; captures `tm-option.value` while displaying
   projected label. **RTL residual diagnosed and closed:** the spike's RTL symptom root-caused, position
   mirroring and `matchWidth` verified under `dir="rtl"`. **Mouse-interaction specs (real mouse events)
   green, guarding [angular/components#32504](https://github.com/angular/components/issues/32504):** clicking
   an option commits and closes, clicking outside closes, clicking the trigger toggles
   ([§10](#10-testing-tellmacore-ui-testing)).
7. Select prepopulated/async value integrity: a **prepopulated value survives until its `ngOption`
   renders** (not just its label) — the `FormValueControl<T>` model stays source-of-truth and is re-applied
   to aria's listbox when options arrive, defeating aria's unmatched-value auto-prune; **a prune never
   commits, wipes the form value, or closes the panel** (the activation-commit guard); the **trigger label
   resolves via `displayWith`** before any option renders.
8. Pending/async-validation state shows `aria-busy` + spinner and suppresses stale "valid".
9. Every entry point is **within its concrete bundle ceiling** ([§8](#8-performance-budget)); the token
   preset passes the schema + missing-ref gate in both schemes;
   runtime CSS-variable override demonstrated (`setProperty` changes `--color-primary` live); the
   `@layer tm.base, tm.theme` precedence is verified (a `tm.theme` delta overrides base regardless of load
   order).
10. The **contracts-boundary lint** passes: `@tellma/core-ui/contracts` imports nothing from `@angular/core`
    or the component modules; no cross-package leakage; no bare `outline: none`.
11. The controls are **shaped** for grid embedding (rule 6) but not locked to the draft contracts: a bare
    `<input tmInput>` mounts with no `tm-form-field` and holds no document-level listeners; a `tm-select`
    panel anchors to an arbitrary element and exposes `cancel()` for a host to drive (the second-Esc revert
    is the grid host's, not the standalone control's — [§3.4](#34-select--tm-select)); each control
    separates a pure display formatter from its interactive behavior. The draft `TmCellEditor`/`TmCellDisplay`
    interfaces ([§2.1](#21-shared-contracts), [§9](#9-data-grid-forward-compatibility-contract)) are **not**
    test-hardened in this phase.
12. The library's font piece is in place: self-hosted woff2, `@font-face` with `unicode-range` subsetting +
    `font-display: swap` — no CDN reference; tests
    assert an unconfigured script contributes no eager download. (Runtime per-tenant preload injection is
    distribution-shell scope, not tested here.)
13. The reference **Arabic locale pack `@tellma/locale-ar` ships and works end-to-end**: installed (via
    `provideTellmaLocaleAr()`), it adds Arabic library strings resolved through `TM_UI_TRANSLATE` and
    contributes self-hosted **Noto Sans Arabic** (woff2 + `@font-face` stylesheet next to the
    multi-token). A test asserts that **with** the pack an Arabic locale renders Arabic strings (and the
    Arabic face is available), and **without** it the same keys fall back to English (no blank/raw key) and
    no Arabic font is fetched; **switching the active locale at runtime re-renders already-visible error
    text in the new locale** (the reactive `errors` derivation, [§5](#5-forms-integration-signal-forms)).
    The core stays English-only; the pack is the template later packs copy.
14. `components.json` is generated and **validated against its JSON Schema** ([§11](#11-docs--mcp-pipeline-tellmacore-ui-mcp));
    the scoped MCP server answers `list`/`describe`/`example`; `llms.txt` and the showcase app render.
    API goldens committed; `api:approve` gate active.
15. Forced-colors and reduced-motion are **Playwright-gated** (`emulateMedia`); bidi `dir="auto"` fields
    verified with mixed AR/EN content in both LTR and RTL roots; message precedence + ICU/param
    interpolation covered by the component unit specs.
16. All tooling (the showcase app, tests, MCP server) runs on OS-assigned free ports — two worktrees in
    parallel, no collision.

## Decisions confirmed

The earlier open questions are settled:

1. **Repo home** — the UI family lives in `client/projects/core/`; locale packs (the reference
   `@tellma/locale-ar`, and later ones) live in the sibling `client/projects/locale/`.
2. **Build tooling** — pnpm + Angular CLI for Phase 1; nx revisited later if the in-repo project count or
   suite runtime grows ([§1.2](#12-build-tooling--pnpm--angular-cli-nx-deferred)).
3. **i18n** — standardize on Transloco behind the thin `TM_UI_TRANSLATE` escape-hatch token; the default
   path is zero-config for distributions ([§7](#7-rtl-i18n--l10n)).
4. **Density/typography runtime axes** — deferred, but a design requirement to be addable later without a
   major refactor (token-set switching; no component-internal changes).
5. **Showcase** — the internal showcase app (dev-only, never published), which is also the e2e target;
   the sample distribution is out of scope for now.
6. **Templates** — inline for these small components (v22 best practice supersedes D5 here).

The Select-architecture and forms-precedence questions were investigated against `@angular/aria@22` and
`@angular/forms@22` source and are settled in [§2](#2-behavior-layer-and-shared-contracts),
[§3.4](#34-select--tm-select), [§5](#5-forms-integration-signal-forms), and
[§9](#9-data-grid-forward-compatibility-contract): aria owns Select's keyboard/typeahead/active-descendant/
open-close as DI directives (so `tm-select` owns only the scalar↔array bridge, value→key mapping, label
resolution, and grid commit/cancel — no separate pattern class), and Signal Forms binds field state into
the control's declared optional inputs (the documented contract), with a conflicting template binding
forbidden by lint rather than relied on as framework write-order. The riskiest piece — composing aria's
inline-deferred popup with CDK-Overlay connected positioning — was settled by a running Angular-22 +
Playwright spike, leaving only the spike's RTL symptom to root-cause (CDK position mirroring itself is
automatic), tracked in the DoD. Implementation can proceed against this spec.
