# PC-F69: Re-split the @tellma/core primary entry point by spec family

## Implementer notes

### Summary

The brief asked for a re-split of "." into secondary entry points by spec family, a pure move with no
behaviour change, so that each spec's services leave the first frame unless the shell needs them at
boot. I measured what an app's boot actually reaches before moving anything. **Boot reaches nearly
all of "."**, so no family of services can leave the first frame by a pure move. Moving any of them
to an entry point that "." imports statically would only re-label bytes that still download at boot.
D-205 R2-6 rules that out. So this checkpoint creates **no new entry points**.

It makes the one honest saving the measurements found: `TmDecimals` sat on every first frame
without being used. The rest of the gap to 31 KB needs behaviour changes or API changes, so those are
listed below for a ruling and are not made.

### Measurements (all on this branch's built FESMs unless noted)

Scratch probes, in the shared scratchpad with the `PC-F69-` prefix:
- `PC-F69-split.mjs`: an app's first frame over the built FESMs, esbuild with `splitting: true`, the
  Angular linker and the CLI's optimization plugins, production defines. It is PC-107 R2's
  `PC-107-r2-split.mjs`, generalised to every `@tellma/core` entry point. The app imports Acme's
  five names (`bootstrapTellma`, `provideTellma`, `tmEnglish`, `tmFeature`, `tmMetadataLoaders`).
- `PC-F69-marginal.mjs`: for every runtime export of ".", the gzip it adds to that boot set, on the
  gate's own tree-shaking bundler (`createProductionBundler` with imports).
- `PC-F69-acme-attr.mjs`: attributes the `@tellma/core` code in an Acme production build's
  `main-*.js` to spec families through the build's source map. Each family's minified code is gzipped
  alone, and the results are scaled to the 35.93 KB that the primary's code weighs there together.
  (An attribution of the FESM bundle through Babel and rollup maps turned out to misattribute
  unmapped linker output, so it is not used.)

| Figure | Before | After |
|---|---|---|
| "." on the gate's linked measure (every export) | 38.49 / 38.5 | 38.49 / 38.5 |
| An app's first frame over the FESMs (entry + shared chunk; lazy shell, recents and locale-write chunks excluded) | 37.79 KB | **37.10 KB (−0.69)** |
| Acme `ng build` initial total (raw / transfer) | 582.17 kB / 152.63 kB | 582.17 kB / 152.63 kB (see Acme below) |
| Acme `main` (raw / transfer) | 545.25 kB / 145.75 kB | unchanged |

The marginal probe found only these exports not reached at boot (KB gzip over the boot set):
`TmServerText` 0.30, `ɵTmStateDump` 0.12, `tmAppConfigOf` 0.06, the declaration functions
`tmOverride`/`tmWidget`/`tmCodedReport`/`tmPage`/`tmEntity` about 0.04 each, `tmPreferenceKey` 0.04
and `ɵTM_DRAFTS_STORAGE` 0.03. Every other export adds 0.00–0.01, so boot already reaches it. The
boot set and all exports differ by 0.76 KB on that bundler.

None of those is worth an entry point of its own, and none would stay off the first frame:
- `TmServerText`: spec 0027's shell (`Distribution_About`), spec 0028's attention surfaces and
  spec 0024 §11's error presenter all use it at start.
- `ɵTmStateDump`: spec 0026's support registers the dump section at startup. D-216 already carries
  this (registerDumpSection without the memory services).
- The declaration functions and `tmPreferenceKey`: they are called in features and module-level
  keys, so at app start.

Why each family stays on the first frame, by what reaches it at boot:
- **spec 0024**:
  - the HTTP layer is reached by `provideTellma` and the startup;
  - `TmSession` and its paths by the startup and `tmSessionGuard` in the route table;
  - `TmLocalization` with `TmServerText`'s formatter by `provideTellma`;
  - `TmUrls` with the search query codec (`TmUrls`' builders and `parse` are synchronous) by the
    startup;
  - the route table, route host, skeletons, error views, launch redirect and title strategy by the
    derived routes;
  - the merge by `provideTellma`;
  - the metadata loaders.
- **spec 0025**: `TmClientStore`, `TmTabBus`, `TmCacheRefresher`, `TmLocks`, `TmLaunchCache` and
  `TmPreferences` are all injected by `provideTellma`, the startup or the route host.
- **spec 0029**: `TmEntities` is used by the route host.

Split by spec, Acme's production build (approximate, as gzip shares its dictionary):
- spec 0024's own services about 23.8 KB (14 KB share);
- spec 0025's primary services about 11.3 KB (9 KB share);
- spec 0029's `TmEntities` about 0.45 KB;
- spec 0026's seams about 0.26 KB;
- the platform icon names about 0.16 KB, beside their lucide data.

### What was built

- `client/projects/core/tellma-core/src/json/tm-decimals.ts`: `TmDecimals` is now
  `/* @__PURE__ */ Object.freeze({...})`, with an inline comment that says why.
  - The primary ships as one module (the FESM, plus one shared chunk because of the lazy
    locale-write chunk), so a bundler keeps every top-level call it cannot prove pure.
  - So `Object.freeze` kept the whole helper on every first frame, including apps that never touch
    a decimal. Nothing in the primary imports it.
  - The annotation lets esbuild drop it, for −0.69 KB on the first frame (the split probe, 37.79 →
    37.10).
  - Acme's own build compiles `@tellma/core` from its sources through tsconfig paths, so its
    `sideEffects: false` package already dropped the unused module. That is why Acme's figures do not
    move.
  - This is a measurement fact for the orchestrator: **Acme's initial bundle is not the first frame
    of an app over the published package.** Acme splits by source file, and a published app splits by
    FESM.
- `client/projects/core/tellma-core/package.json`:
  - a `"tellma".treeShakes` check for ".": an app that imports only Acme's five names ships neither
    "which is not a decimal" nor "which is not a finite number". On the pre-change dist the probe
    found both strings, and after the change neither, so the check can fail.
  - a `budgetNotes["."]` paragraph recording the review's outcome, the first-frame figures and the
    per-spec split. The ceiling stays 38.5.
- Nothing else changes: no entry point, export, import path, golden, consumer or behaviour.
  ARCHITECTURE.md already describes the primary as the warm-launch bundle with the lazy entry
  points it lists, so it needs no change.

### What would lower "." further (listed, not made: each changes behaviour or API)

Estimates come from Acme's minified bytes per file, at the primary's ratio of about 0.33 gzip to
minified. Each is a candidate for a ruling:

1. **TmPreferences' flush engine** (D-216's carry), about 1.2 KB, roughly half of the 7.4 KB
   minified class.
   - The schedule, flush, write, failure handling and pagehide send would load with `import()` at
     idle or at the first `set`.
   - The overlay, `get` and the `prefs.changed` receiver stay.
   - It changes when the first write can go out, and a pagehide before the chunk loads must still
     send. That is a timing and behaviour change of spec 0025 §9, so it is not a pure move.
2. **The search query codec**, about 1.3 KB (3.9 KB minified).
   - `TmUrls`' builders (`search`, `list`) and `parse`/`parseList` are synchronous and use it.
   - Making them async, or moving them to the page families, is an **API change** of spec 0024
     §9.4/§9.5.
3. **The not-found page, the forbidden view and the launch redirect's page**, about 0.9 KB
   (`tm-route-error.ts` 1.7 KB and `tm-launch-redirect.ts` 1.1 KB minified; the offline and
   client-outdated views must stay eager).
   - These would get a lazy chunk of their own (D-205/D-206 left "a chunk of their own" to this
     item).
   - Their first render becomes asynchronous, and the launch page at `/` would add a fetch to every
     launch there, so only the not-found and forbidden views are plausible.
4. **TmHttpApi's rarely used transports** (the XHR upload, download streaming and the blob
   fetcher), not measured separately, inside 12.4 KB minified.
   - They would load with `import()` on first use.
   - That is a timing change only, but it touches the call pipeline that every request shares.

Even with all four, "." would land near 34–35 KB, not 31. What remains is boot code that the specs
place in the primary, which leaves one choice:
- the shares grow (spec 0024 about 24 KB against 14; spec 0025 about 11.3 against 9);
- or the specs move named services behind `import()`.

### Deviations

None against a spec. Against the brief:
- No new entry points, static-graph excludes, consumer import changes or golden updates, because no
  family can leave the first frame by a pure move (the measurements above).
- The "." ceiling stays 38.5 and does not fall toward 31.

### Tests and gates run locally

- `ng build core`, then `size:check` (green, with the new treeShakes line), `api:check` (no drift),
  `typecheck`, `lint`, `tokens:check`, `docs:build` and `ng test core --include=src/json/**`
  (2 files, 26 tests).
- `lint:test`: 261/262 in the full run. `tools/tests/union-order.spec.mts` "orders number and
  bigint literals together by value" timed out at 5.8 s against vitest's 5 s default. It ran beside
  the core-tools fixture's `ng build` in the same run, and it creates a type-aware program. Run
  alone it passes (41/41, 3.1 s). This change does not touch that rule. It is a flake candidate for
  the orchestrator: a type-aware rule test with the default timeout, under load.
- `dotnet build Tellma.slnx`: Build succeeded, 0 warnings. `dotnet format Tellma.slnx
  --verify-no-changes`: clean.
- Acme `ng build` before and after: the same `main` hash (`main-JAZ62EHL.js`).

### Open risks

- The first-frame figures come from a scratch probe, not the gate. The gate's "." figure is a
  self-weight of every export, so it cannot show the TmDecimals saving. The new treeShakes check is
  what pins that saving.
- The per-spec split is an attribution through source maps (each family's bytes gzipped alone,
  scaled). It is approximate to a few tenths of a KB.

### What reviewers should examine hardest

- Whether my conclusion holds that no family leaves the first frame by a pure move. Re-run the
  marginal probe and the split probe, or name an export or service that boot does not reach.
- Whether the pure annotation is safe: `Object.freeze` on a fresh literal has no observable effect
  other than the frozen result, so dropping an unused one is sound.
- The measurement caveat about Acme (source build) against an app over the published package.
