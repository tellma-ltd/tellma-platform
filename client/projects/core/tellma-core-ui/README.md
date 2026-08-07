# @tellma/core-ui

The Tellma UI component library: signal-first, zoneless Angular form controls
built on `@angular/cdk` + `@angular/aria`, Signal Forms native (`[formField]`
binds every control; the bound field is authoritative for
disabled/readonly/required).

## Entry points

| Import | Contents |
|---|---|
| `@tellma/core-ui` | `provideTellmaUi()` / `provideTellmaForms()`, the `TM_UI_TRANSLATE` i18n seam + `TM_UI_MESSAGE_CONTEXT`, `TM_ACTIVE_LOCALE`, the `TmL10n` formatting facade, `TM_CALENDAR` + `provideTmCalendar()`, `TmClientCache`, the `tmMinDate`/`tmMaxDate` validators, field-error resolution, self-hosted Latin/Mono fonts (`fonts/fonts.css`) |
| `@tellma/core-ui/contracts` | Dependency-free contracts: `SignalLike`, `TmFormFieldControl`, the grid cell interfaces |
| `@tellma/core-ui/l10n` | Framework-free number/date codecs (`tmFormatNumber`, `tmParseNumber`, `tmFormatDate`, `tmParseDate`, …) and the `TmCalendar` seam with the Gregorian default |
| `@tellma/core-ui/calendar-umalqura` | `tmUmalquraCalendar()` — the Umm al-Qura (Hijri) calendar pack |
| `@tellma/core-ui/calendar-ethiopic` | `tmEthiopicCalendar()` — the Ethiopic (Amete Mihret) calendar pack |
| `@tellma/core-ui/input` | `tmInput` — a bare directive on the native `<input>` and `<textarea>` |
| `@tellma/core-ui/number` | `tmNumber` — locale-aware numeric input (percent mode, display rounding) |
| `@tellma/core-ui/date-picker` | `tm-date-picker` — calendar-aware date input + popup (ISO `YYYY-MM-DD` model) |
| `@tellma/core-ui/entity-picker` | `tm-entity-picker` — server-searched FK selector with advanced-search/create/edit modal pages |
| `@tellma/core-ui/checkbox` | `tm-checkbox` — native-input tri-state checkbox |
| `@tellma/core-ui/form-field` | `tm-form-field` — label/hint/message chrome around any control. Validation messages are an anchored popover shown while the control holds focus, never a row under the field, so an error can never reflow the page; the words themselves live in a permanently rendered visually-hidden live region |
| `@tellma/core-ui/select` | `tm-select` + `tm-option` — overlay single-select |
| `@tellma/core-ui/button` | `tmButton` — variants, sizes, and the `pending` suppressed-activation state |
| `@tellma/core-ui/tabs` | `tm-tab-group` + `tm-tab` — active-only-DOM tabs with `preserveContent` |
| `@tellma/core-ui/modal` | `TmModal` + `TmModalRef` — service-opened dialogs with a typed result channel |
| `@tellma/core-ui/popover` | `tm-popover` + `tmPopoverTriggerFor` — anchored non-modal dialog panels |
| `@tellma/core-ui/tooltip` | `tmTooltip` — plain-text tooltips with an always-available description |
| `@tellma/core-ui/alert` | `tm-alert` — page/section status messaging with live announcement |
| `@tellma/core-ui/image` | `tm-image` — cached record images with an edit (replace/re-fit/delete) mode |
| `@tellma/core-ui/files` | `tmFilePicker` + `tm-dropzone` — file selection over one guardrail engine |
| `@tellma/core-ui/file-preview` | `TmFilePreview` — the modal file viewer (download-only for HTML/unknown) |
| `@tellma/core-ui/menu` | `tm-menu` + `tmContextMenuTrigger` — programmatic and context menus |
| `@tellma/core-ui/grid` | `tm-grid` — the editable data grid |
| `@tellma/core-ui/tree-grid` | `tm-tree-grid` — the hierarchical grid |
| `@tellma/core-ui/grid-engine` | The framework-free grid engine behind `tm-grid` |
| `@tellma/core-ui/spinner` | `tm-spinner` — the shared decorative pending/progress glyph |
| `@tellma/core-ui/private` | Unstable wiring shared between Tellma libraries — no API goldens, never for app code |

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

The build pipeline fingerprints the font binaries like any other
CSS-referenced asset (see the showcase's `angular.json` for the reference
wiring, and the workspace's `scripts/inject-font-preloads.mjs` for the
post-build step that injects `<link rel="preload">` tags for the emitted font
URLs). Theming, sizing, and typography all flow from the token variables —
the components ship no hardcoded sizes or colors.

## Authoring conventions

- The package root is the code root: every folder either is an entry point
  (has an `ng-package.json`) or belongs to the primary entry point.
- Entry points import shared code via `@tellma/core-ui` only — never a
  relative `../` path (each entry point is its own compilation unit).
- Component hosts must be `display: block`: an inline host wrapping a block
  child hit-tests above the child in Chromium, swallowing real clicks.
- Usage examples live in co-located `*.examples.ts` files (dependency-free
  template objects); they feed the docs pipeline and are compile-checked
  against the live components by `docs-examples.spec.ts`.
