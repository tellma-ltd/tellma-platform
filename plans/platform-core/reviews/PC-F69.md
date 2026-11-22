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

Split by spec (approximate, as gzip shares its dictionary), on two bases (corrected in review 1).
The proportions come from Acme's production build:

| Family | Acme source build (35.93 KB) | Scaled to the gate's 38.49 KB | Share |
|---|---|---|---|
| spec 0024's own services | about 23.8 KB | about 25.5 KB | 14 KB |
| spec 0025's primary services | about 11.3 KB | about 12.1 KB | 9 KB |
| spec 0029's `TmEntities` | about 0.45 KB | about 0.5 KB | — |
| spec 0026's seams | about 0.26 KB | about 0.3 KB | — |

The platform icon names come to about 0.16 KB in Acme, beside their lucide data. The earlier
budgetNotes figure for spec 0024 (26.7 KB) is on the gate's measure. It was the residual after the
other shares and the icon data, so it also held the seams and the exports that boot does not reach.
Nothing moved between the two bases.

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
3. **The not-found page**, about 0.1–0.2 KB: `TmNotFoundPage` (`src/routes/tm-routes.ts`) and the
   `not-found` entry of `TM_ROUTE_ERROR_VIEWS`. This is estimated from the FESM, not measured alone.
   (Corrected in review 1 from 0.9 KB, which had counted all of `tm-route-error.ts` and
   `tm-launch-redirect.ts`.)
   - It would get a lazy chunk of its own (D-205/D-206 left "a chunk of their own" to this item).
     Its first render becomes asynchronous.
   - `TmRouteError` cannot move. It is the one data-driven component that also renders the offline
     and client-outdated views, which must stay eager.
   - The forbidden view is not a component either. It is `<tm-route-error [view]="views.forbidden">`
     inside `TmRouteHost`'s template, so only its four strings could move.
   - The launch redirect renders at `/` on launches, so a chunk of its own would add a fetch to
     every launch there.
4. **TmHttpApi's rarely used transports** (the XHR upload, download streaming and the blob
   fetcher), not measured separately, inside 12.4 KB minified.
   - They would load with `import()` on first use.
   - That is a timing change only, but it touches the call pipeline that every request shares.

Even with all four, "." would land near 35 KB, not 31. What remains is boot code that the specs
place in the primary, which leaves one choice:
- the shares grow (on the gate's measure, spec 0024 about 25.5 KB against 14 and spec 0025 about
  12.1 against 9);
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

## Review 1

Reviewed commit be5219bc against 0024 §1, §10.4 and §12, 0025 §1 and §9.4, D-205 R2-6, D-214,
D-216 and D-225. Run locally on that commit: `ng build core` (green); `size:check` (gate OK, "."
38.49 of 38.5, the new line "ships none of which is not a decimal, which is not a finite number");
`ng test core --include=src/json/**/*.spec.ts` (2 files, 26 tests passed). The pc/PC-F69 CI run
(45052328715) was skipped, so there is no CI evidence.

Independent checks, with my own probes (scratchpad `r1f69/`), not the implementer's:
- **What boot reaches.** An esbuild split build of an app importing Acme's five names from the
  built FESM, with the Angular linker, the CLI's optimization plugins and production defines. The
  first frame (the entry plus its static chunks) is 36.63 KB gzip with `lucide` external, and
  37.30 KB with the annotation stripped from a copy of the FESM. That is −0.67 KB, which matches
  the claimed −0.69. I attributed every top-level declaration of the FESM to its source file
  through the FESM's source map and checked which ones survive unminified. The only declarations
  boot drops are `TmServerText` with its helpers, `tmPreferenceKey`, `TmStateDump`, `tmAppConfigOf`,
  the declaration functions (`tmPage`, `tmOverride`, `tmEntity`, `tmWidget`, `tmCodedReport`), the
  `ɵtmMergeAppDeclarations` alias, the boot-record and login-mark readers, `tmUserKeyBounds`,
  `ɵTM_DRAFTS_STORAGE` and `TmDecimals`. Every service class is kept. The lazy entry points import
  from "." only `tmPreferenceKey` and the draft keys beyond what boot already reaches, so resolving
  them changes nothing material. The claim holds: no sizable service of "." is unreached at boot,
  and none can move to a secondary entry point without becoming asynchronous.
- **The annotation.** `Object.freeze` on a fresh object literal of methods has no effect beyond its
  result, so dropping an unused call loses nothing. When the helpers are used, the call stays, and
  the object is frozen as before. Angular's application builder runs `markTopLevelPure` with
  `topLevelSafeMode: true` for every package outside `node_modules/@angular`
  (`javascript-transformer-worker.js:106-108` in `@angular/build` 22.0.5). So without the
  annotation, a real app over the FESM keeps the call, and the saving is real for published
  consumers.
- **The check can fail.** I ran the gate's own `createProductionBundler` over a copy of the FESM
  with the annotation removed: both needles were kept, so the check fails. With the annotation,
  neither is kept. As a positive control, the same app importing `TmDecimals` ships both needles,
  so they are live strings and not dead text.
- **Acme compiles from sources.** `distributions/acme/.../tsconfig.json` maps `@tellma/core` to
  `client/projects/core/tellma-core/public-api.ts`, and the implementer's before and after Acme
  builds both lack "which is not a decimal". So Acme's initial bundle is not the first frame of an
  app over the published package. This matters beyond this checkpoint: 0024 §12's initial-JS
  budget is measured on Acme.
- **The four candidates.**
  - (2) The query codec is a sync API change of `TmUrls`, as stated.
  - (4) The transports' public methods already return promises, so their API would not change.
    But the first upload, download or blob fetch would wait on a chunk, and could fail with a
    chunk-load error outside the `TmApiError` kinds. `fetchBlob` also serves `TM_BLOB_FETCHER`,
    which first-frame images may use. So it is not a pure move, as stated.
  - (1) and (3) are R1-3 and R1-2 below.

### Findings

**R1-1 (minor): the new per-spec split is on a different basis from the ceiling, and it
contradicts the paragraph before it.** `client/projects/core/tellma-core/package.json:46`.
- The new paragraph gives spec 0024's own services "about 23.8 KB", scaled to the 35.93 KB that the
  primary weighs in Acme's source build. The preceding paragraph of the same note (the 34 → 38.5
  raise) gives "the remaining 26.7 KB or so" on the gate's linked measure. 0025 goes from 11.0 to
  11.3 in the same way.
- 0024 §12 sets the ceiling as the sum of the shares on the gate's measure (38.49), and D-214's
  question for Ahmad is how far 0024 and 0025 overrun their 14 KB and 9 KB shares. A reader of the
  note would conclude that the 0024 overrun shrank by about 2.9 KB. Nothing moved. The figure only
  changed its basis, and Acme's build already drops what this change dropped.
- Fix: state the split scaled to the gate's 38.49 KB (about 25.5 and 12.1 at the same
  proportions), or give both bases. Say in one clause that the earlier 26.7 was a residual on the
  linked measure, so the figures can be compared.

**R1-2 (minor): candidate 3's estimate overstates what can leave the first frame, about 0.9 KB
against roughly 0.2.** `plans/platform-core/reviews/PC-F69.md:118-124`.
- The estimate counts all of `tm-route-error.ts` and `tm-launch-redirect.ts`. But `TmRouteError` is
  the one data-driven component that also renders the offline and client-outdated views, which the
  note says must stay eager.
- The forbidden view is not a component of its own. It is `<tm-route-error
  [view]="views.forbidden">` inside `TmRouteHost`'s template (`src/routes/tm-route-host.ts:162`),
  so only its four-string entry in `TM_ROUTE_ERROR_VIEWS` could move.
- The launch redirect renders at `/` on launches, as the candidate itself concedes.
- What a chunk of its own could take is `TmNotFoundPage` (`src/routes/tm-routes.ts:51-70`) and the
  not-found entry: a few hundred bytes minified, on the order of 0.1–0.2 KB gzip (estimated from
  the FESM, not measured alone).
- Fix: correct the figure and drop the forbidden view from the candidate, so that the ruling is not
  made on 0.9 KB.

**R1-3 (minor; needs a ruling): D-216 carried TmPreferences' flush engine to this checkpoint, and
the commit's Deviations do not name it as unmade.** Commit message (Deviations); review file
lines 107–113.
- D-216 says "TmPreferences' flush engine is left for PC-F69". The commit lists it only among
  unnamed "candidates … listed in the review file". PLAN §7.1 item 1 asks that a deliverable left
  out be justified under Deviations.
- Its reason is sound as far as it goes. A lazy engine delays a curated flush, which 0025 §9.4
  says goes "at once", for any `set` before the chunk lands, so it is not a pure move.
- But the residual change can be bounded:
  - keep `get`, the overlay, the dirty map, the `prefs.changed` receiver and the `pagehide` send
    (with its 16 KB cut) eager;
  - load the schedules, `#flush`, `#write`, the failure handling and the adoption with `import()`,
    preloaded at idle after the first render.
- Then only a curated `set` made before idle waits for one cached chunk, and nothing is lost on
  `pagehide`. That is the same class of change D-221 accepted for the locale write, which loads
  with `import()` on the first saved change. The saving is about 1.2 KB by the implementer's estimate.
- Fix: either make the move with a named deviation against 0025 §9.4 (curated writes before the
  chunk lands wait for it), or name the D-216 carry explicitly under Deviations as deferred for a
  ruling.

### Responses

- R1-1: fixed in the "PC-F69: review 1 fixes" commit (child of be5219bc), per D-228.
  - `budgetNotes["."]` now gives the split on both bases, each labelled. On the gate's 38.49 KB
    measure, at Acme's proportions: spec 0024 about 25.5 KB, spec 0025 about 12.1, TmEntities
    about 0.5, the 0026 seams about 0.3. In Acme's source build: 23.8, 11.3, 0.45 and 0.26.
  - It says that nothing moved between the two. It also says that the note's earlier 26.7 KB is on
    the gate's measure, as the residual after the other shares and the icon data, so it also held
    the seams and the exports that boot does not reach.
  - The implementer notes carry the same two-basis table. Their candidate summary now quotes the
    gate-basis shares.
- R1-2: fixed in the same commit, per D-228. Candidate 3 is now the not-found page alone, about
  0.1–0.2 KB (`TmNotFoundPage` and its `not-found` data entry, estimated from the FESM). The notes
  give the reasons:
  - `TmRouteError` also renders the offline and client-outdated views, which stay eager;
  - the forbidden view is only a data entry inside `TmRouteHost`'s template;
  - the launch redirect renders at `/` on launches.
- R1-3: not made, per D-228. The brief makes this checkpoint a pure move.
  - The commit's Deviations now name the D-216 carry: TmPreferences' flush engine behind `import()`,
    about 1.2 KB. It is a timing change against 0025 §9.4's "at once" for curated writes, deferred
    for Ahmad's ruling.
  - The bounded variant this review describes is recorded there too: `get`, the overlay, the dirty
    map, the receiver and the `pagehide` send stay eager, and the rest preloads at idle.
- Flaky lint:test case (D-011), root-caused and fixed in the same commit.
  - Cause: `printedForEveryOrder` in `client/tools/tests/union-order.spec.mts` called
    `sortUnionMembers` once per ordering, 120 times for five members. Each call runs
    `ts.transpileModule` (a one-file program, its syntactic diagnostics and an emit) and then
    `ts.createSourceFile`. That fixed cost × 120 is the case's whole 160 ms when run alone.
  - In the full `lint:test` run the "tools" project runs its files in parallel workers. Those
    workers import TypeScript and build type-aware programs: 812 s of import time and 1115 s of test
    time summed over workers in a 541 s run. The file took 32 s instead of 1.1 s, a 30× slowdown, so
    the case crossed vitest's 5 s default. (The fixture `ng build` is a separate group that runs
    afterwards, so it was not the contender. Other slots building on the same machine add to it.)
  - Fix: every ordering becomes one declaration of a single source, separated by a fixed line
    comment, and is sorted in one call. Each union is still sorted independently, and the comments
    between declarations are kept as written (a case in the same file pins that), so splitting at
    the separator gives each ordering's text.
  - Alone, the bigint case fell from 160 to 31 ms, the five-member literal case from 129 to 26 ms,
    and the four-member case from 55 to 20 ms. A scratch check confirmed that the split returns one
    text per declaration, unsorted ones included.
  - No timeout was raised and no retry was added.
