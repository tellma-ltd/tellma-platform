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
| `@tellma/core-tools/lucide-icons` | Types only: the declaration of Lucide's per-icon modules (`lucide/dist/esm/icons/<name>.mjs`), which the `lucide` package does not type (below) |

## The ESLint rules

| Rule | Refuses |
|---|---|
| `tm/core-import-boundary` | Inside `@tellma/core`, a static import that leaves the entry-point graph or crosses entry points by a relative path (but for a spec's path to another entry point's `*.testing.util` rig); everywhere, an import below an entry point, and `@tellma/core/private` outside platform packages |
| `tm/no-raw-http` | `fetch`, `XMLHttpRequest` and Angular's `HttpClient`: every request goes through `TmApi` |
| `tm/no-web-storage` | `localStorage`, `sessionStorage`, `indexedDB` and `caches`: the platform's client store keeps data per user |
| `tm/lazy-layouts` | In `tmEntity`, `tmPage`, `tmFeature`, `tmWidget`, `tmOverride` and `tmCodedReport`, a layout member (`details`, `page`, `create`, `search`, `component`, `display`, `dialog`, `home`, `settingsEditor`, `panel`) that is not an inline `() => import('…')`; an entity's `details` may be an object whose `page` and `create` are checked instead, and `icons` and `providers` are not entered |
| `tm/literal-declaration-keys` | A declaration key that is not a string literal |
| `tm/no-decimal-operators` | `+`, `+=`, `<`, `<=`, `>` and `>=` on a `TmDecimal` (type-aware) |
| `tm/telemetry-names` | A literal name passed to `TmTelemetry`'s `event` or `timing` that is not `<scope>.<area>.<event>` in at most 64 characters, or a `tm.` name outside the platform's areas; a literal page action id (in an object literal with `id`, `labelKey` and `invoke`, or passed to `TmPageActions.invoke`) that is not two to five lowercase parts. With type information the receivers are known by type, else by name |

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

## Icons, one module each

Import each Lucide icon from its own module, never from the `lucide` index. The index re-exports
every icon, so once a module the first frame loads imports it, every icon is reachable from the
first frame, and the bundler puts each icon a lazy chunk draws into a chunk the first frame loads.
The `lucide` package types only its index; a file that imports icons one module each names this
package's declaration of those modules once:

```ts
/// <reference types="@tellma/core-tools/lucide-icons" />
import HandCoins from 'lucide/dist/esm/icons/hand-coins.mjs';
```

Each module's default export is the icon's data, the value the index exports under the icon's
name, so it registers (`provideTellmaIcons`, a feature's `icons`) and draws (`<tm-icon [svg]>`) as
it is. The declaration resolves `lucide` from the project that references it.

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
| `serve [--port n]` | Runs the emit, exports the ASP.NET Core development certificate (once, as `<slug>.pem` and `<slug>.key` where ASP.NET Core's SPA templates keep it), then `ng serve` over HTTPS on `--port`, else `<SLUG>_CLIENT` from the worktree's `.dev-ports.local`, else 4200. Every path the server owns (the Core chunk's routing words, and its words under a tenant id) is proxied to `https://localhost:<SLUG>_WEB`, else 7052, websockets included. It first writes `tm-boot.js` and the default brand files to `generated/root/`, so the dev server serves them as the built app does (the overrides come from `public/brand/`), and defines the brand files' ratios for the app's code as `build` does. It measures the ratios once, as it starts: after adding, removing or replacing a file in `public/brand/`, restart `serve`, or the images keep the old file's box (`build` measures them on every run). No CSP and no service worker. Inside the platform's repository it first runs `assets:generate` (the token stylesheets and `@tellma/core`'s boot script template). The application needs a `serve` target on `@angular/build:dev-server`. |
| `build [--configuration c] [--no-dotnet-build]` | The production build and its checks, in the six steps below. Inside the platform's repository it first runs `assets:generate`, as `serve` does. |

`build` runs:

1. the emit; then the source checks: the config check (`src/app.config.ts` loaded through `tsx` in
   a Node process of its own, `@angular/compiler` first, and `tmCheckAppConfig` run against the
   emitted Core and Entities chunks and the string packs), the `index.html` rules, the manifest
   (members, PNG icon sizes, short names), the brand files and the budget notes; then it refreshes
   `generated/root/` as `serve` writes it, so no stale copy reaches the output, and runs
   `ng build --stats-json` with Angular's chunk optimizer on (`NG_BUILD_OPTIMIZE_CHUNKS=true`, on
   Rollup: `NG_BUILD_CHUNKS_ROLLDOWN=false`), which bundles esbuild's chunks again from the main
   entry so the first frame loads a few files rather than many. It then settles the optimizer's
   output (`src/output/chunk-names.ts`): it deletes the esbuild chunks no import reaches from
   `index.html` once no file of the output names them, checks that each chunk's imports in
   `stats.json` are exactly the chunks its text names, and renames every chunk the app loads to
   `<stem>-<hash>.js`, the hash eight characters of Crockford's base32 SHA-256, where Rollup's
   names carry a base64url hash and the main entry keeps the hash esbuild gave it before Rollup
   changed its bytes. A chunk outside an import cycle hashes its final bytes; one in a cycle (the
   lazy chunks import the main entry) hashes the whole cycle as seen from it, so a name always
   serves one content. Every reference follows: the chunks' import specifiers, `index.html`, the
   source maps' `file` and `stats.json`. The new hash has the old one's length, so no byte moves and
   the maps and the stats' byte counts stay exact. It passes `--define TM_BRAND_RATIOS=...`: each
   brand file's viewBox width over its height (such as `{"wordmark-on-dark":3.5384615384615383,...}`),
   which `@tellma/core/shell`'s frame writes on the root element as `--tm-brand-<name>`; every image
   of a brand file takes its width from its height times that ratio, so its box is final before the
   file loads and nothing beside it moves when it lands. A source problem stops the build before
   `ng build`;
2. copies the brand files (`public/brand/<name>` over `@tellma/core`'s `brand/<name>`), writes
   `tm-boot.js` from `@tellma/core`'s template and the string packs' boot strings, and adds
   `name_localized` and `short_name_localized` to the output manifest;
3. adds the preloads to the built `index.html`: the fonts of Latin and each offered language's
   script, the Core chunk and the default language's string pack (`as="fetch"`), and the `/shell`
   and `/attention` chunks with their static imports (`modulepreload`), but none of the chunks
   those entry points load with `import()`, except the tenantless pages' chunk of `/shell` and
   its own static imports, which it prefetches (`rel="prefetch"`, outside the initial
   JavaScript), so a launch to `/tenants` or `/signed-out` fetches it beside `/shell`;
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
`detailsRoute` (80), `echartsEngine` (170), `workerShell` (warn 1,536), `corePrimaryFirstFrame`
(55), `codedReports` (16) and `bootScript` (3 KB for the script with English, plus 0.4 KB per
further string table).

A route's figure is what its first visit loads before it renders, beyond the initial set: the
static imports of the chunk its host loads with `import()` (`@tellma/core/search` or
`@tellma/core/details`), and of each of the distribution's own lazily loaded chunks that uses that
entry point, such as a details layout. A chunk the platform loads on use from there, such as an
entity's action dialog, is not on the route; it prints on a lazy line of its own.

`corePrimaryFirstFrame` is the `@tellma/core` primary entry point's code on this app's first frame:
what its modules (the module it starts at and every module reached from it through relative
imports, whether from the built package or its sources) contribute to the initial JavaScript, cut
out of those files through their hidden source maps and compressed alone. Primary code the app
never uses is shaken out first, and primary code that loads with `import()` later is not on the
first frame, so the figure is a lower bound of the primary's own weight, not its cap: the
platform's `size:check` holds the whole entry point to its 55 KB cap (the product owner raised it
from the platform's 50 KB), and this line lets a distribution check the same level on its own app. A build in which no module is the primary's, no
initial file holds its code, or a file's source map disagrees with the stats' count of its primary
bytes (beyond 0.5% under or 1% over, plus 16 bytes) reports `budget-unmeasurable`; a file holding
primary code without a source map fails the build. The figure is cut through the maps, which Rollup
composes from esbuild's down to each source; the stats' count is the optimizer's estimate, each
esbuild chunk's rendered bytes split among its modules in proportion to what esbuild gave them, so
the band also bounds that estimate. Every other figure sums whole files. Every
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
real `ng build` of it, through every later step and the output checks. `lint:test` runs that spec
in a group of its own after every other spec file, so the build and the other specs' real
processes never compete for the machine. The lint rule specs, here and the workspace's own under
`client/tools/tests/`, build their ESLint `RuleTester` with `fixtures/rule-tester.ts`, which hands
it Vitest's `describe` and `it` so that each valid and invalid case runs as a test of its own.
`fixtures/source-maps.ts` writes source maps from plain runs of generated code, so the budget
specs state which source each byte came from.
The platform's own client workspace dogfoods every preset: its ESLint and stylelint configs
register this package's rules, and its `tsconfig.json` extends the TypeScript preset.
