# r8 — editor B log (H-2, H-3)

All edits are unstaged; nothing staged or committed. Files stay LF, no BOM (checked).

## H-2 (Q2): font-swap line removed

- **Spec 0024 §7.6**, the Fonts bullet: deleted "metric-adjusted fallbacks keep the swap shift-free
  (spec 0002)". It now reads "- **Fonts.** `build` preloads them (§2.6)."
- **ARCHITECTURE.md**, Accessibility & i18n paragraph: the parenthetical "(with `unicode-range`
  subsetting, `font-display: swap`, and metric-adjusted local fallback faces for a
  layout-shift-free swap)" became "(with `unicode-range` subsetting and `font-display: swap`)".
  The rest of the paragraph is unchanged.

Greps (docs/specs/0010–0035 and ARCHITECTURE.md): `metric-adjusted`, `shift-free`,
`layout-shift-free`, `font-display`, `preloads them`, `fallback face`, `size-adjust`,
`ascent-override`, `layout shift`, `CLS`, and `swap` in 0023–0025. Only the two hits above made
the claim. The other hits do not depend on it:
- 0027 §8.7 and §14 (Stable layout, "CLS 0 on a warm launch"): the pinned bar hides chips until
  `document.fonts` is loaded, so these stay valid without metric-adjusted fallbacks.
- 0026 line 66 (layout shift as a later RUM item), and the zero-layout-shift test rows in 0024,
  0028 and 0035: these are about geometry, not font swaps.

## H-3 (Q3): recents skip pages the user can no longer access

### Where the rule lives and why

The rule is filtered **once at the source, in `TmRecents.pages` (spec 0025 §13)**.

Layering:
- `TmRecents` is in the `@tellma/core` primary entry point (0025 §1).
- Its two consumers are the launcher (0027 §9.3, in `@tellma/core/shell`) and the home page
  (0035 §15.3, in `@tellma/core/dashboards`). 0035 §1 says `/dashboards` imports only the primary
  and `/attention`, so the home page cannot use anything in `/shell`.
- 0027 §8.3 defined resolution only as a table, and only `TmPins` used it. `TmPins` is in
  `/shell`, so neither the primary nor `/dashboards` could call it.
- 0024 §2's import graph says the primary imports no other entry point.

So both placements needed the resolver in the primary. Filtering in each consumer would also have
stated the rule twice. Everything §8.3 reads is already in the primary: `TmUrls.describe` (0024
§9.4), `TmPermissions` (0029 §7) and `TmNavigation`'s `visibleWhen` evaluation (0027 §3.1). So
the resolver became one primary method, `TmNavigation.resolve(href)` in 0027 §3.1. It sits beside
`byHref`, which recents already used for title and icon. `TmRecents` (the primary) calls it, and
pins and recents share the same code. The state table stays in 0027 §8.3, which owns resolution.
0025 §13 owns the recents filter, and 0027 §9.3 and 0035 only cross-reference it.

### Changes

**Spec 0027**
- §3.1 contract: added the member
  `resolve(href: string): 'ok' | 'no-access' | 'unavailable'; // a stored URL's state (§8.3): pins, recents`.
- §8.3 intro: replaced "Each pin resolves on render from `TmUrls.describe('/' + t + '/' + url)`"
  with: `TmNavigation.resolve(href)` gives a stored URL's state from `TmUrls.describe(href)`. Pins
  resolve on render, and recents filter on it (spec 0025 §13).
- §8.3 table, `unavailable` row: "the pin's path" became "the URL's path". The heading
  "Resolving a pin" is unchanged.
- §9.3 item 2: dropped "skipping a page whose URL §8.3 resolves `unavailable`". The band now just
  shows `TmRecents.pages` (spec 0025 §13), which holds only openable pages.
- §13 tests:
  - The `TmNavigation` unit row gains "`resolve`'s §8.3 states, a URL hidden by `visibleWhen`
    included". That coverage moved there from the `TmPins` row, which loses it.
  - The `TmLauncherHarness` row: "a recent page hidden by `visibleWhen` and one hidden by a lost
    permission".

**Spec 0025**
- §13 `TmRecents` sketch comment: "the routed tenant's openable ones, newest first, at most 10".
- §13 new **Access** bullet: `pages` skips an entry whose URL `TmNavigation.resolve` (spec 0027
  §8.3) finds `no-access` or `unavailable`, and recomputes when access changes. The filter runs on
  read, so `nav.recent` keeps the entry for when access comes back.
- §14, the "Page state, drafts, tree, recents" row: the recents part gains "a `no-access` or
  `unavailable` page skipped, back when access returns, with `nav.recent` unchanged".
- Context dependency list: added "**spec 0027** — §3.1, §8.3: `TmNavigation.byHref` and
  `resolve`, which recents read." The list had no 0027 line, although §13 already cited §3.1.

**Spec 0035**: no edit needed. §9.2's `core.recents` row renders `TmRecents.pages()`, so the
filter reaches it. §15.3 and §15.4 add no rule of their own, and no 0035 test names recents
visibility.

### Greps run

- `recent` / `Recent` in 0025, 0027, 0035 and ARCHITECTURE.md (ARCHITECTURE line 53 is a glossary
  line, so it is unaffected).
- `unavailable|no-access` in 0025, 0027 and 0035.
- `§8.3` cross-references to 0027 across 0010–0035: only 0027's own §8.4 and §13 refer to it, and
  both still hold.
- `byHref`, `no-access`, `revoked pin` across 0010–0035 and ARCHITECTURE.md: no other spec states
  a recents or pin access rule.
- 0035 `Imports` (§1) and 0024 §2's import graph, to confirm the layering.

### Deviations from the pinned decisions

- The decisions offered two placements: the source, if the layering allowed it, or else each
  consumer. Neither worked as written, because the only §8.3 resolver was in `/shell`. I took the
  source option and added one primary member, `TmNavigation.resolve`, to 0027 §3.1, so that
  `TmRecents` can call the resolver.
- The decisions ask to update 0035's tests "if any". There are none, so 0035 is untouched.

## Notes for Ahmad

1. **New contract member.** `TmNavigation.resolve(href)` is a new member of the 0027 primary API.
   It moves §8.3's logic from `/shell` into 0027's 5 KB share of the primary. I left that budget
   unchanged because the code is small, but it is your call.
2. **Stored reports and dashboards still show after access is lost.** §8.3 counts them as `ok`
   ("Their page reports a refusal"). So a stored report the user can no longer open still appears
   in Recently visited, and opening it shows the page's refusal. This follows §8.3 as written. If
   recents should hide these too, `TmNavigation` would need the stored definition's access, and it
   does not have it.
3. **The stored list is not compacted.** Hidden entries still count against the cap of 10 in
   `nav.recent`. A user who lost access to several recent pages can therefore see fewer than ten
   until new visits push the hidden ones out. This follows from "the stored value is not pruned".
