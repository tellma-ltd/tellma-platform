# Residue pass — last fixes before the readability pass

One line per edit: item — file § — what changed.

## F-3 (ruled ACCEPTED: a cold tenant entry awaits the cold refresh before the page host; warm awaits nothing)

- F-3 — 0024 §4.4 — new "Cold entry" row owns the rule: while `TmLaunchCache.warm()` is false the
  route host keeps the skeleton and awaits 0025 §3.2's cold refresh before creating the page; the
  page chunk and `TmEntities.load()` load meanwhile; the page's data read starts after the refresh;
  a warm entry awaits nothing; no page, `tmPage` permission gate or home runs before the first `me`
  and `settings`; a failed refresh follows 0025 §3.2's failure rows.
- F-3 — 0024 §2.4 `securable` row — "The route host checks it only after a cold entry's wait (§4.4)."
- F-3 — 0024 §4.3 — step 3 adds "(on a cold entry, after the launch documents, §4.4)"; the
  "one network wait" sentence becomes: a warm launch has none; a cold launch at most two, each
  under a skeleton (the launch redirect §9.3, the cold tenant entry §4.4).
- F-3 — 0024 §14 Stable layout row — how: a cold entry holds the route skeleton until the launch
  documents arrive (§4.4); pin: `cold-deep-link.spec` (a cold deep link to a details page renders
  the record, never a forbidden flash). Warm-launch row kept.
- F-3 — 0024 Decisions record — new 17 (a cold tenant entry creates the page only after the launch
  documents arrive). Review flags — new 14 (alternative: read beside the sync and re-plan).
- F-3 — 0025 §3.2 cold row — the refresh "starts before the page is created; the page's data request
  follows the documents (spec 0024 §4.4)"; failure rows unchanged.
- F-3 — 0027 §7.5 cold-pending row, `main` — "the route host's skeleton until the sync answers, then
  the page (spec 0024 §4.4)".
- F-3 — 0029 §7 — verified already applied (the route host never creates a page host before the
  first `me` and `settings`, citing 0024 §4.4); no change.
- F-3 (consistency) — 0031 §3.1 "Route host" bullet — the host is created after `TmEntities.load()`
  and, on a cold entry, after the launch documents arrive (cites 0024 §4.4).
- F-3 (consistency) — 0035 §15.4 cold row — situation names the route host's `home` skeleton (spec
  0024 §4.4); Recents cell drops "until `me` arrives" (home is never created before `me`).

## Sweep: items found unresolved and fixed

- F-4 (residue) — 0025 §3.2 Example step 2 — the boot step reads the user partition and tenant 17's
  partition in one transaction (was tenant partition only, against §2.6).
- F-6 / K-0034a (residue) — 0024 §9.2 Page routes row — dropped "which the details host and the
  report studio register": the studio now sits inside the details host (0034 §12.1).
- F-6 / K-0034a (residue) — 0031 §9 `TmDetailsDialogs` sketch comment — dropped "also used by the
  report studio (spec 0034 §12.1)"; the studio gets the dialogs through `tm-details-page`.
- F-6 / K-0034a (residue) — 0031 §11.3 `tmLeaveGuard` sketch comment — "the host calls it, for every
  page it hosts (the report studio's included, spec 0034 §12.1)" (was "so does the report studio").

## Process words (0010, 0023–0035)

- Process words — 0024 Decisions record 12 — "the design's "19 Jun 2026"" → "dates read "19 Jun
  2026", not "Jun 19, 2026"".
- Grep over 0010 and 0023–0035 for brief question ids (Q1/Q2, "Details Q2", "Search Q1"), "the
  brief", "design canvases", "the design draws/'s", "as designed", rule ids (2A–2F, "rule 2…"),
  rounds, reviewers, rulings, addenda, ARCHITECTURE.md: no other hit. Remaining "Q1"/"Q3" are
  quarter labels in examples; "rule N" hits cite numbered rules inside spec 0033 §3.4 and 0035 §5.

## Sweep: verified resolved (no edit)

- known-issues: K1, K3, K21, K-F, K48, K5, K7, A7, K49, K11, K50, A2, A3, K14, K13, K44, K43a, K17,
  K18, K31, K32 (0027 §9.4 reads English through Transloco's `translate(key, {}, 'en')`), K38, K39,
  K33, K24, K22, K23, K15, K37, K42, K43, K30, K26, K12, K-0032a, K-0032b, K-0034a, K-0035a,
  K-0035b, K20.
- addenda 1–10: all resolved in the current text.
- final-review: F-1 (`IDataBatch.EvaluateConstant`, 0012 §5.2; no `IDataBatch.Constant` anywhere),
  F-2 (`DetailsPlan.AddComputedExtra`, 0015 §5.3; cited by 0018 §4.3, 0020 §7.8, 0032 §5.5, 0033
  §5.2, 0035 §6.1), F-4, F-5, F-6, F-7, F-8, F-9, F-10, F-11, F-12, F-13, F-14, F-15, F-16, F-17,
  F-18, F-19, F-20, F-21, F-22, F-23, F-24, F-25, F-26, F-27 — resolved, apart from the residue
  fixed above. Accepted fixer deviations stand: F-12's named export not added; F-5's "not
  pinnable" not added; F-8's `activate`/`visible` changes not taken (K-0032b names Delete only).

## Left open

- `.tmp/round-2/design/00-architecture.md` §7.1 step 5 still says `client/sync` and the page's data
  request "start together"; the F-3 ruling supersedes it. Not edited (a design doc, not a spec).
