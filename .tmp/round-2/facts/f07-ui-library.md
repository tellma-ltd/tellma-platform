# F07 — UI component library digest (specs 0002, 0004, 0005, 0006 + `client/projects/core` + memory notes)

Digest for the round-2 designers. Specs 0002/0004/0005/0006 are **frozen** (changes = amendments in
spec 0010). Section refs like "0004 §6.2". Code paths are relative to
`client/projects/core/` unless stated. Where the code and spec disagree, the code wins and the
difference is listed in §5.

## 1. Sources read

| Source | Lines |
|---|---|
| `docs/specs/0002-component-library-foundation.md` | 1351 |
| `docs/specs/0004-component-library-datagrid.md` | 1141 |
| `docs/specs/0005-component-library-basics.md` | 1226 |
| `docs/specs/0006-component-library-entity-picker.md` | 858 |
| `client/projects/core/**` (entry points, public-api files, API goldens, package.json budgets) | see §2.1 |
| memory: `client-workspace-phase1.md` (40), `datagrid-implementation.md` (96), `tree-grid-milestone.md` (33), `component-basics-implementation.md` (291), `entity-picker-implementation.md` (472), `entity-picker-spec-0006.md` (33), `design-alignment-implementation.md` (76), `aria-grid-v22-findings.md` (60) | 1101 |

## 2. Key facts

### 2.0 Foundation (spec 0002)

**Packages** (0002 §1, §12). All under `client/projects/core/` plus `client/projects/locale/`:
- `@tellma/core-ui` — components; every component is its own **secondary entry point**; the
  **primary EP is component-free by rule** (providers, i18n, fonts, forms infra only).
- `@tellma/core-ui-tokens` — `TmTokens` TS contract, default preset
  (`presets/tellma-default.ts`), tokens→CSS emitter, JSON Schema, schema + missing-ref gate.
- `@tellma/core-ui-testing` — CDK component harnesses (TestBed only; Playwright uses raw locators,
  0002 §10).
- `@tellma/core-ui-mcp` — plain Node package; generated `components.json`, MCP server with
  `list`/`describe`/`example`, `llms.txt` (0002 §11).
- `@tellma/locale-ar` — reference locale pack: Arabic library strings (Transloco scope via
  `provideTellmaLocaleAr()`) + Noto Sans Arabic woff2 + `@font-face` stylesheet (0002 §1, §7.1).
- Consumed through workspace path mappings, not published packages (0002 Simplifying assumptions).
- Library layout: package root IS the code root (no `src/`); EPs import shared code via
  `@tellma/core-ui`, never `../`; shared non-public machinery goes in a `private/` EP (0002 §12).

**Framework rules** (0002 §1): Angular v22, standalone, zoneless, OnPush default, signal APIs
(`input()`/`model()`/`output()`), `host` object (no `@HostBinding`), inline templates for small
components. `@angular/aria` and Signal Forms are stable in v22; one pinned Angular minor across
the repo. **No SSR** (one-way door: top-layer `[popover]`, `font-display: swap`, zoneless).
Lint: `tm-`/`Tm` prefix on selectors and exports; stylelint `tm-` class prefix; stylelint ban on
hardcoded px/rem/em sizing in component CSS (tokens only; `0`, hairline `1px` allowed); no bare
`outline: none`.

**Behaviour layer** (0002 §2): no per-component headless pattern class; `@angular/aria` is the
behaviour layer (listbox, combobox, menu, grid, tree); the headless-engine approach is reserved for
the grid (0004 built it).

**Contracts EP** `@tellma/core-ui/contracts` (0002 §2.1): `SignalLike<T>`, `WritableSignalLike<T>`,
`TmFormFieldControl` (controlId, ownsChrome, describedByIds, setDescribedByIds, setLabelId?,
required/disabled/readonly/touched/dirty/invalid/pending/errors, onContainerClick?), `TmFieldError
{kind, message}`, draft `TmCellEditor<T>` / `TmCellDisplay<T>` (hardened by 0004). Lint: contracts
may not import `@angular/core` or components.

**Phase-1 components** (0002 §3): `tm-form-field` (label, hint, `size` sm|md|lg, plain `error`
input for unbound use; separate hint element and persistent `aria-live="polite"` error element),
`input[tmInput]` (directive on native input; `dir="auto"`; chrome lives in `tm-form-field`;
adornments via `[tmPrefix]`/`[tmSuffix]` projection), `tm-checkbox` (`checked` model,
`indeterminate` via native IDL property; no `value` — `FormCheckboxControl`; future
`tm-checkbox-group` named but not built), `tm-select` + `tm-option` (single-select; `value` model,
`valueKey`, `displayWith`, `placeholder`; aria `ngCombobox` on a non-input host + `ngListbox`
activedescendant/explicit; CDK connected overlay `usePopover:'inline'`, `matchWidth`, lazily
created; commit on activation events never `valueChange`; typeahead from closed trigger;
multi-select, option groups, virtual scroll **not built**, "not precluded").
`tm-spinner` (`@tellma/core-ui/spinner`) decorative, `aria-hidden`, reduced-motion aware.

**Icons** (0002 §3.5): SVG only, no icon font; built-in glyphs are inline SVG in owning templates;
a future `tm-icon` + `TmIconRegistry` (Lucide default) "arrives with the first component that
accepts consumer-supplied icons" — see §2.9 for what shipped.

**Tokens & theming** (0002 §4): three tiers primitive → semantic → component; emitted to static CSS
custom properties at build time. Cascade layers: emitter writes `@layer tm.base, tm.theme;` at the
top of every sheet; library default (light and dark) in `tm.base`, distribution deltas in
`tm.theme`, runtime overrides as inline style (`setProperty`). **Dark mode = `[data-theme=dark]`
selector** (light = `:root`/`[data-theme=light]`). No theme-builder UI ever. Runtime theme
switching = setting `data-theme` / CSS vars, no rebuild. Density/type-scale runtime axes deferred
(must be addable as token sets).

**Forms** (0002 §5): Signal Forms only (no CVA). `[formField]` on the control; the control declares
optional `FormUiControl` inputs (`disabled`, `readonly`, `required`, `invalid`, `errors`,
`touched`, `pending`, …) and emits `touch` on blur. Error display default:
`invalid() && (touched() || dirty())`; `submit()` marks all descendants touched, so no `[tmForm]`
directive (v22 `form[formRoot]` exists). Authors must not template-bind
`disabled`/`readonly`/`required` on a bound control (NG8022 compile error in v22 — memory
client-workspace-phase1). Async validation debounce is the consumer's (`debounce()` in the
schema). Providers: `provideTellmaForms()` (display policy, message resolver, field defaults) and
`provideTellmaUi()` (umbrella: forms + Transloco-backed `TM_UI_TRANSLATE`).

**i18n** (0002 §5, §7): `TM_UI_TRANSLATE` = `(key, params?) => Signal<string>`; default backed by
Transloco; English ships in the core, all other languages are locale packs; fallback chain active
→ English (`fallbackLang: 'en'` + `useFallbackTranslation: true`) → raw kind + dev warn. ICU via
`@jsverse/transloco-messageformat`. Live locale switch re-renders visible errors. Distributions
set root `lang`; text inputs use `dir="auto"`. Direction from CDK `Directionality`, logical CSS
only, no per-component `rtl` flag.

**Fonts** (0002 §7.1, memory client-workspace-phase1): self-hosted woff2, `unicode-range`
subsetting, `font-display: swap`; core ships Noto Sans + Noto Sans Mono; Arabic ships in
locale-ar. `@font-face` sheets are plain `angular.json` `styles` entries; preloading is a
post-build script `scripts/inject-font-preloads.mjs` scanning emitted CSS; metric-adjusted local
fallback faces (`@capsizecss/metrics`) make the swap layout-shift free. `--font-ui` is ONE
multi-script stack; leading via `[lang]:lang(x)` rules at explicitly marked lang roots; typography
never keys on `dir`.

**Budgets** (0002 §8): per-EP gzipped ceilings in each package.json `"tellma".budgetsInKb`,
ratchets; baseline Angular+CDK excluded. Initial: primary ≤4, contracts ≤1, input ≤3, checkbox ≤4,
form-field ≤4, select ≤8, spinner ≤1, tokens runtime ≤8. (Current values in §2.1.)

**Testing & docs** (0002 §10, §11): harnesses per component; API Extractor goldens per EP +
`api:approve`/`api:check` gate; axe-core static floor + Playwright behavioural a11y specs
(forced-colors, reduced-motion via `emulateMedia`); full suite always runs. Docs pipeline:
co-located `*.examples.ts` + `*.md` → `components.json` (schemaVersion, components[] with name,
kind, group, selector, entryPoint, formControl, inputs, outputs, slots, tokens, a11y, examples,
harness, status) → showcase, `llms.txt`, MCP. Showcase app = `client/projects/internal/showcase`
(dev-only; story pages addressable by dir × theme query params; Playwright/axe target). Storybook
removed. Free-port tooling for parallel worktrees (0002 §1.3).

**Target size** (0002 §6): WCAG 2.2 AA 24×24 minimum (not 44); dense grid rows conform via the
24px minimum and spacing/essential exceptions.

### 2.1 Entry-point inventory (as implemented)

Installed: `@angular/core` 22.0.5, `@angular/cdk` 22.0.x, `@angular/aria` 22.0.3 (its EPs:
`accordion`, `combobox`, `grid`, `listbox`, `menu`, `tabs`, `toolbar`, `tree`, `private`),
`@jsverse/transloco` 8.4.0, `@internationalized/date` 3.12.2, TypeScript 6.0.3. Workspace is ESM,
pnpm 11 (config in `pnpm-workspace.yaml`). Projects in `client/angular.json`: `core-ui`,
`core-ui-tokens`, `core-ui-testing`, `locale-ar` (ng-packagr) and `showcase` (application).
**No distribution app project exists yet.**

Budgets = `client/projects/core/tellma-core-ui/package.json` `"tellma".budgetsInKb` (gzipped
self-weight; raises must carry a `"tellma".budgetNotes` entry). API goldens in `client/api/*.api.md`
(ɵ members and the `./private` EP excluded).

| Entry point | Main exports | Ceiling KB (spec) | Harness |
|---|---|---|---|
| `@tellma/core-ui` (primary, component-free) | `provideTellmaUi(options?: TmUiOptions {forms?, availableLangs?})`, `provideTellmaForms(options?: TmFormsOptions {errorDisplay?, formFieldDefaults?})`, `TM_UI_TRANSLATE`, `TmUiTranslateFn`, `TM_UI_I18N_SCOPE = "tmUi"`, `TM_UI_STRINGS_EN`, `tmEnglishString`, `tmDefaultUiTranslate`, `TM_UI_MESSAGE_CONTEXT` (default `{gender:'other'}`), `TM_ACTIVE_LOCALE`, `tmDefaultActiveLocale`, `TM_CALENDAR`, `provideTmCalendar`, `TmL10n`, `TmClientCache`, `TM_CELL_EDITOR_HOST`, `TM_ERROR_DISPLAY`/`tmDefaultErrorDisplay`/`TmErrorDisplayPolicy`/`TmErrorDisplayState`, `TM_FORM_FIELD_DEFAULTS`/`TmFormFieldDefaults {size, requiredMarker}`, `TmControlSize = 'sm'\|'md'\|'lg'`, `tmMinDate`/`tmMaxDate`, `tmResolveFieldErrors`, `tmErrorParams` | 5 (0002: 4; 0005: 5) | — |
| `/contracts` | `SignalLike`, `WritableSignalLike`, `TmFormFieldControl`, `TmFieldError`, `TmCellEditor`, `TmCellDisplay`, `TmCellEditorHost`, `TmCellEdit`, `TmRowId`, `TM_PARSE_ERROR`/`TmParseError`, `TmParseContext {locale, sourceLocale?, foreignSource?, sourceCalendar?}`, `TmPasteContext {+signal, sourceTenantId?, sourceDistributionKey?}`, `TmLabelResolution<V>`, grid state snapshot types (`TmGridColumnWidths`, `TmGridContentState`, `TmGridScrollPosition`, `TmGridSelectionSnapshot`, `TmGridRangeSnapshot`) | 1 | — |
| `/input` | `TmInput` (`input[tmInput]`, `textarea[tmInput]`) | 4 | `TmInputHarness` |
| `/checkbox` | `TmCheckbox` (`tm-checkbox`), `TM_CHECKBOX_CELL_DISPLAY` | 4 | `TmCheckboxHarness` |
| `/form-field` | `TmFormField` (`tm-form-field`: `label`, `hint`, `error`, `size`), `TM_FORM_FIELD_CONTROL` | 5 (4) | `TmFormFieldHarness` |
| `/select` | `TmSelect`, `TmOption` | 8 | `TmSelectHarness`, `TmOptionHarness` |
| `/spinner` | `TmSpinner` (no inputs; `--spinner-size` 14px) | 1 | — |
| `/button` | `TmButton` (`button[tmButton]`), `TmButtonVariant`, `TmButtonSize` | 2 | `TmButtonHarness` |
| `/number` | `TmNumber` (`input[tmNumber]`) | 4 | `TmNumberHarness` |
| `/l10n` | `tmFormatNumber`, `tmParseNumber`, `TmNumberFormatOptions`, `TmNumberParseOptions`, `TM_NUMBER_MAX_DIGITS = 15`, `tmNumberDigitCount`, `tmFormatDate`, `tmParseDate`, `tmDatePlaceholder`, `TmDateStyle`, `TmDateFormatOptions`, `TmDateParseOptions`, `TmCalendar`, `TmCalendarParts`, `tmGregorianCalendar`, `tmFirstDayOfWeek` | 8 | — |
| `/date-picker` | `TmDatePicker` (`tm-date-picker`) | 14 | `TmDatePickerHarness` |
| `/calendar-umalqura` | `tmUmalquraCalendar(): TmCalendar` | 5 | — |
| `/calendar-ethiopic` | `tmEthiopicCalendar(): TmCalendar` | 2 | — |
| `/image` | `TmImage` (`tm-image`), `TmImagePlaceholder`, `TmImageEdit`, `TmImageFit`, `TM_BLOB_FETCHER`, `TmBlobFetcher` | 10 | `TmImageHarness` |
| `/files` | `TmFilePicker` (`button[tmFilePicker]`), `TmDropzone` (`tm-dropzone`), `TmFileSelection`, `TmFileRejectionReason` | 5 | `TmFilePickerHarness`, `TmDropzoneHarness` |
| `/file-preview` | `TmFilePreview` service, `TmPreviewFile` | 8 | `TmFilePreviewHarness` |
| `/tabs` | `TmTabGroup`, `TmTab`, `TmTabContent`, `TmTabLabel`, `TmTabSelectionMode` | 4 | `TmTabGroupHarness`, `TmTabHarness` |
| `/modal` | `TmModal`, `TmModalRef`, `TmModalConfig`, `TmModalResult`, `TmModalSize`, `TM_MODAL_DATA`, `TmModalFooter` (`[tmModalFooter]`) | 6 | `TmModalHarness` |
| `/popover` | `TmPopover`, `TmPopoverContent`, `TmPopoverTrigger` (`button[tmPopoverTriggerFor]`), `TmPopoverAnchor` | 4 | `TmPopoverHarness` |
| `/tooltip` | `TmTooltip` (`[tmTooltip]`) | 3 | `TmTooltipHarness` |
| `/alert` | `TmAlert`, `TmAlertKind`, `TmAlertLive` | 2 | `TmAlertHarness` |
| `/menu` | `TmMenu`, `TmContextMenuTrigger` (`[tmContextMenuTrigger]`), `TmMenuItem`, `TmMenuEntry`, `TmMenuSeparator`, `TmMenuAnchor`, `TmMenuOpenOptions` | 6 | `TmMenuHarness`, `TmMenuItemHarness` |
| `/grid-engine` | headless engine: `TmGridEngine`, `TmGridNav`, `TmGridSelectionModel`, `TmGridEditState`, `TmGridClipboard`, `TmGridHistory`, `TmGridDataModel`, `TmGridCellAnnotations`, `TmGridInvalidInputReason`, `TmGridColumnType`, `tmComputeAxisWindow` (windowing helper), `tmSerializeTsv`, `tmSerializeHtmlTable`, `tmParseTsv`, … | 15 (12) | — |
| `/grid` | `TmGrid` (`tm-grid`), `TmGridColumn` (`tm-grid-column`), `TmGridDisplayDef`/`TmGridEditorDef`/`TmGridHeaderDef`/`TmGridEmptyDef`/`TmGridLoadingDef` (+ contexts), `TmGridColumnAlign`, `TM_GRID_CONTEXT`/`TmGridContext`, `TmGridStateStore`, `TmGridStateHandle` | 38 (0004: 24; 0005: 35; 0006: 37) | `TmGridHarness`, `TmGridCellHarness`, `TmGridRowHarness` |
| `/tree-grid` | `TmTreeGrid` (`tm-tree-grid`) | 8 | `TmTreeGridHarness` |
| `/entity-picker` | `TmEntityPicker` (`tm-entity-picker`), `TmEntityId`, `TmEntitySearchFn`, `TmEntitySearchResult`, `TmEntityPick`, `TmEntityPicked`, `TmEntityPickerPage`, `TmEntityPickerPageData` | 12 (0006 §10: 11) | `TmEntityPickerHarness`, `TmEntityPickerOptionHarness` |
| `/private` (no stability guarantee, no golden) | `tmCreateAnchoredOverlay`, `tmLogicalPositions`, `TmAnchoredOverlay*`, `TmOverlaySide = 'block-end'\|'block-start'\|'inline-start'\|'inline-end'`, `TmOverlayAlign = 'start'\|'center'\|'end'`, `tmPushEscapeDismissal`, `ɵTmErrorPopover` | 4 | — |
| `@tellma/core-ui-tokens` | `TmTokens`, `tmTokensDefault`, `tmEmitCss`, `tmEmittedSchemeVars`, `tmValidateTokens`, `tmResolveVar`, `tmRefToVarName`, ramp types | (8) | — |
| `@tellma/locale-ar` | `provideTellmaLocaleAr()`, `TM_LOCALE_AR_STRINGS`; `fonts/fonts.css` (Noto Sans Arabic variable woff2) | — | — |
| `@tellma/core-ui-mcp` | Node MCP server with tools `list`, `describe`, `example` over `components.json` | — | — |

**There is no** `tm-icon`/`TmIconRegistry`, toast, drawer, splitter, chip, tile, pivot, chart,
radio, switch, multi-select, accordion, toolbar, breadcrumb, pager, badge, avatar or skeleton
component (see §3).

### 2.2 Tokens, size ladder, themes, breakpoints

Source: `tellma-core-ui-tokens/presets/tellma-default.ts` (579 lines), `contract/tokens.ts`,
`emit/emit-css.ts`. Emitted CSS `css/tellma-default.css` is **generated and gitignored**
(`pnpm run tokens:build-css`). `pnpm run tokens:check` = zod schema + missing-ref + a
**dangling-var gate** (`tools/tokens/css-refs.mts`: every `var(--…)` a library stylesheet reads
must be an emitted name). Component tokens emit `--<component>-<key>` (kebab; two adjacent
capitals do not split: `paddingXSm` → `--…-padding-xsm`). Text-size tokens are `--text-xs`/
`--text-sm`, not `--font-size-*` (memory entity-picker-implementation).

- **Primitives**: radius xs 4 / sm 6 / md 10 / lg 16 / xl 24 / full 999; space 0,1=4,2=8,3=12,
  4=16,5=20,6=24,8=32,10=40,12=48,16=64,20=80,24=96 px; font size xs 12 / sm 14 / base 16 / lg 18;
  weights 400/500/600/700; leading tight 1.2, snug 1.35, body 1.6, arabic 1.9; shadow xs/sm/md/lg
  (overlays only); motion durationFast 120ms, normal 180ms, slow 280ms.
- **Semantic scheme roles** (light/dark): `text.{strong,body,secondary,muted,onDark,link}`,
  `surface.{page,subtle,sunken,card,inverse,hover,selected}`, `border.{subtle,default,strong,divider}`,
  `selection.{bg,text}`, `action.{primary(teal-600),primaryHover,primaryActive,onPrimary,accent(teal-400)}`,
  `status.{success,warning,error,info}.{fg,bg,border}`, `field.*`. Dark `surface.subtle` (#121E25)
  must differ from `surface.page` (#0D181E) or zebra/readonly/header tints vanish (memory
  datagrid-implementation). `--text-muted` fails axe AA on panels; `--text-secondary` is the quietest
  accessible ink (memory entity-picker-implementation).
- **Size ladder** (`semantic.formField`): height sm 30 / md 38 / lg 46 px; paddingX 10/12/14;
  fontSize 13/14/15; labelGap 4/6/7; optionHeight 26/30/34; labelFontSize 13 and hintFontSize 12 for
  every size. **Workspace default is `sm`** (`DEFAULT_SIZE = 'sm'` in `forms/provide-tellma-forms.ts`,
  overridable via `provideTellmaUi({forms:{formFieldDefaults:{size}}})`; the grid's `size` input
  defaults from `TM_FORM_FIELD_DEFAULTS` too). Buttons ride the field heights.
- **Grid tokens**: rowHeight md 34 / sm 30 / lg 38 (the brand sheet's 26/30/34 moved up one step
  "because 26 reads as cramped"), so **default grid rows are 30px**; header bg `surface.sunken`,
  rowHeaderWidth 34, checkColWidth 30, minColWidth 48, indent 16, cellFontSize 12, plain (readonly)
  cell font 12.5 and padding 11, headerFontSize 11.5, statusHeight 30, handleSize 24, selection fill
  `rgba(76,160,182,0.14)`, zebra `surface.subtle`, find-match `status.warning`.
- **Other component groups**: alert, button, checkbox (box 18, never on the ladder), datePicker
  (popupWidth 256, cellHeight 30), select (panelMaxHeight 280), entityPicker (panelMaxHeight 280,
  panelMinWidth 200), files, image, menu (minWidth 180, itemHeight 30), modal (widthSm 420,
  widthMd 640, margin 16, lgMargin 48), spinner (size 14, duration 700ms), preview, popover
  (padding 12, maxInlineSize 320), tabs (height 34, vertical 30), tooltip (delay 500ms,
  maxInlineSize 200), errorPopover (maxInlineSize 320).
- **Themes** (0002 §4, `emit-css.ts`): light vars on `:root`, dark under `[data-theme=dark]`, both
  in `@layer tm.base`; every emitted sheet starts with `@layer tm.base, tm.theme;`. **No
  `prefers-color-scheme` handling anywhere in the library** — "System" must be implemented by the app
  (matchMedia → `data-theme`). The showcase writes `data-theme`, `dir`, `lang` onto `<html>` from
  `?theme=`/`?dir=` query params and its language toggle.
- **Breakpoints: none defined.** No breakpoint tokens; the only width media query in the library is
  a raw `@media (max-width: 600px)` in `modal/internal/tm-modal-shell.css` (all modal sizes go
  near-full-screen; comment: custom properties don't resolve in media queries). The grid detects
  touch via `matchMedia('(pointer: coarse)')` (selection handles). Forced-colors (23 blocks) and
  `prefers-reduced-motion` (7) are honoured.
- Stylelint bans hardcoded sizing in component CSS (tokens only). Design-alignment pass
  (branch `component-library-touchup`, 2026-08-06, recorded in ARCHITECTURE.md, not in a spec)
  flipped defaults to `sm`, added focus-only error popovers, ruled/plain grid looks, Lucide glyphs.

### 2.3 Fonts and icons

- Core `tellma-core-ui/fonts/`: `fonts.css`, `noto-sans-latin-wght-normal.woff2`,
  `noto-sans-latin-ext-wght-normal.woff2`, `noto-sans-mono-latin-400/700-normal.woff2`, `OFL.txt`.
  locale-ar `fonts/`: `noto-sans-arabic-arabic-wght-normal.woff2`. `--font-ui` stack: `'Noto Sans'`,
  `'Noto Sans Arabic'`, `'Noto Sans Fallback'`, `'Noto Sans Arabic Fallback'`, system-ui, …
- What an app adds to `angular.json` `styles` (the showcase's list): tokens CSS
  (`tellma-core-ui-tokens/css/tellma-default.css`), `tellma-core-ui/fonts/fonts.css`, the three
  directive stylesheets `tellma-core-ui/styles/tm-button.css`, `tm-input.css`, `tm-number.css`
  (directives cannot carry component styles), `tellma-locale-ar/fonts/fonts.css`, then app styles.
  Post-build: `node scripts/inject-font-preloads.mjs dist/<app>/browser` (`build:showcase`).
- **Icons**: no icon component, registry or `lucide` npm dependency. Built-in glyphs are
  "Lucide-derived inline SVGs" (24 viewBox, 1.75 stroke, `currentColor`, `aria-hidden`) copied into
  each owning template (alert, checkbox, date-picker, entity-picker, file-preview, dropzone,
  form-field, grid find bar/status bar/menu icons in `grid/internal/icons.ts`, image, modal shell,
  error popover, select, spinner). Consumer icons today = `TemplateRef<void>` (`TmMenuItem.icon`) or
  projected `<svg>` (`tmButton`, `[tmPrefix]`). 0002 §3.5 and ARCHITECTURE.md: `tm-icon` +
  `TmIconRegistry` (Lucide default, sanitized/Trusted Types, swappable) "arrive with the first
  component that accepts consumer-supplied icons".

### 2.4 i18n and l10n plumbing (code + 0002 §5/§7, 0005 §2.2)

- `provideTellmaUi(options)` **itself calls `provideTransloco`** with `defaultLang:'en'`,
  `fallbackLang:'en'`, `availableLangs` (default `['en']`; must be non-empty or Transloco treats
  langs as scopes), `missingHandler {useFallbackTranslation:true, logMissingKey:false,
  allowEmpty:false}`, `reRenderOnLangChange:true`, and **no `loader`**; registers
  `ɵTmMessageFormatTranspiler` (ICU compiled against the formatting locale) and merges
  `TM_UI_STRINGS_EN` under `tmUi` into `en`. Locale packs `setTranslation({tmUi:…}, 'ar',
  {merge:true})` and extend `availableLangs` at runtime.
- `TM_UI_TRANSLATE: (key, params?) => Signal<string>` resolves synchronously against loaded
  translations via a shared version signal (no per-call subscription). Keys are relative to `tmUi`
  (`'errors.required'`, `'grid.menu.cut'`, `'entityPicker.noResults'`…). String groups in
  `TM_UI_STRINGS_EN`: `errors`, `datePicker`, `select`, `entityPicker` (+`announce`, `errors`),
  `grid` (`menu`, `op`, `announce`, `cellErrors`, `find`), `alert`, `modal`, `preview`,
  `filePicker`, `image`. EN↔AR recursive parity gate exists (memory component-basics).
- `TM_UI_MESSAGE_CONTEXT: Signal<Record<string, unknown>>`, default `{gender:'other'}` — ambient
  ICU params (Arabic imperatives conjugate by addressee gender).
- `TM_ACTIVE_LOCALE: Signal<string>` — default = live Transloco active lang (probe:
  `TRANSLOCO_TRANSPILER` present), else `LOCALE_ID`. Bare `ar` → Latin digits in ICU; the showcase
  overrides the token mapping `en→en-US`, `ar→ar-SA` (0005 §17). A distribution nominates regional
  locales the same way.
- `TM_CALENDAR: InjectionToken<Signal<TmCalendar>>` (default Gregorian), `provideTmCalendar(cal |
  Signal<cal>)`; calendars `tmGregorianCalendar()` (l10n), `tmUmalquraCalendar()`,
  `tmEthiopicCalendar()`; `TmCalendar {id, toParts, fromParts, monthsInYear, daysInMonth, today}`;
  ids `'gregory'`, `'islamic-umalqura'`, `'ethiopic'` (0005 §2.2).
- `TmL10n` (root service): `locale`, `calendar` signals, `formatNumber(value, options?)`,
  `formatDate(iso, options?)` — the reactive template facade; **no pipes** by decision (0005 §2.2).
- Formatting option set is a **closed cross-platform contract** (0005 §2.2): number = locale ×
  `minDecimals`/`maxDecimals`(default 20)/`percent`; date = locale × calendar × `dateStyle`
  (`numeric` yMd / `medium` yMMMd / `long` yMMMMd); era suffix stripped; half-away-from-zero;
  committed golden `format-golden.json` (`pnpm run golden:approve`) to be reused by the future C#
  implementation. **No time-of-day, date-time, relative-time ("5 min ago") or currency formatting
  exists.** Dates are pure `YYYY-MM-DD`; precision coarser than a day is out of scope (memory).
- **Direction**: CDK root `Directionality` reads `<body dir>`/`<html dir>` **once at
  construction** (verified in `@angular/cdk` 22 `_directionality-chunk.mjs`); a live LTR↔RTL switch
  needs a `[dir]` (CDK `Dir`) wrapper — the showcase wraps its outlet in `<div class="shell-main"
  [dir]="dir()">`. CDK `Overlay.create` defaults `direction` from the **injector's**
  `Directionality`; `TmModal` passes no `direction`, so a globally hosted dialog opened after a live
  switch likely takes the stale root value (inference from source; no test covers it).

### 2.5 Forms, `tm-form-field`, the error popover (0002 §3.1/§5 + code)

- Shipped `TmFormFieldControl` differs from 0002 §2.1: `localizedErrors` (not `errors`),
  optional `ownsErrorIcon?`, optional `setFieldError?(showsError)` (field pushes its displayed-error
  state to controls owning their invalid look: select, checkbox, pickers), `describedByIds` readonly
  array. Controls provide `TM_FORM_FIELD_CONTROL`.
- **Validation messages are a popover anchored to the control, shown only while it holds focus**
  (code doc of `TmFormField`; ARCHITECTURE.md "Forms"); out of focus the field keeps its invalid
  border and an in-field glyph; the hint stays visible; the messages live in a permanently rendered,
  visually hidden `aria-live="polite"` region in `aria-describedby`; the visible bubble
  (`tm-error-popover`, `data-tm-error-for`) is `aria-hidden` and `pointer-events:none` except on its
  text. The grid's active-cell error renders the same bubble. This replaces 0002 §3.1's hint/error
  rows (spec text not updated).
- Display policy `TM_ERROR_DISPLAY` default: `!pending && invalid && (touched || dirty)`.
  `tmResolveFieldErrors(errors, translate, formatDate?)` maps framework errors to
  `TmFieldError {kind, message}`; schema-inline `{message}` wins, else `tmUi.errors.<kind>`;
  date params are formatted in the field's locale/calendar (0005 §15).
- Signal Forms facts: `[formField]` binds state into control inputs; `[disabled]`/`[min]`/`[max]`
  template bindings on a `[formField]` host are compile errors (NG8022) → date picker uses
  `minDate`/`maxDate`. `submit()` marks descendants touched. `FieldState.errorSummary()` aggregates
  descendant errors; field nodes materialize lazily per level (memory aria-grid-v22-findings).
- Known gap (memory entity-picker-implementation, confirmed in code): standalone (no `[formField]`)
  `tm-date-picker` and `tmNumber` show the invalid mark for their own parse errors but do not merge
  the parse **message** into `localizedErrors` (the entity picker does) — flagged to Ahmad then.

### 2.6 Data grid — `tm-grid` (spec 0004 + code)

**Shape** (0004 Context, §1–§3): two components, `tm-grid` (flat, `role="grid"`) and
`tm-tree-grid` (`role="treegrid"`), both thin shells over `ɵTmGridBase` → `ɵTmGridCore` (4,940-line
`grid/internal/grid-core.ts`) → the headless `grid-engine` (no DOM, no DI; boundary-linted).
Readonly vs editable is a mode of the same instance. `@angular/aria` grid rejected (render-space
coordinates, per-cell boolean selection, no range gestures in 22.0.3/22.1.0-next.2, focus-restore
fights virtualization); it is a blueprint only (0004 §2, Decision #11).

**Inputs** (`ɵTmGridBase`, excluded from the golden; read from `grid/internal/grid-base.ts`):

| Input | Type / default | Notes |
|---|---|---|
| `gridId` | `string`, **required** | state-store key; two live grids with one id → dev throw |
| `contentKey` | `string \| number \| undefined` | per-content state key (0004 §12) |
| `data` | `readonly T[]` | readonly binding; exactly one of `data`/`field` (dev throw) |
| `field` | `FieldTree<T[]>` | editable binding (Signal Forms) |
| `rowId` | `(row) => TmRowId`, **required** | identity for selection/undo/state |
| `readonly` | `boolean` = false | editable ⇔ `field` bound ∧ `!readonly` |
| `newRow` | `(parent?: T) => T` | enables placeholder row + paste-overflow rows |
| `loading` | `boolean` = false | spinner overlay, headers kept, `aria-busy` |
| `searchable` | `boolean` = false | find bar (Mod+F while focused) |
| `selectable` | `boolean` = false | row checkbox column; **readonly grids only** (dev throw) |
| `selectedIds` | `model<ReadonlySet<string\|number>>` | two-way; every change a fresh Set |
| `extraMenuItems` | `readonly TmMenuItem[]` = [] | appended to the context menu |
| `size` | `TmControlSize` = `TM_FORM_FIELD_DEFAULTS.size` (`sm`) | row density |

Public members: `errorCount: Signal<number>`, `pendingCount: Signal<number>`,
`applyTransaction(edits: readonly TmCellEdit[], opts?: {label?})`, `clearHistory()`, `focus()`.
Content children: `tm-grid-column` (display order), `[tmGridEmpty]`, `[tmGridLoading]`.
**The grid has no outputs** (no row-activate, sort, scroll, expansion, active-cell or
selection-change event) and no public active-cell/active-row signal or setter.

**`tm-grid-column` inputs** (golden): `key`, `type` (`'text'|'number'|'boolean'|'date'|'enum'|
'entity'|'custom'`), `header` (string) or `*tmGridHeader` template (context `{$implicit: header
string}` only), `value` (accessor ⇒ readonly), `format(value,row)`, `parse(text, ctx)`,
`defaultValue` (cleared value; `false` for boolean, else `null`), `options`/`optionLabel`/
`optionValue` (enum), `search`/`itemId`/`itemLabel`/`advancedSearch`/`create`/`edit` (entity,
0006 §7.1), `resolvePastedLabels(labels, ctx) => Promise<ReadonlyMap<string, TmLabelResolution>>`,
`readonly` (bool or `(row)=>bool`), `width` (px) | `flex` (fr share, emitted
`minmax(minWidth, <flex>fr)`) + `minWidth`, `align` (`start|end|center|left|right`; defaults
number/date → `right` physical, boolean → `center`, else `start`), `minDecimals`/`maxDecimals`,
`hierarchy` (tree column). Templates: `*tmGridDisplay` (context `{$implicit: value, row, rowId,
invalid, readonly}`; the costlier per-cell embedded view), `*tmGridEditor` (context `{$implicit,
row}`), `*tmGridHeader`. Template context guards type them.

**Rendering & virtualization** (0004 §4, §7):
- Div-based, each row `display: grid` with `grid-template-columns: var(--grid-template)`; one scroll
  container owns both axes; header row (incl. corner) `position: sticky; top: 0` — **frozen
  header**; row-header (numbers) column is *not* sticky; no column (horizontal) virtualization.
- Bespoke fixed-row-height windowing (not CDK viewport): spacer `rowCount × rowHeight`, window =
  visible ± 4 overscan, one translate; `@for` tracks by `rowId`; the **active row is always
  rendered** (focus/editor survive scroll-out). DoD target 100k × 30. Re-windows only on a real
  `scroll` event (memory).
- Cells are static DOM (`format` text or boolean glyph); exactly one live editor grid-wide.
- Single-line cells, `text-overflow: ellipsis`, fixed row height (no user row resize).
- Widths: fixed px or proportional flex; **widths never depend on data** (no auto-fit), so column
  widths are stable while data loads. Resize by a ≥24px handle on the header's trailing edge;
  drag first freezes every still-proportional column at rendered px (memory: keeps the drag local).
  No keyboard resize (known limitation, 0004 §14).
- The host is `display:block; inline-size:100%`; the view is `display:flex; block-size:100%`, the
  scroller `overflow:auto; flex:1 1 auto` → the consumer must give the grid a definite height to
  get an internal scrollbar. **No `scrollbar-gutter` is set anywhere in the library**, so the
  vertical scrollbar appearing when rows arrive narrows the content box and re-flows `flex`
  columns.
- Looks: readonly = "plain" (no vertical rules, **zebra striping** in `tm-grid`; tree grid keeps a
  uniform background); editable = "ruled" (all gridlines, no zebra) — rule colour goes transparent,
  never the border, so switching never shifts a pixel (code comment, grid-view.css).
- States (0004 §5.4): `loading` → `tm-spinner` overlay + localized string, headers kept,
  `aria-busy`; zero-row readonly → centered localized empty message; overrides `*tmGridLoading`/
  `*tmGridEmpty`; transitions announced (`grid.announce.loaded`/`loading`).

**Data model** (0004 §5): consumer owns the array; in-place changes reconcile by `rowId`
(selection remaps, active cell follows its row, open editor cancelled with announcement only if its
row vanished, undo entries apply by id or skip with announcement). Flipping `readonly` never
mutates data (open editor cancels). New-row placeholder (§5.5): one `*` row after the data,
not in the model, materializes on typing/paste/commit via `newRow()`; `canAddRows ⇔ newRow`.

**Built-in editors** (0004 §6.2 as amended by 0005 §5/§6.6 and 0006 §7): `text` → bare `tmInput`;
`number` → `tmNumber` (grid owns parse; commits/pastes round to `maxDecimals`; 15-digit envelope →
invalid-input reason `'precision'`); `date` → `tm-date-picker` (built-in format/parse defaults with
`TM_CALENDAR`; pick = commit, no move); `boolean` → no editor, cell toggles on Space/click/Enter
(glyph from `TM_CHECKBOX_CELL_DISPLAY`); `enum` → `tm-select` anchored to the cell; `entity` →
`tm-entity-picker` when `search` is set (else consumer `*tmGridEditor`, else dev error unless
readonly); `custom` → editable only with `parse` or `*tmGridEditor`. Editors self-register through
`TM_CELL_EDITOR_HOST` (primary EP). Hardened contracts (0004 §6.3): `TmCellEditor<T> {value:
WritableSignalLike<T>, text: SignalLike<string|null>, commit(), cancel(), focus(), seed?(text)}`
(`onKeydown` dropped); `TmCellDisplay<T> {formatValue(value, locale), displayClass?(value)}`.
Display locale and calendar are reactive (0005 §6.6).

**Selection** (0004 §8.1): engine-held, view-space `TmGridRange {anchor, focus, kind:
'cells'|'rows'|'cols'|'all'}`, multiple ranges (Mod+Click/Drag), O(ranges). Headers highlight on
intersection. Expand/collapse collapses selection to the active cell.

**Row checkbox selection** (0004 §8.8): `selectable` (readonly only) adds a checkbox column at
inline-start (after row header) with a tri-state select-all header; Shift+click range-checks
(Gmail); Space toggles the active row; Ctrl+Shift+Space toggles select-all; the column is outside
the cell coordinate space (no ranges/copy/find/arrow stops); checked rows get row `aria-selected`;
announcement "N of M selected"; tree parents don't cascade. **Code behaviour that matters for
cross-page selection**: the tri-state counts only ids present in the current `data`
(`checkAllState`), and toggling select-all sets `selectedIds` to `new Set()` (from `all`) or to a
fresh set of **only the current rows' ids** (from `none`/`mixed`) — ids of rows not in `data` are
dropped (`toggleSelectAllCheckbox`, grid-core.ts). Row toggles preserve foreign ids.

**Keyboard** (0004 §8.2; Mod = Ctrl, ⌘ on macOS): arrows (direction-mapped), Mod+Arrow data-edge
jump, PageUp/Down, Home/End, Mod+Home/End, Shift+motion extends, Shift+Space rows, Ctrl+Space
columns, Mod+A all, Tab/Shift+Tab (editable: next editable cell wrapping, exits after last;
**readonly: single tab stop, Tab exits**), Enter (editable: open editor; boolean toggles;
**readonly: activates the first interactive element in the ACTIVE CELL**
(`a[href], button, input, select, textarea, [tabindex]`), else moves down), Shift+Enter up, F2,
printable → enter mode seeded, Delete/Backspace clear, Esc (clears marquee → else focus to the grid
container; Tab then leaves), Menu/Shift+F10 context menu, Mod+F find (only when `searchable`;
shadows browser find while focused), Mod+C/X/V, Mod+D fill down, Mod+Z/Y/Shift+Z, Mod+Alt+Minus
delete rows, Mod+Alt+Plus insert above, Alt+ArrowDown opens enum/entity/date dropdown (editor +
dropdown in one press), tree Alt+ArrowRight/Left expand/collapse. Editing mode: Enter/Shift+Enter
commit+move (Tab-run origin return), Tab commits+moves without opening the next editor, Esc cancels
(two-stage with dropdowns), arrows commit+move in enter mode, IME composition opens unseeded.

**Mouse/touch** (0004 §8.3, §8.6): click = activate + select the cell (commits an open editor);
drag = range (pointer capture, edge auto-scroll); Shift/Mod+click; header clicks select rows/cols
(not when the press lands on interactive projected header content); corner = all; double-click =
edit; right-click = select then context menu; touch: tap select, double-tap edit, long-press menu,
pan scrolls, coarse-pointer selection handles. **There is no row-click activation**: the sanctioned
list-screen pattern is consumer links in a `*tmGridDisplay` first column (native href
affordances) plus the checkbox column (0004 §8.8, Decision #18; `selectionUnit: 'row'` rejected).

**Context menu** (0004 §8.5): `tm-menu` built-ins Cut, Copy, Copy with headers, Paste (async
Clipboard API; falls back to "Press {shortcut} to paste"), Insert N row(s) above/below, Delete N
row(s), tree: Insert child row; plus `extraMenuItems`.

**Find** (0004 §8.7): floating bar at top inline-end; case-insensitive substring over each cell's
text representation (the `format` output), chunked; counter "3 of 41"; Enter/Shift+Enter cycle and
activate; tree scan includes collapsed rows and expands ancestors.

**Clipboard** (0004 §9): native ClipboardEvents; TSV + HTML (`data-tm-grid` JSON meta
`{v, tenantId, distributionKey, locale, cols, headers?, calendar}` + `data-tm-v`/`data-tm-h`
per cell + `data-tm-rowid` for full rows); in-memory fingerprint LRU fast path; paste ladder
(fast path → Tellma HTML → foreign HTML → TSV); header-row detection; fill/tile/overflow; one undo
op; full-row cut/paste in the same grid = row move (the sanctioned replacement for drag-and-drop).
Cross-tenant guard = `(distributionKey, tenantId)` pair from `TM_GRID_CONTEXT`
(`{tenantId: Signal<string|undefined>, distributionKey: string|undefined}`; default both
`undefined` → guard off). Async label→value resolution: one batched `resolvePastedLabels` per
column per paste, `ctx.signal` aborts, pending spinner per cell, `pendingCount`; suggested backend
shape `POST /resolve-labels { collection, labels[] }` (0004 §9.4.4 — a suggestion only).

**Errors** (0004 §10): field-invalid (Signal Forms) or invalid input (raw text kept, model cleared
to the column's cleared value; reasons `'parse'|'precision'|'notFound'|'ambiguous'|
'resolutionFailed'`); error tint; active errored cell shows the shared error bubble (top-layer, no
layout shift); status bar (editable only, fixed 30px) with tally chip + prev/next jump + pending
spinner.

**Undo/redo** (0004 §11): 100 ops; data ops only (not widths/expansion/scroll); restores
selection and scrolls; `applyTransaction` joins the stack; `contentKey` change resets.

**State memory** (0004 §12; `TmGridStateStore`, root, **in-memory only**): widths per `gridId`
(LRU 200), scroll / active cell + selection / undo / tree expansion per `gridId + contentKey`
(LRU 50). `serializeWidths(gridId): string|null` / `restoreWidths(gridId, blob)` let an app
persist widths; "the library itself never touches storage". `TmGridStateHandle`
(`register(gridId, contentKey)` → `getWidths/setWidths/getContentState/setContentState/
switchContent/clearHistory/release`) is public. Restores clamp (scroll to extent, active cell by
rowId else index, selection only if all endpoints resolve).

**A11y/RTL/perf** (0004 §14–§16): roles + `aria-rowcount`/`aria-colcount` (model counts),
`aria-rowindex`/`aria-colindex`, roving tabindex with real DOM focus; `LiveAnnouncer`
announcements (selection, paste, undo, rows, resolution, copy failure, lazy-load failure, error
jump); arrows direction-mapped; number/date columns stay physically right-aligned in RTL; perf
targets (scroll no long task > 50 ms; keydown→paint ≤ 16 ms) — the CI timing gate was **not**
built (structural half only, memory). Editable binding is for entry-scale data (hundreds to low
thousands of rows); 100k rows = readonly `data` path (Signal Forms cost model, 0004 §16).
**No `aria-sort`** anywhere (no sorting exists).

**Explicit non-goals** (0004 Non-goals): column re-ordering, pinning, hiding, auto-fit UI; row
drag-and-drop; **sorting, filtering, grouping, aggregation UI ("the consumer owns the array")**;
horizontal virtualization; fill handle; merged cells; selection statistics; CSV/XLSX export;
printing; server-side/infinite row models; **pivot table** (a separate future component with its
own engine; only the clipboard serializer and `tmComputeAxisWindow` are written shape-neutral).

### 2.7 Tree grid — `tm-tree-grid` (0004 §13 + code)

- Extra inputs: `parentId: (row) => string|number|null` (**required**), `parentIdKey?: string`
  (model property for editable re-parenting), `hasChildren?: (row) => boolean` (lazy marker),
  `loadChildren?: (row) => Promise<void>`, `defaultExpandedDepth?: number` (`undefined` = fully
  expanded). Flat adjacency list; orphans and cycles become roots with dev warnings.
- Hierarchy renders in the first column (or `hierarchy` column): indent `level × --grid-indent`
  + expander (`tabindex="-1"`, pointer only; keyboard = Alt+Arrows). Collapsing an ancestor of the
  active cell moves activation to it.
- **Lazy children**: on expand the grid calls `loadChildren(row)` (one call per node), shows a
  spinner in reserved space beside the expander (no layout shift; expander stays interactive);
  **the consumer appends the fetched rows to its own array**; rejection restores collapsed state +
  announces `grid.announce.lazyLoadFailed`. Implementation: the engine seeds lazy `hasChildren`
  nodes expanded at default depth, so `ɵTmGridCore.collapseUnloadedLazyRows()` runs after every
  seed/restore (memory tree-grid-milestone).
- **Expansion state** lives in the engine + `TmGridStateStore` (in-memory, per gridId+contentKey);
  there is **no public expanded-ids model/output, no expansion serialization, and no "start with
  these ids expanded" input** — only `defaultExpandedDepth`.
- Tree rows carry `aria-level`, `aria-expanded`, `aria-posinset`, `aria-setsize`. Insert child row
  via the menu (`newRow(parentRow)`); one root-level placeholder; tree paste overflow makes
  siblings; subtree row moves with descendant-target rejection.
- **No per-row styling hook** (no `rowClass`), so "ancestor shown only because a descendant
  matched" cannot be styled distinctly except via `*tmGridDisplay` templates per cell.

### 2.8 Menu — `tm-menu` (0004 §8.5 + code)

- Data-driven: `items: readonly TmMenuEntry[]` where `TmMenuItem {id, label?, labelKey?,
  icon?: TemplateRef<void>, shortcut?: string (display only), disabled?, action()}` and
  `TmMenuSeparator {separator: true}`; `ariaLabel`; `open(anchor: Element|DOMRect|{x,y},
  options?: {restoreFocus?: HTMLElement})`, `close({restoreFocus?})`, `isOpen`, outputs `opened`,
  `closed`, `itemSelected`. `[tmContextMenuTrigger]="menu"` (+`…Disabled`) opens at the pointer on
  right-click / Menu key / long-press.
- Built on `@angular/aria/menu` (roles, roving focus, typeahead via `searchTerm`) with a
  **parentless `ngMenu`**; `TmMenu` intercepts Escape at document capture (aria consumes it with a
  no-op handler) through the shared `tmPushEscapeDismissal` stack (memory).
- **Missing**: a button trigger directive (no `tmMenuTriggerFor`; buttons call `open(el)`),
  submenus (aria supports them; "deferred until a consumer needs one"), group headings/sections,
  checkable/radio items, link (`href`) items, rich item templates, per-item descriptions.

### 2.9 Basics (spec 0005 + code)

**Shared overlay helper** (0005 §2.1; `@tellma/core-ui/private`): `tmCreateAnchoredOverlay(config)`
centralizes config + measurement (consumer keeps its own `<ng-template cdkConnectedOverlay>` and
open state): lazy creation, `usePopover: 'inline'` (native top-layer, escapes `overflow:hidden`)
with `{type:'parent', element}` override where sibling insertion breaks ARIA, `disableClose: true`
(consumer owns Esc), logical position sets via `Directionality`, `matchWidth` opt-in, keep-on-screen
push, re-measure `'macrotask'|'afterNextRender'|'none'`; returned handle exposes `overlayConfig()`,
`handleAttach/Detach/OutsideClick`, `reanchor()`. Consumers: select, menu, grid error overlay,
popover, tooltip, date-picker, entity-picker, form-field error bubble. `tmPushEscapeDismissal(onEscape)
=> pop` is ONE document-capture Escape stack shared by menu/tooltip/popover etc. (separate
same-node capture listeners double-dismissed — memory component-basics M18).

**`TmClientCache`** (0005 §2.3, primary): `clearAll(): Promise<void>` — best-effort, never throws;
**implementation deletes only CacheStorage keys starting `tm-`** (`caches.keys()`); IndexedDB
databases and web-storage keys are promised "the day a feature first uses them". Rule: every
library browser store is `tm-`-prefixed; distributions call it on logout; **tenant switching clears
nothing** (keys carry tenant identity because the tenant id is in every API URL).

**`tmButton`** (0005 §3): `button[tmButton]`; `variant: 'primary'|'secondary'|'ghost'|'danger'`
(default `secondary`); `size` (default workspace `sm`); `pending` (aria-busy, swallows activation,
keeps exact size, overlays `tm-spinner`, not `disabled`); `type` defaults to `"button"`; icon-only
needs an accessible name (dev warning). `a[tmButton]` deferred (non-goal).

**Textarea** (0005 §4): `textarea[tmInput]`, fixed `rows`, `resize: none` (size-stability rule; no
autosize).

**`tmNumber`** (0005 §5): `input[tmNumber]`, `value: model<number|null>`, `minDecimals` (0),
`maxDecimals` (20), `percent`, `min`/`max`, `placeholder`; `type="text"` + `inputmode="decimal"`;
physical `text-align: right`; focus never rewrites text; blur reformats; commit rounds to
`maxDecimals` (model = display; programmatic writes untouched); 15-significant-digit envelope
(kind `numberPrecision`); unparseable → kind `parse`, model `null`. No currency (non-goal; future
string-backed control).

**`tm-date-picker`** (0005 §6): `value: model<string|null>` ISO `YYYY-MM-DD` (bounds
`0001-01-01`–`9999-12-31`; no `Date` in API); inputs `minDate`, `maxDate` (ISO), `calendar`
(per-instance override of `TM_CALENDAR`), `dateStyle` (`'numeric'` default), `placeholder`
(default `tmDatePlaceholder()`), `ariaLabel`; output `picked` (day/Today/Clear picks, not typing);
methods `openPopup()`, `isPopupOpen()`. Calendar button `tabindex="-1"`; keyboard
Alt+ArrowDown (and plain ArrowDown standalone); input role combobox (axe forced it). Free-text
parse (0005 §6.3): ISO fast path, locale field order, active-calendar interpretation, month names,
completion from today, 2-digit pivot [today−80, today+19], strict rejection. Popup: non-modal
`role="dialog"`, day/month/year views, Today/Clear footer, fixed size in every month/view (Ahmad
reversed "size to content", memory round 4), `@defer`-loaded views, `rendered` → `reanchor()`.
Validators `tmMinDate`/`tmMaxDate` (primary `forms/`). **No date-range, time, date-time picker**
(non-goal).

**Calendars** (0005 §6.5): Umm al-Qura via `@internationalized/date` `IslamicUmalquraCalendar`,
accurate window AH 1300–1599 (degrades silently to arithmetic Islamic outside; upstream table has a
1600 discontinuity); Ethiopic is self-implemented (upstream `EthiopicCalendar` maps two Gregorian
days to Pagume 5 in non-leap years; anchor 2023-09-12 = 1 Meskerem 2016; 13 months). Month/era names
from Intl at runtime; calendar EPs ship no language data, locale packs no calendar data. Backend
caveats: .NET has no Ethiopic calendar; .NET `UmAlQuraCalendar` covers only 1900–2077 CE.
Note: current CLDR puts SA/YE week start on **Sunday** (memory).

**`tm-image`** (0005 §7): inputs `src` (base URL), `etag`, `alt`, `shape: 'rect'|'circle'`,
`width`/`height` (fixed px box, zero CLS), `srcForSize` (default appends `?size=<bucket>`; buckets
64/128/256/512/1024/2048 by `max(w,h) × min(dpr,2)`), `defer` (IO, 200px margin, default true),
`editSrc`, `mode: 'view'|'edit'`, `maxFileBytes` (20 MB), `maxEdgePx` (4096); output
`imageChange: TmImageEdit {blob|null, fit {rect, focal}} | null`; placeholder
`ng-template[tmImagePlaceholder]`. Network through `TM_BLOB_FETCHER` (default `HttpClient`, so
interceptors/BFF cookies apply; requires `provideHttpClient()`). Blob cache = CacheStorage
`tm-images-v1`, key = full sized URL, etag compare, `If-None-Match` revalidation when `etag` is
null, per-tab in-flight coalescing, quota → delete cache and continue uncached. `shape:'circle'` is
the closest thing to an avatar.

**Files** (0005 §8): `button[tmFilePicker]` (`accept`, `multiple`, `maxFileSize` 100 MB,
`maxFiles`; `filesSelected: TmFileSelection {accepted: File[], rejected: {file, reason:
'size'|'type'|'count'|'folder'}[]}`; `open()`), `tm-dropzone` (host-directive re-exposure; focusable
`role="button"`, paste of files, document-level missed-drop guard). Transport guidance only
(multipart JSON+binary, or staged upload with TTL/GC). `TmFilePreview.open(file: TmPreviewFile
{name, type?, size?, source: Blob | (signal)=>Promise<Blob> | {url}}): TmModalRef<void>` — per-kind
rendering (raster/SVG via `<img>`, PDF via non-sandboxed iframe only for bytes it re-typed, media
with `canPlayType`, text ≤1 MB, HTML/unknown never rendered).

**Tabs** (0005 §9): `tm-tab-group` (`selectedId: model<string|undefined>` default first enabled,
`selectionMode: 'follow'|'explicit'` default `follow`, `orientation: 'horizontal'|'vertical'`),
`tm-tab` (`id` required, `label` or `ng-template[tmTabLabel]`, `disabled`, `preserveContent`),
content **must** be `ng-template[tmTabContent]`; only the active panel is in the DOM (destroyed on
deactivate unless `preserveContent`, then hidden + `inert`); strip scrolls on overflow (no
pagination buttons); **router-driven tabs out of scope** (pure UI container). Built on
`@angular/aria/tabs` with the definition-directive pattern (aria directives cannot be projected
across DI boundaries — NG0201).

**Modal** (0005 §10): `TmModal.open<R>(content: Type|TemplateRef, config?: TmModalConfig {size?:
'sm'|'md'|'lg' (default md; sm 420px, md 640px, lg = viewport − 48px each side), panelClass?,
title?, data? (→ `TM_MODAL_DATA`), showClose? (true), backdropDismiss? (true), escapeDismiss?
(true), canDismiss?: () => boolean|Promise<boolean>}): TmModalRef<R>`; `TmModalRef.close(value?)`,
`closed: Promise<TmModalResult<R>>` (`{via:'api', value}` | `{via:'close-button'|'backdrop'|
'escape'}`); `[tmModalFooter]` projection; `--flush` panel variant (internal). On
`@angular/cdk/dialog` (`disableClose: true`, every path through `TmModalRef`): focus trap +
restore, stacking (topmost-only dismissal), top-layer dropdowns paint above modals. Never resizes
while open; ≤600px viewport → near-full-screen. **No drawer/side-sheet variant, no confirm/alert
helper, no blocking full-page busy overlay.**

**Popover** (0005 §11): `tm-popover` (`position: TmOverlaySide` default `block-end`, `align:
TmOverlayAlign` default `start`, `ariaLabel`; `open(anchor: Element|DOMRect)`, `close()`,
`isOpen`, `opened`/`closed`) + `button[tmPopoverTriggerFor]` (`popover` input); content must be
`ng-template[tmPopoverContent]` (lazy, destroyed on close); non-modal `role="dialog"`, focus moves
to the first tabbable on open and back to the trigger on close; Esc/outside click/Tab-out close;
flips; `--popover-max-inline-size` 320px; no width/size input. Focusout close decides by
`relatedTarget` (memory).

**Tooltip** (0005 §12): `[tmTooltip]="text"` plain text only; hover after 500 ms, immediate on
`:focus-visible`, long-press on touch; hoverable surface; Esc; `AriaDescriber`; one tooltip at a
time; does not show on natively `disabled` buttons (documented limitation).

**Alert** (0005 §13): `tm-alert` `kind: 'info'|'success'|'warning'|'error'` (required), `live:
'off'|'polite'|'assertive'` (default `off`; polite → `role="status"`, assertive → `role="alert"`,
empty-then-populate), `heading`; projected content; visually-hidden kind prefix; **no dismiss
affordance** (visibility is the consumer's); for page/section messaging, not field errors.

**0005 non-goals that round 2 hits**: toast/snackbar, drawer/side panel, accordion,
wizard/stepper, segmented date editing, week numbers, in-picker calendar switcher, currency input,
time/date-time/date-range pickers, `a[tmButton]`, textarea autosize, upload transport/progress.

### 2.10 Entity picker — `tm-entity-picker` (spec 0006 + code)

0006 was revised once after implementation to fold the deviations into the body (memory
entity-picker-implementation), so its text describes what shipped.

- **Data seam** (0006 §2): `value: model<Id|null>(null)` (FK id; `Id extends string|number`),
  `search: TmEntitySearchFn<T> = (query, signal: AbortSignal) => T[] | {items, hasMore?} |
  Promise<…>` (required), `itemId: (item) => Id` (required), `itemLabel: (item) => string`
  (required, reactive), `displayWith?: (id) => string|null` (reactive; committed-id label),
  `advancedSearch?`/`create?`/`edit?: TmEntityPickerPage` (`Type` or `{component, size?, title?}`),
  `createLabel?`, `editLabel?`, `placeholder` (''), `searchDebounce` (50 ms), `ariaLabel`,
  `ariaDescribedby`; output `picked: TmEntityPicked {id, label, item?, source: 'list'|'auto'|
  'advanced'|'create'|'edit'}` and `touch`; methods `openDropdown()`, `isDropdownOpen()`,
  `focus()`, `seed()`, `commit()`, `cancel()`. Signal-Forms state inputs aliased:
  `fieldPending`/`fieldInvalid`/`fieldTouched` bind as `pending`/`invalid`/`touched`
  (`pending = fieldPending || resolving`).
- **Labels**: the library knows nothing about `Name/Name2/Name3`; a distribution writes one
  reactive `getMultilingualLabel(entity)` closure (Decision #1). Committed text resolution:
  `displayWith(id)` → memo of the pick's label (frozen; no locale re-render) → `String(id)` + dev
  warning. Cache warming = the consumer fills its own store from `picked` (`item` included for list
  and modal picks).
- **No entity cache in the library.** The picker holds exactly **one** settled answer (for the text
  currently in the box, keyed also by the `search` function identity), retired by any content write;
  re-opening on unchanged text re-applies it without a request (0006 §4.2). Non-goals: cross-query
  result cache, recents/MRU (express via `search('')`), client filtering/re-ranking, min query
  length, virtual scroll (dev warning > 200 results), multi-select, rich option templates, match
  highlighting, clear button, standalone delete.
- **Search lifecycle** (0006 §4.2): opens on typing, pointer click on the input, Alt+ArrowDown,
  plain ArrowDown/Up standalone only; pristine text (empty or the committed label) queries `''`
  (browse); debounce = **sliding** 50 ms leading+trailing (the shipped first version was a throttle;
  fixed — memory); every change aborts the in-flight request and clears stale rows immediately
  (immediate spinner; deferred-spinner grace rejected by Ahmad); sync results skip debounce and
  spinner; fixed-height status area (spinner / "No results" / "Search failed") damps panel churn;
  `hasMore` → aria-hidden truncation hint row + "N+ results" announcement.
- **Panel** (0006 §4.1): listbox options (single line, ellipsized, check on the committed id), hint,
  status, footer rows Advanced search… / Create… / Edit… as real `role="option"` rows (hidden while
  loading; Edit… only while the text names an entity); single logical position decided once from
  anchor geometry + max-height clamp (fixes the deferred-content flip flash); anchor = the
  `tm-form-field` box / grid cell / host; `matchWidth` + `--entity-picker-panel-min-width` 200px.
- **Keyboard** (0006 §4.3): typed query auto-highlights the first result; pristine browse
  highlights nothing; Enter commits highlight / opens footer modal / requests resolution while
  loading; Tab commits highlight and lets focus move; Space always types; Esc closes (text intact);
  ArrowUp from none → last option; Home/End move the caret (capture-layer override of aria);
  list scrolls to follow the highlight.
- **Resolution** (0006 §5.2): blur/Enter on unresolved text awaits the in-flight search; exactly
  one result → auto-pick (`source:'auto'`); zero/many/failure → keep text, model `null`, kind
  `parse` error ("No match for ‘{text}’" / "‘{text}’ matches more than one item" / "Search
  failed"); field invalid from blur so a racing submit is blocked; transient "unresolved" error
  never displayed mid-typing.
- **Modals** (0006 §6): launched via `TmModal` (advanced `lg`, create/edit `md`) with
  `TmEntityPickerPageData {query (pristine-mapped to ''), id? (edit)}`; page closes through
  `TmModalRef<TmEntityPick | null>`: `close({id,label,item?})` applies; `close(null)` (edit only) =
  entity gone → clear; dismissal = no change. Pages themselves are consumer-supplied.
- **Grid** (0006 §7): built-in `entity` editor when the column has `search` + `itemId` +
  `itemLabel`; column `format` doubles as `displayWith`; typed-commit resolution adopts the
  editor's own search (`ɵadoptSearch`) then falls to `resolvePastedLabels` per the §7.4 table (at
  most one resolver call); pending state is the grid's `pendingCount`.
- Tokens `--entity-picker-panel-max-height` 280px, `--entity-picker-option-height`,
  `--entity-picker-panel-min-width` 200px. Budget 12 KB (raised for the in-field invalid glyph).

### 2.11 Testing, showcase, docs, gates

- **Harnesses** (`@tellma/core-ui-testing`, TestBed only): `TmAlertHarness`, `TmButtonHarness`,
  `TmCheckboxHarness`, `TmDatePickerHarness`, `TmDropzoneHarness`, `TmEntityPickerHarness`
  (+`TmEntityPickerOptionHarness`), `TmFilePickerHarness`, `TmFilePreviewHarness`,
  `TmFormFieldHarness`, `TmGridHarness`/`TmGridCellHarness`/`TmGridRowHarness` (+filters,
  `TmGridEditorOpenVia = 'enter'|'f2'|'type'`, `TmGridEditorCommitVia = 'enter'|'tab'`),
  `TmTreeGridHarness`, `TmImageHarness`, `TmInputHarness`, `TmMenuHarness`/`TmMenuItemHarness`,
  `TmModalHarness`, `TmNumberHarness`, `TmOptionHarness`, `TmPopoverHarness`, `TmSelectHarness`,
  `TmTabGroupHarness`/`TmTabHarness`, `TmTooltipHarness`. Playwright uses raw locators.
- **Showcase** (`client/projects/internal/showcase`): routes `''` and `story/:id`; stories
  `welcome, theming, button, form-field, alert, input, number, date-picker, checkbox, image, files,
  file-preview, select, entity-picker, tabs, modal, popover, tooltip, menu, grid-readonly,
  grid-list-screen, grid-states, grid-editable, grid-entity, grid-custom-editor, tree-grid, i18n`;
  query params `?theme=dark`, `?dir=rtl`, `?size=sm|md|lg`; header toggles light/dark, EN/AR,
  display calendar; `[dir]` wrapper for live direction; maps `ar→ar-SA` for formatting. The word
  "sandbox" is reserved for sandbox tenants (memory).
- **Scripts** (`client/package.json`): `start` (serve showcase), `build`, `build:showcase`
  (+ font preload injection), `test` (vitest via `@angular/build:unit-test`, browser mode, loops per
  project, aborts on first failing), `lint` (eslint + stylelint), `typecheck`, `e2e`
  (`scripts/e2e.mjs`; projects chromium full, firefox/webkit `@cross-engine` only, touch = Pixel 7;
  run sequentially locally; CI serves a prod build), `tokens:build-css`, `tokens:check`,
  `api:check`/`api:approve` (`scripts/api-extractor.mjs`; reads ng-packagr **dist** d.ts — rebuild
  first; fails on any `// (undocumented)`; Windows may reorder union members → hand-revert),
  `docs:build`, `golden:approve` (format golden), `size:check` (`scripts/bundle-size.mjs`).
  E2E specs: 39 files (alert … tree-grid; incl. `fonts`, `locale-ar`, `tokens-theming`,
  `showcase-smoke`, 17 `grid-*` specs + `tree-grid`).
- **App budgets**: only the showcase has Angular build budgets (initial warn 1 MB / error 2 MB;
  anyComponentStyle 12/13 kB). Per-EP library budgets are the gzipped ratchets in §2.1.
- **axe**: static floor via the Playwright battery in light and dark, LTR and RTL, every state
  (open popups included); contrast is axe's job only (token-level contrast math was deleted);
  forced-colors/reduced-motion via `emulateMedia`. Behavioural a11y specs are the real gate.
- **Docs**: co-located `*.examples.ts` (`export const Name = { template: \`…\` }`) + JSDoc tags
  (`@tmGroup`, `@tmA11yNotes`) → `components.json` (`COMPONENTS_JSON_SCHEMA_VERSION` 2.0.0; `harness: null` when no
  `class <Name>Harness`) → showcase, `llms.txt`, MCP `list`/`describe`/`example`. ARCHITECTURE.md
  also mentions MCP `generate_theme`/`validate_theme` tools — **not implemented**.

### 2.12 aria / CDK / Angular facts that bind new components (memory notes)

- aria `ngOption` cannot be content-projected into `ngListbox` (DI follows the declaration site;
  NG0201) — the definition-directive + `ngTemplateOutlet` pattern (`tm-option`, `tm-tab`) is how
  any new aria-based component must be built.
- aria combobox relays trigger keydowns to the popup through a signal (one relay per CD; tests
  type one key per stabilization); host bindings of aria directives win over template bindings for
  the same attribute; attribute bindings write only on change (first-render traps).
- aria grid: render-space only; no range gestures; `focusMode="activedescendant"` recommended for
  virtual scroll. aria `tree` = nested-list `role=tree` (not a treegrid). aria `menu` supports
  submenus/typeahead. aria `toolbar`, `accordion`, `tree` EPs exist in 22.0.3 and are unused.
  angular.dev aria docs run ahead of npm (e.g. `enableRangeSelection` unreleased).
- **CDK 22 top-layer overlays (`usePopover:'inline'`) render IN PLACE** — the
  `.cdk-overlay-pane` is a child of the origin's DOM subtree; only globally hosted overlays (modals)
  sit in `.cdk-overlay-container`. Any container that delegates events must ignore events from
  inside `.cdk-overlay-pane` (the grid's `inOverlay(node)`); with `'inline'` the host is inserted as
  the origin's next sibling (ARIA violation inside `role=row` → use `{type:'parent'}`).
- Deferred-content dropdowns: CDK's single automatic `apply()` measures an empty pane → flip flash;
  fix = decide the side before attach with a one-element position list + max-height clamp; any
  `@defer` content inside an anchored overlay must call `reanchor()` after render.
- Repeater moving a focused row's node drops focus to `<body>` without focusout; elements that
  remove themselves in their own click handler trip focus-based teardown — discriminate by the
  preceding press (`focusDroppedByRerender()`).
- `TranslocoService` is `providedIn:'root'`; optional-inject still instantiates it → probe
  `TRANSLOCO_TRANSPILER` instead. Library builds on library need per-lib `tsconfig.lib.json`
  `paths` to `dist/`. Component hosts must be `display:block`. Fragment hrefs under
  `<base href="/">` resolve to `/#…` and unload the SPA (use same-path absolute hrefs).
- Signal Forms binds control state by **public input name** (alias trick for merged `pending`);
  `transformedValue.set()` parses unconditionally; `value.set()` resets the linked raw text.
- `@angular/build:unit-test`: specs AOT-compiled; `fixture.whenStable()` hangs on a gated pending
  `resource()`; CDK overlay prebuilt CSS is not loaded in unit tests (geometry assertions belong in
  e2e).

## 3. Gaps for round 2

### 3.1 Components that do not exist

| Missing | Needed by (brief) | Why it is a gap |
|---|---|---|
| Toast / snackbar (+ a stacking host, live-region policy) | 0028 toasts and banners; save success, share notifications | 0005 non-goal; `tm-alert` has no dismiss, no host, no timing |
| Drawer / side sheet | 0028 bell panel / assignments tray on narrow screens; future help drawer (brief "snaps-in from the right"); narrow-screen nav | 0005 non-goal; `tm-modal` only `sm/md/lg` centered |
| Splitter / resizable panes | 0034 report studio editor↔preview divider | nothing resizable except grid columns (pointer-only, grid-internal `column-resize.ts`) |
| App bar / shell layout, pinned bar, toolbar | 0027 (40px app bar, 32px pinned bar) | no layout components; `@angular/aria/toolbar` available, unused |
| Icon component + registry (`tm-icon`, `TmIconRegistry`) | every shell/page icon (menu, bell, tray, pin, back/forward, refresh, filter, columns, export) | 0002 §3.5 deferred until a consumer supplies icons; glyphs are copied inline SVG today |
| Badge / counter | bell and assignments counts, pinned folders | none |
| Avatar | session info | only `tm-image shape="circle"` (needs a URL; no initials fallback) |
| Chips / tag input, multi-select | enum column filter "multi-selection of the enum values"; future filter pills | `tm-select` is single-select; `tm-checkbox-group` named in 0002 §3.3, not built; picker multi-select is a 0006 non-goal |
| Radio group, switch/toggle, segmented control / toggle-button group | theme Light/Dark/System, grid↔tiles, tree↔list, bool filter true/false, Active-only toggle | none built (0002 called radio/toggle "near-zero marginal cost" from checkbox) |
| Tiles / card lattice (incl. tree of tiles) | 0030 tiles view (image + 4 corner values), narrow-screen default | none |
| Pivot table | 0034 aggregate pivot (two-axis virtualization, tree headers, totals styling, frozen row+column headers) | 0004 non-goal: separate engine; only `tmComputeAxisWindow` + clipboard serializer are reusable |
| Chart | 0034 chart reports, 0035 dashboards | none; charting research (ECharts behind a facade) is not in the library |
| Keyboard-shortcut display (`kbd`) and a shortcut registry | 0027 shortcuts modal, Ctrl/Cmd-K, menu hints | `TmMenuItem.shortcut` is a display string; the grid formats Mod hints privately |
| Skeleton loaders / placeholder blocks | instant warm launch (page chrome renders, data shows loading at once) | only `tm-spinner` (14px glyph) and the grid's loading overlay |
| Command-palette / launcher list (sectioned listbox inside a combobox, recents, synonyms) | 0027 launcher and global search | `tm-select` has no option groups; picker has no sections or rich rows; menu has no headings |
| Tree picker with drag sources; drag-and-drop | 0029 path tree picker, column editor (tree + reorderable list), 0034 field list dragged onto dimensions/measures/filter text | no DnD anywhere (0004 row DnD non-goal); aria `tree` exists unused; tree grid is a grid, not a picker |
| Reorderable list | column editor "adjacent list where the user can reorder them", dashboard layout | none |
| Pager / paging controls, paging status | 0030 row 2, 0031 "5 / 355" + arrows | none |
| Search field (instant input with clear, debounce helper) | 0030 search field, tenant picker, launcher | `tmInput` + `[tmPrefix]` only; no clear button |
| Card / panel / page-header / layout primitives | 0031 details layout (grey canvas, white cards, responsive reflow), page titles | none |
| Blocking busy overlay (full-page spinner, navigation prevented) | 0031 save | none (`tmButton.pending` is per button) |
| Confirm dialog helper ("Leave without saving?"), error dialogs (capped 422 list, 400, 5xx + trace id + copy button) | 0024/0031 | `TmModal` + `canDismiss` exist; no prebuilt confirm/error content, no copy-to-clipboard button |
| Empty-state component | search page, dashboards, home widgets | grid has its own; nothing generic |
| Date-range, time, date-time pickers; relative time | date filters "between"; report parameters; "last refreshed" on dashboards; notification timestamps | 0005 non-goals; `l10n` formats dates (day precision) and numbers only |
| Currency input/format | report measures, amounts | 0005 non-goal (`TmCurrencyAdapter` never built) |
| Expression/code editor (Queryex text with inline errors, parameter detection, drop-at-caret) | advanced filter popup, report studio filter/having | none (`textarea[tmInput]` only, fixed rows) |
| Accordion / collapsible section | settings categories on narrow screens, studio panels | 0005 non-goal; aria accordion unused |
| Dashboard grid layout | 0035 (reflowing report grid) | none |

### 3.2 Capabilities of existing components that round 2 will need changed

**Grid / tree grid (each an amendment to 0004 via 0010):**
- **Row activation**: no output and no click-anywhere-on-row; click selects a cell; Enter only
  activates an interactive element *in the active cell* — the brief's "click anywhere on a row
  (except checkbox) opens details" and "Enter opens the highlighted row" need a row-activation
  mode/output (0004 Decision #18 rejected a row unit).
- **External active-row drive**: no API to set/move the active row or read it — the brief's
  "typing sends focus to the search field, first row highlighted, vertical arrows move the
  highlight, Enter opens" (focus in the field, highlight in the grid) is impossible today.
- **Sort UI**: no sort indicator, no `aria-sort`, header click selects the column; sorting must
  ride `*tmGridHeader` interactive content, which loses the click-on-label gesture.
- **Header filter button**: possible only via `*tmGridHeader` (context = header string only); no
  per-column filter affordance or active-filter indicator.
- **Cross-page checkbox selection**: select-all replaces/clears `selectedIds` and its tri-state
  counts only current rows (§2.6) — conflicts with "selection survives search and paging".
- **Row styling hook**: no `rowClass`/per-row state → tree search results cannot mark
  "ancestor-only" rows distinctly; aggregate totals rows cannot be styled.
- **Tree expansion persistence and bulk restore**: expansion is in-memory per gridId+contentKey;
  no expanded-ids model/output, no serialization, no initial-expanded-set input; `loadChildren`
  is one call per node (restoring N expanded parents = N requests unless the consumer preloads).
- **Scrollbar gutter**: none — the appearing scrollbar re-flows flex columns (design rule D).
- **Scroll-to/flash a row by id**: no API (needed for child-row deep links and drill-through,
  brief §Details/§Non-aggregate).
- **Grid budget**: 38 KB vs the 24 KB 0004 set; every new grid feature lands on a ceiling already
  flagged as "worth a dedicated pass".
- Keep in mind: the grid binds a client array only (no server row model) and owns no paging,
  sorting or filtering — the search page owns those and must reconcile by `rowId`.

**Menu (0004 §8.5):** button trigger directive, submenus, section headings, checkable/radio items,
link items (`href`, open-in-new-tab), rich items; needed by the session menu, export/actions
menus, launcher entries.

**Modal (0005 §10):** a drawer/side variant; explicit `direction` from the live `Directionality`
(stale-direction risk after a live language switch, §2.4); prebuilt confirm/error contents.

**Popover (0005 §11):** width/size control beyond the 320px `--popover-max-inline-size` (filter
popups, column editor, advanced filter, notifications panel); `panelClass`-style hatch.

**Select (0002 §3.4):** multi-select, option groups, virtual scroll (all "not precluded").

**Entity picker (0006):** the picker itself is domain-free, so the generic binding (0029) must
supply `search`/`itemId`/`itemLabel`/`displayWith` and an entity cache for `displayWith`; rich
two-line options, sections (recents) and multi-select would be amendments.

**`l10n` / formatting (0005 §2.2):** time-of-day, date-time, relative time and currency formats;
each widens the closed cross-platform option set and the `format-golden.json`.

**`TmClientCache` (0005 §2.3):** must actually sweep `tm-`-prefixed IndexedDB databases and
web-storage keys before 0025 creates any; today it only deletes CacheStorage keys.

**Theme (0002 §4):** "System (default)" theme (matchMedia + `color-scheme`), and possibly a
`data-theme` writer service; no library support today.

**i18n (0002 §5/§7):** `provideTellmaUi` owns `provideTransloco` with no loader option — app
strings, lazy translation files cached by the service worker, and 0013 server string packs need
a loader/merge seam; live direction switching needs a documented `Dir` wrapper pattern.

**Breakpoints:** a closed set of width thresholds (brief) does not exist; CSS custom properties
cannot feed media queries, so the set needs build-time constants (or container queries) and the
modal's raw 600px must align with it.

**Shared private machinery:** `tmCreateAnchoredOverlay` and `tmPushEscapeDismissal` live in the
`private` EP (no stability guarantee); app-shell overlays built outside the library cannot use
them without making them public — or must be library components.

## 4. Likely change points

| Section | Why a round-2 design will edit it |
|---|---|
| 0004 §8.8 + Decision #18, §8.2 (Enter on readonly), §8.3 (click) | row activation for search pages and reports; brief open question "click row vs link column" |
| 0004 §5/§6 (inputs/outputs), §2.6 public members | active-row API (get/set/move), row-activate output, `rowClass`, sort-state input/output |
| 0004 §6.1 `header`/`*tmGridHeader` + §14 | sort indicator, `aria-sort`, header filter button, click-to-sort without selecting the column |
| 0004 §8.8 select-all semantics | cross-page `selectedIds` (count/scope of the tri-state; "select all on this page" vs all) |
| 0004 §12 `TmGridStateStore` | persisted scroll/expansion for 0025 page-state cache; expansion serialization like `serializeWidths` |
| 0004 §13.3 lazy loading | batch restore of remembered expanded parents; ancestor-vs-match styling for tree search |
| 0004 §4 / §7 | `scrollbar-gutter: stable`; tiles view if it lives in the grid package |
| 0004 §16 budgets | grid 38 vs 24 KB; new features need a budget decision |
| 0004 Non-goals (pivot) | 0034 pivot component; decide whether it is a library EP reusing `tmComputeAxisWindow` + clipboard serializer |
| 0004 §8.5 / menu EP | trigger directive, submenus, headings, checkable items, links |
| 0005 Non-goals (toast, drawer, accordion, time/date-range pickers) | new library components for 0027/0028/0030/0034 — each a new EP with budget, harness, golden, locale-ar strings |
| 0005 §2.3 `TmClientCache` | IndexedDB/localStorage sweep for 0025 stores; sign-out flow (brief: "clears the cached distro state … all tabs") |
| 0005 §2.2 `l10n` | time/relative/currency formatting; possibly a server-side C# twin for report rendering |
| 0005 §10 modal | drawer variant, direction, confirm/error helpers, full-page busy overlay |
| 0005 §11 popover | width/size; programmatic open is already there |
| 0002 §3.5 icons | `tm-icon` + `TmIconRegistry` now that the shell supplies icons |
| 0002 §4 themes | System theme, theme switching service; tenant colour overrides (`setProperty`) |
| 0002 §5 `provideTellmaUi` | Transloco loader/merge seam for app strings + 0013 string packs; availableLangs from tenant settings |
| 0002 §7 / §7.1 | live direction switching pattern; per-distribution font preloading for Arabic |
| 0002 §8 / §1 budgets | an app-level initial-bundle budget for distributions (only the showcase has one: 1 MB/2 MB) |
| 0002 §3.4 select | multi-select (enum filters), option groups (launcher) |
| 0006 §2 / Non-goals | rich options, recents section, multi-select, and the generic entity binding contract consumed by 0029 |
| `private` EP → public | if shell components outside the library need the overlay helper or the Escape stack |

## 5. Contradictions and surprises

1. **Row activation model conflicts with the brief.** Brief: click anywhere on a row (except the
   checkbox) opens details; Enter opens the highlighted row. 0004 §8.8 / Decision #18: activation
   is via consumer links in the first column, clicks select cells for range copy, and
   `selectionUnit: 'row'` was rejected. The brief's open question #1 therefore decides a 0004
   amendment, not just a search-page detail.
2. **"Highlight" is not a grid concept the page can drive.** The brief's highlight moves with
   arrows while focus stays in the search field; the grid's active cell requires DOM focus inside
   the grid (roving tabindex) and has no external setter.
3. **Select-all drops off-page selections** (code, §2.6) — the brief's "select across pages"
   breaks the first time the header checkbox is used.
4. **Defaults differ from the specs' text**: workspace size is `sm` (0002 said `md` default; 0005
   §3 says button default `md` but code defaults to the workspace size); grid rows are 30/34/38 px
   (memory notes from the alignment pass mention 26px — superseded in the preset); field heights
   30/38/46. Designers using spec numbers will be off by one notch.
5. **Validation display is not what 0002 says.** 0002 §3.1/§6 describe a visible error row; the
   shipped design (ARCHITECTURE.md "Forms", `TmFormField` docs) is a focus-only popover + invalid
   border + in-field glyph. The brief's "422 → red border on every field" works out of focus, but
   messages appear only on focus. No spec records the change (it was an ARCHITECTURE.md-only
   decision).
6. **ARCHITECTURE.md is stale on l10n**: it still names `TmDateAdapter`/`TmNumberAdapter`/
   `TmCurrencyAdapter` and "a Hijri calendar provided by a Locale pack"; shipped = the
   `TmCalendar` seam + separate calendar EPs (calendars are not locale packs), no adapters, no
   currency. It also names MCP `generate_theme`/`validate_theme` tools that do not exist.
7. **`provideTellmaUi` owns Transloco.** It calls `provideTransloco` (no loader); a distribution
   cannot add its own `provideTransloco` without a collision, so app strings and 0013 string packs
   must merge via `setTranslation(..., {merge:true})` or a new option. Easy to miss when designing
   0024 localization loading.
8. **Live language switch and direction**: root CDK `Directionality` reads `dir` once; the
   showcase needs a `[dir]` wrapper; `TmModal` passes no `direction`, so modals (and anything else
   in `.cdk-overlay-container`) may open in the stale direction after a live EN→AR switch. Either
   the shell reloads on language change or the library is amended. Not covered by any test.
9. **No breakpoints, no System theme** exist although the brief assumes both; the one width
   query (modal ≤600px) is a raw literal.
10. **Budgets are well over spec**: grid 38 (0004 said 24), grid-engine 15 (12), entity-picker 12
    (0006: 11), form-field 5 (0002: 4). The only app budget is the showcase's 1 MB/2 MB —
    nothing measures a distribution's initial bundle for the Ethiopia/Sudan/Lebanon target.
11. **No entity cache anywhere.** The task framing mentions "the cache entry point" of the picker;
    the picker holds one settled answer for the current text and nothing else; `displayWith`
    expects the consumer's (0029/0025) reactive cache. `TmClientCache` is a cleanup hook, not a
    cache.
12. **`TmClientCache.clearAll()` clears CacheStorage only**, despite 0005 §2.3 framing it as the
    sweep for "every browser store a library feature creates"; anything 0025 puts in IndexedDB or
    localStorage survives sign-out unless the hook is extended (or 0025 sweeps its own).
13. **Top-layer overlays live inside their origin's DOM subtree** (CDK 22 `usePopover:'inline'`).
    A shell or page that delegates key/pointer events at a container (launcher keyboard handling,
    "type anywhere sends focus to the search field") will receive events from dropdowns opened
    inside it unless it filters `.cdk-overlay-pane` — the grid learned this three times (memory).
14. **One Escape stack**: menu/tooltip/popover share `tmPushEscapeDismissal` (private EP). New
    shell overlays (launcher, bell panel, tray) that handle Esc on their own will double-dismiss or
    close the wrong layer.
15. **Grid shadows browser find** (Mod+F) only when `searchable` and focused; the brief's
    Ctrl/Cmd-K launcher shortcut does not collide with any grid binding, but Mod+D (fill down),
    Mod+Alt+Plus/Minus, Alt+Arrows are taken inside grids; a shortcut registry must avoid them.
16. **Readonly grid = single tab stop; Tab exits.** Search-page keyboard flows that expect Tab to
    move between rows will not work; arrows and Enter are the in-grid keys.
17. **Grid height is the consumer's job.** The grid scrolls internally only when its host has a
    definite height ("the grid occupies the rest of the screen" needs a flex/height layout from
    the page) — and a scrollbar that appears re-flows `flex` columns (no `scrollbar-gutter`).
18. **Physical right alignment in RTL** for number and date columns and `tmNumber` (Excel
    behaviour) — deliberate; designers of tiles/pivots should follow it.
19. **Tabs are not router-aware** and destroy inactive panels by default; details-page "selected
    tab survives navigation" and per-panel scroll memory need `selectedId` bound to page state and
    `preserveContent` or external state.
20. **Tooltips never show on natively disabled buttons**; disabled-with-reason affordances (e.g.
    permission-gated actions) need a wrapper or `pending`-style suppression.
21. **Menus are action-only** (`action()` callbacks), so launcher/pinned entries built on
    `tm-menu` lose native link affordances (open in new tab, copy link) that the grid's list
    pattern deliberately keeps.
22. **`TM_GRID_CONTEXT` defaults to no tenant/distribution** — the cross-tenant paste guard is off
    unless the app provides it; it belongs in the app foundation (0024) providers.
23. **Specs 0005 and 0006 were revised post-implementation** to fold in deviations (memory), so
    their text is the shipped behaviour; 0002 and 0004 were not (implementation deviations for
    those live in memory notes and code: `TM_CHECKBOX_CELL_DISPLAY` instead of checkbox editor
    registration, `TmRowCol` naming, `TM_GRID_CONTEXT` tenant seam, no CI perf-timing gate,
    authored rather than recorded Excel/Sheets fixtures, `localizedErrors`/`ownsErrorIcon`/
    `setFieldError` contract members).
24. **`@angular/aria` 22.0.3 ships `accordion`, `toolbar` and `tree`** — unused by the library and
    available as behaviour layers for the accordion, app bar toolbar and path tree picker; aria's
    online docs run ahead of npm, so APIs must be checked against installed types.

