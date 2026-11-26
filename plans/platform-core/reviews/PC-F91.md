# PC-F91: Stop lucide's index from hoisting icon glyphs into the first frame

## Implementer notes

PC-F91 has no CHECKPOINTS.md entry. It is a fix item, scoped by its LEDGER row (§Fixes), D-281,
D-301, D-351 and the brief. Nothing under `docs/` changed. The icon registration API
(`TmIconSource`, `TmIconRegistry`, `provideTellmaIcons`, `TM_PLATFORM_ICONS`, a feature's `icons`)
is unchanged, and so is every API golden.

### The mechanism, confirmed

esbuild gives a module an entry bit for every entry point that reaches it through any import
record, used or not. `lucide/dist/esm/lucide.mjs` re-exports all 2,015 icon modules, and five
first-frame modules imported it (the primary's `platform-icons.ts`, `tm-app-bar`, `tm-banner-bar`,
`tm-toast-host`, `tm-page-header`). So every live icon module was reachable from main, and each
icon a lazy chunk draws went into a chunk main loads. In the clean baseline Acme build, the 3
initial files held 50 lucide modules (the index plus 49 icons, 6,808 B raw), and no icon module at
all sat only in a lazy chunk.

### What was built, and where

- **Per-icon imports, everywhere in `client/projects`.** All 58 files that imported the index (35
  core-ui sources, 10 core sources, 9 specs, 4 showcase files) now import each icon's own module
  as a default import: `import Check from 'lucide/dist/esm/icons/check.mjs'`. The module paths
  come from the index's own re-export table, so each binding is exactly the value the index
  exported. Two index names were aliases, so their bindings now take the canonical name of the
  module they come from: `Trash2` is `trash.mjs` (`Trash`, in `grid-core.ts` and `tm-image.ts`)
  and `Building2` is `building-complex.mjs` (`BuildingComplex`, in `tm-session-menu.ts`).
  `Image as ImageGlyph` keeps its local name. The rewrite was scripted; nothing else in those
  files changed.
- **Types for the per-icon modules: `@tellma/core-tools/lucide-icons`.** `lucide` types only its
  index, and its `dist/esm/icons/*.mjs` have no declarations (TS7016 under `strict`). The new
  `client/projects/core/tellma-core-tools/presets/lucide-icons.d.ts` declares the pattern module
  `lucide/dist/esm/icons/*.mjs` with a default export of lucide's own `IconNode` (imported as a
  type from `lucide`). Because the type is lucide's named `IconNode`, the emitted d.ts still read
  `lucide.IconNode` and every API golden is byte-identical. It is exported types-only from
  `@tellma/core-tools/package.json` (`"./lucide-icons": { "types": … }`).
  - Each importing file names it with `/// <reference types="@tellma/core-tools/lucide-icons" />`.
    A per-file directive is the only route that reaches every program: ng-packagr compiles each
    entry point with `rootNames = [public-api.ts]` (ng-packagr 22.0.1,
    `src/lib/ts/tsconfig.js:111`) and resolves sibling entry points to their built d.ts, so a
    tsconfig `include` or a reference in one shared file does not reach the others. The fixture,
    Acme and the showcase compile the sources through path mappings and pick the directive up
    with each file. TS 6 drops reference directives from emitted d.ts, so the published types
    carry none. `@typescript-eslint/triple-slash-reference`'s default (`types: 'prefer-import'`)
    accepts it, so no lint disable is needed (PC-118's attempt used `path` references and a
    disable per file).
  - A distribution gets the same types from the package it already depends on, so the docs now
    show the per-icon form (below).
- **Guards.**
  - `client/eslint.config.mjs`: `LUCIDE_INDEX_RESTRICTION` refuses an import of `lucide` (the
    bare index) in every file under `projects/`, with a message giving the per-icon form. It is
    named in every block that sets `no-restricted-imports` (flat config's last match replaces the
    rule's whole setting), and the realtime folder, which the SignalR block skips, gets a block of
    its own. Probed on four files (alert, realtime, l10n, grid): each reports the rule.
  - `build.fixture.spec.ts`, new test "keeps the icons only lazy chunks draw out of the initial
    JavaScript": on the real fixture build, no module of the build is lucide's index, and the
    session menu's `user-cog`, `keyboard` and `calendar-days` are in the build but in none of the
    initial files. It fails on the baseline (the index is an input, and all three sit in the
    initial chunk).
- **Tooling.** `client/tools/workspace.mjs`: `discoverNodePackages` took every typed `exports`
  subpath for a compiled entry point and sent it to api-extractor, which then looked for
  `src/lucide-icons.ts`. A subpath whose `types` is outside `./dist/` is now a preset shipped as
  written (like the tsconfig preset), not an entry point. No other export changes classification.
- **Docs.** TSDoc of `TmIconNode`, `TmIconRegistry.register`, `provideTellmaIcons` (its example)
  and `TmIcon`; the core-ui README's icon example and its icon/kbd note; the core-tools README
  (exports table and a new "Icons, one module each" section); ARCHITECTURE.md's Icons paragraph
  and its `@tellma/core-tools` row. The stray calendar doc comment in `eslint.config.mjs`, which
  sat above `SIGNALR_RESTRICTION`, now sits above `CALENDAR_BOUNDARY`.
- Not changed: `client/e2e/specs/*-glyphs.spec.ts` import the index in Node to compute expected
  geometry. They ship nothing, and the lint rule covers `projects/` only.

### Measurements

Every figure is gzip KB from a clean build: `rm -rf client/dist`, `pnpm build`, `size:check`;
Acme `rm -rf dist` then `tellma-client build --no-dotnet-build`; the fixture from a fresh copy
(`copyFixtureApp`) through `tellma-client build` with the spec's fake Migrator. Chunk optimizer on.
Base: e742b04d (platform-core at this branch's start).

| Build | initialJs (files) | corePrimaryFirstFrame | detailsRoute (files) | workerShell (files) | chunks named |
|---|---|---|---|---|---|
| Acme, before | 220.71 (3) | 52.00 | 31.17 (5) | 667.14 (59) | 43 |
| Acme, after | **219.68** (3) | 51.97 | 32.08 (7) | 669.91 (63) | 47 |
| Fixture, before | 220.00 (3) | 51.41 | 30.30 (4) | 590.53 (54) | 38 |
| Fixture, after | **218.97** (3) | 51.40 | 31.17 (6) | 593.32 (58) | 42 |

- initialJs falls by 1.03 KB in both builds. corePrimaryFirstFrame holds (it counts the primary's
  own modules, which never included lucide's).
- **Glyphs that left the first frame (26, plus the index module):** `building-complex`,
  `calendar-days`, `camera`, `check`, `chevron-down`, `circle-alert`, `circle-check`,
  `circle-off`, `clock`, `copy`, `crop`, `external-link`, `globe`, `image`, `image-off`, `inbox`,
  `keyboard`, `lock`, `minus`, `pencil`, `plus`, `refresh-cw`, `search-x`, `trash`, `user`,
  `user-cog`. The initial files now hold 23 icon modules (2,809 B raw, from 6,808), each one a
  first-frame module draws: the 13 of `TM_PLATFORM_ICONS`, the app bar's five (`menu`, `search`,
  `eye`, `flask-conical`, `wrench`), and the banner and toast glyphs (`circle-check-big`,
  `circle-x`, `info`, `triangle-alert`, `x`). Same list for the fixture.
- The bytes moved to the chunks that draw them: detailsRoute +0.91 / +0.87, the app-level lazy
  rows (session panels +1.49, tenantless +1.23, errors +1.59, zone prompt +0.69, about +0.48,
  shortcuts +0.31, updates +0.31, arrival toasts +0.26, image edit pane +0.23). Those rows have no
  levels. workerShell rises by 2.77 / 2.79 KB, because the glyphs shared by several lazy chunks
  are now four more small shared chunks, and each file gzips on its own; it stays far under its
  1,536 warn level.
- `size:check` (library measures, the per-entry-point ceilings and every
  `lazyChunkBudgetsInKb` chunk): every row moves by at most 0.05 KB (image 9.30 to 9.25, the
  largest), because each library measure already tree-shook the index to the icons it used. No
  ceiling dropped by more than 0.5 KB, so no ceiling is lowered (and none is raised).

### Deviations

1. Spec 0023 §3.1's example ("`HandCoins` imported from `lucide`") and §1's "imported one icon at
   a time" are kept in spirit but given a concrete form: one module per icon
   (`lucide/dist/esm/icons/<name>.mjs`), never the index, typed by the new
   `@tellma/core-tools/lucide-icons`. The spec should name the form, since a distribution that
   follows the example as written puts every lazy platform glyph back in its first frame.
2. `@tellma/core-tools` gains a public, types-only export (`./lucide-icons`). Its declaration
   imports `IconNode` from `lucide`, which core-tools does not depend on; it resolves from the
   project that references it, which depends on `lucide` to import icons at all. I did not add an
   optional peer dependency, to keep the lockfile untouched.
3. The per-icon paths reach into `lucide`'s `dist/` layout, which is not a declared public API
   (lucide has no `exports` map). A layout change fails the build loudly (unresolved module), not
   silently.

### Open risks

- `lucide` is `^1.66.0` in core and core-ui. A 1.x minor that moves `dist/esm/icons/` would break
  every consumer's build until core-ui follows. Pinning `~1.66.0` would trade that for duplicate
  installs; left as is, for a ruling if wanted.
- TypeScript keeps the leading `/// <reference types=…/>` comment in the emitted JavaScript, so
  43 FESM files carry it as a plain comment. It is inert (a JS comment; the app build's minifier
  drops it).
- A distribution that imports the index in its own first-frame code still hoists every lazy glyph.
  The workspace lint rule does not reach distributions; the distribution ESLint preset's rule list
  is spec 0024 §2.1's, so I did not add it there (decision for the orchestrator).

### What reviewers should examine hardest

- That every rewritten import binds the same value as before (the alias renames above; the
  scripted mapping came from `lucide.mjs`'s re-export table).
- The flat-config ordering in `eslint.config.mjs`: every block that sets `no-restricted-imports`
  must name `LUCIDE_INDEX_RESTRICTION`.
- The `workspace.mjs` classification change: no other Node package export should drop out of the
  API goldens (only core-tools has a non-`dist` `types`).
- The workerShell rise and the lazy rows: confirm no first-frame or route budget got worse in a
  way the gates do not see.

### Tests run

- Client, after `tokens:build-css` and a clean `pnpm build`: `typecheck`, `lint` (plus the four
  probe files above, deleted), `lint:test` (47 files, 534 tests, the fixture build spec and its new
  test included), `test` (core 2,655, core-testing 47, core-ui 2,759 plus 3 CSP, tokens 40,
  locale-ar 13, showcase 1: all pass), `tokens:check`, `api:check` (96 ok, no golden changed),
  `docs:build`, `size:check` (OK).
- The new fixture test's failing case is the baseline itself: the clean baseline fixture build
  has the index among its inputs and `user-cog`, `keyboard` and `calendar-days` in its initial
  chunk (from its stats, measured by a scratch script).
- Client e2e, `scripts/e2e.mjs --project chromium --grep-invert "@nightly|@perf|@quarantine"` on
  icon, glyphs, basics-glyphs, entity-picker-glyphs, grid-glyphs, alert, banner, toast, chip,
  checkbox, image, page-header, pager, menu-rich, tree and showcase-smoke: 214 passed. The glyph
  specs compare drawn glyphs' geometry with lucide's own data, and icon.spec drives the icon
  stories. No client e2e spec takes screenshots.
- Acme e2e, chromium, `--grep-invert "@nightly|@perf|@quarantine"`, on tenant-shell, session,
  launch, updates-network, route-feedback, warm-launch, cold-deep-link, layout-shift and rtl-boot:
  - served from the clean after-change build (`TELLMA_E2E_WEBROOT`): 30 passed, 14 skipped;
  - under the PC-F86 lazy-delay harness (40 sites in 29 files, host-built): 30 passed, 14
    skipped. The harness was removed with its own script; the tree's diff is byte-identical to
    the one before it was applied.
- `dotnet build Tellma.slnx`: Build succeeded, 0 warnings. `dotnet format --verify-no-changes`:
  clean. No .NET test touches this change, so none ran.
