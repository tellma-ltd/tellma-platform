# D-3 verification: page state survives a permissions move

Checked against `applied-pagestate.md`, `git diff HEAD` and a full read of every edited section.
Confirmed right, no finding: spec 0025 §10's permissions/settings bullets, sketch comment, test
row, Decision 15, flags renumbered 1–12 with no cross-spec reference to them (the "flag 10/12"
hits in spec 0027 are self-references); spec 0029 §5.3 "Tag moves"; spec 0030 §5.4; spec 0032
§7.7 steps 3–7 (no step is cited by number); spec 0034 §3.7; no spec 0023–0035 or
`ARCHITECTURE.md` text still forgets page state or clears display names on a permissions move.
Files are LF, no BOM.

## docs/specs/0025-client-state-and-cross-tab-sync.md

- [P-01] (should) 0025 §10 - "**A stale entry** restores query, scroll and selection, renders its
  held rows at once, and refetches them. Each host in the table below defines this" -> replace
  the first sentence with "**A stale entry** renders its held state at once, then refetches its
  data." and keep the second. Why: D-3 (each host defines a stale entry). Details (spec 0031 §13),
  report (spec 0034 §3.7) and dashboard (spec 0035 §11.4) entries hold no query or selection, and
  the sentence restates search's own rule (spec 0030 §15.2).
- [P-02] (should) 0025 §10 - "forgets every entry of `t`. Their rows may hold a shape the tenant's
  languages no longer admit." -> delete the second sentence. Why: Decision 15 already records this
  reason ("a settings change can change the rows' shape"), and the permissions bullet beside it
  gives none; rationale lives only in the Decisions record (decisions.md preamble).

## docs/specs/0029-client-entity-model.md

- [P-03] (should) 0029 §17, row "descriptors, permissions" - "`TmDisplayNames.label` given a
  `TmDecimal` id for an `int` key, and a number for a `long` key" -> append "; the
  `TmDisplayNames` cache cleared by a `settings` move and kept by a `permissions` move". Why: D-3.
  §5.3 "Tag moves" now splits the two tags, and no suite pins it (spec 0025 §14 pins the
  page-state split).

## docs/specs/0030-search-page.md

- [P-04] (should) 0030 §15.2 step 2 - "When `TmPageState.isStale(key)` holds (a write on the entity
  or a permissions move, spec 0025 §10)" -> "When `TmPageState.isStale(key)` holds (spec 0025
  §10)". Why: spec 0025 §10 owns the causes; this copy is the one D-3 had to edit, and any new
  cause would need the same edit here.

## docs/specs/0031-details-page.md

- [P-05] (must) 0031 Decisions record 11 - "**Returning restores the remembered record with no
  request** — unless a write marked it stale" -> replace "unless a write marked it stale" with
  "unless spec 0025 §10 marked it stale". Why: D-3 — a permissions move also marks the entry stale
  (spec 0025 §10, "A permissions move marks entries stale"); §13's "Stale" bullet already says so.
- [P-06] (should) 0031 §12.2 "At an edge" step 3 - "an empty answer disables the arrow. Otherwise
  the host remembers a new search snapshot" -> replace "an empty answer disables the arrow." with
  "an empty answer or a failed read disables the arrow, and the current record stays." Why: D-3
  now keeps the search snapshot through a permissions move (stale, spec 0025 §10). Its `request`
  may select a column the user can no longer reach, which `query` refuses with `403` (spec 0015
  §5.2's traversal rule). The steps define no failure.

## docs/specs/0034-report-renderers-and-studio.md

- [P-07] (should) 0034 §14 host harness - "page-state restore, the `modifiedAt` discard, and the
  run after 5 minutes or on a stale entry;" -> replace "on a stale entry" with "on an `isStale`
  entry". Why: §3.4 uses `stale` for a failed refresh, and the bullet just above lists "`held`,
  `stale` and the `forbidden` clear"; §3.7 names `isStale`.

## docs/specs/0035-dashboards-and-home-page.md

- [P-08] (must) 0035 §11.4 - "renders the same way, then runs every tile silently, as a result
  older than 5 minutes does." -> "renders the same way, then silently runs every tile that has a
  held result, whatever its due time." Why: "every tile" includes the built-in lists, which only a
  forced refresh ticks (§9.2 "Only a forced refresh ticks them", §11.1 "never ticked"), and bodies
  that never report (§9.3). `held` holds report results only (§11.4, first bullet).
- [P-09] (must) 0035 §17, Scheduler row - "the page-state return, and a stale entry running every
  tile" -> "the page-state return, and an `isStale` entry running every tile with a held result
  while the built-in lists stay untouched". Why: follows P-08 (§9.2). "stale" alone collides with
  the tile state `stale` (§16 telemetry) and the stale marker (§8.5).

## Notes for Ahmad

- **Every settings save forgets page state**, not only a language change. An invoice-prefix save
  also drops every tab's scroll and selection for the tenant (spec 0025 §10, spec 0032 §7.7 step
  3). D-3 keeps this rule. Narrowing it to a change of the tenant's languages would need the
  client to compare the old and new settings documents. Only you can decide whether that is worth
  it.
- **P-06 is a small design choice.** The alternative to disabling the arrow is to fall back to
  paging within `ids`, as with `request: null`. Either option closes the gap.
