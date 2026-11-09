# @tellma/core-tools

Node build tooling for Tellma distributions: the `tellma-client` command and the ESLint,
stylelint and TypeScript presets a distribution's client project extends. It is a Node ESM
package, built with `tsc -b` into `dist/`, and nothing in it ships to the browser.

| Export | Holds |
|---|---|
| bin `tellma-client` | The distribution build's command line. It reserves `emit`, `serve` and `build`, answers `--help` and `--version`, and exits 0 for success, 1 for a usage error, 2 for a build failure and 3 for a build problem. This version reports that it cannot run the three commands yet. |
| `@tellma/core-tools/eslint` | The ESLint preset (default export), `tmEslintConfig(options)` to configure it, and the `tm` plugin (`tmEslintPlugin`) |
| `@tellma/core-tools/stylelint` | The stylelint preset (default export) and the `tm/allowed-breakpoints` plugin |
| `@tellma/core-tools/tsconfig.json` | The TypeScript preset: strict type checking and strict templates |

## The ESLint rules

| Rule | Refuses |
|---|---|
| `tm/core-import-boundary` | Inside `@tellma/core`, a static import that leaves the entry-point graph or crosses entry points by a relative path; everywhere, an import below an entry point, and `@tellma/core/private` outside platform packages |
| `tm/no-raw-http` | `fetch`, `XMLHttpRequest` and Angular's `HttpClient`: every request goes through `TmApi` |
| `tm/no-web-storage` | `localStorage`, `sessionStorage`, `indexedDB` and `caches`: the platform's client store keeps data per user |
| `tm/lazy-layouts` | A layout member of a declaration (`details`, `page`, `create`, `search`, `component`, `display`, `dialog`, `home`, `settingsEditor`, `panel`) that is not an inline `() => import('…')` |
| `tm/literal-declaration-keys` | A declaration key that is not a string literal |
| `tm/no-decimal-operators` | `+`, `+=`, `<`, `<=`, `>` and `>=` on a `TmDecimal` (type-aware) |

The preset also enables typescript-eslint's `restrict-plus-operands` at its strictest, so a decimal's
text is never added to a number, and takes the distribution's slug as the component and directive
selector prefix: by default, the `prefix` of the one application in the nearest `angular.json`.

```js
// eslint.config.mjs
export { default } from '@tellma/core-tools/eslint';
```

```js
// stylelint.config.mjs
export { default } from '@tellma/core-tools/stylelint';
```

```json
// tsconfig.json
{ "extends": "@tellma/core-tools/tsconfig.json" }
```

## Development

The specs sit beside the sources (`src/**/*.spec.ts`) and run with the client's `lint:test`; their
fixtures under `src/eslint/fixtures/` are miniature packages on disk. The platform's own client
workspace dogfoods every preset: its ESLint and stylelint configs register this package's rules,
and its `tsconfig.json` extends the TypeScript preset.
