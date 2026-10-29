# Editor B — applied changes (G-2 without the spec 0026 line, G-4)

## G-2. `visibleWhen` on navigation entries

Verified first: the flat launcher members are repeated in each declaration's own sketch, not only
listed in spec 0027 §3.2. `tmPage` in spec 0024 §2.4 (one line), `tmEntity` in spec 0029 §3 (one
line), `tmCodedReport` in spec 0034 §2.2 (one line). Feature `entries` are typed `TmNavEntry`
(spec 0024 §2.2 `TmFeatureDefinition.entries`), so they inherit the member from spec 0027 §3.1.
The member therefore goes on each of those three lines, matching `section`/`order`/`icon`.

The distribution document is `TmLaunchCache.distribution: Signal<TmDistributionInfo | undefined>`
(spec 0025 §3.1; the `distribution` user document of §2.3). Its `support { enabled,
providerAccess }` is spec 0011 §5.9's `DistributionInfo`, as spec 0026 §12.1 reads it.

- **0027 §1** — `TmNavContext` added to the primary entry point's Navigation row.
- **0027 §2** — one row in the "Anything beyond that" table: "Show an entry only under a
  distribution option | `visibleWhen: ({ distribution }) => …` on its declaration | §3.1".
- **0027 §3.1 contract** — `TmNavEntry.visibleWhen?: (context: TmNavContext) => boolean`;
  `interface TmNavContext { readonly distribution: TmDistributionInfo }`.
- **0027 §3.1 bullets** — new `visibleWhen` bullet: visible when the permission rule passes and
  `visibleWhen` (when present) returns true; hidden while the `distribution` document is absent;
  hides navigation only, the route stays, the server governs access. "Recomputed" gains the
  `distribution` document change.
- **0027 §3.2 Members** — `visibleWhen` joins the flat members list.
- **0027 §8.3** — the `unavailable` row also covers a pin whose route's owning declaration, or
  whose path's feature entry, is hidden by `visibleWhen`. §8.12's `unavailable` treatment applies
  unchanged ("This page no longer exists.", Unpin).
- **0027 §13** — `TmNavigation` unit: `visibleWhen` with the `distribution` document absent and
  changing. `TmPins` unit: a pin hidden by `visibleWhen`.
- **0024 §2.4** (`tmPage` sketch), **0029 §3** (`tmEntity` sketch), **0034 §2.2**
  (`TmCodedReportDefinition` sketch) — `visibleWhen?: (context: TmNavContext) => boolean` appended
  to the existing launcher-members line. 0029's comment now reads "shown when allows(name,
  'query') and visibleWhen passes" and cites 0027 §3.1, §3.2; 0034's cites 0027 §3.1, §3.2.
- Spec 0026 §12.5 left to editor A, as assigned.

### Deviations (G-2)

- **`TmNavContext.distribution` is non-optional, and an entry with `visibleWhen` is hidden while
  the document is absent.** The cache signal is `TmDistributionInfo | undefined` (on a first
  launch the document arrives only with the idle-time refresh, spec 0025 §2.3). Typing the context
  as non-optional keeps the pinned predicate `({ distribution }) => distribution.support.enabled`
  valid without a null check. The cost is that such an entry shows only once the document is
  stored.
- **Pin state: `unavailable`, not `no-access`.** The user's grants are not what hides the entry, so
  "You no longer have access" would be wrong. "This page no longer exists" is the closer fit.
- **The §2 table row** goes past the pinned list. It is where a distribution author looks first,
  and it is one row.

## G-4. Measured overflow (Review flag 4)

### Precedent reused

- **Spec 0023 §2 ("Recompute on resize only, never on input: chip collapse, …") and §4.3
  (`tm-chip-set` `overflow: 'collapse'`: "the fit is recomputed on resize and when the chips
  change, never on a keystroke")** — the library's existing measured-overflow pattern. Spec 0027
  §8.7 cites it and reuses its wording ("the fit is recomputed on resize and when the items
  change, never on hover, focus or …"). I reused the rule, not the component. The pinned bar's
  chips are links with roving focus, folder menu buttons and drag sources, and they overflow into
  a More menu, not a "+N" tooltip chip.
- **Spec 0034 §7.3** — "one coalesced `ResizeObserver`" for the chart instance. Spec 0027 uses the
  same single-observer phrasing.
- **Spec 0024 §7.6** — fonts are preloaded and their "metric-adjusted fallbacks keep the swap
  shift-free". Cited for the font-swap re-fit.
- **Render phases.** No unfrozen spec states an `afterRenderEffect` read/write convention.
  Frozen specs 0002 and 0005 use `afterRenderEffect` / `afterNextRender` only for aria pruning and
  overlay re-measure, not a phase rule. So §8.7 states the mechanism itself. One `afterRenderEffect`
  measures in its `read` phase, after the render's DOM writes, and sets a fitted-count signal.
  Angular renders that signal in the same tick, because `ApplicationRef.tick` re-runs change
  detection for views a render effect dirtied, so no layout read interleaves a write.

### Changes (all in spec 0027)

- **§8.1** — "More … shows past §8.7's count" → "shows when items overflow (§8.7)".
- **§8.7** — rewritten. The breakpoint count table is gone, replaced by:
  - **Lead.** As many leading items as fit; recomputed on resize and when items change, never on
    hover, focus or (from `md` up) the current page.
  - **Items and fit.** Top-level pins and folders in stored order fill the space from the bar's
    start to the pin control. More's width is reserved first when anything overflows.
  - **Widths.** A hidden, out-of-flow, `aria-hidden` measuring row renders each item at the
    current chip's weight (500), so becoming current never widens a chip. Widths are cached per
    item and re-read on a label, language or direction change, or on `document.fonts`
    `loadingdone`.
  - **Measuring.** One `ResizeObserver` on the bar, a pin change or a re-read width re-runs one
    `afterRenderEffect`, which reads in its `read` phase and sets the count, rendered in the same
    tick.
  - **First render.** The chips stay `visibility: hidden` inside the fixed 32 px row until the
    first fit. That fit lands in the tick that first renders the bar, so no unfitted frame paints
    and the hold never shows. A later font swap re-fits inside the fixed row (spec 0024 §7.6).
  - **Below `md`.** The pills fit the same way. The current pill leads (§8.1), so the fit there
    also follows navigation.
  - The More and current-pin-in-More bullets are kept.
- **§13** — the `TmPinnedBarHarness` "§8.7's counts" case is now the fit with long Arabic labels,
  in RTL, across a resize and a font swap, with the current pin in More, and unchanged by hover,
  focus or a navigation.
- **§14 Stable layout** — "overflow by count" → "overflow fitted before the first paint and never
  on hover or focus". `shell-layout.spec` adds "a hover along the pinned bar". CLS 0 on a warm
  launch is kept.
- **Decision 11** — "Measured overflow, fitted in the render's read phase; one tab stop for the
  bar", with a one-clause why.
- **Review flag 4** — "Measured overflow. Alternative: a fixed count per breakpoint. Flips if
  measuring costs frames or causes visible shifts."

### Deviations (G-4)

- **Measuring at the current chip's weight** goes past the pinned list. §8.1 gives the current
  chip weight 500. Without this rule, navigating would widen a chip and could push the last item
  into More, which breaks "never on the current page".
- **Below `md`, the fit follows navigation.** §8.1 puts the current pill first, so the shown set
  already depends on the page there. A fixed count would not avoid that, so I measure the same way
  everywhere and state the exception.
- **The first-render hold and the same-tick fit are both specified.** The fit alone keeps the
  first paint correct. The hold costs nothing and guarantees that no unfitted frame paints.
- 0027 grew by about 22 lines (1352 → about 1374). The growth is the new `visibleWhen` rule and the
  measuring mechanism, not justification.

## Notes for Ahmad

1. **`visibleWhen` while the `distribution` document is absent.** On a first launch, or after the
   idle window, the document arrives only with the idle-time refresh. Until then the Support
   requests entry is hidden. If you want it shown sooner, spec 0025 §2.3 could fetch
   `distribution` on a cold launch.
2. **Copy for a pin hidden by `visibleWhen`.** It reuses `unavailable`'s "This page no longer
   exists." The route still opens, so the copy is slightly off. A distinct line such as "This page
   isn't available here." would need a third state in `TmPinView.state`.
3. **Recents** (spec 0025 §13) may still list a page whose entry `visibleWhen` hides. I did not
   touch 0025. Say if recents should filter on it too.
4. **Render-phase wording.** No unfrozen spec had an `afterRenderEffect` read/write convention to
   cite. §8.7's mechanism ("`read` phase … renders in the same tick") is my statement of Angular
   22's behaviour. Worth a quick check against the Angular source when implementing.
