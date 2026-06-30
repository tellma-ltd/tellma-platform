# Spec: UI Component Library — Forms Foundation

**Author:** Ahmad Akra
**Date:** 29 Jun 2026

**Status:** Foundation specification — the frozen, authoritative description of the work. The research
analysis that preceded it is superseded by this document.

**Departures from the research analysis's locked decisions** (this spec supersedes them where they
conflict):
- **D9 → Signal Forms only.** Drop the `ControlValueAccessor` dual-compat requirement. Signal Forms
  is stable in Angular v22 and every consumer is greenfield v22+, so the CVA fallback buys nothing.
- **D6 → CSS-variable theming, no builder.** No theme-builder UI is planned, ever. Themes are the
  emitted CSS custom properties; overrides are authored as CSS (or set at runtime on a scope). Drop
  the `dt()`/typed-passthrough framing.
- **`@angular/aria` is stable in v22** (graduated from developer preview) — the analysis's open
  "aria maturity" question is resolved; we build on it.
- **Inline templates for small components.** The Angular CLI MCP's `get_best_practices` is the
  source of truth for framework conventions and takes precedence over the research analysis, so D5's
  external-template preference is superseded: small components (all of Phase 1) use inline templates;
  external `.html` is reserved for larger components with rich named slots.

## Context

The platform builds a greenfield Angular component library, shipped as a `core-*` package family
every distribution references, built on `@angular/cdk` + `@angular/aria`, signal-first,
`tm-`-prefixed. The rationale and the Material/PrimeNG comparison that preceded this spec live in
the research analysis at
[`docs/research/angular-component-library-analysis.md`](../research/angular-component-library-analysis.md)
(its locked decisions are cited below as **D1–D13**), now superseded by this document. The default
look — colors, type, spacing, the shared form-field token group, focus ring, dark mode, RTL/Arabic
posture — is fixed by the Tellma design system (`tellma-brand/design-system`, especially
`tokens/*.css` and the `forms/` reference components).

This spec covers **Phase 1**: the *foundation* — the thinnest end-to-end slice that stands
up the whole architecture (all four core packages plus the reference locale pack, the `@angular/aria` behavior layer, the token emitter, the
forms contract, harnesses, the a11y/RTL/perf gates, the docs/MCP pipeline) while shipping only
**three production components**:

1. **Text input** — single-line text field.
2. **Checkbox** — boolean / tri-state choice.
3. **Select** — single-select dropdown (a listbox in an overlay panel).

Plus the one piece of scaffolding all three need to be usable: **`tm-form-field`** (label /
required marker / hint / error). `tm-form-field` is the single most underweighted-yet-essential
component — every form control depends on it for labelling, the required marker, and error display —
so it is in scope as supporting infrastructure, not as a fourth headline component.

### Why these three (the de-risking rationale)

The foundation exists to **force every load-bearing architectural seam to be built once**, on
the thinnest possible slice, so the risky integrations are proven before 40 later components are
templated on top of them. The set is chosen to maximize *distinct hard-seam coverage*, not feature
breadth. Each of the three pierces a seam the others do not:

- **Text input** — the foundational seam: the `tm-form-field` contract, the Signal Forms
  custom-control binding, the base field. Irreducible; everything text-like descends from it.
- **Checkbox** — a *custom-rendered binary control* (hidden native input + styled box), distinct in
  DOM shape from text, owning the tri-state (the native `.indeterminate` IDL property, which the browser
  surfaces as `checked="mixed"`). It is the template for a
  whole family — radio, toggle/switch — at near-zero marginal cost.
- **Select** — the high-leverage seam the flat controls miss entirely: **CDK-Overlay connected
  positioning composed with aria's inline-deferred popup** (the riskiest Phase-1 seam — now
  **validated by a running spike**, see [§3.4](#34-select--tm-select)), **`@angular/aria`
  listbox/combobox** (which validates the central build-on-aria decision of D1/D4), **keyboard
  navigation + typeahead + active-descendant a11y**, and a *collection* harness rather than a
  single-value one. This infra is reused by autocomplete, date picker, entity picker, menu, popover,
  and **every dropdown editor in the future data grid** — which is exactly why proving it once, now,
  matters. *"Reused" here means concrete code reuse, not just shared concepts:* the overlay/aria
  composition wiring of [§3.4](#34-select--tm-select) (the `cdkConnectedOverlay` + `ngComboboxPopup`
  nesting, the `usePopover:'inline'` clipping-escape, the `updatePosition()`-on-`(attach)` fix) and
  the pure value→key / label-resolution helpers ([§2.1](#21-shared-contracts)) are extracted as shared
  functions/directives those later components import directly. What is *not* shared code is each
  component's own public API, template, and chrome — those follow the established **template/pattern**
  but are written per component. So the reuse is both: shared plumbing as code, plus a proven shape to
  copy for the rest.
  Select also stress-tests the grid-embedding contract (rule 6) harder than any flat control: an
  overlay anchored to a cell, with Esc/commit/Tab interplay against grid navigation, is the case
  that actually shapes the cell-editor design.

The point of Phase 1 is **not** breadth. It is to prove the spine — component (+ `@angular/aria` where
needed) → tokens → Signal Forms → harness → axe/RTL/bundle gates → generated docs — works once, across
both a flat control *and* an overlay/collection control, so every later component is a fill-in-the-blank
exercise against an established template.

### Guiding rules (from the task brief)

These are acceptance constraints, not aspirations. Every component below is checked against them:

1. **Fast and smooth to render, especially on low-cost mobile.** No unnecessary deep object
   hierarchy; minimal DOM per control; zoneless + OnPush (the v22 default); overlays created lazily;
   no per-keystroke layout thrash.
2. **Accessibility to WCAG 2.1 AA.** Verified by axe-core in CI, not by inspection.
3. **Fluent on mobile and touch.** Adequately sized touch targets (the conformance rule is the
   WCAG-2.2 AA **24×24** minimum, *not* 44px — see [§6](#6-accessibility)); no hover-only
   affordances, no `:hover` traps.
4. **Native LTR and RTL.** CSS logical properties only; direction from CDK `Directionality`;
   overlay positions are authored RTL-aware and tested (not assumed); no per-component `rtl` flag.
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

- Stand up all four `@tellma/core-ui*` packages (plus the reference `@tellma/locale-ar` pack, below)
  with real (if small) contents and working build, test, lint, and docs pipelines.
- Ship `tmInput`, `tm-checkbox`, `tm-select`, and `tm-form-field` to production quality:
  a11y-complete, RTL-complete, themed from the brand tokens, Signal-Forms-native.
- Ship the **`@tellma/locale-ar` (Arabic) locale pack** as the **reference locale pack**. The
  locale-pack mechanism (a locale's library strings **+** its self-hosted font subset **+** its
  `TM_FONT_SUBSETS` manifest entries, merged at build and preloaded per tenant — [§7](#7-rtl-i18n--l10n)/
  [§7.1](#71-fonts--web-font-loading)) is a load-bearing seam, so the foundation proves it once with a
  real pack rather than leaving it on paper. Arabic is the natural first pack: the brand is RTL-first,
  Tellma's primary markets (KSA/UAE) need it on day one, and it exercises the RTL + Arabic-font +
  Arabic-strings path the spec already gates. **The core stays English-only** ([§7](#7-rtl-i18n--l10n));
  Arabic ships as this separate, installable package — that separation *is* the thing being proven.
- Prove the shared overlay/positioning + aria-listbox + keyboard-navigation infrastructure once,
  via Select, so later overlay/collection components reuse it.
- Establish the canonical component template, the harness template, and the
  `*.stories.ts` → `components.json` docs template that every later component copies.
- Encode the brand design tokens into the typed `TmTokens` contract + emitter, with one default
  preset that reproduces `tellma-brand/design-system` and a build-time WCAG-contrast gate.

**Non-goals (explicitly deferred)**

- All other components (numeric, currency, textarea, **date picker**, entity picker, data grid,
  radio, toggle, multi-select, autocomplete, buttons, layouts, nav, modal, menu, popover, etc.).
- Multi-select, option groups, and virtual scroll for long option lists. Phase-1 Select is
  single-select with a flat option list; the component is shaped not to preclude these.
- The `TmNumberAdapter`/`TmDateAdapter` and the components that need them (numeric, date picker). A
  **date picker is its own future component** — a dropdown-calendar overlay built on the same
  CDK-Overlay + aria infra Select establishes; the `TmDateAdapter` is the multi-calendar
  (Gregorian/Hijri/Ethiopian) abstraction that picker *depends on*, not a text-field substitute for
  it. See [§7](#7-rtl-i18n--l10n).
- **Locale packs beyond Arabic** (Amharic, and any other script/language). The locale-pack mechanism is
  proven once with the reference `@tellma/locale-ar`; additional packs are mechanical follow-ons against
  that template, not foundation work. (`@tellma/locale-ar` itself **is** in scope — see Goals.)
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

## 1. Package & build foundation

The four `@tellma/core-ui*` packages are created under `client/projects/core/`, plus the reference
`@tellma/locale-ar` pack. Phase 1 puts real contents in three core packages, a stub-but-wired version
in the fourth (`-mcp`), and a real (if small) Arabic locale pack.

| Package | Phase-1 contents |
|---|---|
| `@tellma/core-ui` | The components — `tmInput` directive; `tm-checkbox`; `tm-select` + `tm-option` (overlay panel via CDK Overlay, listbox via `@angular/aria`); `tm-form-field`; `provideTellmaForms()`/`provideTellmaUi()`; the static base CSS; the self-hosted default fonts + `@font-face` ([§7.1](#71-fonts--web-font-loading)). Plus a **`@tellma/core-ui/contracts`** secondary entry point holding the `SignalLike`/`WritableSignalLike` boundary types and the `TmFormFieldControl`/`TmCellEditor`/`TmCellDisplay` interfaces ([§2.1](#21-shared-contracts)). Each component is its own secondary entry point (`@tellma/core-ui/input`, `/checkbox`, `/select`, `/form-field`); the primary `@tellma/core-ui` entry point carries the providers, i18n, fonts, and forms infrastructure ([§12](#12-directory-layout)). |
| `@tellma/core-ui-tokens` | `TmTokens` TS contract; the brand default preset; the `tokens → CSS variables` emitter; generated JSON Schema; build-time schema + WCAG-contrast validation. |
| `@tellma/core-ui-testing` | `TmInputHarness`, `TmCheckboxHarness`, `TmSelectHarness` (+ `TmOptionHarness`), `TmFormFieldHarness`. |
| `@tellma/core-ui-mcp` | Generated `components.json` for the components; a minimal MCP server exposing `list/describe/example` tools over it. Wired into the build; tool breadth is later. |
| `@tellma/locale-ar` | The **reference locale pack**: Arabic translations for the library's built-in strings (validator-key messages, placeholders, the required-field announcement) as a Transloco scope, **plus** the self-hosted **Noto Sans Arabic** woff2 + `@font-face` (`unicode-range`) and its `TM_FONT_SUBSETS` manifest entries — all wired by a single **`provideTellmaLocaleAr()`** a distribution adds to its app config (merge mechanism in [§7.1](#71-fonts--web-font-loading)). Installing it adds Arabic to a distribution; not installing it leaves the core English-only. It is the template every later locale pack (`@tellma/locale-am`, …) copies. |

**Build & tooling (shared, established once):**

- pnpm workspace + **ng-packagr** per package; per-component secondary entry points;
  `"sideEffects": false`. (D1/D2 — no Bazel.)
- Angular **v22**, standalone, **zoneless**, signal-first public API (`input()`/`model()`/`output()`).
  Follow the v22 best practices: do **not** set `standalone` or `OnPush` explicitly (both default in
  v22); host bindings live in the `host` object (never `@HostBinding`/`@HostListener`); `computed()`
  for derived state; `inject()` over constructor injection; `@Service` for new singletons; native
  control flow; no `ngClass`/`ngStyle`.
- Depends on `@angular/cdk` (Overlay, Portal, a11y, Directionality), `@angular/aria` (listbox/
  combobox + harnesses), and `@angular/forms/signals` (Signal Forms) as the shared foundation.
  **`@angular/aria` and Signal Forms are stable as of the v22 release** (graduated from developer
  preview), per the [Angular v22 announcement](https://blog.angular.dev/announcing-angular-v22-c52bb83a4664),
  the [`@angular/aria` npm package](https://www.npmjs.com/package/@angular/aria), and the v22 docs at
  [angular.dev/guide/aria](https://angular.dev/guide/aria/listbox) and
  [angular.dev/guide/forms/signals](https://angular.dev/guide/forms/signals/custom-controls). The
  Angular CLI MCP's `get_best_practices` (v22) likewise lists Signal Forms as stable and OnPush as the
  default. **Version pinning:** `@angular/aria` ships in lockstep with the Angular framework, so it is
  **pinned to a single Angular minor at a time** — the **22.x** line at authoring — with `@angular/aria`
  and `@angular/core` always on the *same* minor. "Single pinned version" and "tracks the latest stable"
  are not in tension: the platform holds **one** Angular minor across every package and **bumps it
  deliberately, as one platform-wide step**, to the latest **stable** release (never `next`/preview,
  never an automatic per-package float). At any moment there is exactly one Angular version in the repo;
  moving it is a conscious, all-at-once upgrade.
- ESLint flat config + Prettier. A custom ESLint selector rule enforces the `tm-` / `Tm…` prefix on
  component selectors and exported symbols (D3); a **stylelint rule enforces a `tm-` prefix on every
  CSS class name** the library authors, so no library class can collide with a distribution's own
  styles. A second **stylelint rule bans hardcoded sizing values in component CSS** — numeric lengths
  (`px`/`rem`/`em`) on size/space/typography properties (height, padding, margin, gap, font-size,
  border-radius, inline/block sizing, etc.) must come from a **token CSS variable** (`var(--field-*)`,
  `var(--space-*)`, …), not a literal. This is what keeps the deferred **density/typography runtime
  axes** ([Non-goals](#goals--non-goals), [§4](#4-tokens--theming-tellmacore-ui-tokens)) addable later
  without a refactor: if every component already sizes itself from tokens, switching the token set is
  the *only* change needed. (Allowed literals: `0`, hairline `1px` borders, and anything explicitly
  allowlisted; the rule targets sizing, not e.g. `1px solid`.) (Commit-message linting — conventional commits, `commitlint` — is a **repo-wide** concern
  configured once at the platform root, not specific to this library, so it is out of scope here.)
- **API goldens** per entry point via Microsoft API Extractor + an `approve-api` CI gate (D11) — see
  [§10](#10-testing-tellmacore-ui-testing).
- CI gates: unit + harness tests, **axe-core**, **bundle-size budget**, API golden, lint. Tests
  always on. (No SSR gate — distributions are client-rendered. This is a deliberate **one-way door**:
  the top-layer `[popover]` overlay, `font-display: swap`, and zoneless assumptions are CSR-shaped, so a
  future surface that needs SSR/SSG would have to revisit them — flagged so it's a conscious choice, not
  a surprise.)

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

Phase 1 uses **pnpm workspaces + the Angular CLI + ng-packagr** — no nx. The reason is structural, not
just "small now": **nx's headline wins (project-graph caching, `affected`, distributed cache) scale
with the number of *projects*, and the UI library's project count is essentially fixed — four core
packages plus a small, slowly-growing set of locale packs (one per supported language, a low-tens
ceiling at most). It does not grow as the component library fills out.** What *does* grow with every new
component lives *inside* those core packages — more components, tokens, harnesses, tests, stories, and
`components.json` entries — and that intra-package growth is served by the **test runner's own
incremental/changed-file selection**, not by a cross-project task graph. (Each new locale pack is one
more project, but it is a self-contained leaf with no cross-project graph to cache, so it doesn't move
the nx calculus either.) So nx would optimize the axis that stays constant while
adding onboarding and tooling-sprawl cost (the same low-onboarding argument that rejected Bazel in D1;
the Angular side already sits beside the .NET MSBuild build). We **revisit nx** only if the *project*
count climbs — many in-repo distributions, or the UI family splitting into many packages. Package
boundaries are nx-ready regardless, so adoption later is additive.

### 1.3 Worktree-isolated, port-free tooling

The platform's parallel-local-development rule applies: every build/test/run path and any hosted
tool must run in parallel across isolated git worktrees with **no hardcoded localhost ports** and no
shared mutable global state:

- Storybook, the test runner, the MCP server, and any dev/preview server bind to an
  OS-assigned free port (or read the worktree's `.dev-ports.local`), never a literal port.
- Test artifacts, caches, and any emitted files are written under the worktree (or a per-worktree
  namespaced path), so two agents testing two worktrees never collide.
- The MCP server and Storybook are launched by scripts that follow the same free-port discovery the
  platform's `dotnet tellma setup-worktree` flow uses; nothing assumes a singleton instance.

## 2. Behavior layer and shared contracts

**There is no per-component headless "pattern" layer.** `@angular/aria` (stable in v22) *is* the
headless behavior layer for everything with a non-trivial keyboard/selection model — listbox,
combobox, menu, grid, tree — and each styled `tm-*` control owns the rest of its own logic **directly,
as an ordinary Angular component/directive** (signals, `effect()`, DI, lifecycle — all available and
used normally). Genuinely-shared, framework-agnostic helpers (value→key mapping, value formatters) are
plain exported functions, not a class per control.

A separate `Tm*Pattern` class per control (the earlier D4 split) was **dropped**: with aria providing
the real behavior layer, the leftover per-control logic is too thin to justify a second layer plus the
`SignalLike` indirection and the no-`effect()` constraint a non-DI class would impose. The
headless-engine approach is **reserved for the future editable data grid** — a substantial,
aria-uncovered state machine (tab/enter/arrow nav, range selection, virtual scroll, clipboard,
undo/redo) where an isolated, separately-tested core genuinely earns its keep; it would ship in its
own package when built, driven by a real second consumer rather than speculatively now.

### 2.1 Shared contracts

The cross-cutting contracts live in a **secondary entry point of `@tellma/core-ui`**
(`@tellma/core-ui/contracts`), **not a separate package** — they are zero-/low-runtime types plus a
couple of pure helpers, and the only thing that needs them besides the components is the future grid,
which depends one-directionally on `@tellma/core-ui` anyway (no cycle to break). A lint keeps this
entry point free of component/DI imports so the grid can import the contracts without pulling in the
components.

`SignalLike`/`WritableSignalLike` are the **boundary types a host uses to drive a control it owns** —
in particular the grid, which owns a cell's value and passes it to the editor through the write
channel:

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
  readonly empty: SignalLike<boolean>;           // control currently holds no value — drives the field's
                                                 //   empty/placeholder styling and "show hint vs error" logic
  readonly describedByIds: SignalLike<string[]>; // ids the control currently exposes via aria-describedby
                                                 //   (read so the field can merge, not clobber, existing ones)
  setDescribedByIds(ids: string[]): void;        // field pushes its hint/error element ids; control writes
                                                 //   them into aria-describedby (the MatFormFieldControl seam)
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
// `key` is the validator key that produced the error — 'required', 'minlength', 'email', … — the same
// key §5's message resolver maps to a localized default. It is the machine-readable category, distinct
// from `message` (the human-readable, already-localized text); consumers branch styling/logic on `key`.
export interface TmFieldError { readonly key: string; readonly message: string; }

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
them, never *how to translate* them. **Value ownership:** Signal Forms requires the bound
`value`/`checked` `model()` to live on the control ([§5](#5-forms-integration-signal-forms)); in a
form, the control owns it; in a grid, the host owns it and passes it to the control through
`TmCellEditor.value` (the `WritableSignalLike` write channel), and the control keeps a private
`lastCommitted` for revert. The per-control specifics (what each control owns vs. delegates to aria)
are in [§3](#3-the-styled-layer-tellmacore-ui).

## 3. The components (`@tellma/core-ui`)

Each `tm-*` control is an ordinary Angular component/directive that owns its own logic: it declares
the Angular public API (`input()`/`model()`/`output()`), holds its state in signals, binds ARIA +
classes out via the `host` object, renders an **inline template** with `@if`/`@for`/`@let` (small
components, per the v22 best practice), implements the relevant **Signal Forms custom-control
interface** ([§5](#5-forms-integration-signal-forms)), and — where the behavior is non-trivial —
composes `@angular/aria` directives in its template for keyboard/selection/a11y. There is no separate
pattern object to wire to.

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
input drops into a grid cell with nothing to strip), that chrome lives in **`tm-form-field`**. This
"chrome lives in the field" pattern is **specific to the input directive**. `tm-checkbox` and
`tm-select` are components that render their own structure, so they **own their chrome** (the
checkbox box; the select trigger + overlay panel). The throughline is the same — a *bare* behavior
host with chrome supplied around it — but for the directive the chrome is a sibling wrapper, while
for the components it is internal. (A field-less adorned input — `tmInput` with adornments but no
label — is just `tm-form-field` used without a `label`; there is no separate `tm-input-shell`.)

### 3.1 `tm-form-field`

The shared label / required-marker / hint / error scaffold (brand `FormField`), reading the
`--field-*` token group. It queries its **projected control** (content child) through the
`TmFormFieldControl` contract ([§2.1](#21-shared-contracts)) and reads the field state the control
surfaces from `[formField]` ([§5](#5-forms-integration-signal-forms)) — `errors`/`touched`/`dirty`/
`invalid`/`pending`/`required`. It generates and wires ids (`<label for>` ↔ `controlId`; hint and
error ids fed back via `setDescribedByIds` → the control's `aria-describedby`) and mirrors `required`.
The hint and error are **separate persistent elements** (the error element is the persistent
`aria-live="polite"` region per [§6](#6-accessibility)); the display policy toggles their *visibility*
(at most one shown — error when invalid-and-displayed, else hint) rather than swapping text inside a
single node, so announcements are clean. Logical-property layout mirrors in RTL. Inputs: `label`,
`hint`, `size` (`sm | md | lg`); it does **not** take an `error` string for form-bound controls
(errors come from the field), though a plain `error` input remains for non-form usage.

### 3.2 Text input — `tmInput`

- **Selector:** `input[tmInput]` (`textarea[tmInput]` reserved for later).
- **API:** `value = model<string>()` (the FormValueControl value); `placeholder`; `size`
  resolved from the enclosing `tm-form-field` or set directly; **`disabled`/`readonly`/`required`
  apply only in non-form (unbound) usage** — when bound via `[formField]` the field is authoritative
  ([§5](#5-forms-integration-signal-forms)). Implements `FormValueControl<string>` +
  `TmFormFieldControl`, and declares the optional Signal Forms state inputs (`disabled`, `readonly`,
  `invalid`, `errors`, `touched`, `pending`, `required`, …) that `[formField]` binds.
- **`size`** = the control's height/density variant, mapping to the brand field-height tokens:
  `sm` → `--field-height-sm` (30px), `md` → `--field-height` (38px, default), `lg` →
  `--field-height-lg` (46px). It is the static, per-instance density knob (distinct from a global
  density *system*, [Non-goals](#goals--non-goals)); it also adjusts padding and font-size tokens.
- **Host bindings** (in the `host` object): `--field-*` styling, the focus ring, `aria-invalid`,
  `aria-required`, `aria-describedby`, `disabled`.
- **Leading/trailing slots** = *adornments* placed before (leading) / after (trailing) the text —
  e.g. a search icon, a currency code, a clear button — supplied by **content projection** on
  `tm-form-field` via attribute-selector `ng-content` (`[tmPrefix]` / `[tmSuffix]`), not baked into
  the bare input. Example: `<tm-form-field><i tmPrefix data-lucide="search"></i><input
  tmInput></tm-form-field>`.

### 3.3 Checkbox — `tm-checkbox`

- **Selector:** `tm-checkbox`.
- **API:** `checked = model<boolean>()`; `indeterminate`; projected label; `disabled`/`required`
  (non-form usage only — field-authoritative when bound). Implements `FormCheckboxControl` +
  `TmFormFieldControl`, plus the optional Signal Forms state inputs.
- **No `value` property.** Signal Forms is explicit: *a `FormCheckboxControl` must not have a `value`
  property* — the value channel is `checked`.
  Multi-checkbox selection is a future **`tm-checkbox-group`** component that owns the array value and
  maps each child's identity; the individual `tm-checkbox` stays a pure boolean control.
- **Rendering:** visually-hidden native `<input type="checkbox">` for semantics + the styled box (teal
  when checked/indeterminate, `--radius-xs`, check polyline / indeterminate bar), space-to-toggle, focus
  ring.
- **Indeterminate is the native `.indeterminate` IDL property, *not* a manual `aria-checked="mixed"`.**
  Because the control wraps a *real* `<input type="checkbox">`, the tri-state is driven by host-binding
  the input's **`.indeterminate` DOM property** (`[indeterminate]="indeterminate()"`) — it is an IDL
  property only, with no HTML attribute and no value channel, so it is set imperatively/by host binding,
  never as an attribute. The browser then **exposes `checked="mixed"` in the accessibility tree
  automatically**; we do **not** add `aria-checked="mixed"` ourselves (doing so on a native input would
  be redundant and can conflict with the browser's computed value). `aria-checked="mixed"` would only be
  needed if the box were a custom `role="checkbox"` element with no real input — which this is not. The
  visible "mixed" glyph (the indeterminate bar) is purely CSS, keyed off the same `indeterminate()`
  signal. Note `indeterminate` is independent of `checked`: setting it does not change the input's
  `checked`, and a user toggle clears it (matching native behavior).
- **Touch-target mechanism:** the visible box stays at the brand 18px, but the **clickable region is
  the whole `<label>`**, padded so its hit box clears the target-size rule; where a bare checkbox has
  no adjacent label, a transparent `::before` pseudo-element expands the pointer target while the box
  renders at 18px. Pointer/click events bind to the enlarged region, not the glyph. The hit-box target
  is the [§6](#6-accessibility) sizing rule (≥24px to conform; ≈44px on standalone touch-primary
  controls where layout allows), not a fixed 44px — so the same control is conformant in a dense grid
  and comfortable on a touch form.

### 3.4 Select — `tm-select`

- **Selectors:** `tm-select` (trigger + value display) with projected `tm-option` children.
- **API:** `value = model<T>()` (single-select); `placeholder`, `disabled`, `required`,
  `compareWith`, `size`. `tm-option`: `value` + projected label content; outputs
  `selectionChange`/`opened`/`closed`. Implements `FormValueControl<T>` + `TmFormFieldControl`.
- **`compareWith` is ours, not aria's, and not redundant with signals.** Signal equality is
  *referential* by default, so two option objects describing the same entity (the model's `{id:7,…}`
  vs. a freshly-fetched `{id:7,…}`) are unequal and selection would fail to highlight. Verified:
  `@angular/aria` provides **no `compareWith`** — its listbox selection is strict `===` on whatever is
  bound to `ngOption [value]`. So `tm-select.compareWith` is implemented in our adapter by mapping each
  domain value to a **stable primitive key** before handing it to aria (and back for display) — that
  key-mapping *is* the `compareWith`. (Primitive-id values, the common ERP shape, need nothing.)
- **Built on the aria Select directives (verified API).** The template composes v22's `@angular/aria`
  Select: `ngCombobox` (the trigger, `[(expanded)]`) on a **non-`<input>` host**, the
  `ngComboboxPopup` widget, and `ngListbox` + `ngComboboxWidget` + `ngOption` with
  `focusMode="activedescendant"`, `selectionMode="explicit"`, `[(value)]` (an **array** model), and
  `[activeDescendant]="listbox.activeDescendant()"`. These directives — not hand-written code — own
  keyboard navigation, typeahead, `activeDescendant()`, `scrollActiveItemIntoView()`, single-Escape,
  and all `aria-*`. **Editable vs select mode is chosen by the host element tag, not a config flag:**
  aria derives `isEditable` from `tagName === 'input'`, so `tm-select` (non-editable) puts `ngCombobox`
  on a `<div>`/`<button>`; the future editable details-picker (below) puts it on an `<input>`.
  `tm-select` itself owns the brand chrome, the form-control glue, the scalar↔array bridge, label
  resolution, and the grid commit/cancel.
- **Value source of truth, and aria's auto-prune (load-bearing direction of the bridge).** Verified:
  `@angular/aria`'s listbox runs an `afterRenderEffect` that **drops any selected value not matching a
  currently-rendered option** (`value.set(value.filter(v => options.some(o => o.value() === v)))`).
  That bites the prepopulated/async case directly: if the scalar↔array bridge wrote a prepopulated
  domain key into aria's listbox `value` before its `ngOption` existed, aria would **silently discard
  it**. So the bridge is **one-directional by requirement**: `tm-select`'s own `FormValueControl<T>`
  `value = model<T>()` is the source of truth; it is *mirrored into* aria's listbox value (mapped to
  the stable key) and **re-applied when options arrive**, and aria's listbox value is **never** treated
  as authoritative for a value whose option may not be materialized yet. (This is separate from the
  trigger *label* path below, which `displayWith` already covers — here it is the selected *value*
  itself that must survive.) The DoD tests that a prepopulated value survives until its option renders.
- **Display one property, capture another — yes.** `tm-option`'s **`value`** is what lands in the
  model, its **projected content** is what the user sees:
  `<tm-option [value]="record.id">{{ record.label }}</tm-option>` captures the id, displays the label.
- **Trigger label resolution — and the prepopulated-value problem.** Caching the projected
  option's label only works once that option has rendered. A form frequently arrives with `value`
  **already set before any `tm-option` exists** (an edit screen; an async/virtualized list). So the
  trigger resolves its label in this order: (1) if a **`displayWith: (value) => string`** input is
  provided, use it — it needs no materialized option, so it is the robust path for prepopulated and
  async/virtualized lists; (2) else, the projected option matching `value` (via `compareWith`) once
  present; (3) else, the placeholder until an option resolves. **`displayWith` is not mandatory** for
  static option lists (the projected option is in the DOM immediately), but it is **required in
  practice for async/virtualized or prepopulated-without-static-options cases**, and the docs say so;
  the DoD tests the prepopulated path. This trigger logic is the control's own concern; the
  `TmCellDisplay.formatValue` grid surface ([§2.1](#21-shared-contracts)) *delegates to the same
  resolver* but the standalone trigger does **not** depend on the grid-facing interface.
- **Popup positioning — settled by a running spike (Angular 22 + Playwright, 36/36).** aria's popup
  does **no positioning**: `ngComboboxPopup` is a structural directive (`ng-template[ngComboboxPopup]`,
  `hostDirectives: [DeferredContent]`) that renders its content **inline** via `createEmbeddedView`
  when `expanded` flips true. We need CDK-Overlay positioning + clipping-escape on top. **Decision: the
  official nested pattern** — a `cdkConnectedOverlay` wraps `<ng-template ngComboboxPopup [combobox]="cb">`
  wraps the `ngListbox ngComboboxWidget` panel, with CDK-open and aria's `DeferredContent` both gated on
  the same `expanded` signal. The single-renderer alternative (drop `ngComboboxPopup`, drive the overlay
  alone) was **tested and rejected**: the combobox's keyboard relay, `aria-controls`, and
  `aria-activedescendant` all derive from the popup that `ngComboboxPopup` registers
  (`combobox._registerPopup` / `popup._registerWidget`), so removing it silently kills keyboard
  navigation and the ARIA id chain unless you hand-rebuild that plumbing. Required wiring, confirmed in
  the spike:
  - overlay config `{ origin, usePopover: 'inline', matchWidth: true }` — **`usePopover: 'inline'`
    renders the panel into a native top-layer `[popover]` host, which is what escapes `overflow:hidden`
    clipping** (verified: panel exits a clipping container); `matchWidth` sizes it to the trigger.
  - a `[bottom-start, top-start]` position set for flip; and — because `DeferredContent` inserts the
    panel one render pass *after* CDK attaches and measures — the component **must call
    `overlayRef.updatePosition()` on `(attach)` via a macrotask** (`afterNextRender`/microtask fire too
    early), or flip measures a zero-height panel and never flips up.
  - listbox **`focusMode="activedescendant"`** (mandatory — the `roving` default moves real DOM focus
    into the panel and nulls `aria-activedescendant`) and **`selectionMode="explicit"`** (the `follow`
    default commits on every arrow); bind `ngComboboxWidget [activeDescendant]` from the listbox.
  - the combobox `value` (a string model for editable comboboxes) is left **unbound** — the listbox
    owns the value; only the scalar↔array bridge above touches it.
  The overlay is created lazily on first open; outside-click and Esc close it. **Residual (tracked in
  the DoD):** under `dir="rtl"` the spike saw the panel left-align and `matchWidth` not apply, so
  `tm-select` needs an explicit RTL-mirrored position set and an RTL `matchWidth` re-check.
- **Known upstream bug to guard — mouse input in the overlay ([angular/components#32504](https://github.com/angular/components/issues/32504), P3).**
  An aria combobox/listbox composed via `hostDirectives` **inside a `cdkConnectedOverlay`** — exactly
  this composition — currently misbehaves **on mouse input only**: a click on an option may not register
  the selection, and an outside click may not close the panel (keyboard paths are fine). This is **open**
  and could affect our build. (The positioning spike's 36/36 above covered flip/clipping/keyboard and the
  ARIA id-chain — **not** this mouse-in-overlay path, which is proven separately by the mouse specs below,
  not by the spike.) Mitigation: `tm-select` carries an explicit mouse path (pointerdown-driven
  option selection + an outside-pointer close) ready to switch on if the bug bites, and the test suite
  pins the behavior with **real mouse-event Playwright specs** ([§10](#10-testing-tellmacore-ui-testing),
  DoD item 6) so a regression — ours or the upstream bug surfacing in our composition — fails CI rather
  than shipping a select that ignores clicks.
- **Commit and close are host-wired; focus and Esc are aria's.** Verified in the spike: aria does
  **not** auto-close on selection, so `tm-select` closes the panel (`expanded = false`) on the
  listbox's `valueChange` (and option click), and commits the value via the scalar↔array effect.
  **Focus never leaves the trigger** (the `activedescendant` model), so no focus-restore is needed.
  Esc is owned by aria and is **not** swallowed by the overlay (confirmed): **stage 1 — Esc closes the
  open panel — is the only Esc behavior in a standalone `tm-select`.** A standalone control has nothing
  to revert to and no edit-mode to exit, so it does **not** listen for a second Esc. **Stage 2 — a
  second Esc that reverts and exits edit mode — is purely a grid-host concern:** the grid owns edit-mode,
  and on the second Esc (panel already closed) it calls the control's `TmCellEditor.cancel()` to revert
  the cell. So `tm-select` *implements* `cancel()` for a host to call ([§9](#9-data-grid-forward-compatibility-contract)),
  but the two-stage sequencing lives in the grid, not in the standalone component. Notes: re-selecting
  the **same** value emits no `valueChange`, so close-on-commit needs an explicit close-on-activate for
  that case; and Tab is not relayed to the listbox, so "commit on Tab" (if wanted) is wired explicitly.
- **RTL positioning is authored and tested, not free.** CDK connected-position strategies are
  explicit `{originX/Y, overlayX/Y}` pairs; `Directionality` flips how `start`/`end` resolve, but we
  still **author an RTL-aware position set and test it** — the "mirrors automatically" framing is
  dropped. The RTL spec in the DoD covers exactly this.
- **Keyboard & a11y:** the aria directives supply the combobox/listbox roles +
  `aria-expanded`/`aria-selected`/`aria-activedescendant` and the keyboard model + typeahead. Because
  the listbox is **portaled outside the trigger's DOM subtree**, the trigger references it with
  **`aria-controls`** (and `aria-activedescendant` points at option ids inside that portaled panel); a
  Playwright AT-relationship test asserts the trigger→listbox→active-option id chain resolves
  ([§6](#6-accessibility)/[§10](#10-testing-tellmacore-ui-testing)).
- **Not the entity picker.** `tm-select` is for in-memory/simple option lists. Selecting a
  related entity on an ERP screen — e.g. **Supplier** on a purchase invoice — needs server-side search
  on the typed string with complex filtering, an inline "create new" affordance in the overlay, and a
  "launch advanced-search modal" escape. That is a **distinct future component** (`tm-entity-picker`)
  built on aria's **editable combobox** mode (`ngCombobox` on an `<input>`) + the same
  overlay infra `tm-select` proves, **not** bolted onto `tm-select`. Keeping them separate avoids
  overloading the simple control; the shared aria/overlay foundation is the reuse.
- **Forward-compat (not in Phase 1, not precluded):** multi-select (aria multiselect mode, value →
  array), option groups, and **virtual scroll** (`cdk/scrolling` replaces the static `@for` without an
  API change).
- **Touch:** option rows sized for comfortable pointer/touch use; full-width-friendly panel on narrow
  viewports (target sizing per the WCAG-2.2 rule in [§6](#6-accessibility)).

## 4. Tokens & theming (`@tellma/core-ui-tokens`)

Theming is a typed TS/JSON token model in three tiers (primitive →
semantic → component), emitted to CSS variables. Phase 1 builds the contract and the emitter and
ships **one default preset reproducing `tellma-brand/design-system`** — same hexes, same `--field-*`
/ `--focus-ring` / spacing / type tokens, same `[data-theme=dark]` inversion.

**Why TS/JSON tokens rather than hand-written CSS**: the CSS variables are still the
runtime currency — the TS layer sits *above* them and buys what raw CSS cannot:

- **Type safety** — autocomplete, and a reference to a missing token won't compile.
- **Build-time validation** — generate a JSON Schema from `TmTokens`, validate every preset against
  it **and** run a WCAG-contrast check (both light and dark) so a preset that breaks contrast or
  references a missing token **fails the build**. Thresholds are the **fixed WCAG 2.1 AA** ratios (not
  TBD): **4.5:1** for normal text, **3:1** for large text (≥24px, or ≥18.66px bold), and **3:1** for
  UI-component boundaries and focus indicators. This makes the brand's own rules enforceable
  (action-teal = teal-600 for text-on-fill clears 4.5:1; focus-ring = teal-500 clears 3:1).
  **Keeping the pair list complete as components grow.** The contrast pairs are **not** a hand-curated
  side list that silently rots. Each component's token group **declares its own foreground/background
  pairings as typed metadata** in the contract (a `contrastPairs` field naming `{ fg, bg, kind }`
  where `kind ∈ text | largeText | uiComponent`). The contrast gate derives the full check from those
  declarations, and a **completeness lint fails the build** if a component token group introduces a
  foreground token (text/icon/border) without declaring the background it sits on. So adding a
  component forces its pairs to be declared — the check grows with the contract by construction rather
  than by remembering to update a list.
  **Justified exemptions (so "reproduce the brand exactly" and "fail on sub-AA" can't deadlock).**
  "Reproduce the brand" and "fail the build below AA" are **jointly unsatisfiable** for some brand pairs
  by design — e.g. canonical teal-400 carries white text at only 2.97:1 (the brand itself flags this and
  routes *text* through teal-600, using teal-400 only as a decorative fill/border/dot that never carries
  text). To reconcile them without weakening the gate, the contract carries a typed
  **`contrastExceptions`** allowlist: each entry names the exact `{ fg, bg }` pair plus a **mandatory
  `reason`** (and optional `expires`/owner). The gate **skips only the listed pairs** and **fails the
  build on an exception with an empty/missing `reason`**, so every below-AA pair is an *explicit,
  reviewed, justified* decision that shows up as a diff — never a silent lowering of the threshold. A
  pair not on the list still must pass. (The brand's teal-400-as-decoration is the canonical first
  entry: reason = "brand-identity surface only; never carries text — text uses `--color-primary`
  teal-600".) Adding an exemption is the manual escape hatch; the default remains hard-fail.
- **One source, many outputs** — the same contract emits the CSS variables, the JSON Schema, the
  docs/MCP metadata, and (later) a Figma sync.
- **Safe composition** — presets extend a base by typed merge, not copy-paste.
- **Agent-authorability** — an agent emits a typed object that is validated at build, not free CSS.

**Runtime theme switching — yes, supported, with no rebuild.** Because tokens emit to CSS
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

**Cascade ordering — three override sources, made explicit.** A `@layer` strategy governs the
cascade. There are exactly three places a token value can come from, and they must compose
deterministically regardless of stylesheet load order:

1. **Library base** — the default preset, emitted into a named layer `@layer tm.base`.
2. **Distribution build-time delta** — a distribution's overrides, emitted into `@layer tm.theme`.
3. **Runtime override** — a settings-screen `setProperty` on a scope (e.g. `:root` or a theme
   container), written as an **inline style**.

Precedence is **runtime > distribution > base**, achieved by declaring the layer order once —
`@layer tm.base, tm.theme;` — so `tm.theme` always wins over `tm.base` no matter the link order, and
inline-style runtime writes beat any layered stylesheet by the normal cascade.

**Dark mode and the layers — the default dark scheme is `tm.base`, not `tm.theme`.** A scheme is a
variable set *scoped by a selector* (`[data-theme=dark]`); the layer it lives in is orthogonal to that
selector. The library's default **light *and* dark** schemes both ship in `@tellma/core-ui-tokens` and
both belong to **`tm.base`** — they are library defaults, one scoped to `:root`/`[data-theme=light]`,
the other to `[data-theme=dark]`. Only a **distribution's** overrides (whether of the light scheme, the
dark scheme, or both) ride `tm.theme`, and runtime `setProperty` writes ride inline. Because layer order
is independent of selector specificity, a distribution/runtime override correctly wins **within whichever
scheme is active** — override a color and it beats the library default in light and in dark alike. So
dark mode is **not** a fourth mechanism and is **not** inherently `tm.theme`-level: it is a second base
scheme, with the same three override sources stacked on top. Component CSS consumes the variables from
outside these layers (or in a later `tm.components` layer) so it never accidentally out-ranks a theme
override.

**A slice of `TmTokens`** — the most-reused artifact, so it is concrete here (Phase-1 subset;
full lists are design-in-progress). Primitive ramps → semantic roles via typed refs → the shared
`formField` group every input inherits:

```ts
export type Ref = `{${string}}`;                 // a typed reference to another token, e.g. '{teal.600}'
export type ColorRamp = Record<50|100|200|300|400|500|600|700|800|900, string>;

export interface TmTokens {
  primitive: {
    color: { ink: ColorRamp; teal: ColorRamp; grey: ColorRamp; white: string };
    radius: { xs: string; sm: string; md: string; lg: string; full: string };
    space:  Record<0|1|2|3|4|6|8, string>;
    font:   { sans: string; arabic: string; mono: string; size: Record<'xs'|'sm'|'base'|'lg', string> };
  };
  semantic: {
    colorScheme: { light: SchemeColors; dark: SchemeColors };  // both validated for contrast at build
    focusRing: { width: string; color: Ref; offset: string };   // e.g. color: '{teal.500}'
    motion:   { durationFast: string; easeStandard: string };
    formField: {                       // one override restyles every input (the ERP runs on dense forms)
      bg: Ref; bgDisabled: Ref; border: Ref; borderHover: Ref; borderFocus: Ref; borderInvalid: Ref;
      text: Ref; placeholder: Ref; icon: Ref; radius: Ref;
      height: string; heightSm: string; heightLg: string; paddingX: string; fontSize: Ref;
    };
  };
  component: Record<string, Record<string, Ref | string>>;  // tm-checkbox, tm-select … ref semantic
}
interface SchemeColors { textStrong: Ref; textBody: Ref; surfacePage: Ref; surfaceCard: Ref; border: Ref; /* … */ }
```

**Brand source of truth:** the **TS `TmTokens` contract is canonical** for the
platform; the brand CSS is a starting import. A conformance test asserting the emitted CSS matches
`tellma-brand` anchors is **deferred** (the brand is still in flux — keep it flexible for now). The
schema + WCAG-contrast gates ship in Phase 1 regardless (they don't depend on the brand).

## 5. Forms integration (Signal Forms)

Signal Forms is **stable in Angular v22** and is the only forms mechanism the library supports — no
`ControlValueAccessor`, no dual path (every consumer is greenfield v22+).

**How the field state actually reaches the control and the wrapper.**
In Signal Forms, `[formField]` is applied to the **control element**, and the authoritative state
lives in the `Field` (`myForm.email`). The directive detects which interface the control implements
and binds:

```html
<tm-form-field label="Email">
  <input tmInput [formField]="form.email" />
</tm-form-field>
```

- The control implements `FormValueControl<T>` (`tmInput`, `tm-select`) or `FormCheckboxControl`
  (`tm-checkbox`) and exposes `value = model<T>()` / `checked = model<boolean>()`. `[formField]` binds
  that, **and** sets the control's declared **optional state inputs** — `disabled`, `disabledReasons`,
  `readonly`, `invalid`, `valid`, `errors`, `touched` (the control emits `touch` on blur), `pending`,
  `required`, `min`/`max`/`minLength`/`maxLength`/`pattern`, `name`. The control therefore *is* the
  thing that holds field state.
- **`tm-form-field` reads that state off the control via `TmFormFieldControl`** (it does **not** get
  the `Field` reference — the control does, and re-surfaces it through the contract,
  [§2.1](#21-shared-contracts)). The wrapper queries the projected control (content child) and reads
  `errors`/`touched`/`dirty`/`invalid`/`pending`/`required` to render. This is the Material
  `MatFormFieldControl` shape adapted to Signal Forms.

**Error-display policy (field-scoped) and the submit question.** The default policy is
**field-scoped**: show errors when `invalid() && (touched() || dirty())` — every signal it needs is on
the field-control contract, so no form-level plumbing is required. *"After a submit attempt"* is
**form-scoped** state that the per-field `[formField]` binding does not carry. To honor it, a distro
opts into an optional **`[tmForm]` directive on the `<form>`** that provides the form's submitted
signal through DI to descendant `tm-form-field`s; the display policy reads it when present. Phase 1
ships the field-scoped default and the `[tmForm]` provider hook; richer cross-field policy is deferred.

**`disabled` / `readonly` / `required` precedence — what is contractual vs. what is not.** The rule we
want is **field wins when bound; the control's own input applies when unbound**. It is worth being
precise about which half of that is a documented Angular contract:

- **Documented (a public contract):** `[formField]` **binds the field's state into the control's
  optional state inputs** — `disabled`, `readonly`, `required`, `disabledReasons`, `invalid`, `errors`,
  `touched`, `pending`, etc. This is the
  [Signal Forms custom-controls guide](https://angular.dev/guide/forms/signals/custom-controls): a
  custom control *declares* these optional inputs and the framework *writes field state into them*. So a
  field-bound control reflecting the field's `disabled()`/`readonly()`/`required()` is guaranteed
  behavior. When the control is **unbound** (no `[formField]`), nothing writes those inputs and they keep
  the author-provided value or default — also well-defined. We therefore declare exactly **one**
  `disabled` input (the optional state input `disabled = input(false)`, likewise
  `readonly`/`required`/`disabledReasons`) and read it directly — **no `computed` merge, no manual
  detection** in the normal (single-writer) case.
- **NOT documented (an implementation detail, do not rely on it):** what happens if an author *also*
  template-binds the same input on a field-bound control — i.e. the **tie-break ordering**. Tracing
  `@angular/forms@22` source shows the control-directive host protocol
  (`ɵɵControlFeature` → `setInputOnDirectives` → `writeToDirectiveInput`) writes the field value into the
  input node *after* ordinary element bindings each change-detection pass, so the field happens to be the
  last writer and "wins". But those are **private `ɵ`-prefixed internals with no stability guarantee** —
  there is **no public documentation promising this write order**, and it could change in a patch. So we
  **do not treat the ordering as a contract.** Instead we **forbid the conflict**: authors must not
  template-bind `disabled`/`readonly`/`required` on a field-bound control (enforced by lint + a doc
  note), which removes the ambiguity entirely. A single regression test pins the observed behavior so a
  framework change surfaces loudly rather than silently flipping precedence.

The `FormField` directive *does* provide an injectable **`FORM_FIELD`** token, so a control **may**
`inject(FORM_FIELD, { optional: true })` — but **only** to branch *other* behavior on bound-vs-unbound
(e.g. dropping a standalone-search default), never to choose between two disabled values. (`disabled()`
also populates `disabledReasons`, which feeds tooltips.)

**Async / pending validation — and who debounces.** ERP forms have server-side/async validators. The
control exposes the field's `pending` signal; while `pending()` is true the control sets
`aria-busy="true"` and shows a small inline spinner, `tm-form-field` suppresses a stale "valid"
affirmation, and the display policy holds errors until validation resolves (DoD covers this).
**Debouncing the server call is the consumer's concern, not the library's:** the async validator and
its cadence are defined in the consumer's form schema, where Signal Forms' **`debounce()`** rule (and
`debounce('blur')`) controls how often the model — and therefore the validator — fires. The library's
job is only to **cooperate** with that: the control emits its **`touch`** output on blur (so
`debounce('blur')` works) and does not push value updates faster than the user types. So the control
never bakes in a hardcoded server-call debounce; it gives the schema the hooks to do it.

**Numeric (a later phase)** will use the stable **`transformedValue`** utility (`@angular/forms/signals`)
for the string↔number parse/format with automatic parse-error reporting — which is why numeric is a
cheap follow-up rather than foundation-worthy.

**Providers — split, not bundled.** Two functions, so i18n/fonts don't hide inside a *forms*
provider:

- **`provideTellmaForms()`** — forms only: the error-display policy, the validation-message
  resolver, and form-field defaults (`size`, required-marker). **Message precedence:** a
  schema-inline message (the `{message: …}` passed to a validator in the form schema) **wins when
  present**; only when a validator produces an error with no inline message does the resolver map its
  **key** (`required`, `minlength`, …) to a **localized** default via the i18n runtime
  ([§7](#7-rtl-i18n--l10n)). So inline message → else key-resolved default; the control surfaces the
  resolved string through `errors` ([§2.1](#21-shared-contracts)).
  **Missing-translation fallback (the non-EN / not-yet-translated case).** A distribution can run a
  locale before it has supplied every library string — e.g. an Amharic tenant hits a `required` error
  before `am` translations for the built-in validator keys exist. The resolution is a defined **fallback
  chain**, never a blank or a raw key shown to a user: (1) the **active locale's** string for the key;
  (2) else the **English** string — **English is the only library-string locale that ships in-package**;
  every other locale, **Arabic and Amharic included**, ships as an **optional per-distribution locale
  pack** ([§7](#7-rtl-i18n--l10n)). Two *different* failure modes reach English, and they need **two
  different Transloco settings** — both set by the `TM_UI_TRANSLATE` default, because `fallbackLang`
  alone does **not** cover the second:
  - **Whole pack absent** (the language never loads — an Amharic tenant before `@tellma/locale-am`, or an
    Arabic one before `@tellma/locale-ar`): handled by **`fallbackLang: 'en'`**, which loads English when
    the active language fails to load.
  - **Pack installed but a key is missing** (an incomplete translation): `fallbackLang` does **not** fire
    here — it triggers on a failed *language* load, not a missing *key* in a loaded one. So the default
    also sets **`useFallbackTranslation: true`** (Transloco's per-key fall-through to the fallback
    language), or equivalently a custom **`MissingHandler`** that resolves the key against English — so a
    missing key in a present locale still renders the English string, not the raw key.

  (3) The raw key (`required`) is only a **last-resort guard** if even English lacks it — which can't
  happen for built-in keys (English always ships them) and signals a **missing custom key**, surfaced by
  a dev-mode `console.warn` from the `TM_UI_TRANSLATE` default and (optionally) a CI check that every
  validator key used has an English entry. So: built-in keys always resolve to at least English; a
  not-yet-installed or incomplete locale pack degrades to English, never to a blank or a broken UI.
  **Param interpolation + ICU:** validators carry params (`minlength` → `{requiredLength, actualLength}`,
  `min`/`max` → the bound, etc.). The resolver passes those params to the translate call, so the
  translation string interpolates them (`"At least {requiredLength} characters"`), and **plurals/gender
  use ICU MessageFormat** via Transloco's MessageFormat plugin (`@jsverse/transloco-messageformat`) —
  i.e. ICU lives in the translation layer, not in our resolver. The built-in **English** preset ships
  ICU-formatted strings for the built-in validator keys; other locales' ICU strings arrive with their
  **locale pack**.
- **`provideTellmaUi()`** — the umbrella a distribution actually calls: composes
  `provideTellmaForms()` **+** the default Transloco-backed `TM_UI_TRANSLATE` **+** any UI-wide
  defaults. A distribution on the defaults calls `provideTellmaUi()` once and writes **zero** other
  config. (Font preloading is a distribution-shell concern, not wired here — [§7.1](#71-fonts--web-font-loading).)

## 6. Accessibility

Target **WCAG 2.1 AA**. **axe-core is necessary but nowhere near sufficient:** it catches
static violations (missing roles, contrast, names) but **cannot** verify keyboard navigation, focus
return on close, `aria-activedescendant` tracking, the two-stage Esc, or screen-reader announcements —
which is precisely where Select's compliance is hard. Those are gated by **behavioral Playwright
tests** ([§10](#10-testing-tellmacore-ui-testing)), with axe as the static floor. **Playwright is the
standardized runner for these specs — not left to the implementer's choice:** these assertions depend on
real focus semantics, `:focus-visible`, layout/measurement, the CDK overlay portal's positioning, real
mouse-vs-keyboard input, and `emulateMedia` (forced-colors/reduced-motion), so a real browser engine is
mandatory (plain `jsdom`/`happy-dom` unit tests cannot cover them) and the whole library uses **one**
tool — Playwright — so the behavioral suite is uniform and portable across components. **Caveat:**
Playwright verifies the **DOM/ARIA mechanism** (roles, `aria-live` region updates, focus moves,
id-relationship chains) — it **cannot verify that a screen reader actually speaks** the right thing.
Real assistive-technology verification (NVDA/JAWS/VoiceOver) is a **manual pass, out of DoD scope**;
the automated suite asserts the mechanism that *should* drive that speech.

- Text input: native semantics, `aria-invalid`/`aria-required`/`aria-describedby`, label association.
- **Live-region decisions (error/hint announcements).** Concrete, to avoid the swap-content
  double-announce/missed-announce trap: the **hint and the error are separate elements**, not swapped
  content in one node. The **error element is a persistent live region** — `aria-live="polite"`,
  `aria-atomic="true"` — that exists in the DOM whether or not it currently holds text, so a
  transition from empty→message (or message→different message) is announced once; it is **never**
  reused to host the hint. The hint is **not** a live region (it is referenced by `aria-describedby`
  for on-demand reading). Politeness: **`polite`** for inline field validation (don't interrupt
  typing); on a blocked submit the form may escalate its summary to **`assertive`**/`role="alert"`,
  but per-field errors stay `polite`. Both hint and error ids are wired into the control's
  `aria-describedby` so a screen reader reads them when the field is focused.
- Checkbox: native checkbox semantics; the tri-state is the native `.indeterminate` IDL property, so
  the browser exposes `checked="mixed"` in the a11y tree automatically (no manual `aria-checked`); space-
  to-toggle, clickable label.
- Select: `@angular/aria` combobox/listbox roles, `aria-expanded`/`aria-selected`/
  `aria-activedescendant`, full keyboard model, focus returned to the trigger on close. No focus
  trap (the combobox+activedescendant model keeps focus on the trigger). **Portaled-overlay sharp
  edge:** because the listbox renders in a CDK overlay *outside* the trigger's subtree, the
  trigger must reference it with **`aria-controls`** for the active-descendant relationship to be
  exposed to assistive tech; a Playwright test asserts the trigger→listbox→active-option id chain
  resolves across the portal boundary (`aria-owns` is the fallback if a tested AT needs the implicit
  containment).
- **Focus ring — "the brand teal halo, never removed without replacement"**: the focus
  ring is the visible indicator shown when an element holds keyboard focus — here the brand's teal
  halo with a white gap (`--focus-ring`), applied on `:focus-visible`. "Never removed without
  replacement" means we never write `outline: none` (the common a11y regression that makes keyboard
  navigation invisible) unless we provide an equally-visible substitute indicator. This satisfies
  **WCAG 2.4.7 Focus Visible** and is enforced by the axe gate plus a lint check against bare
  `outline: none`.
- **Forced-colors and reduced-motion are gated, not just asserted.** `@media (forced-colors: active)`
  and `prefers-reduced-motion` are honored (the latter disables the 120–280ms fades), and both are
  **tested in Playwright** via `page.emulateMedia({ forcedColors: 'active' })` and
  `{ reducedMotion: 'reduce' }`, asserting the computed result (borders/focus ring remain visible
  under forced-colors; transition durations collapse under reduced-motion). They move off the
  manual-pass list because Playwright can emulate both media features.
- **Target size — WCAG 2.2 AA, not 44px.** The conformance criterion is
  **2.5.8 Target Size (Minimum) = 24×24 CSS px** (WCAG 2.2, level AA), with its standard exceptions
  (sufficient **spacing**, an **equivalent** control elsewhere, **inline** targets, **essential**
  presentation). 44×44 is **2.5.5 Target Size (Enhanced), which is AAA** (and the Apple-HIG comfort
  figure) — a target we aim for on **standalone, touch-primary** controls where layout allows, not a
  conformance floor. **Dense ERP contexts are explicitly fine (resolves the 32px-grid-row tension):**
  a 32px grid row, or a compact `sm` field, conforms via the 24px minimum and the spacing/essential
  exceptions — dense tabular data is essential presentation and the grid is primarily
  keyboard/pointer-driven. So there is no conflict between "≈44px comfortable touch targets" (forms,
  touch) and "32px dense rows" (grid, desktop); they are different contexts under the same 24px rule.
- CDK a11y utilities (`FocusMonitor`, `LiveAnnouncer`, `Directionality`) reused, not reinvented.

## 7. RTL, i18n & l10n

- **RTL (rule 4 / D7):** CSS **logical properties only**; direction from CDK **`Directionality`**
  (auto-detected), never a per-component `rtl` flag. Adornment order, checkbox box side, and label
  alignment mirror via logical properties; the **Select overlay's connected position** is **authored
  RTL-aware and tested** — `Directionality` flips `start`/`end`, but we still write and verify the
  position set ([§3.4](#34-select--tm-select)), not assume it. Arabic type uses `--font-arabic`
  and the larger Arabic leading from the brand tokens.
- **Bidi text inside fields (mixed Arabic/English).** Form values routinely mix scripts (an Arabic
  name with a Latin code, a phone number in an RTL paragraph). The browser's Unicode Bidi Algorithm
  handles the *display* ordering, but the field's base direction still has to be right or punctuation
  and Latin runs land in the wrong place. So text inputs set **`dir="auto"`**, which makes each field
  pick its base direction from its **own content's first strong character** — independent of the
  page/app direction — so a Latin-first value reads LTR and an Arabic-first value reads RTL even within
  the same RTL form. This is the standard fix and needs no per-field JS. **Alignment follows that
  auto-detected base direction** (`text-align: start` resolves against the field's own `dir`), so —
  to answer the common case directly — **a field holding only English text is left-aligned with an
  LTR caret even inside an RTL (Arabic) form**, while an Arabic-first field in the same form is
  right-aligned; the surrounding label, required marker, and field chrome still mirror to RTL via
  logical properties. Known rough edges (caret jumps, neutral-character placement at run boundaries)
  are covered by mixed-content tests in both LTR and RTL roots; we do not hand-roll a bidi algorithm.
- **Runtime i18n/l10n via Transloco.** The library's own labels (required-field
  announcement, select placeholder default, validation messages) are translated through a **runtime**
  i18n library. **Decision: standardize on Transloco** as the platform i18n runtime, consumed behind
  a *thin* one-function seam rather than the full multi-backend adapter of D8 (a thin seam keeps one
  mechanism for the whole platform while leaving a clean swap point, without the cost of a full
  abstraction). Concretely: an injection token `TM_UI_TRANSLATE` resolving to
  `(key: string, params?) => Signal<string>`, with the default implementation in `@tellma/core-ui`
  backed by Transloco (scoped/lazy-loaded library strings). **A distribution on the default Transloco
  path writes zero config code** — `provideTellmaUi()` ([§5](#5-forms-integration-signal-forms)) wires
  the Transloco-backed default itself; the token only needs supplying to override it. The
  `contracts` entry point never imports Transloco (it stays dependency-free); only the components'
  default provider does. This keeps one mechanism for the whole platform while leaving a clean swap
  point if ever needed. **Only the English** library-string preset ships in the core; **Arabic, Amharic,
  and every other locale ship as optional per-distribution locale packs** — the same locale-pack
  mechanism that ships their fonts ([§7.1](#71-fonts--web-font-loading)). A locale pack bundles **both**
  a locale's library strings and its font subset/manifest entries, wired through a single
  **`provideTellmaLocale*()`** provider (Transloco scope + `TM_FONT_SUBSETS` multi-token entries + a
  static `@font-face` stylesheet — merge mechanism in [§7.1](#71-fonts--web-font-loading)). A
  distribution installs the packs its tenants need (a KSA/UAE distribution installs `@tellma/locale-ar`);
  the core stays English-only.
  **The Arabic pack `@tellma/locale-ar` is delivered in this phase** as the reference pack that proves
  the mechanism end-to-end ([§1](#1-package--build-foundation), Goals); other locale packs are later,
  mechanical follow-ons against its template.
- **Adapters named as future seams, not shipped in Phase 1.** `TmNumberAdapter` / `TmCurrencyAdapter` /
  `TmDateAdapter` (D8) are the locale/calendar seams *later* components will need (numeric, currency,
  date picker — e.g. a Hijri calendar from a Locale pack). **None of the three Phase-1 controls needs
  any of them, so Phase 1 neither declares nor implements them** — they appear here only as roadmap
  context, recording where that seam lands when the component that needs it arrives.

### 7.1 Fonts & web-font loading

Fonts are shared by the components (via `--font-*` tokens) and the distribution shell, and the app
must run on an **isolated intranet (no font CDN)**. Strategy, optimized for low latency / fast first
text paint, and scalable to many scripts (Amharic, Japanese, Hindi, Russian, …) **without eagerly
loading all of them**:

- **Self-hosted, content-hashed `.woff2`** served from the app origin (works offline/intranet). No
  Google Fonts CDN in production. `@tellma/core-ui` ships only the **Latin** family — **Noto Sans** —
  plus **Noto Sans Mono** for code, and the `@font-face` machinery; the components reference only the
  `--font-*` tokens. **Arabic (Noto Sans Arabic) and every other script ship with their locale pack**,
  not in the core — the same English-only baseline as the library strings ([§7](#7-rtl-i18n--l10n)).
- **`unicode-range` subsetting per `@font-face`** is the key to not eagerly loading every script: the
  browser downloads a face **only when the page actually contains glyphs in that range**. Non-Latin
  scripts ship as **Locale packs** (`@tellma/locale-ar` → Arabic, `@tellma/locale-am` → Amharic, …),
  each contributing its `@font-face` blocks **and** that locale's library strings; faces are fetched on
  demand and nothing for an uninstalled or unused script is ever downloaded.
- **`font-display: swap`** so text paints immediately in a fallback and swaps when the web font
  arrives (fast TTI; no invisible-text delay).
- **Preload is resolved at runtime from per-tenant locale config** — and the **library/shell split
  matters**. A distribution may support any number of locales; each tenant configures up to
  three and switches at runtime, so two tenants in the *same* distribution may run English+Arabic vs.
  English+Amharic. **Latin is always preloaded** (the universal fallback); the additional subsets to
  preload are exactly those the resolved tenant locales need. The responsibility boundary:
  - **The core library ships** the Latin/Mono `@font-face` rules (with `unicode-range`) and their
    self-hosted woff2, the typed **`TM_FONT_SUBSETS` manifest** shape (seeded with its Latin/Mono
    entries), and a small **pure** helper **`fontPreloadLinks(subsets, locales) → PreloadLink[]`**.
  - **How a locale pack contributes (the merge mechanism — this is the seam being proven).** Each pack
    exposes a provider (e.g. **`provideTellmaLocaleAr()`**) a distribution adds to its app config. It
    contributes three things by three standard mechanisms: (a) the locale's **library strings** as a
    Transloco scope/loader; (b) its **font-subset manifest entries** into **`TM_FONT_SUBSETS`, a
    `multi: true` DI token** the core also seeds — so the *injected* token is the **union** of the core
    plus every installed pack; and (c) its **`@font-face` rules** as a static stylesheet bundled in the
    pack (present whenever the pack is a dependency; faces fetch on demand via `unicode-range`). There is
    **no build-time scan and no central registry to edit** — installing a pack and calling its provider
    is the whole wiring.
  - **The distribution app shell owns** the runtime act: it reads per-tenant locale config (a
    distribution concern, outside the component library), injects **`TM_FONT_SUBSETS`** (the merged
    union), calls **`fontPreloadLinks(subsets, tenantLocales)`**, and injects the
    `<link rel="preload" as="font" crossorigin>` tags. The library does not read tenant config or touch
    the document head.
  The preloadable set is the union of the distribution's installed Locale packs; unconfigured scripts are
  never preloaded and only fetch on demand via `unicode-range` if their glyphs appear. Accordingly, the
  DoD tests the library's piece (the `@font-face`/`unicode-range` setup, the `TM_FONT_SUBSETS` multi-token
  merge, and `fontPreloadLinks`), not the distribution-owned runtime injection.
- **Variable fonts** where available, to cut file count/weight (one file spans weights).
- **Long-cache immutable** (content-hashed filenames, `Cache-Control: immutable, max-age=1y`) plus
  the PWA service-worker cache, so repeat loads are instant.

## 8. Performance budget

- **Zoneless + OnPush** (the v22 default; not set explicitly). Signal-driven, so only the changed
  control re-renders.
- **Minimal DOM:** text = one `<input>` + the field wrapper only when labelled; checkbox = label +
  hidden input + one box; select trigger = one element, and the **overlay panel is created lazily on
  first open** and torn down on close — closed selects cost nothing.
- **Long option lists:** `@for` + `track` now; `cdk/scrolling` virtual scroll drops in later without
  an API change.
- **Bundle budget** per entry point in CI — with **concrete initial ceilings, not "TBD"**, so
  the DoD's "within budget" is not circular. Starting ceilings (gzipped, self-weight excluding shared
  Angular/CDK already in the app): `tmInput` ≤ 8 KB, `tm-checkbox` ≤ 8 KB, `tm-form-field` ≤ 6 KB,
  `tm-select` ≤ 24 KB (it carries the Overlay/listbox wiring), `@tellma/core-ui-tokens` runtime ≤ 4 KB.
  These are **starting ceilings** — set to catch regressions now, to be **inspected and tightened**
  once real builds land. They are **ratchets**: CI fails on regression and we tighten them as builds
  land, never loosen silently. The ceilings measure each component's **own weight on top of an assumed Angular + CDK
  baseline** — because that baseline is a *given*: any real distribution ships components that
  pull in CDK, so counting CDK against `tm-select` would double-count a cost the app already pays.
  `sideEffects:false` + per-component entry points keep tree-shaking honest, and the fact that CDK
  Overlay enters only via the `select` entry point (so a text/checkbox-only app avoids it) is a
  genuine but **secondary** nicety — not the basis for the budgets.
- Static, build-time token/base CSS — no runtime style generation ([§4](#4-tokens--theming-tellmacore-ui-tokens)).

## 9. Data-grid forward-compatibility contract

The editable Excel-like data grid is out of scope, but Phase 1 must not foreclose it (rule 6). Two
**draft** contracts shape every Phase-1 control to be grid-ready. **They are stubs:** `TmCellEditor`
and `TmCellDisplay` are deliberately minimal forward-compat placeholders, **to be properly designed
and hardened when the grid is actually built** — the grid's real requirements (range selection,
clipboard, virtual scroll) will reshape them. Phase 1 declares them and shapes the controls around
them, but does **not** test-harden them or treat them as a frozen surface.

- **`TmCellEditor<T>`** ([§2.1](#21-shared-contracts)) — the *edit* path. Defined as a TS interface
  so every grid-able control's pattern implements commit/cancel/focus/keydown **uniformly**.
  Guarantees: external value ownership (the grid owns the model), **no self-owned focus trap or
  document-level listeners** (the grid owns Tab/Enter/Esc/arrow navigation and forwards only what the
  cell editor consumes), and explicit `commit()`/`cancel()` (Enter/Tab commit, Esc cancels; for
  Select, Esc closes the panel first, then cancels — the Excel dropdown-cell behavior). The Select
  overlay anchors to an arbitrary element (a cell rect) via the same `cdkConnectedOverlay` +
  `usePopover:'inline'` composition proven in [§3.4](#34-select--tm-select), which the grid inherits.
- **`TmCellDisplay<T>`** ([§2.1](#21-shared-contracts)) — the *readonly* path: a virtualized grid
  renders **every non-edited cell as plain, non-interactive DOM** (a formatted value in a `<span>`, a
  token-styled checkbox-glyph instead of a real checkbox) and instantiates the full interactive control
  **only for the one cell being edited**. This is a standard, very worthwhile technique (ag-Grid/Excel),
  and it is **cleanly supportable** because each control already separates a *pure display formatter*
  (`formatValue`, and an optional token-driven `readonlyClass` for non-text glyphs) from its interactive
  behavior. The grid calls `formatValue` to paint thousands of cells with zero component instances, then
  swaps in the live editor on entering edit mode. Phase 1 shapes all three controls around this
  edit-path/display-path split so the draft interfaces *can* be implemented later; it does not ship
  grid-specific code, and — because the interfaces are stubs — does not lock them in with tests.

**What the edit-cell hosts (verified, and it differs by control).** The host always owns the writable
value channel ([§2.1](#21-shared-contracts)) and drives the editor through the `TmCellEditor<T>`
interface the control implements. For **text and checkbox** the behavior is simple and DOM-native, so a
grid edit-cell can mount the bare `<input tmInput>` / `tm-checkbox` directly and drive it via
`TmCellEditor`. For **select**, the behavior is delivered as `@angular/aria` *directives* that require
an Angular injection/template context — there is no way to instantiate the combobox/listbox behavior
outside a component — so a grid edit-cell **mounts the full `tm-select` component** and listens for its
`commit()`/`cancel()` to write back or discard. A short "embedding a control in a cell" note goes in each
component's docs to keep this visible.

## 10. Testing (`@tellma/core-ui-testing`)

- **Component harnesses** (D11/D16) for all four: `TmInputHarness`, `TmCheckboxHarness`,
  `TmSelectHarness` (+ `TmOptionHarness` — a *collection* harness: open the panel, list/select
  options, read the active option) and `TmFormFieldHarness`. Built on the CDK harness infrastructure
  (and `@angular/aria`'s shipped harnesses for the listbox). This is the template every later
  component copies.
- **API goldens** — for each entry point, **API Extractor** emits a `*.api.md` "golden": a
  human-readable, diff-able snapshot of the complete public API surface (every export, signature, and
  type), committed to the repo. A PR that changes the public API shows up as a golden diff in review,
  so drift is never silent — which matters when agent-generated code depends on a stable surface.
- **`approve-api` CI gate** — CI re-extracts the API and compares it to the committed
  golden; **if they differ, CI fails**. To land an intended API change, a maintainer runs the
  `approve-api` script to regenerate and commit the golden, making every public-API change an
  explicit, reviewed act rather than an accident.
- **A shared `form()` test fixture.** Behavioral tests for `[formField]` binding, the disabled/required
  precedence, pending state, and message resolution all need a live Signal Form. So the testing package
  ships a tiny **host harness** (a Storybook decorator + a TestBed helper) that wraps the control under
  test in a host exposing a `form()` with a configurable schema (validators, async validators,
  `debounce`, inline messages) and binds `[formField]` to the chosen field. Stories that exercise form
  behavior render through this decorator; the same fixture backs the unit and Playwright specs, so
  there is one way to stand up a form context.
- **Unit tests** per component (zoneless test env), using that fixture: value flow via Signal Forms,
  validity/touched, **pending/async-validation state**, **prepopulated-value trigger label via
  `displayWith`**, **disabled/required field-vs-input precedence**, **message precedence + ICU/param
  interpolation**, indeterminate, and — for Select — open/close, keyboard nav, typeahead, selection,
  `compareWith`, Esc/outside-click close.
- **axe-core** specs per component (including the open Select panel) as the **static floor** —
  necessary, not sufficient.
- **Behavioral a11y specs (Playwright, the real gate):** keyboard navigation (arrows/Home/End/
  typeahead), **focus return to the trigger on close**, **`aria-activedescendant` tracking** across the
  portal, the **Esc behavior** (standalone: Esc closes the panel; grid: the host's second Esc cancels —
  [§3.4](#34-select--tm-select)/[§9](#9-data-grid-forward-compatibility-contract)), the
  **trigger→listbox `aria-controls` AT-relationship** chain, and the **announcement *mechanism***
  (error `aria-live` region updates, `aria-busy` while pending). Playwright is the standardized runner
  ([§6](#6-accessibility)) — a real browser engine is required and the choice is not left to the
  implementer. They cover exactly what axe cannot — but they assert the DOM/ARIA mechanism, **not** that
  a screen reader speaks it; real AT verification is a manual pass outside the DoD.
- **Select mouse-interaction specs (real mouse events, guarding a known aria-in-overlay bug).** There is
  an open Angular CDK/aria issue —
  [angular/components#32504](https://github.com/angular/components/issues/32504) (P3) — where an aria
  combobox/listbox composed via `hostDirectives` **inside a `cdkConnectedOverlay`** misbehaves **on
  mouse input only**: clicking an option may not register the selection, and an outside click may not
  close the panel (keyboard paths are unaffected). Because `tm-select` is exactly this composition
  ([§3.4](#34-select--tm-select)), the Playwright suite **must include mouse-driven specs** — driving
  selection with **real input events** (`locator.click()` / `page.mouse.click()`, which Playwright
  delivers as trusted mouse input), **not** synthetic `dispatchEvent('click')` and **not** the keyboard
  path — for: **clicking an option commits and closes**,
  **clicking outside closes**, and **clicking the trigger toggles** — so a regression (or the upstream
  bug surfacing in our composition) fails CI. If the bug bites our build, the workaround is wired in
  `tm-select` (explicit pointerdown/selection handling on the option rows and an outside-pointer close),
  and these specs verify the workaround holds. Tracked as a DoD item.
- **RTL specs:** mirrored layout, checkbox side, and the **authored** Select overlay positions under
  `dir="rtl"` (positions are tested, not assumed — [§3.4](#34-select--tm-select)).
- **Contracts entry-point boundary + lint hygiene.** The `@tellma/core-ui/contracts` entry point
  ([§2.1](#21-shared-contracts)) must stay importable by the future grid without dragging in the
  components, so a lint **fails CI if `contracts` imports anything from `@angular/core` or the
  component modules** — it is types (`SignalLike`/the interfaces) plus pure helpers only. **This is a
  few lines of ESLint config, not test code:** a path-scoped `no-restricted-imports` (or an
  import-boundary rule such as `eslint-plugin-boundaries`) on the `contracts/` folder, run as part of
  the normal lint pass. (No signal-primitive allowlist is needed anymore: with the pattern layer
  collapsed, the components are ordinary Angular code that may use the full framework; only the
  `contracts` surface is constrained.)
  The same lint job also fails on cross-package leakage and on bare `outline: none`
  ([§6](#6-accessibility)).
- **e2e:** the behavioral specs above run against Storybook stories on a real browser (Storybook is the
  only showcase surface — [§11](#11-docs--mcp-pipeline-tellmacore-ui-mcp)).
- **Changed-test selection.** CI runs only the tests whose code changed: on PRs, the test
  runner's `--changed`-against-merge-base filtering (per package), **plus** the direct consumers of
  any changed package (so a `contracts` or tokens change re-tests the components); on `main`/release, the full
  suite always runs (changed-only can miss cross-package breakage). This is the pnpm + Angular CLI
  path; if it proves insufficient as the repo grows, nx `affected` is the upgrade
  ([§1.2](#12-build-tooling--pnpm--angular-cli-nx-deferred)).
- Tests are **on in CI** (D16).

## 11. Docs & MCP pipeline (`@tellma/core-ui-mcp`)

Per **D12/D13**, docs are generated from source as a single source of truth. The Phase-1 showcase is
**Storybook only** — no dedicated showcase app, and the sample distribution is out of
scope for now.

- Co-located `*.stories.ts` per component (runnable demos + interaction tests) + a short narrative `*.md`.
- API Extractor (`.api.json`) + a thin extractor → **`components.json`** (single source of truth).
- `components.json` feeds: the Storybook showcase, `llms.txt`, the scoped **`@tellma/core-ui-mcp`**
  server (Phase 1: `list` / `describe` / `example` tools), and the API goldens.
  - **`llms.txt`** is a single, flat, agent-readable Markdown digest of the library's surface (the
    components, their selectors, inputs/outputs, tokens, and a canonical example each) at a conventional
    path. It is the *static, no-server* path for a coding agent — including the ones building
    distributions — to load the whole library as context in one fetch, where the MCP server is the
    *interactive* path (query a single component on demand). Both are generated from `components.json`,
    so they never diverge from the code; `llms.txt` is just the offline, whole-surface projection of it.
- The federated `dotnet tellma mcp` umbrella is not in Phase 1.

**`components.json` schema (defined here because it feeds everything else).** It is a generated,
versioned JSON document validated against its own JSON Schema in CI (so the MCP server, `llms.txt`,
Storybook, and goldens consume a stable shape). Phase-1 shape:

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
  examples: ExampleDoc[];           // title, code, from the stories
  harness: string;                  // 'TmSelectHarness'
  status:  'stable' | 'experimental' | 'deprecated';
  deprecation?: { since: string; replacement?: string };  // pairs with @breaking-change
}
interface PropDoc { name: string; type: string; default?: string; required: boolean; description: string; signal?: 'input'|'model'|'output'; }
interface SlotDoc { name: string; selector: string; contextType?: string; description: string; }
interface ExampleDoc { title: string; code: string; }
```

The extractor derives every field from typed source (signal `input()`/`model()`/`output()`, JSDoc,
the harness, and co-located stories); nothing is hand-authored, so docs can't drift from code, and the
schema is the contract the MCP/goldens/showcase build against.

## 12. Directory layout

Each **component is its own secondary entry point** — a flat sibling folder under the package with its
own `ng-package.json` + `public-api.ts`, importable as `@tellma/core-ui/select`. The **primary**
`@tellma/core-ui` entry point holds only the cross-cutting, component-free code (providers, i18n,
fonts, forms infrastructure, shared pure helpers) and uses the Angular-CLI default `src/lib/` layout.

```
client/projects/core/
├── tellma-core-ui/
│   ├── ng-package.json        # primary entry point @tellma/core-ui
│   ├── src/
│   │   ├── public-api.ts      #   re-exports the providers/i18n/fonts/forms/shared surface (no components)
│   │   └── lib/
│   │       ├── forms/         #     provideTellmaForms(), tmForm directive, field-state helpers, message resolver
│   │       ├── providers/     #     provideTellmaUi() umbrella (composes forms + i18n + fonts defaults)
│   │       ├── i18n/          #     TM_UI_TRANSLATE token + Transloco-backed default
│   │       ├── shared/        #     pure helpers (value→key mapping, formatters)
│   │       └── fonts/         #     @font-face + self-hosted woff2; TM_FONT_SUBSETS manifest + fontPreloadLinks()
│   ├── contracts/             # secondary EP @tellma/core-ui/contracts — ng-package.json + public-api.ts:
│   │                          #   SignalLike/WritableSignalLike, TmFormFieldControl, TmFieldError, draft TmCellEditor/TmCellDisplay
│   ├── input/                 # secondary EP @tellma/core-ui/input    — tmInput directive
│   ├── checkbox/              # secondary EP @tellma/core-ui/checkbox — tm-checkbox (inline template)
│   ├── form-field/            # secondary EP @tellma/core-ui/form-field — tm-form-field (inline template)
│   └── select/                # secondary EP @tellma/core-ui/select   — tm-select + tm-option (@angular/aria + CDK Overlay)
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

The reference locale pack lives in a sibling `locale/` family (locale packs are not `core-ui`
packages), proving the structure later packs (`@tellma/locale-am`, …) copy:

```
client/projects/locale/
└── tellma-locale-ar/         # @tellma/locale-ar — the reference Arabic locale pack
    ├── ng-package.json
    └── src/
        ├── strings/ar.json   # Arabic translations for the built-in library strings (Transloco scope)
        ├── fonts/            # self-hosted Noto Sans Arabic woff2 + @font-face (unicode-range), static stylesheet
        ├── manifest.ts       # TM_FONT_SUBSETS entries (Arabic script → asset URL + range)
        └── public-api.ts     # provideTellmaLocaleAr(): Transloco scope + TM_FONT_SUBSETS multi-token + @font-face
```

**Why this shape (and how it scales to ~40 components later).**

- **Flat per-component folders, *not* category-nested folders.** Material, the CDK, and PrimeNG all
  keep components as a **flat list** of sibling folders, never a `forms/`, `layout/`, `feedback/`
  directory tree. Category ("form control" vs. "layout" vs. "complex visual") is **metadata, not a
  folder** — it lives in the Storybook group title and a `group` field on `ComponentDoc`
  ([§11](#11-docs--mcp-pipeline-tellmacore-ui-mcp)), where it can be re-grouped freely. So adding the
  later components is **append-a-folder**, with **no reorganization** of what exists — which directly
  answers the "will reorg be too much work" worry: there is no reorg, because the taxonomy was never
  encoded in the directory tree.
- **Per-component secondary entry points.** Each component being its own entry point gives three
  things a single `src/lib` barrel cannot: an **import path decoupled from disk location**
  (`@tellma/core-ui/select` is stable even if the folder moves, so future reorg is free), a **hard
  tree-shaking boundary** (a text/checkbox-only app importing `@tellma/core-ui/input` never pulls in
  Select's CDK-Overlay/aria weight — the basis for the per-entry-point [§8](#8-performance-budget)
  budgets), and a natural unit for the **API golden** per surface. The cost is one small
  `ng-package.json` per component, which `ng generate` scaffolds.
- **Why `provideTellmaForms()` and `provideTellmaUi()` sit in different folders.**
  `provideTellmaForms()` is a **forms-domain** artifact (it configures the error-display policy and
  message resolver that the rest of `forms/` implements), so it lives **with its domain** in `forms/`.
  `provideTellmaUi()` is the **app-composition umbrella** — it composes the forms, i18n, and font
  defaults across domains — so it lives in the neutral `providers/` folder rather than privileging any
  one domain it pulls together. (Minor call; the alternative of co-locating both in `providers/` is
  fine too — the separation just keeps each domain provider beside its domain.)

Storybook config lives in the workspace (free-port launch per [§1.3](#13-worktree-isolated-port-free-tooling)).

## 13. Definition of done

1. All four core packages **and `@tellma/locale-ar`** build, lint (incl. the `tm-` selector rule), and
   are consumable by an in-repo app via workspace path mappings; `@tellma/core-ui/contracts` resolves as
   a secondary entry point.
2. `tmInput`, `tm-checkbox`, `tm-select`, `tm-form-field` work bound via `[formField]` in a **Signal
   Form** (each implementing the correct interface — `tm-checkbox` via `FormCheckboxControl` with
   **no `value` property**, enforced by lint/API golden), themed from the brand preset, in light and
   dark, LTR and RTL.
3. `tm-form-field` renders the localized **error/hint** by reading field state off the control
   (`errors`/`touched`/`dirty`/`invalid`/`pending`), and the **disabled/required precedence** rule
   holds (field wins when bound; component inputs apply only unbound).
4. Each component: unit tests green, harness shipped, **axe clean** (static floor), and **behavioral
   Playwright a11y specs green** — keyboard nav, focus return, `aria-activedescendant` + `aria-controls`
   across the portal, the standalone Esc-closes-panel behavior, `aria-live`/`aria-busy` announcements.
   **Playwright is the standardized a11y runner ([§6](#6-accessibility)) — not implementer's choice.**
5. **RTL spec green** with the **authored** Select overlay positions verified under `dir="rtl"`.
6. Select: the settled **nested `cdkConnectedOverlay` + `ngComboboxPopup` composition**
   ([§3.4](#34-select--tm-select)) works — `usePopover:'inline'` panel escapes an `overflow:hidden`
   clipping ancestor; flip-up works (with the `updatePosition()`-on-`(attach)` macrotask fix); aria's
   popup registration and the `aria-controls`/`aria-activedescendant` id chain resolve across the
   overlay relocation; `focusMode="activedescendant"` + `selectionMode="explicit"`; lazy overlay;
   captures `tm-option.value` (e.g. a record id) while displaying projected label. **RTL residual from
   the spike is closed:** an RTL-mirrored position set is authored and `matchWidth` verified under
   `dir="rtl"` (the spike saw both fail by default). **Mouse-interaction specs (real mouse events) green,
   guarding [angular/components#32504](https://github.com/angular/components/issues/32504):** clicking an
   option commits and closes, clicking outside closes, clicking the trigger toggles
   ([§10](#10-testing-tellmacore-ui-testing)).
7. Select prepopulated/async value integrity: a **prepopulated value survives until its `ngOption`
   renders** (not just its label) — i.e. the `FormValueControl<T>` model stays source-of-truth and is
   re-applied to aria's listbox when options arrive, defeating aria's unmatched-value auto-prune; the
   **trigger label resolves via `displayWith`** before any option renders.
8. Pending/async-validation state shows `aria-busy` + spinner and suppresses stale "valid".
9. Every entry point is **within its concrete bundle ceiling** ([§8](#8-performance-budget)); the
   token preset passes the schema + fixed-WCAG-AA-contrast gate in both schemes; runtime CSS-variable
   override demonstrated (`setProperty` changes `--color-primary` live); the `@layer tm.base, tm.theme`
   precedence is verified (a `tm.theme` delta overrides base regardless of load order).
10. The **contracts-boundary lint** passes: `@tellma/core-ui/contracts` imports nothing from
    `@angular/core` or the component modules; no cross-package leakage; no bare `outline: none`.
11. The controls are **shaped** for grid embedding (rule 6) but not locked to the draft contracts:
    a bare `<input tmInput>` mounts with no `tm-form-field` and holds no document-level listeners; a
    `tm-select` panel anchors to an arbitrary element and exposes `cancel()` for a host to drive (the
    second-Esc revert is the grid host's, not the standalone control's — [§3.4](#34-select--tm-select));
    each control separates a pure display formatter from its interactive behavior. The draft
    `TmCellEditor`/`TmCellDisplay`
    interfaces ([§2.1](#21-shared-contracts), [§9](#9-data-grid-forward-compatibility-contract)) are
    **not** test-hardened in this phase — they are designed when the grid is built.
12. The library's font piece is in place: self-hosted woff2, `@font-face` with `unicode-range`
    subsetting + `font-display: swap`, the `TM_FONT_SUBSETS` manifest, and `fontPreloadLinks()` — no
    CDN reference; tests assert an unconfigured script contributes no eager download. (Runtime
    per-tenant preload injection is distribution-shell scope, not tested here.)
13. The reference **Arabic locale pack `@tellma/locale-ar` ships and works end-to-end**: installed, it
    adds Arabic library strings (validator-key messages, placeholder, required-field announcement)
    resolved through `TM_UI_TRANSLATE`, and contributes self-hosted **Noto Sans Arabic** (woff2 +
    `@font-face` + `TM_FONT_SUBSETS` manifest entries) merged at build. A test asserts that **with** the
    pack an Arabic locale renders Arabic strings (and the Arabic face is available), and **without** it
    the same keys fall back to English (no blank/raw key) and no Arabic font is fetched. The core stays
    English-only; the pack is the template later locale packs copy.
14. `components.json` is generated and **validated against its JSON Schema** ([§11](#11-docs--mcp-pipeline-tellmacore-ui-mcp));
    the scoped MCP server answers `list`/`describe`/`example`; `llms.txt` and a Storybook showcase
    render. API goldens committed; `approve-api` gate active.
15. Forced-colors and reduced-motion are **Playwright-gated** (`emulateMedia`); bidi `dir="auto"`
    fields verified with mixed AR/EN content in both LTR and RTL roots; message precedence + ICU/param
    interpolation tested via the shared `form()` fixture.
16. All tooling (Storybook, tests, MCP server) runs on OS-assigned free ports — two worktrees in
    parallel, no collision.

## Decisions confirmed

The earlier open questions are settled:

1. **Repo home** — the UI family lives in `client/projects/core/` in `tellma-platform`; locale packs
   (the reference `@tellma/locale-ar`, and later ones) live in the sibling `client/projects/locale/`.
2. **Build tooling** — pnpm + Angular CLI for Phase 1; nx revisited later if the in-repo project
   count or changed-test needs grow ([§1.2](#12-build-tooling--pnpm--angular-cli-nx-deferred)).
3. **i18n** — standardize on Transloco behind the thin `TM_UI_TRANSLATE` escape-hatch token; the
   default Transloco path is **zero-config** for distributions ([§7](#7-rtl-i18n--l10n)).
4. **Density/typography runtime axes** — deferred, but a design requirement that they be addable
   later without a major refactor (token-set switching; no component-internal changes).
5. **Showcase** — Storybook only; sample distribution and any showcase app are out of scope for now.
6. **Templates** — inline for these small components (v22 best practice supersedes D5 here).

The Select-architecture and forms-precedence questions were investigated directly against
`@angular/aria@22` and `@angular/forms@22` source and are settled in
[§2](#2-behavior-layer-and-shared-contracts), [§3.4](#34-select--tm-select),
[§5](#5-forms-integration-signal-forms), and [§9](#9-data-grid-forward-compatibility-contract): aria
owns Select's keyboard/typeahead/active-descendant/open-close as DI directives (so `tm-select` owns only
the scalar↔array bridge, value→key mapping, label resolution, and grid commit/cancel — no separate
pattern class), and Signal Forms binds field state into the control's declared optional inputs (the
documented contract), so a field-bound control reflects the field's state — with a conflicting template
binding *forbidden by lint* rather than relied on as framework write-order ([§5](#5-forms-integration-signal-forms)).
The riskiest piece — composing aria's inline-deferred popup with
CDK-Overlay connected positioning — was **settled by a running Angular-22 + Playwright spike** (the
nested `cdkConnectedOverlay` + `ngComboboxPopup` pattern with `usePopover:'inline'`;
[§3.4](#34-select--tm-select)), leaving only an RTL position-mirroring detail tracked in the DoD.
Implementation can proceed against this spec.
