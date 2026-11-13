# @tellma/core-tools

Node build tooling for Tellma distributions: the `tellma-client` command and the ESLint,
stylelint and TypeScript presets a distribution's client project extends. It is a Node ESM
package, built with `tsc -b` into `dist/`, and nothing in it ships to the browser.

| Export | Holds |
|---|---|
| bin `tellma-client` | The distribution build's command line: `emit`, `serve` and `build` (below). It exits 0 for success, 1 for a usage error, 2 for a build failure (a step that could not run) and 3 for a build problem (a check that found one). |
| `@tellma/core-tools/fonts` | Font preloading: `tmFontFragments(languages)`, `tmIsFontForFragments`, `tmBuiltFontUrls` and `tmInjectFontPreloads(browserDir, fragments)`, which `build` runs and the platform workspace's showcase build reuses |
| `@tellma/core-tools/eslint` | The ESLint preset (default export), `tmEslintConfig(options)` to configure it, and the `tm` plugin (`tmEslintPlugin`) |
| `@tellma/core-tools/stylelint` | The stylelint preset (default export) and the `tm/allowed-breakpoints` plugin |
| `@tellma/core-tools/tsconfig.json` | The TypeScript preset: strict type checking and strict templates |

## The ESLint rules

| Rule | Refuses |
|---|---|
| `tm/core-import-boundary` | Inside `@tellma/core`, a static import that leaves the entry-point graph or crosses entry points by a relative path; everywhere, an import below an entry point, and `@tellma/core/private` outside platform packages |
| `tm/no-raw-http` | `fetch`, `XMLHttpRequest` and Angular's `HttpClient`: every request goes through `TmApi` |
| `tm/no-web-storage` | `localStorage`, `sessionStorage`, `indexedDB` and `caches`: the platform's client store keeps data per user |
| `tm/lazy-layouts` | In `tmEntity`, `tmPage`, `tmFeature`, `tmWidget`, `tmOverride` and `tmCodedReport`, a layout member (`details`, `page`, `create`, `search`, `component`, `display`, `dialog`, `home`, `settingsEditor`, `panel`) that is not an inline `() => import('…')`; an entity's `details` may be an object whose `page` and `create` are checked instead, and `icons` and `providers` are not entered |
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

## The `tellma-client` commands

The commands run in a distribution's client project, the folder holding its `package.json` (with
`"tellma": { "migrator": "<path of the Migrator project's folder>" }`), its `angular.json` (one
application on `@angular/build:application`, with `serviceWorker: false`, `outputHashing: all`, the
hidden source maps, `public/**` as assets, `generated/assets/**` copied to `tm-metadata/`, and
`generated/root/**` copied to the output root, the asset
`{ "glob": "**/*", "input": "generated/root", "output": "/" }`; the browser output is a subfolder of
the output folder, never the folder itself), its `src/index.html` (`<base href="/">` before any
element that loads a URL, so a deep link loads every file from the root, then `tm-boot.js` as the
first script) and `src/app.config.ts`, which exports `appConfig`.

| Command | Does |
|---|---|
| `emit [--configuration c] [--no-dotnet-build]` | Runs `dotnet run --project <Migrator> -- emit-client-assets <project>/generated`, which writes the client metadata, the string packs and the loaders module. |
| `serve [--port n]` | Runs the emit, exports the ASP.NET Core development certificate (once, as `<slug>.pem` and `<slug>.key` where ASP.NET Core's SPA templates keep it), then `ng serve` over HTTPS on `--port`, else `<SLUG>_CLIENT` from the worktree's `.dev-ports.local`, else 4200. Every path the server owns (the Core chunk's routing words, and its words under a tenant id) is proxied to `https://localhost:<SLUG>_WEB`, else 7052, websockets included. It first writes `tm-boot.js` and the default brand files to `generated/root/`, so the dev server serves them as the built app does (the overrides come from `public/brand/`). No CSP and no service worker. Inside the platform's repository it first runs `assets:generate` (the token stylesheets and `@tellma/core`'s boot script template). The application needs a `serve` target on `@angular/build:dev-server`. |
| `build [--configuration c] [--no-dotnet-build]` | The production build and its checks, in the six steps below. Inside the platform's repository it first runs `assets:generate`, as `serve` does. |

`build` runs:

1. the emit; then the source checks: the config check (`src/app.config.ts` loaded through `tsx` in
   a Node process of its own, `@angular/compiler` first, and `tmCheckAppConfig` run against the
   emitted Core and Entities chunks and the string packs), the `index.html` rules, the manifest
   (members, PNG icon sizes, short names), the brand files and the budget notes; then it refreshes
   `generated/root/` as `serve` writes it, so no stale copy reaches the output, and runs
   `ng build --stats-json`, with Angular's chunk optimizer off so the metafile is the output's true
   chunk graph. A source problem stops the build before `ng build`;
2. copies the brand files (`public/brand/<name>` over `@tellma/core`'s `brand/<name>`), writes
   `tm-boot.js` from `@tellma/core`'s template and the string packs' boot strings, and adds
   `name_localized` and `short_name_localized` to the output manifest;
3. adds the preloads to the built `index.html`: the fonts of Latin and each offered language's
   script, the Core chunk and the default language's string pack (`as="fetch"`), and the `/shell`
   and `/attention` chunks with their static imports (`modulepreload`);
4. copies `ngsw-worker.js` and `safety-worker.js` from `@angular/service-worker`, writes
   `dist/<slug>/ngsw-config.json` (a `shell` group cached at install, an `on-demand` group of the
   chunks only the ECharts engine, the report studio or the support tools reach, with the manifest
   and icons, and navigation URLs that exclude every server path) and runs `ngsw-config`;
5. moves the hidden source maps to `dist/<slug>/sourcemaps/`;
6. runs the output checks: the budgets (printed as a table, gzip level 9 with Brotli beside), the
   hashed names, and the built `index.html`.

Each problem prints to standard error as one line, `<code> <key> <feature> - <message>`, where the
key is the declaration or file concerned and the feature its owner (the distribution's slug for its
own files). `label-missing` and `budget-warning` are warnings, which print and pass.

**Budgets.** `initialJs` (warn 230 KB, error 260 KB), `initialCss` (40), `searchRoute` (110),
`detailsRoute` (80), `echartsEngine` (170), `workerShell` (warn 1,536), `codedReports` (16) and
`bootScript` (3 KB for the script with English, plus 0.4 KB per further string table). Every
lazily loaded chunk no budget covers, a `@defer` block's or a distribution route's, prints on a
line of its own, `lazy:<the module it starts at>`, with no level: its static imports beyond the
initial set. A distribution sets a level in `package.json`; a raise needs a note:

```json
{ "tellma": { "budgets": { "initialJs": { "error": 280, "note": "Why the raise is worth it." } } } }
```

`src/build/` holds the data both checks share: the rules of `index.html` (`TM_INDEX_HTML_RULES`)
and `tmRenderBootScript`, which renders `tm-boot.js` from the template by replacing its one
configuration marker with the checked boot configuration as JSON.

## Development

The specs sit beside the sources (`src/**/*.spec.ts`) and run with the client's `lint:test`; the
lint fixtures under `src/eslint/fixtures/` are miniature packages on disk. `fixtures/` holds a
minimal distribution client (`fixtures/app/`) and the data of a stand-in Migrator
(`fixtures/migrator/`): the command specs copy the app to `fixtures/run-<name>/` (gitignored),
answer the emit's `dotnet run` from that data, and `src/commands/build.fixture.spec.ts` runs one
real `ng build` of it, through every later step and the output checks. The platform's own client
workspace dogfoods every preset: its ESLint and stylelint configs register this package's rules,
and its `tsconfig.json` extends the TypeScript preset.
