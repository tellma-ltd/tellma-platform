# Charting-Library Analysis — Backing Engine for Tellma Core-UI Charts

**Purpose:** Survey the 2026 charting-library landscape and choose a backing engine (or decide to build
our own) for the chart components of Tellma's greenfield, **agent-first** Angular-22 ERP component
library — Bar (grouped/stacked/normalized, vertical + horizontal), Pie/Doughnut, Line
(regular/stacked/normalized, ± markers), Area (regular/stacked/normalized), Heatmap, Treemap,
Histogram, Gauge, with more types to follow. Each chart takes a data array of one **measure** column +
0/1/2 **dimension** columns plus metadata (colour scheme, gauge thresholds, formatting), is interactive
(click a mark → drillthrough or dashboard cross-filter), and must satisfy the criteria in
[Part 0](#part-0--the-requirement).

**Status:** Research/rationale document. It records the comparative analysis and a recommendation; it is
**not** the authoritative design. A later charting **spec** will supersede it where they differ (as
[`0002-component-library-foundation.md`](../specs/0002-component-library-foundation.md) superseded the
[component-library analysis](angular-component-library-analysis.md)). Frozen once merged — not updated as
libraries evolve.

**Field surveyed (research conducted July 2026; versions and figures are point-in-time and will drift):**
18 options — 16 libraries plus "build our own" plus the two Angular-relevant late additions (Unovis,
VisActor VChart). Every headline claim (exact SPDX licence, real min+gzip bundle, the true 100k-point
story for the renderer, whether heatmap/treemap/gauge are genuinely built-in, and each Angular wrapper's
maintenance + Angular-22/signals/zoneless/SSR status) was web-checked against npm, GitHub, bundlephobia
and official docs, then adversarially re-verified. Where a number is a vendor estimate rather than a
measurement, it is flagged.

**Anchoring decisions** from the [component-library analysis](angular-component-library-analysis.md) and
[spec 0002](../specs/0002-component-library-foundation.md) that constrain this choice:

- **D1 — Angular-only forever, on `@angular/cdk` + `@angular/aria`.** No cross-framework engine; a
  third-party lib is acceptable only if we can own the Angular integration.
- **Apache-2.0 for the library**, shipped in `core-*` packages that distributions **redistribute** to
  commercial ERP customers → the engine's licence must be permissive (MIT/BSD/ISC/Apache-2.0). Copyleft
  and commercial/OEM tiers are disqualifying.
- **Signals-first, zoneless, standalone, SSR/hydration**, with a **CI bundle-size budget**.
- **D6 — theming = typed design tokens emitted as CSS custom properties**, light/dark, per-distribution
  branding, WCAG-contrast-validated at build.
- **D7 — a11y baked in (axe as a CI gate); RTL/Arabic first-class.**
- **Agent-first** — a built-in assistant must emit a correct chart on demand, so the authoring surface
  should be a small declarative spec, and the library ships an MCP server + `components.json` + goldens.

---

## Part 0 — The requirement

The brief listed seven criteria plus "**anything else I forgot?**". The research answered that last
question; the additions are marked **(+)** and expanded in [Part 4](#part-4--cross-cutting-what-the-brief-didnt-list).

1. **Performance** — small bundle (CI budget), smooth on low-end mobile, **up to 100,000 data points**.
2. **Customizability** — ERP customers demand deep visual/behavioural control.
3. **Stability/maturity** — mature, widely supported, low bus-factor.
4. **Angular-native** — a plus (see D1).
5. **Agent-authorable** — the assistant emits a correct chart spec on the fly.
6. **Localization + RTL/Arabic.**
7. **Free & OSS, Apache-2.0-compatible licence.**
8. **(+) Everything else that will actually decide it:** data-viz accessibility (WCAG 2.2, hidden data
   table, keyboard nav, colour-blind-safe palettes, decals); SSR/hydration; **who owns the data
   transform** (histogram binning, heatmap rollups, group-by, top-N, LTTB downsampling); export
   (PNG/SVG/PDF/CSV) + print/high-DPI; real-time/streaming; number/date/currency localization
   *consistency* across chart + tooltip + table + export; security (prototype-pollution CVEs, label XSS);
   responsive/resize; licensing *beyond* the engine (fonts, icons, map tiles, GeoJSON) + an SBOM gate;
   dashboard **cross-filter orchestration**; and a **facade/adapter** so the engine is swappable as
   "more types later" arrive.

---

## Part 1 — At-a-glance: the whole field

Renderer is the single most decisive property: **SVG/DOM collapses at ~3–5k marks**, so it cannot do
100k; **Canvas** does 10k–100k in one bitmap; **WebGL** goes beyond. "Fit" is the adversarially-adjusted
score (1–10) for *this* ERP, not general quality. Bundle = realistic min+gzip.

| Library | Licence | Renderer | 100k pts | v1 types native? | Bundle (gzip) | Angular fit | Agent spec | RTL | Fit |
|---|---|---|---|---|---|---|---|---|:--:|
| **Apache ECharts** | Apache-2.0 | Canvas **+SVG** | ✅ LTTB+progressive | 7/8 (histogram via ecStat) | ~110–170k tree-shaken (368k full) | wrapper or **own ~200-LOC adapter** | ✅ JSON `option` | ⚠️ manual | **8** |
| Chart.js | MIT | Canvas | ⚠️ line-only decimation | 4/8 (heat/tree/gauge = plugins/manual) | ~35–45k / ~70k full | ng2-charts lags (≤A21) | ✅ `{type,data,options}` | ⚠️ legend/tooltip only | 6 |
| D3 (primitives) | ISC/BSD | Canvas+SVG (DIY) | ✅ if you build it | 2/8 (treemap+bin native; rest compose) | ~22–45k | ✅ math-only + signals | ❌ imperative | ✅ if you build it | 6 |
| Build-our-own (on D3) | ISC + our Apache-2.0 | Canvas+SVG | ✅ (our loop) | 0/8 native (all composed) | byte-precise | ✅ native by construction | ✅ our DSL | ✅ first-class by design | 5.5 |
| VisActor VChart | MIT | Canvas 2D | ✅ LTTB+progressive | **8/8** | ~250–400k / 614k full | ❌ **no wrapper** | ✅ JSON + **MCP server** | ❌ tooltip only | 5 |
| AntV G2 | MIT | Canvas/SVG/WebGL | ⚠️ WebGL+sampling, not turnkey | 8/8 (histogram=binX) | ~200–330k / 377k full | ❌ no wrapper | ✅ GoG spec | ❌ (#3938, 2022) | 5 |
| billboard.js | MIT | D3 SVG **+opt-in canvas** | ⚠️ canvas 2D, no downsample | 6/8 (**no heatmap**; histogram manual) | ~70–95k / 135k full | ❌ no wrapper | ⚠️ verbose JSON | ⚠️ x-axis flip only | 5 |
| Vega / Vega-Lite | BSD-3 | Canvas+SVG | ❌ sweet-spot ~10k | 6/8 (treemap=Vega-only; **no gauge**) | ~220–280k | ❌ wrapper dead | ✅✅ **best** (JSON Schema) | ⚠️ not first-class | 5 |
| Carbon Charts | **Apache-2.0** | **SVG** | ❌ SVG ceiling | **8/8** | 159k JS **+231k CSS** | official wrapper (footgun) | ⚠️ per-type schema | ⚠️ chrome only | 4 |
| Plotly.js | MIT | SVG+WebGL | ⚠️ scattergl only | 8/8 (fragmented bundles) | ~500–700k / 1.46M full | NgModule, no A22, SSR breaks | ✅ `{data,layout}` | ⚠️ buggy bidi | 4 |
| Observable Plot | ISC | **SVG** | ❌ SVG ceiling | 5/8 (**no pie/treemap/gauge**) | ~125k | ❌ no wrapper | ⚠️ JS, not JSON | ❌ (PR in flight) | 4 |
| ngx-charts | MIT | **SVG** | ❌ jank from ~1k | 7/8 (gauge: no thresholds) | ~109k weak-treeshake | **Angular-native** but **no A22** | ⚠️ per-component | ❌ none | 4 |
| Unovis | **Apache-2.0** | **SVG** | ❌ SVG, no downsample | 6/8 (**no gauge/histogram**) | ~40–70k / 182k full | **first-party A22 wrapper** (NgModule) | ❌ JS accessors | ❌ none | 4 |
| uPlot | MIT | Canvas | ✅✅ fastest | 2/8 (line/area only; **no pie/tree/gauge**) | **~22k** | ❌ no wrapper (easy DIY) | ⚠️ JS options | ⚠️ DIY | 4* |
| AG Charts | MIT **+ commercial** | Canvas | ✅ M4 (free types) | 4/8 — heat/tree/hist/gauge = **Enterprise** | ~180–230k / 367k full | **first-party** A20+ | ✅ typed JSON | ⚠️ chrome only | 3 |
| ApexCharts | **proprietary** (v5.1+) | SVG | ❌ | 7/8 | ~100k+ / 150–220k | excellent (but pinned v5) | ✅ JSON | ⚠️ CSS hacks | 2 |
| Highcharts | **proprietary** | SVG (+boost WebGL) | ⚠️ boost only | 8/8 (modules) | ~102k core + modules | official, signals | ⚠️ huge surface | ⚠️ manual | 2 |
| Google Charts | **proprietary + remote-load** | SVG | ❌ | 6/8 (**no heatmap**) | remote (un-bundleable) | wrapper A16–20, SSR broken | ⚠️ imperative | ❌ | 1 |

\* uPlot scores ~8 **as a line/area specialist**, 4 as a sole engine (it cannot do pie/treemap/gauge).

**What the table says at a glance:** exactly **one** permissive engine simultaneously (a) covers the v1
types natively, (b) clears 100k on canvas with *built-in* downsampling, (c) also has an SVG renderer for
SSR/print, and (d) exposes a JSON spec agents can emit — **Apache ECharts**. Everything else fails at
least one hard gate: wrong renderer for 100k (Carbon, ngx-charts, Unovis, Observable Plot, Vega, billboard),
missing required types (uPlot, Observable Plot, Unovis, billboard), no Angular path (VChart, AntV G2, Vega),
or a disqualifying licence (AG Charts, ApexCharts, Highcharts, Google Charts).

---

## Part 2 — The shortlist, ranked

Ordering follows the decisive criteria (permissive licence → 100k renderer → all v1 types →
Angular-22/zoneless/SSR fit → agent-authorability), which deliberately overrides the raw fit-scores where
they conflict (the score scale is compressed and doesn't separate SVG libraries that *fail* the 100k bar
from those that pass it).

1. **Apache ECharts** *(recommended, behind a Tellma facade + in-house adapter)* — the only permissive
   engine covering every v1 type natively **and** clearing 100k via built-in LTTB/progressive canvas,
   with an SVG renderer for SSR and a JSON `option` model that is the best realistic agent target;
   ASF governance ⇒ lowest bus-factor.
2. **AntV G2 v5** — the genuine MIT ECharts-class alternative (grammar-of-graphics JSON, canvas+WebGL,
   named LTTB, all types) — but heavier, **no Angular wrapper**, **zero RTL**, China-first docs. Keep as
   the fallback engine.
3. **uPlot** — not a suite, but the ~22 KB MIT real-time/large-series specialist that belongs **behind
   the same facade** as the escape hatch for high-frequency streaming and dense line/area.
4. **billboard.js** — MIT, most v1 types native, opt-in canvas in v4 — but **no heatmap**, no
   downsampling, DIY Angular. A plausible-but-weaker substitute.
5. **Chart.js** — cleanest licence, smallest capable baseline — but canvas-only/no SSR, gauge/treemap/heatmap
   are plugins/manual, and 100k only for narrowly-configured line series. Good for a lean subset.
6. **Vega/Vega-Lite** — best-in-class declarative agent-authorability with a published JSON Schema — but
   heavy (~220–280 KB), no native treemap/gauge, and cannot render 100k without server pre-aggregation.
7. **Build-our-own (D3 primitives)** — cleanest licence and the highest theming/RTL/agent-DSL ceiling and
   byte-level bundle control — but ~15+ person-months and perpetual maintenance; realistic only **as the
   facade *over* ECharts**, not from scratch.
8. **VisActor VChart** — MIT, all 8 native, best agent story (JSON + official MCP server) — but **no
   Angular wrapper**, RTL absent, heavy, community ~75× smaller than ECharts.
9. **IBM Carbon Charts** — Apache-2.0 with every v1 type native and an official A22 wrapper — but
   **pure-SVG** (100k is a non-starter), heavy JS+CSS, Carbon-token theming lock-in.
10. **Unovis** — the rare Apache-2.0 lib with a **first-party Angular-22 wrapper** and native
    heatmap/treemap — but SVG (fails 100k), **no RTL at all**, **no gauge/histogram**, NgModule/`@Input`
    wrapper, JS-accessor config.
11. **Observable Plot** — ISC grammar-of-graphics but SVG-only, **missing pie/treemap/gauge**, emits JS
    not JSON, pre-1.0 with a ~17-month release gap.
12. **Plotly.js** — MIT with the widest native coverage but 500–700 KB, weak NgModule wrapper, SSR
    breakage, 100k only via scattergl.
13. **ngx-charts** — the one truly Angular-native MIT option, but SVG (no 100k) and **no Angular-22
    support today**.

**Disqualified on licence/architecture** (documented, not adoptable): **AG Charts** (heatmap/treemap/
histogram/gauge + advanced interactivity are commercial Enterprise, and the EULA's redistribution model
is incompatible with an Apache-2.0 downstream); **ApexCharts** (relicensed mid-2025 to proprietary
source-available — free only under $2M revenue, paid OEM for platform embedding); **Highcharts**
(proprietary; embedding it forces every downstream ERP customer into an OEM licence); **Google Charts**
(proprietary, remote-load-only from gstatic — ToS forbids self-hosting; breaks air-gapped/SSR).

---

## Part 3 — Contender-by-contender

### A. Apache ECharts — the recommendation

Apache-2.0 with **zero** commercial tiers (every type — gauge, heatmap, treemap, sankey — is free).
Canvas renderer by default **plus** a selectable SVG renderer and a zero-dep server SVG-string path.
v6.1.0 (2026-05-19); an ASF Top-Level Project since 2018; ~66.9k stars, ~4.3M weekly downloads, commits
landing daily → the lowest bus-factor of any permissive engine. Depends on `zrender` (the unavoidable
~fixed baseline) + `tslib`; the optional WebGL layer `echarts-gl` is MIT.

- **100k:** genuinely strong. `large:true` + `largeThreshold`, **built-in `sampling:'lttb'`**, and
  `progressive`/`progressiveThreshold` chunked drawing keep the main thread responsive; comfortably
  ~100k–few-hundred-k on canvas (WebGL beyond — trim the earlier "1M on mobile" claim). SVG renderer is
  for crisp small charts/print, not 100k.
- **Types:** native Bar (grouped/stacked; horizontal by axis swap), Pie/Doughnut (`radius:[inner,outer]`),
  Line (`showSymbol` markers, `stack`), Area (`areaStyle`+`stack`), **Heatmap**, **Treemap** (drill-down +
  breadcrumb), **Gauge** with `axisLine` colour-band thresholds that map directly onto warning/error
  bands. Two caveats: **histogram** needs the `ecStat` transform (one extra dep) or manual binning, and
  **100%-normalized** stacking is not a built-in mode (pre-compute percentages). The `custom` series draws
  arbitrary marks for future/exotic types.
- **Data model:** `dataset` + `encode:{x,y}`/`seriesLayoutBy` maps a row array of 1 measure + 1–2
  dimensions almost 1:1 — a strong fit.
- **Interactivity:** `chart.on('click', …)` payloads carry `seriesName/seriesIndex/name/value/data/dataIndex`
  — exactly what a typed drill/cross-filter event needs; plus `dataZoom`, `brush` (rect/lasso select),
  legend toggle, and `connect` for linked dashboards.
- **Bundle:** heavy but modular. Full build ~368 KB gzip; a tree-shaken few-type build via `echarts/core`
  + explicit `use([...])` is ~110–170 KB gzip (the zrender+core floor is real). A **naïve `import 'echarts'`
  pulls everything** and will blow the budget — Angular 19+ forbids the umbrella import anyway. Mitigate by
  route-level lazy-loading (charts are client-only).
- **Angular:** the community `ngx-echarts` wrapper is current (v22.0.0, 2026-06-11, standalone provider API)
  but single-maintainer; because ECharts is framework-agnostic vanilla JS, **write Tellma's own ~200-line
  signal/zoneless adapter against `echarts/core`** — removes the bus-factor dependency, gives
  tree-shaking and zoneless control, and (with the SVG renderer + `renderToSVGString()` + `hydrate()`)
  enables the SSR path the wrapper doesn't wire up.
- **The two real weaknesses:** **RTL is not first-class** (glyphs render since 5.5.0 but there's no RTL
  layout mode — mirrored axes, RTL legends/tooltips are manual per chart; open issue #19609), and the
  **bundle** pressures the CI budget. Both are Tellma-owned regardless of engine (RTL lives in the facade;
  bundle is solved by lazy-loading), so neither is a reason to pick a different engine — see Parts 4–5.

### B. uPlot — the companion / real-time escape hatch

MIT, zero deps, **~22 KB gzip** — the smallest capable lib by a wide margin — and the fastest: ~166k
points cold in ~25 ms, and dramatically the most efficient streamer (≈10% CPU / 12 MB at 3.6k pts @60fps,
vs Chart.js 40%/77 MB and ECharts 70%/85 MB). But it is **strictly Cartesian**: excellent line/area,
core-only bar (grouped/stacked bars are unshipped demo plugins), heatmap/histogram DIY, and
**pie/doughnut/treemap/gauge are architecturally impossible**; there is no first-class click-carries-datum
event. Single-maintainer. Conclusion: **not a sole engine, but the ideal specialist behind the same
facade** for a future high-frequency real-time monitoring surface — exactly where ECharts' documented
streaming memory-accumulation would bite.

### C. Strong permissive engines that aren't the primary

- **AntV G2 v5** (MIT, canvas+WebGL) — declarative grammar-of-graphics JSON (near-ideal for agents),
  all v1 types native (gauge/heatmap/treemap first-class; histogram via `binX`), **named LTTB** sampling.
  Held back for Tellma by: **no maintained Angular wrapper**, **~377 KB gzip** (200–330 tree-shaken),
  **no RTL** (issue #3938 open since 2022), near-zero canvas a11y, China-first docs, and a churning
  high-level layer (G2Plot in maintenance, `@ant-design/charts` React-only). The credible fallback engine.
- **VisActor VChart** (MIT, ByteDance, Canvas 2D + LTTB/progressive) — **all 8 native** and the strongest
  agent story (declarative JSON **plus an official `vchart-mcp-server`**). Held back by: **no Angular
  wrapper of any kind** (React/Vue/mini-program only → you own the integration), **RTL absent** (the only
  `rtl` in the codebase is tooltip text-direction), **a11y unshipped** (ARIA is only an aspiration),
  ~614 KB gzip full / ~250–400 KB tree-shaken, and a community ~75× smaller than ECharts with
  machine-translated docs. If agent-authoring becomes the central strategy, its MCP pipeline is the one
  reason to prototype it over ECharts.
- **billboard.js** (MIT, NAVER, D3 SVG + opt-in canvas in v4) — bar/pie/line/area/treemap and a
  threshold-aware **gauge** native, clean `data.onclick` datum; but **heatmap is entirely missing**,
  histogram is manual, canvas mode is 2D (no downsampling, vendor-benchmarked only to 50k), no Angular
  wrapper, RTL = x-axis inversion only, single-maintainer, ~135 KB gzip full.
- **Vega / Vega-Lite** (BSD-3) — **the best agent-authorability**: a compact declarative JSON grammar with
  a published, versioned JSON Schema and a huge LLM training corpus (via Altair/Streamlit/Observable), so
  an assistant emits *and self-validates* specs. But ~220–280 KB gzip, **fails 100k** (sweet-spot ~10k, no
  downsampling, no maintained WebGL renderer — needs server pre-aggregation or the VegaFusion WASM add-on),
  **treemap is Vega-only/verbose and gauge is absent**, and there's no maintained Angular wrapper. Its
  lesson (a stable declarative grammar as the public contract) is exactly what the Tellma facade should
  copy — see Part 5.
- **Chart.js** (MIT) — the safest, smallest baseline (~35–45 KB gzip tree-shaken; 13.85M weekly
  downloads). Bar/pie/line/area native; **built-in LTTB/min-max decimation** makes 100k **line** viable
  (with strict preconditions: line-only, linear/time x, `parsing:false`). But **heatmap/treemap are
  plugins, histogram is manual, and gauge has no maintained option**; canvas has weak a11y and no SSR; the
  `ng2-charts` wrapper caps at Angular 21 and never mentions signals/zoneless. Good for a lean subset, not
  the full mandate.
- **Plotly.js** (MIT, SVG+WebGL) — every v1 type is a native trace and interactivity is excellent, but
  the bundle is ~500–700 KB gzip even trimmed (no single partial bundle covers the v1 set), 100k works
  only via `scattergl`, the wrapper is NgModule/no-A22/SSR-breaking, and theming is JS-object not
  CSS-variable. A chart-breadth fallback, not a primary.
- **Observable Plot** (ISC, SVG) — a lovely concise grammar, but **pie, treemap, and gauge don't exist**
  (3 of 8), no native click/brush/zoom, spec is executable JS (not storable JSON), SVG fails 100k,
  ~125 KB, pre-1.0 with a ~17-month release gap and a two-person bus-factor.

### D. Angular-native / Apache-licensed, but renderer- or maturity-limited

- **IBM Carbon Charts** (Apache-2.0) — remarkably, **every v1 type is native** (heatmap+treemap+gauge+
  histogram all built-in is rare) with **best-in-class a11y** (keyboard nav, ARIA, screen-reader data
  table) and an official Angular wrapper on A20/21/22 peerDeps. But it is **pure D3/SVG with no canvas/
  WebGL/downsampling → 100k is architecturally impossible**; ~159 KB JS **+ ~231 KB mandatory CSS** with
  no `sideEffects` (poor tree-shaking); the wrapper's `latest` dist-tag is a 2-year-old Angular-<16 build
  (A22 hides behind `@next` — an install footgun); it's not signals-based; and theming is locked to Carbon
  design tokens rather than arbitrary CSS variables. The reference for *what full coverage + a11y look
  like*, not adoptable under the 100k bar.
- **Unovis** (`@unovis/ts` + `@unovis/angular`, Apache-2.0, F5-backed) — the **only** contender with a
  genuine, current, first-party Angular package (peerDeps `12 - 22`, so it installs on A22), native
  **heatmap + treemap**, clean datum-carrying events, and excellent `--vis-*` **CSS-variable theming**.
  But it is **SVG for every chart type with no downsampling** (fails 100k), has **no RTL/Arabic of any
  kind**, **no gauge and no histogram**, DIY normalized modes, an **NgModule + `@Input()`** wrapper (no
  standalone/signals — those are 1.7 *roadmap*) with no documented SSR, and a JS-accessor config that
  isn't JSON-serializable (weak agent authoring). Its network-graph/geo-map/timeline components are its
  real differentiator — none of which are on the v1 list. A good *library*, wrong *engine* here.
- **ngx-charts** (Swimlane, MIT) — the one truly Angular-native option with broad native coverage and a
  datum-emitting `(select)`. But **SVG with zero downsampling** (documented jank from ~1k points, 30 s
  large-stacked-bar renders → 100k unattainable), **no Angular-22 support today** (peerDeps 19–21; open
  issues #2085/#2089), not signals-first, zoneless is brand-new/unproven, SSR recently buggy, no
  token theming, weak a11y, ~109 KB with weak tree-shaking. Architecturally wrong for the perf budget.

### E. Build-our-own (D3 primitives + Angular-signals renderer)

The cleanest strategic *alignment* with every Tellma hard constraint: ISC primitives (`d3-scale`,
`d3-shape`, `d3-hierarchy`, `d3-array` binning, `d3-scale-chromatic`) used **math-only** inside
`computed()`/`effect()` — pure functions, so SSR/hydration/zoneless-clean with no wrapper and no
Angular-version lag; **byte-precise bundles** (~22 KB gzip of primitives for a line/bar chart, plus your
renderer); **Canvas** for 100k (`d3-shape` generators accept a 2D context) with LTTB you add; **first-class
RTL and a self-designed agent DSL** because you own every pixel. The hardest maths (squarified treemap,
stacking/normalization, log/time ticks, histogram binning, colour scales) is solved and free.

The catch is cost and risk, not capability: **~9–15+ person-months (likely a floor)** to reach the v1 set
at production quality — axes, legends, tooltips, animation, **canvas hit-testing at 100k**, a **parallel
accessible representation** for canvas, mirrored/bidi RTL, downsampling, theming, docs — then perpetual
maintenance with the bus-factor on our own team and **zero day-one maturity**. The agent "highest ceiling"
also carries a **cold-start penalty**: a novel DSL has no LLM training-data prior, unlike ECharts/Vega-Lite
option JSON. Verdict: don't build the renderer from scratch — **capture D3's upside by owning the facade
layer *on top of* ECharts**, and reserve raw-D3 composition for one-off exotic marks later.

### F. Disqualified on licence / architecture

- **AG Charts** — `ag-charts-community` is MIT with a first-party A20+ Angular component and M4
  downsampling, but **heatmap, treemap, histogram, gauge and all advanced interactivity (zoom/pan/brush/
  crosshair) are Enterprise-only** (commercial EULA, from ~$499/dev). Four of the eight required v1 types
  sit behind the paywall, and the per-developer EULA **forbids the redistribution** an Apache-2.0
  downstream library requires. Excellent engineering, wrong licence shape.
- **ApexCharts** — relicensed at v5.1/5.2 (2025-07-09) from MIT to a proprietary source-available
  "ApexCharts License": free only for orgs under $2M revenue, and a **paid OEM/Redistribution licence is
  explicitly required to embed it into a platform used by other people** — precisely Tellma's case. Only
  ≤v5.0.0 remains MIT (not viable long-term). The excellent `ng-apexcharts` wrapper can't rescue the
  licence.
- **Highcharts** — proprietary commercial; free only for non-commercial use. Embedding it in a
  redistributed Apache-2.0 library forces every downstream ERP customer into their own commercial/OEM
  licence. Retain only as the benchmark for what best-in-class coverage + a11y + interactivity look like.
- **Google Charts** — proprietary, **closed-source, remote-load-only from gstatic.com** (ToS forbids
  self-hosting/bundling), frozen at v52 (2023), no heatmap, SVG-only, broken SSR. Categorically
  incompatible with a self-hostable/air-gapped ERP handling confidential financial data.

---

## Part 4 — Cross-cutting ("what the brief didn't list")

These decide success more than the engine choice, and — crucially — **most of them are ours to own
regardless of engine**, which is the argument for the facade in Part 5.

1. **Renderer strategy.** Canvas-first with an SVG fallback (ECharts gives both): SVG for SSR + tiny
   sparklines + print + <~2k points; Canvas for the 100k interactive workloads; WebGL held in reserve for
   a specific >500k case (and treated cautiously on low-end Android/kiosk WebViews). Never adopt a
   pure-SVG library as the primary engine.
2. **Who owns the data transform.** Split it explicitly. The **engine** owns pixel-level line decimation
   (turn on `sampling:'lttb'` by default over a threshold). **Tellma** owns *semantic* aggregation in the
   facade — histogram binning (Sturges/Freedman-Diaconis or caller-supplied width), heatmap tile rollups,
   group-by for the 1-measure/2-dimension model, top-N + "other" for pie. No engine does currency-correct
   binning; keeping it in our data layer guarantees identical numbers across chart, tooltip, a11y table,
   and export.
3. **Accessibility for data-viz.** Don't outsource it. Always render a **visually-hidden data `<table>`**
   mirroring the chart (the single most reliable screen-reader affordance — and it doubles as the CSV/export
   source); turn on ECharts `aria` + **decal patterns** (so series are distinguishable without colour);
   ship **colour-blind-safe palettes** (Okabe-Ito/Wong categorical, capped ~6–8; Viridis/Cividis
   sequential); honour `prefers-reduced-motion`; and add **roving-tabindex keyboard navigation** over data
   points at the facade (no permissive engine does this well — a Tellma differentiator that also covers
   ECharts' keyboard-nav gap, issue #18585). This is the axe-as-CI-gate posture from D7.
4. **SSR / hydration + zoneless.** Canvas can't SSR; SVG can. ECharts is purpose-built for it
   (`{renderer:'svg', ssr:true}` + `renderToSVGString()` server-side, then a light client `hydrate()`) —
   a capability Chart.js/uPlot lack. Run charts as **zoneless signal-driven leaf components**: instantiate
   the engine in `afterNextRender`, keep it outside Angular's change detection, drive updates via `effect()`
   on input signals.
5. **Export + print + high-DPI.** Own it at the facade: PNG via `getDataURL({pixelRatio})`, SVG via the
   SVG renderer, PDF by composing SVG/PNG into jsPDF or a print stylesheet, **CSV/XLSX straight from our
   data model** (the same array that feeds the a11y table). Re-render (not just resize) on a
   devicePixelRatio change to dodge the blurry-zoom bug.
6. **Real-time / streaming.** Secondary for ERP dashboards (periodic refresh, not tick-by-tick).
   ECharts `appendData` with a **bounded sliding window** avoids its memory-accumulation trap; **uPlot**
   is the pre-integrated escape hatch behind the same facade if a genuine high-frequency surface appears.
7. **Theming via CSS-variable tokens.** No engine natively reads CSS custom properties. Build a
   **token→theme bridge** in the adapter: read the resolved `--tm-*` values off the chart host with
   `getComputedStyle`, map them into an ECharts theme object, and re-render on light/dark or brand switch
   via a signal effect. Never hard-code colours in a chart spec.
8. **Testing.** Two layers: fast deterministic **unit tests asserting the facade emits the correct engine
   spec** from a given data+metadata input (engine-agnostic, most coverage here), plus a thin **Playwright
   visual-regression** layer rendered with `animation:false`, pinned DPR, pinned fonts, and the **SVG
   renderer** for diff-stability.
9. **Security.** Prototype pollution is the recurring chart-lib CVE class (Chart.js CVE-2020-7746, Plotly
   CVE-2023-46308, the dagre-d3-es 9.8). Prefer the ASF-governed clean-record engine (ECharts); pin exact
   versions with provenance/SBOM + `pnpm audit`/OSV gates. In the facade, **never pass caller data into
   HTML-string tooltip formatters** — treat every dimension label/series name as untrusted and use
   text-only formatters or explicit escaping (this mirrors the platform's instruction-vs-data boundary).
10. **RTL / Arabic — the biggest engine gap, and ours to fill.** *No* mainstream permissive engine does
    RTL layout (ECharts #19609/#20351, AntV #3938, VChart tooltip-only, Unovis none). Own it entirely at
    the facade: axis-side swap + reversed category order when `dir=rtl`, RTL legend/tooltip DOM, bidi-safe
    labels, and **Arabic-Indic digit shaping via `Intl`** (`numberingSystem:'arab'`). Because it's
    engine-independent, RTL quality is **not** a tiebreaker between engines — but it *is* the reason the
    facade must sit between app and engine. Note that unlike `tm-select` (which mirrors "for free" via CSS
    logical properties), **canvas charts inherit nothing** — RTL is explicit facade code, in the test
    matrix from day one.
11. **Number/date/currency localization consistency.** No engine embeds a locale system; they hand you a
    formatter callback. Centralize **one Tellma formatting service** (thin wrapper over `Intl`) and inject
    it into every axis/tooltip/label formatter *and* the a11y/export table, so numbers are identical
    everywhere and consistent with the ERP grid. Drive locale/currency/numberingSystem from Tellma's i18n
    context.
12. **Facade / adapter architecture.** "More types later" + divergent engine strengths make a single
    engine a long-term risk. A facade is the standard mitigation and is the highest-leverage decision here
    — see Part 5.
13. **Responsive / resize.** The facade host owns **one debounced `ResizeObserver` → `engine.resize()`**
    (window `resize` misses sidebar/grid reflow) plus a `matchMedia('(resolution)')` listener to re-render
    on DPR/zoom. Boilerplate every consumer would otherwise reimplement.
14. **Licensing *beyond* the engine.** The disqualifying licence is usually not the chart engine. Audit
    adjacent assets: **map tiles** (OSM ODbL attribution; Mapbox/MapTiler commercial), **fonts** (OFL/Apache
    only), **icons** (Font Awesome *Free* SVGs are CC-BY but many glyphs are Pro-only — use our own or
    Lucide/Tabler per D10), and **GeoJSON** boundary data. Add an **SBOM/licence-scan CI gate covering
    bundled assets**, not just the engine.
15. **Dashboard cross-filter orchestration.** The per-chart assessments only cover a single chart's
    click→datum event; sharing selection state across many charts (drill/cross-filter) is entirely
    Tellma's to design and lives above the facade.
16. **The data-viz palette gap (Tellma-specific).** The design-token contract today exposes a single
    brand ramp plus a neutral grey ramp — there is **no categorical/sequential/diverging data-viz palette**.
    Charts need one added to the token contract (CVD-safe categorical + a perceptually-uniform sequential
    scale), **validated for WCAG contrast at build** in the same spirit as D6's token validation.
17. **Bundle budget + lazy-loading.** Charts are client-only, so lazy-load the chart module off the
    initial route and set a CI size budget on the charts entry point. With lazy loading, even ECharts'
    ~110–170 KB gzip may be a non-issue — which changes several perf-vs-weight tradeoffs above.

---

## Part 5 — Recommendation & architecture

**Adopt Apache ECharts as the primary engine, consumed as `echarts/core` with explicit `use([...])`
registration (never the umbrella import, never `ngx-echarts`), behind a Tellma-owned facade and a small
in-house signals/zoneless Angular-22 adapter.** ECharts is the only permissive engine that
simultaneously covers all v1 types natively, clears 100k on canvas via built-in LTTB + progressive
rendering, ships an SVG renderer for SSR/sparklines/print, is ASF-governed (lowest bus-factor), and
exposes a declarative JSON `option` model that is the strongest realistic agent target. Pair it with
**uPlot behind the same facade** as the pre-integrated escape hatch for any future high-frequency
real-time surface. **AntV G2** is the designated fallback engine if ECharts ever disappoints.

**Wrap the engine behind a Tellma facade — unconditionally.** This is the single highest-leverage
decision and it survives every "more types later" and engine-swap risk. Concretely:

- **The public API is Tellma's own small, declarative, JSON-serializable `TmChartSpec`** — data array +
  measure/dimension **roles** + typed metadata (palette tokens, gauge thresholds, formatting) — with a
  **published JSON Schema**, *not* a passthrough of ECharts' option tree. This narrow, validated surface
  serves agent-authorability, security (reject/repair invalid specs, strip HTML from labels), and
  swappability at once. (This is the Vega-Lite lesson: a stable declarative grammar as the public contract,
  engine private underneath.)
- **One `EngineAdapter` interface** (`render` / `update` / `resize` / `dispose` / `export`). Ship the
  **ECharts adapter first**; ship a **uPlot adapter** as proof the seam works (and the real-time hatch).
  Only genuinely engine-specific code lives in the adapter — the option compiler, the token→theme
  resolver, the SSR-SVG bridge — so a future engine swap rewrites one adapter and nothing else.
- **Everything engine-neutral lives in the facade, not the adapter:** semantic aggregation (histogram
  binning, heatmap rollups, group-by, top-N+other), the visually-hidden a11y data `<table>` (doubling as
  CSV/export), roving-tabindex keyboard nav, RTL mirroring + bidi + Arabic-Indic numerals, the
  CSS-token→theme bridge, `Intl`-based formatting injected into every formatter hook, PNG/SVG/PDF/CSV
  export from our data model, and the single debounced `ResizeObserver` + DPR listener.
- **Fit the existing core-UI conventions:** `tm-`-prefixed signal-input components (`<tm-bar-chart>`,
  `<tm-chart [spec]>`), harnesses, co-located `*.examples.ts` feeding the docs/MCP pipeline, an
  API-Extractor golden per entry point, and an MCP tool that validates a `TmChartSpec`.

This facade **is** most of the value the "build-our-own" option promised — first-class RTL/a11y/theming/
agent-DSL and byte-controlled bundles — captured at a fraction of the cost, because ECharts provides the
renderer, downsampling, and type breadth underneath.

---

## Part 6 — Open questions (settle before or during the charting spec)

1. **Quantify the CI bundle budget** for the charts entry point, and confirm **route-level lazy-loading**.
   Every "breaches the budget" verdict here is unfalsifiable without a KB number, and lazy-loading may
   make ECharts' weight moot.
2. **Is 100k *raw* client-side points truly a requirement, or a server-aggregation problem?** The single
   highest-leverage unasked question. An ERP almost always aggregates server-side (group-by/bin/top-N);
   if the server owns semantic aggregation, the client rarely sees more than a few thousand marks, the
   renderer-decisiveness argument weakens, and lighter SVG-capable libraries re-enter contention.
3. **Build a throwaway ECharts-v6 + Angular-22 zoneless + SSR-to-SVG-string proof-of-concept** to
   de-risk the "write our own ~200-line adapter" assumption before committing.
4. **Design the dashboard cross-filter orchestration** (shared selection state across charts) — it's
   entirely ours and undesigned here.
5. **Bootstrap the agent DSL:** since `TmChartSpec` has no LLM training-data prior, ship the JSON Schema
   **plus a curated few-shot example library and a validate/repair loop** from day one, or agent
   reliability will trail raw ECharts-option emission despite the cleaner surface.
6. **Specify the currency/decimal-correctness of the aggregation layer** (rounding mode, mixed-currency
   guards) so totals are identical across chart, a11y table, and export.
7. **Extend the design-token contract with a validated data-viz palette** (CVD-safe categorical +
   perceptually-uniform sequential/diverging), WCAG-contrast-checked at build.
8. **Add an SBOM / licence-scan CI gate covering bundled assets** (fonts, icons, any map tiles/GeoJSON),
   not just the engine — and settle the offline/air-gapped print/PDF asset story.
9. **Optional Unovis check.** Unovis is the only Apache-2.0 lib with a first-party Angular-22 wrapper and
   native heatmap/treemap; this analysis rules it out as the *primary* engine (SVG → no 100k; no RTL; no
   gauge/histogram), but if the answer to (2) is "server aggregates, ~thousands of marks," a small
   head-to-head against the ECharts adapter could be worth an afternoon — its first-party Angular support
   is the one thing every other option lacks.
