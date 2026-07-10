# @tellma/core-ui

The Tellma UI component library: signal-first, zoneless Angular form controls
built on `@angular/cdk` + `@angular/aria`, Signal Forms native (`[formField]`
binds every control; the bound field is authoritative for
disabled/readonly/required).

## Entry points

| Import | Contents |
|---|---|
| `@tellma/core-ui` | `provideTellmaUi()` / `provideTellmaForms()`, the `TM_UI_TRANSLATE` i18n seam + `TM_UI_MESSAGE_CONTEXT`, field-error resolution, `TM_FONT_SUBSETS` + `fontPreloadLinks()`, self-hosted Latin/Mono font assets |
| `@tellma/core-ui/contracts` | Dependency-free contracts: `SignalLike`, `TmFormFieldControl`, the draft grid cell interfaces |
| `@tellma/core-ui/input` | `tmInput` — a bare directive on the native `<input>` |
| `@tellma/core-ui/checkbox` | `tm-checkbox` — native-input tri-state checkbox |
| `@tellma/core-ui/form-field` | `tm-form-field` — label/hint/error chrome around any control |
| `@tellma/core-ui/select` | `tm-select` + `tm-option` — overlay single-select |

## Consuming

Add `@tellma/core-ui-tokens`' emitted stylesheet plus this package's static
font assets (see the workspace root README and the showcase's `angular.json`
for the reference wiring). Theming, sizing, and typography all flow from the
token variables — the components ship no hardcoded sizes or colors.

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
