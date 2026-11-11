# @tellma/core-ui

The Tellma UI component library: signal-first, zoneless Angular form controls
built on `@angular/cdk` + `@angular/aria`, Signal Forms native (`[formField]`
binds every control; the bound field is authoritative for
disabled/readonly/required).

## Entry points

| Import | Contents |
|---|---|
| `@tellma/core-ui` | `provideTellmaUi()` / `provideTellmaForms()` (with the translations loader, the theme seed and a live root `Directionality`), the `TM_UI_TRANSLATE` i18n seam + `TM_UI_MESSAGE_CONTEXT`, `TM_ACTIVE_LOCALE`, the `TmL10n` formatting facade (numbers, dates, instants with zone labels, relative times, month and weekday names), `TM_CALENDAR` + `provideTmCalendar()`, the display and device time zones (`TM_TIME_ZONE`, `TM_DEVICE_TIME_ZONE`) and the clock (`TM_NOW`), `TmBreakpoints` + `TM_BREAKPOINTS`, `TmTheme`, `TmClientCache`, the `tmMinDate`/`tmMaxDate` validators, field-error resolution, self-hosted Latin/Mono fonts (`fonts/fonts.css`) |
| `@tellma/core-ui/contracts` | Dependency-free contracts: `SignalLike`, `TmFormFieldControl`, the grid cell interfaces |
| `@tellma/core-ui/l10n` | Framework-free number/date codecs (`tmFormatNumber`, `tmParseNumber`, `tmFormatDate`, `tmParseDate`, …), the instant formats in an explicit time zone (`tmFormatDateTime`, `tmFormatTime`, `tmFormatRelativeTime`, `tmFormatZoneOffset`, `tmFormatZoneName`), month and weekday names, `tmStripBidiControls` (no formatter writes a bidirectional control character), and the `TmCalendar` seam with the Gregorian default |
| `@tellma/core-ui/calendar-umalqura` | `tmUmalquraCalendar()` — the Umm al-Qura (Hijri) calendar pack |
| `@tellma/core-ui/calendar-ethiopic` | `tmEthiopicCalendar()` — the Ethiopic (Amete Mihret) calendar pack |
| `@tellma/core-ui/input` | `tmInput` — a bare directive on the native `<input>` and `<textarea>` |
| `@tellma/core-ui/number` | `tmNumber` — locale-aware numeric input (percent mode, display rounding) |
| `@tellma/core-ui/date-picker` | `tm-date-picker` — calendar-aware date input + popup (ISO `YYYY-MM-DD` model) |
| `@tellma/core-ui/entity-picker` | `tm-entity-picker` — server-searched FK selector with advanced-search/create/edit modal pages |
| `@tellma/core-ui/checkbox` | `tm-checkbox` — native-input tri-state checkbox |
| `@tellma/core-ui/form-field` | `tm-form-field` — label/hint/message chrome around any control. Validation messages are an anchored popover shown while the control holds focus, never a row under the field, so an error can never reflow the page; the words themselves live in a permanently rendered visually-hidden live region |
| `@tellma/core-ui/select` | `tm-select` and `tm-multi-select` over `tm-option` and `tm-option-group` — overlay single- and multiple-choice selects with option headings |
| `@tellma/core-ui/button` | `tmButton` on a `<button>` or a link — variants, sizes (sizing `tm-icon` children), the `pending` suppressed-activation state, and `disabledReason` (focusable, activation swallowed, the reason as tooltip and description) |
| `@tellma/core-ui/tabs` | `tm-tab-group` + `tm-tab` — active-only-DOM tabs with `preserveContent`, and `instantiate()` to render unselected tabs on request |
| `@tellma/core-ui/modal` | `TmModal` + `TmModalRef` — service-opened dialogs with a typed result channel: centered, as a drawer at the inline-end edge (`placement: 'end'`), or as an alert dialog (`role: 'alertdialog'`) |
| `@tellma/core-ui/popover` | `tm-popover` + `tmPopoverTriggerFor` — anchored non-modal dialog panels, content-sized or at a fixed `sm`/`md`/`lg` width |
| `@tellma/core-ui/tooltip` | `tmTooltip` — plain-text tooltips with an always-available description |
| `@tellma/core-ui/alert` | `tm-alert` — page/section status messaging with live announcement |
| `@tellma/core-ui/image` | `tm-image` — cached record images with an edit (replace/re-fit/delete) mode, and `placeholderOnError` for a placeholder fallback (an avatar's initials) |
| `@tellma/core-ui/files` | `tmFilePicker` + `tm-dropzone` — file selection over one guardrail engine |
| `@tellma/core-ui/file-preview` | `TmFilePreview` — the modal file viewer (download-only for HTML/unknown) |
| `@tellma/core-ui/menu` | `tm-menu` + `tmContextMenuTrigger` — programmatic and context menus |
| `@tellma/core-ui/grid` | `tm-grid` — the editable data grid |
| `@tellma/core-ui/tree-grid` | `tm-tree-grid` — the hierarchical grid |
| `@tellma/core-ui/grid-engine` | The framework-free grid engine behind `tm-grid` |
| `@tellma/core-ui/spinner` | `tm-spinner` — the shared decorative pending/progress glyph |
| `@tellma/core-ui/icon` | `tm-icon` — a Lucide icon by registry name or from icon data, sized by `--icon-size`, mirrored in RTL when directional; `TmIconRegistry` + `provideTellmaIcons()` |
| `@tellma/core-ui/kbd` | The `TmKeys` notation (`'Mod+K'`), `tmKeysMatch` (layout-independent, AltGr-safe), `tmFormatKeys`/`tmKeysText`/`tmAriaKeyShortcuts`, `tmIsApplePlatform`, and `tm-kbd`'s key caps |
| `@tellma/core-ui/overlay` | `tmCreateAnchoredOverlay`, `tmLogicalPositions` and `tmPushEscapeDismissal` — the anchoring and the one Escape stack every Tellma overlay shares, for overlays built outside the library |
| `@tellma/core-ui/private` | Unstable wiring shared between Tellma libraries (the validation bubble) — no API goldens, never for app code |

## Consuming

Add to the application's `styles` array:

- `@tellma/core-ui-tokens`' emitted stylesheet (the token variables);
- this package's `fonts/fonts.css` (the self-hosted Latin/Mono faces);
- one stylesheet per BARE DIRECTIVE — `styles/tm-button.css`,
  `styles/tm-input.css`, `styles/tm-number.css`. A directive has no view of
  its own to carry styles, so its appearance ships as a global sheet;
  skipping these leaves buttons and text/number inputs unstyled. The
  authoritative list is `package.json`'s `"tellma".docs.globalStyles`, keyed
  by the entry point that needs it.

Icons draw from data, not from a font or a sprite. The library registers no
icon names: register the ones the application uses once, importing each
icon's data from `lucide` (a dependency of this package):

```ts
import { ChevronRight, HandCoins } from 'lucide';

providers: [
  provideTellmaIcons({
    'hand-coins': HandCoins,
    // Glyphs that point along the reading direction flip in RTL.
    'chevron-right': { node: ChevronRight, mirrorInRtl: true },
  }),
]
```

The build pipeline fingerprints the font binaries like any other
CSS-referenced asset (see the showcase's `angular.json` for the reference
wiring, and the workspace's `scripts/inject-font-preloads.mjs` for the
post-build step that injects `<link rel="preload">` tags for the emitted font
URLs). Theming, sizing, and typography all flow from the token variables —
the components ship no hardcoded sizes or colors.

## Layout, theme, strings and direction

- **Breakpoints.** One closed set of widths (`sm` 576, `md` 768, `lg` 992,
  `xl` 1200, `xxl` 1440), kept once in `layout/breakpoints.json` and
  exported as `@tellma/core-ui/breakpoints.json`. Pages switch structure
  with `TmBreakpoints`; components adapt with container queries on an inner
  wrapper. Stylesheets spell width queries in range syntax at those values
  only — `(width >= 768px)`, `(width < 576px)`, `(576px <= width < 992px)` —
  which the `tm/allowed-breakpoints` stylelint rule enforces.
- **Touch sizes.** Below `sm`, a field, button or select whose `size` is
  unset renders at `lg` (46 px); an explicit size always wins, and grids
  keep their row size.
- **Theme.** `TmTheme` resolves System, Light or Dark and writes
  `<html data-theme>` (always `light` or `dark`). Seed it with
  `provideTellmaUi({ theme: { initial } })`; the app persists the choice.
- **Strings.** `provideTellmaUi({ translations })` registers the app's
  loader; loaded tables merge with the built-in English and installed locale
  packs. ICU MessageFormat is interpreted, never compiled to code, so it
  runs under a Content Security Policy without `'unsafe-eval'`. Typed
  arguments (`number`, `date`, `time`) format through
  `TM_UI_ARGUMENT_FORMATTER`: numbers with `Intl`, dates and times as ISO
  8601 by default, which an app replaces with its calendar-aware formatting.
- **Direction.** Flip `document.documentElement.dir` (and `lang`): the root
  `Directionality` follows live, so open modals mirror in place and every
  overlay opened afterwards takes the new direction.

## Authoring conventions

- The package root is the code root: every folder either is an entry point
  (has an `ng-package.json`) or belongs to the primary entry point.
- Entry points import shared code via `@tellma/core-ui` only — never a
  relative `../` path (each entry point is its own compilation unit).
- Component hosts must be `display: block`: an inline host wrapping a block
  child hit-tests above the child in Chromium, swallowing real clicks. The
  inline glyphs that sit inside text and controls (`tm-icon`, `tm-kbd`) are
  `inline-flex` instead; neither takes pointer input.
- `@tellma/core-ui/icon` and `/kbd` are the foundation every other entry
  point builds on, so they import no other `@tellma` entry point (lint
  enforces it). Library glyphs draw through `<tm-icon [svg]>` with data
  imported from `lucide`; key combinations are matched and labelled through
  the `kbd` helpers, the library's only platform check.
- Unit spec files share one browser page per run. A spec that changes the
  page irreversibly (a Content Security Policy, which can never be lifted)
  is named `*.csp.spec.ts`: the `test` target excludes that suffix and the
  `test-csp` target runs those files in a run of their own.
- Usage examples live in co-located `*.examples.ts` files (dependency-free
  template objects); they feed the docs pipeline and are compile-checked
  against the live components by `docs-examples.spec.ts`.
