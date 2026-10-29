# Spec 0026 follow-up and spec 0027 feedback — pinned decisions

Ahmad's answers, verbatim (to the questions left after the spec 0026 feedback round):

- Q1. The wording you used is fine. *(no edit: "The {displayName} support team has been notified.")*
- Q2. Add a user notice when the page data is dropped.
- Q3. Add the visibility rule.
- Q4. *(not answered: the daily-limit race stays as written)*
- Q5. No thumbnails is fine. *(no edit)*
- Q6. Spell it out in 0012 §11.4. When an `EntityQuery` asks for every mapped property, the host
  compiles the Queryex part as it does now. It then appends the root's `[JsonColumn]` columns as
  plain column projections (`[root].[DumpJson]`) to the same statement, and the materializer sets
  them by name.

Spec 0027 feedback, verbatim:

- Review flag 4 - Measure, don't count

## G-1 (Q2). Notice when the dump builder drops page data — spec 0026

Context: §10.3's size cap drops, in order, (1) `visible.rows`, (2) `telemetry`, (3) the rest of the
page data (`includesPageData` false, `pageData` in `truncated`), (4) `sections`. Step 3 can run while
the user left "Include the data shown on the page" ticked.

- When step 1 or step 3 runs while the box is ticked, the compose form tells the user, as an inline
  `tm-alert` (tone info, under the checkbox, polite announcement): step 1 alone — "The page's rows are
  too large to send, so they're left out."; step 3 — "The data shown on the page is too large to
  send, so it's left out." Pick the final copy in the spec's voice; keep it short.
- After step 3, the checkbox shows unticked and disabled, and the warning takes its unticked variant
  (§12.2), so nothing on screen claims page data that is not sent.
- The dump is built when the dialog opens and rebuilt when the box changes (verify how §10.4/§12.2
  sequence it); the notice reflects the dump that Send will submit. Name a test in §13.
- Telemetry: `tm.support.sent` already carries `pageData`; add nothing unless needed.

## G-2 (Q3). A visibility rule for navigation entries — spec 0027 (and its declaration owners)

- `TmNavEntry` (and the flat navigation members on `tmEntity`, `tmPage`, `tmCodedReport` and
  feature `entries`, spec 0027 §3.2) gains `visibleWhen?: (context: TmNavContext) => boolean`.
- `TmNavContext` carries `readonly distribution` — the client's cached `distribution` document type
  (spec 0024 / spec 0025 §3.1; find its exact name; it carries `support { enabled, providerAccess }`).
  Add nothing else unless a second real consumer exists.
- An entry is visible when its permission rule passes and `visibleWhen` (when present) returns true.
  `TmNavigation` re-evaluates it when the `distribution` document changes, besides its existing
  triggers (§3.1 "Recomputed").
- A pin to an entry hidden by `visibleWhen` resolves as spec 0027 §8.3/§8.12 resolve an unavailable
  pin (check which state fits: revoked vs unavailable) — decide and state it once.
- `visibleWhen` hides navigation only; it is not access control and does not unroute the page (the
  server still governs access). Say so in one line.
- Spec 0026 §12.5: the Support requests entry declares `visibleWhen: ({ distribution }) =>
  distribution.support.enabled`, replacing the `hide: ['entry:core.SupportRequest']` sentence.
- Wherever the declaration contracts live (spec 0024 §2.x for `tmPage`/feature `entries`/`hide`,
  spec 0029 for `tmEntity`, spec 0034 for `tmCodedReport` — verify), add the member there or state
  that spec 0027 §3.2's flat members include it, whichever matches how `section`/`order`/`icon` are
  documented today (state each fact once).

## G-3 (Q6). `[JsonColumn]` in entity queries — spec 0012 §11.4

- Spell out in §11.4, in the spec's voice, exactly Ahmad's rule: when an `EntityQuery` asks for
  every mapped property, the host compiles the Queryex part as now, then appends the root's
  `[JsonColumn]` columns as plain column projections (`[root].[DumpJson]`) to the same statement, and
  the materializer (§11.5) sets them by name.
- Find how an `EntityQuery` "asks for every mapped property" today (spec 0015 §5.3's details read,
  spec 0016 §3.6). `EntityQuery.Select` is a required string of bare paths (spec 0012 §2-ish
  contract). If there is no explicit signal, add the smallest one to the `EntityQuery` sketch (e.g.
  a `bool` member) and make spec 0015 §5.3 set it; report the choice. Only the root's JSON columns
  (not children's) unless the specs show a child consumer — check specs 0020 (`Schedule.ArgumentsJson`),
  0033 (`DefinitionJson`), 0035 (`ParametersJson`, `SettingsJson` — root or child?), 0013 (settings
  `Value`).
- Make the specs that rely on it cite §11.4 where they currently state the behaviour by inference
  (spec 0026 §12.3 "Reading the dump", and any of 0015/0016/0020/0033/0035 that state it). State the
  fact once in 0012 §11.4; elsewhere cross-reference.
- Spec 0012 §11.5 (the materializer) gets the one line on setting them by name, if §11.4 doesn't
  already cover it.
- Spec 0012 §13 tests: add the case (a `[JsonColumn]` on a fixture root, read by the every-property
  query, absent otherwise). The shared fixture has `fixture.Widgets.Settings [JsonColumn]`.

## G-4. Spec 0027 Review flag 4 — measured overflow

Today (§8.7): a fixed count of leading items per breakpoint (10/8/6/4/2), "the count never depends
on label widths…, so nothing reflows". Decision 11 and Review flag 4 record that.

Replace with measured overflow:
- The bar shows as many leading top-level items (pins and folders, stored order) as fit in the
  space between the bar's start and the pin control, reserving More's width whenever anything
  overflows; the rest go into More. Chips keep their 120 px label cap.
- Measurement: each chip's width is measured once per label, language/direction and font change
  (e.g. a hidden measuring row, or the rendered chips' widths cached), and the fit is recomputed when
  the bar's inline size changes (`ResizeObserver`) and when the pins change. Never on hover, focus or
  the current page, so the chips never jump while the user points at them. Use Angular 22's
  read/write render phases (`afterRenderEffect` / `afterNextRender`) as the platform's other
  measured components do — find a precedent in the platform specs (component library specs
  0002–0005, spec 0010 Part C/D, spec 0031's toolbar, or any "overflow" / "priority+" pattern) and
  reuse its mechanism and wording; cite it.
- First render: decide how the bar avoids a visible reflow before fonts and widths are known (e.g.
  hold the chips `visibility: hidden` within the fixed 32 px row until the first fit, or start from a
  conservative count and only ever grow). State it; it must keep the Stable-layout rule (§14) true.
- Below `md` (pills): measure the same way, or keep a count if the pills row has a reason to differ;
  decide and state it.
- Update: §8.1 ("shows past §8.7's count"), §8.7, §13 (the `TmPinnedBarHarness` "§8.7's counts"
  case → widths: long Arabic labels, RTL, resize, font swap, the current pin in More), §14's Stable
  layout row, Decision 11, and Review flag 4 (now: measured; alternative: by count per breakpoint;
  flips if measuring costs frames or causes visible shifts).

## Rules

Specs 0001–0009 are frozen; don't touch them. Don't stage or commit. Touch only `docs/specs/`,
`ARCHITECTURE.md` (only if it states something now stale) and `.tmp/round-2/r7/`.
