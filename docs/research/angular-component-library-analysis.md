# Angular Component-Library Analysis — Material/CDK vs PrimeNG

> **Superseded.** This is a research/rationale document. The component-library foundation work it
> informed is now specified in [`docs/specs/0002-component-library-walking-skeleton.md`](../specs/0002-component-library-walking-skeleton.md),
> which is the authoritative source and supersedes this analysis where they differ (e.g. the three-
> component skeleton, Signal-Forms-only, stable `@angular/aria`, inline templates). This doc is kept
> for the comparative analysis and the reasoning behind the locked decisions (D1–D13); it is not
> maintained as decisions evolve.

**Purpose:** Mine the two most widely-used Angular component libraries for patterns to replicate (and pitfalls to avoid) in Tellma's greenfield, **agent-first** ERP component library (built in `tellma-platform`, consumed by distributions, targeting Angular 22).

**Status:** Decisions taken (see Part 3) and folded into `ARCHITECTURE.md` → *Frontend → UI component library*. This doc retains the comparative analysis and the rationale; `ARCHITECTURE.md` carries the committed *what*. Remaining open items are in Part 4.

**Versions examined (local checkouts):**
- Angular Material / CDK — `@angular/components` **v22.1.0-next.0** (Bazel + pnpm). Now also ships a new `@angular/aria` headless-primitives package.
- PrimeNG — **v21.1.8** on Angular 21 (pnpm workspaces; `primeng`, `@primeng/themes`, `@primeng/mcp` packages + showcase app).

> Throughout, **M** = Angular Material/CDK, **P** = PrimeNG. Each topic ends with a **→ Tellma** recommendation.

---

## Part 0 — Formalized topic taxonomy

The original 16 questions, regrouped and completed. Added topics are marked **(+)**.

**A. Repo & package architecture**
1. Monorepo layout, workspace tooling & project boundaries
2. Package & entry-point strategy; public-API surface; tree-shaking granularity
3. Behavior/primitive layer vs styled layer (the CDK split) **(+ made explicit)**

**B. Component implementation**
4. Component-logic extensibility (inputs/outputs, signals, DI tokens, provider fns, base classes, `hostDirectives`, adapters)
5. Template organization & template extensibility (inline vs external, control flow, slots, headless)
6. Default styling/theme + style/theme extensibility
7. Reactive-forms integration: `ControlValueAccessor`, validation **(+)**
8. Shared UI infrastructure: overlay/portal/positioning, virtual scroll, drag-drop **(+)**
9. Icon strategy **(+)**
10. Component API-design & naming conventions (agent-legibility) **(+)**

**C. Cross-cutting quality**
11. Accessibility (a11y)
12. Internationalization & localization, incl. **RTL/Arabic** (first-class for Tellma)
13. Performance: change detection, signals, SSR/hydration, zoneless, bundle size, tree-shaking **(+ SSR/zoneless added)**
14. Security: Trusted Types, sanitization **(+)**

**D. Engineering process**
15. Shared 3rd-party dependencies
16. Testing strategy & test types
17. Coding style & governance
18. Documentation: format, storage, freshness
19. CI/CD, release, versioning & support policy
20. Migration/upgrade tooling (`ng update` schematics) **(+)**

**E. Agent & ecosystem**
21. AI-agent support (MCP servers, `llms.txt`, structured metadata) — *most important for Tellma*
22. Industry patterns worth replicating
23. Patterns that turned out suboptimal — change for a greenfield Angular-22 / agentic build
24. Gaps the original list missed

---

## Part 1 — At-a-glance comparison

| Dimension | Angular Material / CDK (v22) | PrimeNG (v21) |
|---|---|---|
| Workspace / build | **Bazel** + pnpm; `ng_package` | pnpm workspaces; **ng-packagr** (lib) + **tsup** (themes/mcp) |
| Published packages | `@angular/cdk`, `@angular/material`, **`@angular/aria`**, date adapters | `primeng`, `@primeng/themes`, `@primeng/mcp` (+ framework-agnostic `@primeuix/*`) |
| Component on disk | Many small files: `*.ts` + external `*.html` + `*.scss` partials + base class + `testing/*-harness.ts` + `*.md` | **One large `*.ts`** (inline template) + `style/<c>style.ts` (TS tokens) + `public_api.ts` + `ng-package.json` |
| Behavior/primitive layer | **Yes — CDK + new Angular Aria** (headless, signal-first) | Partial: `BaseComponent`/`BaseInput`/… base classes; reuses some `@angular/cdk` |
| Change detection | Styled components use **default CD** (no explicit `OnPush` in current source) | **`OnPush`** on ~90 components |
| Signals (public API) | Mostly decorator `@Input`; signal `input()` only in newest (timepicker, form-field, button-base) + all of `@angular/aria` | Internal state = signals/`computed`/`effect`; **public API still decorator `@Input`/`@Output`** (≈2,200 vs 33 signal inputs) |
| Control flow in templates | New `@if`/`@for` adopted | **Predominantly legacy `*ngIf`/`*ngFor`** (992 vs 107) |
| Theming | **Sass, compile-time** M2/M3 design tokens → `--mat-sys-*` / `--mat-<c>-*` CSS vars; mixins; prebuilt CSS themes | **TS design-token objects** (primitive→semantic→component) injected at **runtime** via `@primeuix/styled`; `darkModeSelector`, `cssLayer`, `dt()`, `pt`, `unstyled` |
| a11y toolkit | **Rich CDK a11y** (FocusTrap, FocusMonitor, LiveAnnouncer, AriaDescriber, ListKeyManager family, InteractivityChecker, HighContrastModeDetector) + axe in tests | Lighter: `pFocusTrap`, `pAutoFocus` over `@primeuix/utils`; per-component ARIA; no central key-manager/announcer; no axe |
| RTL | **Central `Directionality`** (cdk/bidi) + RTL-locale regex; flows into key managers | Per-component `rtl` `@Input`; no auto-detection |
| i18n | **Per-component "Intl" provider classes** + `DateAdapter` (native/luxon/moment/date-fns) + `MAT_DATE_LOCALE`/`LOCALE_ID` | **Single central `Translation` object** via `PrimeNG` config `setTranslation()`; nested `aria` labels |
| Testing | **344 specs + 97 component harnesses + API goldens** + tsec + circular-dep checks; axe in new aria | 96 specs, **0 harnesses, no API goldens**; unit tests **disabled in CI** |
| Lint / governance | tslint (custom rules) + stylelint + prettier + ng-dev; CODING_STANDARDS, CODE_REVIEWS, caretaker rotation; conventional commits **drive** release; `@breaking-change` enforced | ESLint flat + prettier + commitlint + husky + lint-staged; no formal standards doc; informal deprecations |
| Docs | **dgeni** + 70 colocated `*.md` + `components-examples/` + API extraction → angular.dev | Showcase app + **auto-generated `components.json` (3 MB)** + `llms.txt`/`llms-full.txt` + per-component LLM markdown |
| Release / versioning | `ng-dev release` — **semver from conventional commits**, LTS branches | `pnpm recursive publish` — **manual** version bumps |
| Agent support | None in the *components* repo; but the **Angular framework** ships `llms.txt` + **Angular MCP server** (v20.2+/v21) + Web Codegen Scorer. Harnesses + API goldens + typed API help agents indirectly | **Ships `@primeng/mcp`** (MCP server, ~34 tools, `components.json`, migration tools v18→v21; installable in Claude Code/Cursor/Copilot/Codex/Windsurf/Zed) + `llms.txt` |

---

## Part 2 — Topic-by-topic

### 1. Monorepo layout, tooling & project boundaries
- **M:** Bazel-driven monorepo. `src/cdk` (behavior), `src/material` (styled), `src/material-experimental`, `src/cdk-experimental`, **`src/aria`** (new headless primitives), three date adapters, `google-maps`, `youtube-player`. Per-target `BUILD.bazel`, `config.bzl` enumerate secondary entry points. Hermetic, reproducible, heavy.
- **P:** Plain pnpm workspaces: `packages/{primeng,themes,mcp}` + `apps/showcase`. Each component dir carries its own `ng-package.json` + `public_api.ts`; ng-packagr generates secondary entry points by convention. Lighter, conventional, faster to onboard.
- **Common:** Monorepo; pnpm; per-component secondary entry points; `sideEffects:false` (explicit in M).
- **Different:** Bazel hermeticity & fine-grained caching vs ng-packagr simplicity; M publishes a standalone reusable behavior package (`@angular/cdk`), P keeps base classes internal.
- **→ Tellma:** Use **pnpm workspaces + ng-packagr**; skip Bazel (overkill for a single-team greenfield, steep agent-onboarding cost). Keep **per-component secondary entry points** + `sideEffects:false`. Adopt M's **explicit behavior/primitive vs styled split** (see #3).

### 2. Packaging & public-API surface
- **M:** Each component is a secondary entry point (`@angular/material/button`); `package.json#exports` exposes Sass entry points & prebuilt theme CSS; API surface frozen via golden `*.api.md`.
- **P:** Single `primeng` package, per-component subpaths (`primeng/button`); no `exports` map (ng-packagr handles it); no API golden.
- **→ Tellma:** Per-component subpath imports for tree-shaking; **publish an API report (golden) per entry point** so agent-generated code can't silently break across versions. Ship a typed `provideTellmaUi()` root provider.

### 3. Behavior/primitive layer vs styled layer ★
- **M:** The defining strength. `@angular/cdk` = unstyled behavior (overlay, portal, a11y, drag-drop, listbox, menu, table, scrolling, text-field, bidi). The **new `@angular/aria`** goes further: headless `@Directive`s (`ngListbox`/`ngOption`, combobox, grid, menu, tabs, toolbar, tree) on native elements, **fully signal-based**, with the state machine factored into a separate **`*Pattern` class** (e.g. `ListboxPattern`), ARIA in host bindings, `Directionality` for RTL, `reportViolations()` runtime correctness checks, and shipped harnesses + axe specs.
- **P:** No clean separation. `BaseComponent`/`BaseInput`/`BaseEditableHolder`/`BaseModelHolder` provide shared lifecycle/styling/PT plumbing via inheritance, but behavior is fused with styling inside each component. Reuses parts of `@angular/cdk`.
- **→ Tellma:** **Adopt the headless-pattern + styled-wrapper split.** Build ERP components (data grid, lookup/picker, master-detail, tree-table, document/journal editor) on top of `@angular/cdk` **and `@angular/aria`** rather than reinventing overlay/keyboard/a11y. Factor each component's behavior into a signal-based **pattern/state class** separate from the styled directive — this is the single most future-proof, agent-legible, testable pattern in either repo.

### 4. Component-logic extensibility
- **M:** Injection tokens for config (`MAT_*_DEFAULT_OPTIONS`, `MAT_BUTTON_CONFIG`), `provideX()` functions, swappable **adapters** (`DateAdapter`, range-selection strategy via token), `exportAs` for template refs, base classes; limited `hostDirectives`.
- **P:** Centralized `PrimeNG` config service (signals for `ripple`, `inputStyle`, `unstyled`, `pt`), `hostDirectives` (e.g. `Bind`), parent/child wiring via `InjectionToken`s (`BUTTON_INSTANCE`, `PARENT_INSTANCE`), the **`pt` passthrough** + **`dt()`** token override + **`unstyled`** mode for deep per-instance customization.
- **Common:** DI tokens for config; base classes; both expose escape hatches.
- **Different:** M favors per-feature tokens + provider fns + adapters (composable, explicit). P favors one global service + passthrough object (powerful, but stringly-typed and verbose; many internal tokens can confuse readers/agents).
- **→ Tellma:** Provider functions + per-feature injection tokens (M) for config; **`hostDirectives` over inheritance** for composition; a **typed passthrough escape hatch** (P's idea, but typed, not string-keyed); **adapters** for anything pluggable (dates, currency, number formatting — essential for ERP).

### 5. Template organization & extensibility
- **M:** External `*.html`; **new control flow** (`@if`/`@for`); slots via `<ng-content select>`; `exportAs`. Headless CDK/aria primitives let consumers own the entire template.
- **P:** **Inline** templates inside the big `*.ts`; **mostly legacy `*ngIf`/`*ngFor`**; rich named-slot system via `<ng-template pTemplate="...">` / `@ContentChild` with **typed template contexts** (e.g. `ButtonIconTemplateContext`).
- **Common:** Both support content projection + template-driven customization.
- **Different:** M = external + modern flow but CSS-selector slots (less discoverable); P = inline + legacy flow but **named, typed template slots** (very discoverable, the better idea).
- **→ Tellma:** **External templates + `@if`/`@for`/`@let`** (M's modernity) **+ named, typed slot contexts** (P's discoverability), using Angular 22's typed template/named-slot support. Avoid P's giant inline templates. Avoid CSS-selector-only slots.

### 6. Default styling & theming ★
- **M:** Sass, **compile-time**. M2/M3 design tokens (`_md-sys-*`, per-component `_m3-*.scss`) emitted as `--mat-sys-*` / `--mat-<c>-*` CSS variables. Theme via `mat.theme(...)`, color/typography/density systems, `mat.<c>-overrides(...)`, prebuilt CSS themes, `cdk.high-contrast` for forced-colors. Type-safe at build, optimized output — but runtime theme switching needs precompiled CSS and you need a Sass toolchain to author themes.
- **P:** **TS design-token objects**, **runtime**. Three tiers — primitive → semantic → component — as plain objects (`@primeng/themes` presets aura/lara/material/nora, built on `@primeuix/themes`/`styled`). `providePrimeNG({theme:{preset,options}})` with `prefix`, `darkModeSelector`, `cssLayer`. Instant runtime switching, `darkModeSelector` dark mode, per-instance `dt()` token overrides + `pt` attribute passthrough + `unstyled`. Trade-off: no compile-time validation, runtime CSS injection (SSR/FOUC care needed), external engine coupling.
- **Common:** Both are CSS-variable / design-token systems with primitive→semantic→component layering and dark-mode support.
- **Different:** authoring format (Sass build-time vs TS runtime), runtime switchability, agent/tool generatability, SSR posture.
- **→ Tellma:** Per-distribution branding + possible agent/tool-generated themes ⇒ **a TS/JSON design-token model is the right primary mechanism** (P-style: typed token objects, runtime switch, `darkModeSelector`, `@layer` for safe overrides, `dt()` + typed passthrough escape hatches). **But borrow from M:** strict token naming, density/typography systems, forced-colors/high-contrast support, and **a typed token contract** (so the schema is validatable and Figma-syncable). Mitigate P's weaknesses: **SSR-safe emission** (precompile base/critical CSS, inject overrides), validate tokens at build, and don't hard-couple to a third-party styling engine you don't control.

### 7. Reactive-forms integration (+)
- **M:** Clean `ControlValueAccessor` + `NG_VALUE_ACCESSOR`/`NG_VALIDATORS` providers (see `MatCheckbox`), `mat-form-field` + `MatFormFieldControl` contract, `ErrorStateMatcher`.
- **P:** `BaseInput`/`BaseEditableHolder`/`BaseModelHolder` hierarchy implements CVA; `floatlabel`/`iftalabel` field wrappers.
- **→ Tellma:** Form-density is the heart of an ERP. First-class CVA on every input, a **form-field contract** like `MatFormFieldControl`, an `ErrorStateMatcher`-style hook, and design for Angular 22 **Signal Forms** readiness. Provide a `provideTellmaForms()`-style validation/messages config.

### 8. Shared UI infrastructure (+)
- **M:** CDK Overlay (best-in-class `FlexibleConnectedPositionStrategy`), Portal, `cdk/scrolling` virtual scroll, `cdk/drag-drop`, `cdk/text-field` autosize.
- **P:** Own overlay/positioning + a `scroller` (virtual scroll); uses `@angular/cdk` drag-drop selectively.
- **→ Tellma:** **Reuse `@angular/cdk`** overlay/portal/scrolling/drag-drop — do not reinvent. ERP grids need virtual scroll + flexible overlays; CDK is the mature, a11y-correct option.

### 9. Icon strategy (+)
- **M:** `mat-icon` with font **and** SVG registry (`MatIconRegistry`), namespaced sets, security-sanitized.
- **P:** `primeicons` font + per-icon components.
- **→ Tellma:** **SVG-sprite/registry, tree-shakeable, Trusted-Types-sanitized**; allow per-distribution icon set registration. Avoid icon fonts (a11y + bundle).

### 10. API-design & naming conventions (+)
- **M:** Disciplined CODING_STANDARDS (no boolean params, avoid `any`, `_` private prefix, getters only for `@Input`, file size limits), `mat-`/`Mat` prefix, `exportAs`.
- **P:** ESLint-enforced `p-` element / `p` attribute prefixes; no class-suffix requirement (bare `Button`, `Accordion`).
- **→ Tellma:** Pick one prefix (e.g. `tl-` / `Tl…`), enforce by lint, and **codify naming/shape conventions** so agents can predict APIs (consistent input names like `disabled`, `value`, `loading`; consistent event names like `valueChange`). Consistency is itself an agent feature.

### 11. Accessibility ★
- **M:** Comprehensive. CDK a11y: `FocusTrap`, `FocusMonitor` (focus origin), `LiveAnnouncer`, `AriaDescriber`, `ListKeyManager`/`ActiveDescendantKeyManager`/`FocusKeyManager`, `InteractivityChecker`, `HighContrastModeDetector`. New `@angular/aria` bakes ARIA into host bindings and runs **axe-core** in specs (`run-accessibility-checks.ts`).
- **P:** Lighter: `pFocusTrap`, `pAutoFocus` over `@primeuix/utils`; per-component ARIA & keyboard handlers; central `aria` translation labels; **no** announcer/key-manager utilities, **no** axe automation.
- **→ Tellma:** **Reuse CDK a11y + `@angular/aria` patterns; run axe-core in CI.** A11y is regulatory for many ERP buyers — make it a build gate, not an afterthought.

### 12. i18n / l10n + RTL ★ (Tellma-critical)
- **M:** Per-component **Intl provider classes** (`MatPaginatorIntl`, `MatDatepickerIntl`, …) with a `changes` Subject; `DateAdapter` abstraction (native/luxon/moment/date-fns) + `MAT_DATE_LOCALE`/`LOCALE_ID`; **central `Directionality`** with RTL-locale auto-detection that flows into keyboard navigation.
- **P:** **Single central `Translation` object** (`setTranslation()`), nested `aria` labels, `Intl.NumberFormat` for digit localization; **per-component `rtl` input, no auto-detection**, no central directionality.
- **Common:** Reactive translation updates; separation of ARIA labels.
- **Different:** decentralized typed Intl classes vs one central object; auto-RTL vs manual RTL.
- **→ Tellma:** **Hybrid:** P's *single central, typed translation object* (simpler, fewer providers) **+** M's *central `Directionality` with auto-RTL* and *`DateAdapter`-style adapters* for dates/numbers/currency. Integrate `@angular/localize`. **RTL/Arabic is first-class**: CSS logical properties everywhere (`margin-inline-start`, not `-left`), auto `dir` from locale, RTL-aware key navigation. Do **not** copy P's manual per-component `rtl` flag.

### 13. Performance: CD, signals, SSR, bundle ★
- **M:** Styled components use **default change detection** in the current source (the `changeDetection` entries in `src/material` are all in test host components); precompiled CSS; modular small files; `sideEffects:false`; fine-grained entry points; new aria is zoneless-tested. Public API mostly decorator inputs.
- **P:** **`OnPush` everywhere** + signals/`computed`/`effect` for internal state (good), **but** huge monolithic files (`table.ts` ≈ 239 KB), legacy control flow, and **runtime CSS-in-JS injection** (small static CSS, but runtime cost + SSR/FOUC risk). Public API still decorator-based.
- **→ Tellma:** Go **zoneless + signals end-to-end** (Angular 22), `OnPush`-equivalent by default, **signal `input()`/`model()`/`output()` for the public API too** (leapfrog both libs). Keep components **composable & small** (avoid P's mega-files). Precompile base CSS for SSR; inject only theme overrides at runtime. Per-component entry points + `sideEffects:false` for tree-shaking. SSR/hydration correctness as a test gate.

### 14. Security (+)
- **M:** `tsec` (Trusted Types) test target + `safevalues`; sanitized icon/HTML handling; tsec exemption goldens.
- **P:** Less formal; relies on Angular sanitization + `pnpm audit` in CI.
- **→ Tellma:** **Trusted Types + safevalues + sanitized passthrough.** ERP handles sensitive data; any `pt`/HTML escape hatch must be sanitized and lint-guarded.

### 15. Shared 3rd-party dependencies
- **M:** Peer `@angular/*` + `rxjs`; `tslib`; `parse5`, `safevalues` (cdk); optional date libs (luxon/moment/date-fns).
- **P:** Peer `@angular/*` + **`@angular/cdk`** + `rxjs`; deps on `@primeuix/{styled,utils,styles,motion}` (+ `@primeng/mcp` on `@primeuix/mcp`); `primeicons`.
- **Common:** Both peer-depend on `@angular/*`, `rxjs`, `tslib`; **both use `@angular/cdk`** (M owns it; P consumes it as a peer).
- **Different:** P externalizes its engine into framework-agnostic `@primeuix/*` shared across PrimeReact/PrimeVue; M keeps everything in-repo.
- **→ Tellma:** Depend on **`@angular/cdk` + `@angular/aria`** as the shared foundation; minimize runtime deps; keep RxJS usage low (signals-first). Only build a framework-agnostic core engine if cross-framework reuse becomes a goal (unlikely for an Angular-only ERP — keep it Angular-native and simpler).

### 16. Testing ★
- **M:** Karma+Jasmine; **344 specs**; **97 component harnesses** (`ComponentHarness`/`HarnessLoader`/`TestbedHarnessEnvironment`) — a typed, implementation-independent automation API shipped *for consumers*; **API goldens** (`*.api.md` + `approve-api`); tsec; ts-circular-deps; axe in new aria; zoneless test env.
- **P:** Karma+Jasmine; **96 specs; 0 harnesses; no API goldens**; unit tests **disabled in CI** (format + audit only).
- **→ Tellma:** **Ship component harnesses from day one** — they double as a stable, typed surface that *both* human tests and agents can drive deterministically. **Adopt API goldens** (agent-generated code is fragile to silent API drift). axe a11y tests + SSR tests + zoneless test env. Keep tests **on** in CI.

### 17. Coding style & governance
- **M:** tslint (custom rules: `require-breaking-change-version`, member naming, import blacklist…) + stylelint + prettier + ng-dev; CODING_STANDARDS / CODE_REVIEWS / CONTRIBUTING; **conventional commits drive semver**; `@breaking-change <ver>` enforced; caretaker rotation; renovate.
- **P:** ESLint **flat config** + prettier + commitlint + husky + lint-staged; no formal standards doc; informal `@deprecated`.
- **→ Tellma:** **ESLint flat config** (tslint is dead — don't copy M here) + prettier + commitlint + husky; **custom lint rules to enforce API conventions** (#10) and `@breaking-change` discipline (M's idea); conventional-commits-driven release; a short CODING_STANDARDS that's *also* fed to agents (see #21).

### 18. Documentation
- **M:** **dgeni** pipeline; **70 colocated `*.md`** narrative+a11y guides; runnable examples in `components-examples/`; API extracted from types/JSDoc; published to angular.dev. Hand-written narrative + generated API.
- **P:** Showcase app + **auto-generated `components.json` (3 MB)** as the single metadata source (props/events/methods/slots/PT/styles/examples), plus `llms.txt`/`llms-full.txt` and per-component LLM markdown. Mostly generated, less narrative.
- **→ Tellma:** **Generate from source as the single source of truth** (typed signal inputs + JSDoc → a `components.json`-style metadata file) and render *both* a human showcase **and** machine formats (`components.json` + `llms.txt` + per-component md). Co-locate runnable examples + short narrative like M. Docs that drift from code are an agent hazard — make them generated and CI-verified.

### 19. CI/CD, release, versioning, support
- **M:** GitHub Actions + Bazel; lint, **API-golden checks**, e2e, integration; `ng-dev release` ⇒ **semver from conventional commits**, LTS/patch branches.
- **P:** GitHub Actions minimal (format check + `pnpm audit`; **unit tests disabled**); `pnpm recursive publish`; **manual** versioning.
- **→ Tellma:** CI gates = lint + **full unit tests** + **API goldens** + axe + SSR + bundle-size budget. **Automate semver from conventional commits.** Define a support/LTS policy aligned with Tellma platform releases (matters for distributions pinning versions).

### 20. Migration / upgrade tooling (+)
- **M:** `ng update` **schematics** in `src/material/schematics` + cdk schematics for automated breaking-change migrations.
- **P:** Less formal schematics, **but** ships **MCP migration tools** (`migrate_v18_to_v19` … `v20_to_v21`) exposing breaking/deprecations/whatsnew.
- **→ Tellma:** Provide **both**: `ng update` schematics for mechanical migrations **and** MCP-exposed migration guides so an agent can drive an upgrade. Upgrades across distributions are a core Tellma concern — invest here.

### 21. AI-agent support ★ (most important)
- **M / Angular:** The *components* repo ships no MCP server, but the **Angular framework** ships official `llms.txt` + `llms-full.txt`, an **Angular MCP server** (introduced v20.2, expanded v21) that lets agents "see" project structure/components and follow current conventions, and the open-source **Web Codegen Scorer** to *measure* AI-generated-code quality. Material's **harnesses, API goldens, rich JSDoc and typed API** also make it agent-legible indirectly.
- **P:** Ships **`@primeng/mcp`** — a real MCP server (built with `tsup`, `bin: primeng-mcp`, on the framework-agnostic `@primeuix/mcp` core) driven by the generated `components.json`. ~34 tools across: component info (props/events/methods/slots), examples, theming/PT/tokens guides, accessibility, **version migration**, search/suggest/“find-by-prop/event”, validate-props, related-components. Installable in Claude Code, Cursor, Copilot, Codex, Windsurf, Zed. Plus `llms.txt`.
- **→ Tellma (headline):** This is where a greenfield library can **lead**, not follow:
  1. **Ship an MCP server from day one**, generated from the same `components.json` metadata as the docs (one source of truth). Expose tools for: list/search/suggest components, props/events/slots/tokens, runnable examples, theming/passthrough guides, a11y info, **validate-props/validate-usage**, and **migration guides**.
  2. Ship **`llms.txt` / `llms-full.txt`** and an **`AGENTS.md`/`CLAUDE.md`** with house conventions.
  3. Make the **public API signal-typed and uniformly named** (#10) so agents predict it; ship **harnesses** (#16) as a deterministic automation surface; ship **API goldens** (#16) so agent code doesn't silently break.
  4. Provide an **MCP "scaffold/validate" tool** that emits and checks Tellma-correct component usage (the ERP analog of Web Codegen Scorer).

### 22. Industry patterns worth replicating
Headless behavior + styled wrapper split (CDK/aria); **component harnesses**; **API goldens**; design-token theming with primitive→semantic→component layering + dark mode + density/typography; CDK **overlay/virtual-scroll/drag-drop**; `ng update` **schematics**; **conventional-commits-driven semver** + LTS; **theme presets** + prebuilt themes; a11y utilities + axe gating; a **dev showcase** app; **generated metadata** (`components.json`) feeding docs + MCP; `llms.txt` + **MCP server**.

### 23. Suboptimal patterns to change in a greenfield (Angular 22, agentic)
- **PrimeNG mega-files** (`table.ts` ≈ 239 KB, inline templates): hard for humans *and* agents to read/edit; hurts review and tree-shaking. → small, composed sub-components + external templates.
- **Legacy `*ngIf`/`*ngFor`** (PrimeNG): → `@if`/`@for`/`@switch`/`@let`.
- **Decorator `@Input`/`@Output` public APIs at scale** (both libs): → **signal `input()`/`model()`/`output()`** everywhere.
- **Default change detection** (Material styled layer): → zoneless + signals.
- **Disabled CI unit tests / no harnesses / no API goldens** (PrimeNG): → keep tests on, ship harnesses + goldens.
- **Runtime CSS-in-JS without an SSR plan** (PrimeNG): → SSR-safe token emission, precompiled base CSS.
- **Compile-time-only Sass theming as the sole mechanism** (Material): bad for per-distribution runtime/agent theming → TS/JSON token model primary.
- **tslint** (Material): dead tool → ESLint flat config.
- **Stringly-typed slots/passthrough** (`pTemplate="..."`, string-keyed `pt`) and **many opaque internal DI tokens** (`BUTTON_INSTANCE`, `PARENT_INSTANCE`): → typed slot contexts + typed passthrough + documented `hostDirectives`.
- **CSS-selector-only `<ng-content select>` slots** (Material): less discoverable → named, typed slots.
- **Icon fonts** (PrimeNG primeicons): → SVG registry.

### 24. Gaps the original list missed
Reactive-forms/CVA contract (#7), shared overlay/scroll/drag infra (#8), icons (#9), API naming conventions (#10), SSR/hydration/zoneless (#13), security/Trusted Types (#14), migration schematics (#20), versioning/LTS support policy (#19), and the **single-source-of-truth metadata pipeline** that feeds docs + MCP + scaffolding (#18/#21).

---

## Part 3 — Decisions (locked)

**D1 — Greenfield on CDK + Aria; never cross-framework.** Build directly on `@angular/cdk` + `@angular/aria`; do not extend or wrap Material/PrimeNG, and do not adopt a framework-agnostic engine (e.g. `@primeuix/*`). Angular-only forever ⇒ own a small Angular-native token emitter rather than depending on an external styling engine.

**D2 — Package family under the platform's Core layer.** The UI library is bare-minimum (every distribution's Angular shell needs it), so it ships as a `core-*` family in `client/projects/core/`, riding the family-major versioning rule:

| Package | Folder | Role |
|---|---|---|
| `@tellma/core-ui-primitives` | `tellma-core-ui-primitives/` | Headless `Tm*Pattern` state classes + behaviors, on `@angular/cdk` + `@angular/aria`. Inputs typed `SignalLike` — framework-decoupled for testability. |
| `@tellma/core-ui` | `tellma-core-ui/` | Styled `tm-*` components (the main import). |
| `@tellma/core-ui-tokens` | `tellma-core-ui-tokens/` | Typed token contract + presets + `tokens→CSS-vars` emitter. |
| `@tellma/core-ui-testing` | `tellma-core-ui-testing/` | Component harnesses. |
| `@tellma/core-ui-mcp` | `tellma-core-ui-mcp/` | Scoped MCP server (data = generated `components.json`). |

`core` = the platform layer; `primitives` = headless. No term overload. Mirrors the .NET Core family (`Tellma.Core.*`).

**D3 — Prefix `tm-` / `Tm…`** for every selector, class, token, and provider (`tm-select`, `TmSelect`, `TmSelectPattern`, `TM_DATEPICKER_CONFIG`), enforced by an ESLint selector rule.

**D4 — Behavior/styled split via signal pattern classes.** Each component = a framework-decoupled `TmXxxPattern` (all state/keyboard/selection/validation as `SignalLike` computeds, exposing `validate()`/`setDefaultState()`/`onKeydown()`/`onPointerdown()`) + a thin styled `TmXxx` directive/component that adapts Angular `input()`/`model()` in and host ARIA bindings out. Built on `@angular/aria` behaviors where they exist (listbox, grid, tree, combobox, menu, tabs, toolbar).

**D5 — Signal-first, zoneless, modern templates.** Public API is `input()`/`model()`/`output()` (leapfrogging both libs); zoneless + OnPush-equivalent; external `.html` with `@if`/`@for`/`@let`; static slots via attribute-selector `ng-content` (documented `[tmXxx]` convention, never bare CSS-class selectors); data-bearing slots via typed `ng-template` contexts guarded by `ngTemplateContextGuard`.

**D6 — Theming = typed TS/JSON design tokens, agent-authored.** Primitive→semantic→component token objects; a typed `TmTokens` contract → generated JSON Schema → build-time validation (schema + WCAG contrast) so an agent-authored preset that breaks contrast or references a missing token cannot merge. Runtime-switchable via CSS variables + `@layer`; `darkModeSelector` dark mode; per-instance `dt()` + typed passthrough; density + typography systems; forced-colors/`@media (forced-colors: active)` support. **SSR-safe emission:** precompiled base/default-theme CSS shipped static; only per-distribution override deltas injected at runtime (server-rendered into initial HTML). Figma-syncable via the JSON Schema.

**D7 — a11y baked in; RTL/Arabic first-class.** CDK a11y (FocusTrap, FocusMonitor, LiveAnnouncer, AriaDescriber, key managers) + aria patterns; axe-core as a CI gate. RTL via CDK `Directionality` auto-detect + CSS logical properties throughout; RTL-aware keyboard nav.

**D8 — i18n = central typed strings behind an adapter token.** One typed `TmUiStrings` object (the library's own labels), sourced via an injectable adapter so a distribution feeds it from **Transloco** (scoped/lazy), `@angular/localize`, or static presets; signal/observable-backed so language switches propagate. Default locale presets shipped in-package. Dates/numbers/currency via `TmDateAdapter`/`TmNumberAdapter`/`TmCurrencyAdapter` (swappable per distribution — e.g. Hijri calendar).

**D9 — Forms: CVA + Signal Forms, dual-compatible, from day 1.** Every input implements `ControlValueAccessor` *and* exposes a signal `value = model<T>()` so it works with reactive/template forms today and slots into Angular 22 Signal Forms natively. Cross-cutting validation/messages/display-policy centralized in `provideTellmaForms()`. CVA is the stable fallback while Signal Forms matures.

**D10 — Icons via `TmIconRegistry` (SVG), default set Lucide.** Reuse **Lucide** (`lucide-angular`, MIT, tree-shakeable SVG) as the default set, **Tabler** as the better-stocked fallback for finance glyphs; wrap behind `tm-icon` + a registry so the underlying set is swappable per distribution and sanitized (Trusted Types). No icon fonts.

**D11 — Quality gates.** API goldens per entry point (Microsoft API Extractor + `approve-api` CI gate); component harnesses shipped; axe + SSR/hydration + bundle-size budget in CI; ESLint flat + prettier + commitlint; `@deprecated` paired with an enforced `@breaking-change <version>`. Tests always on in CI.

**D12 — Docs generated from source.** Pipeline: source (typed inputs + JSDoc + co-located `*.stories.ts`) → API Extractor (`.api.json`) + thin custom extractor → **`components.json`** (single source of truth) → {**Storybook** showcase, `llms.txt`/`llms-full.txt`, MCP server, scaffold/validate tooling, API goldens}. Stories double as runnable demos, interaction tests, and visual-regression snapshots; narrative `*.md` co-located per component.

**D13 — Scoped MCP, federated.** `@tellma/core-ui-mcp` is generated from the UI lib's `components.json` and versioned with it. A `dotnet tellma mcp` umbrella federates the scoped servers a distribution pins (UI + backend) at their pinned versions; cross-layer scaffolding tools live in the umbrella. Clients can also aggregate by listing multiple servers.

## Part 4 — Open questions (remaining)

Resolved this round: cross-framework (never → D1), greenfield-vs-extend (greenfield on CDK/aria → D1), theme authoring (agents → D6), Signal Forms (day 1 → D9), package naming (`core-ui*` → D2), prefix (`tm-` → D3), MCP topology (scoped+federated → D13).

Still open:
- **Repo home:** confirm the UI family lives in `tellma-platform`'s `client/projects/core/` (vs a dedicated repo). Lean: same repo, separate packages, versioning tracks the platform.
- **Theme-builder surface:** since agents author themes (D6), is there *also* a human theme-builder UI, or only the schema + MCP `generate_theme`/`validate_theme` tools? Affects how much UX to build around the token contract.
- **MCP federation mechanics:** how `dotnet tellma mcp` discovers pinned packs and proxies their servers (namespacing, transport, auth in headless/CI runs). Needs design + tooling.
- **`@angular/aria` maturity:** it's new/experimental in v22 — which primitives are stable enough to build on at v1 vs. which we implement ourselves and migrate later.
- **Signal Forms stability:** track Angular's signal-forms API status through v22; keep the CVA bridge load-bearing until it's stable.
- **v1 ERP component set:** working list below; finalize and prioritize.

### v1 ERP component set (working list)

Confirmed (from discussion): basic inputs (text, numeric, percentage, select, textarea); multi-calendar date-picker; Excel-like editable data-grid; entity-picker (FK); tree-table + regular table; responsive nav menu; common layouts (search/detail/edit); modal/popup/context-menu; buttons (regular/icon/split); state-flow; spinner; info/success/warning/error message; basic charting; app shell (brand + nav + profile + omni-search + version-refresh banner + announcements banner); user-image selector + viewer; icons.

Added (ERP-weighted, from analysis):
- **Form inputs:** checkbox, radio group, toggle/switch, multi-select, autocomplete/typeahead, **currency input** (amount + code + FX), **date-range** + time pickers, file/attachment upload, masked input, tags/chips input, password.
- **Form scaffolding:** **`tm-form-field`** (label/hint/error/required), **field-array / line-items editor** (invoice & journal lines), fieldset/section.
- **Data:** pagination, column chooser/reorder/resize/pin, **filter / query builder**, grouping + aggregation/totals footer, export (Excel/CSV/PDF), bulk-select toolbar, empty states, skeleton loaders, row master-detail.
- **Navigation:** breadcrumbs, tabs, stepper/wizard, drawer/sidenav.
- **Overlays/feedback:** tooltip, popover, **toast service**, **confirm-dialog service**, bottom sheet.
- **Display:** badge, tag/chip, avatar group, card/panel, accordion/expansion, **description list** (key-value detail display), **timeline** (audit/approval history), stat/KPI tile, progress bar, divider.
- **ERP composites:** approval/workflow timeline + action bar (this is "state-flow"), audit-trail viewer, attachment list + viewer (PDF/image), import wizard (CSV/Excel mapping), account/dimension picker, fiscal-period picker, optimistic-concurrency conflict UI.
- **Services:** breakpoint/responsive, keyboard-shortcut, clipboard, unsaved-changes guard, theme/dark-mode toggle, global error → toast.

Top five underweighted-yet-essential: **`tm-form-field`**, **field-array/line-items editor**, **filter/query builder**, **description list**, **toast + confirm services**.

---

## Appendix A — Design-token model: Material vs PrimeNG (concrete contrast)

Deep-dive supporting topic 6 and decision **D6**. Grounded in the actual token files: Material `src/material/core/tokens/m3/_md-sys-*.scss` (design-system v0.161) + `src/material/button/_m3-button.scss`; PrimeNG Aura primitive/semantic/component objects from `@primeuix/themes` (the local PrimeNG checkout only re-exports them — read from `primefaces/primeuix` source). Structure is stable across minor versions; exact values drift.

### A.1 — Two structural models

**Material M3 — 2 exposed strata over a hidden palette.** Palettes (Sass input, *not* emitted as CSS vars) → **system tokens** `--mat-sys-*` → **component tokens** `--mat-<comp>-*`. Component tokens are bucketed into `base / color / typography / density` and **duplicated per variant** (filled, outlined, elevated, text, tonal). Light/dark are **two separately-built value maps**.

```
palette primary[40] ─► --mat-sys-primary ─► --mat-button-filled-container-color ─► background
                       (md.sys.color)       (md.comp; "filled" baked into the name)
```

**PrimeNG Aura — 3 exposed strata.** **primitive** `--p-blue-500` → **semantic** `--p-primary-color` (refs primitive via `{blue.500}`) → **component** `--p-button-primary-background`. Component tokens are bucketed into a root set + `colorScheme.{light,dark}` × **severity** × **variant**. Light/dark are **branches inside one object**, switched at runtime by `darkModeSelector`.

```
{blue.500} ─► {primary.500} ─► primary.color ─► button…light.primary.background ─► background
(primitive)   (semantic)       (semantic role)  (component; "primary" is a severity key)
```

### A.2 — Concrete token lists by dimension

**Color** — *partial overlap, structural divergence.*
- Material system color (~50): `primary, on-primary, primary-container, on-primary-container, primary-fixed, primary-fixed-dim, on-primary-fixed, on-primary-fixed-variant, inverse-primary` — **and the identical family for `secondary` and `tertiary`**; `error, on-error, error-container, on-error-container`; surface system `surface, surface-dim, surface-bright, surface-container-lowest/low/—/high/highest, on-surface, surface-variant, on-surface-variant, surface-tint, inverse-surface, inverse-on-surface`; `outline, outline-variant, shadow, scrim, background, on-background`.
- PrimeNG: primitive = 22 color ramps (`slate, blue, emerald, …` each 50–950); semantic = `primary.50…950` + `primary.color/contrastColor/hoverColor/activeColor`, and in `colorScheme`: `surface.0…950`, `text.color/mutedColor/hoverColor/hoverMutedColor`, `content.background/hoverBackground/borderColor/color`, `highlight.background/color/focusBackground/focusColor`, `mask.background/color`.
- **Common:** primary + contrasting foreground (`on-primary` ≈ `primary.contrastColor`), surface ramp, text/foreground, outline/border, overlay/scrim, highlight. **Different:** Material makes **secondary + tertiary + error** first-class *global* roles with full `on-/container/fixed` families and a 7-level surface-container system; PrimeNG keeps only **primary** global and expresses secondary/info/success/warn/help/danger/**contrast** as **per-component severities**, plus a flat `surface 0–950` ramp. Material's **`on-X` pairing for every container** is its signature.

**Typography** — *starkest divergence.* Material ships a full **type scale** (~75 tokens): `display/headline/title/body/label` × `{large,medium,small}`, each with `-font/-size/-weight/-line-height/-tracking` (e.g. `label-large-size: 0.875rem`, `label-large-tracking: 0.006rem`). PrimeNG has **no global typography tokens** — inherits host `font-*`, with only scraps per component (`button.label.fontWeight`, `button.sm.fontSize`).

**Shape** — Material: global semantic corner scale `corner-none/extra-small/small/medium/large/extra-large/full` + directional (`corner-large-start/end/top`). PrimeNG: primitive `borderRadius.{none,xs,sm,md,lg,xl}` + per-area (`content.borderRadius`, `formField.borderRadius`) + per-component (`button.borderRadius`, `roundedBorderRadius`).

**Elevation / State / Motion / Focus** — Material has *systems*; PrimeNG has *scraps* (except focus):

| | Material | PrimeNG |
|---|---|---|
| Elevation | `level0…level5` ladder → shadows | none global; `overlay.*.shadow`, `button.raisedShadow` |
| State | 4 state-layer opacities (`hover .08, focus .12, pressed .12, dragged .16`) — *color × opacity overlay* model | no opacity tokens; enumerated per-state colors (`hoverBackground, activeBackground, hoverColor, hoverBorderColor…`) |
| Motion | duration ladder (`short1–4/medium1–4/long1–4/extra-long1–4`) + easing set (`emphasized/standard/legacy/linear …`) | one `transitionDuration` (+ `mask.transitionDuration`) |
| Focus ring | not tokenized (in component CSS) | **first-class `focusRing`**: `width/style/color/offset/shadow` (semantic + per component) |

The **state model** is the deepest divergence: Material = one color × a shared opacity overlay; PrimeNG = a distinct hard-coded color per state per severity (verbose but literal).

**Form fields** — *PrimeNG's biggest ERP advantage.* PrimeNG ships a **shared semantic `formField` group** (`paddingX, paddingY, sm, lg, borderRadius, focusRing, transitionDuration` + `background, disabledBackground, filledBackground, borderColor, hoverBorderColor, focusBorderColor, invalidBorderColor, color, disabledColor, placeholderColor, floatLabelColor, iconColor, shadow`) inherited by **every** input — one override restyles all inputs. Material has **no shared form-field group**; each control carries its own component tokens, so "restyle all inputs" means touching many components.

### A.3 — Summary

| Dimension | Material M3 | PrimeNG Aura |
|---|---|---|
| Exposed strata | 2 (system → component); palette hidden | 3 (primitive → semantic → component) |
| Raw palette as tokens | No | Yes (`--p-blue-500`, 22 ramps) |
| Global color roles | primary+secondary+tertiary+error, each `on/container/fixed` | primary only; rest are component severities |
| Typography | full scale (~75) | none global |
| Shape | global corner scale + directional | primitive radius + per-area/component |
| Elevation | level0–5 | per-component shadows |
| State | 4 state-layer opacities | enumerated per-state colors |
| Motion | durations + easings | one duration |
| Focus ring | not tokenized | first-class group |
| Form fields | per-component | **shared `formField` group** |
| Light/dark | separate built maps | in-tree `colorScheme.{light,dark}` (runtime) |
| Component shape | `base/color/typography/density`, per-variant duplicated | root + `colorScheme` × severity × variant |
| Authoring / emit | Sass maps → `--mat-sys-*` / `--mat-<comp>-*` | TS objects w/ `{ref}` → `--p-*` (`@layer`) |

**Genuinely shared:** primitive→semantic→component layering (Material hides the primitive tier); a brand "primary" + contrasting foreground; surface/text/border/overlay/highlight roles; radius + disabled + size tokens; per-component tokens that *reference* shared tokens by name; CSS custom properties as the runtime currency.

### A.4 — Synthesis for `@tellma/core-ui-tokens`

A blend, not a copy of either:
1. **Explicit 3-tier model** (PrimeNG): named `primitive` ramps → `semantic` `{ref}` aliases → `component`. The named primitive layer + string indirection is the most agent-legible and schema-validatable shape.
2. **Shared semantic groups** (PrimeNG): keep `formField` (critical for dense ERP forms) + `list/content/overlay/navigation`. Avoid Material's per-component form tokens.
3. **Backfill Material's depth where PrimeNG is thin:** ship a real **type scale**, an **elevation ladder**, and a **motion** set as tokens (coherence across distributions).
4. **Promote secondary/tertiary/severity to first-class *semantic* color roles** (Material's instinct) but named flat (`{severity.danger.color}`) so theming "the brand" edits one place.
5. **Explicit per-state colors** (PrimeNG) over Material's opacity-overlay math — no implicit math for an agent to get wrong — but as a *shared* state group, not Material's per-variant explosion (its button alone defines ~60 tokens across 5 duplicated variants).
6. **Light/dark as in-tree branches** (runtime switch) — but validate *both* branches for WCAG contrast at build (per D6).

Proposed contract skeleton (shape only; exact token lists are design-in-progress):

```ts
export interface TmTokens {
  primitive: {                 // raw, theme-agnostic
    color: Record<string, ColorRamp>;        // slate, blue, emerald… (50–950)
    radius: Scale; spacing: Scale; font: { family; size: Scale; weight: Scale };
  };
  semantic: {                  // role aliases referencing primitive via {ref}
    primary: ColorRamp & { color; contrastColor; hoverColor; activeColor };
    severity: Record<'secondary'|'info'|'success'|'warn'|'danger'|'contrast', SeverityRole>;
    typescale: Record<'display'|'headline'|'title'|'body'|'label', SizeSet>; // backfilled from M3
    elevation: Record<'level0'|'level1'|'level2'|'level3'|'level4'|'level5', Shadow>;
    motion: { duration: Scale; easing: Record<string, string> };
    focusRing: { width; style; color; offset; shadow };
    formField: FormFieldTokens;  // shared across every input
    list; content; overlay; navigation;
    colorScheme: { light: SchemeColors; dark: SchemeColors };  // validated for contrast at build
  };
  component: Record<string, ComponentTokens>;  // tm-button, tm-select… ref semantic
}
```

