# D-3 applied: page state survives a permissions move

Greps run over specs 0023–0035 and `ARCHITECTURE.md` for "0025 §10", `TmPageState`, `markStale`,
`isStale`, "page state", "forget(ten)", `TmDisplayNames`, the `permissions`/`settings` tag-move
wording, and references to spec 0025's review flags. `ARCHITECTURE.md` and spec 0024 have no
page-state or display-name-cache mention. No spec cites spec 0025's review flags by number.

## Edits

### spec 0025 (`docs/specs/0025-client-state-and-cross-tab-sync.md`)

- **§10 sketch, `markStale` comment:** "after a save, delete, import or action" -> "after a write
  or a permissions move".
- **§10 bullets:** the bullet "Permissions or settings moved" (every entry of `t` forgotten on
  either tag; reason: data the user can no longer see, or a shape the languages no longer admit;
  "Spec 0029 §5.3 clears display names on this rule") -> replaced by three bullets placed after
  "Writes mark entries stale":
  - "A permissions move marks entries stale": `TmPageState` calls `markStale({ tenantId: t })`, so
    every entry of `t` refetches on return;
  - "A settings move forgets entries", with the shape reason (the tenant's languages);
  - "Display names follow the same split (spec 0029 §5.3)".
- **§10 "A stale entry":** "restores query, scroll and selection, and refetches its rows" ->
  "restores query, scroll and selection, renders its held rows at once, and refetches them. Each
  host in the table below defines this for its own state."
- **§14 test table, "Page state, drafts, tree, recents" row:** "tag-move invalidation" -> "a
  permissions move marking every entry of the tenant stale, a settings move forgetting them".
- **Decisions record:** added 15, "A permissions move marks the tenant's page state stale; a
  settings move forgets it" — the held rows were read while the user had access and the refetch
  corrects them; a settings change can change the rows' shape — (§10).
- **Review flags:** deleted flag 8 (page state drops entries on either tag move). Renumbered 9–13
  -> 8–12. No other spec or section referenced flags 8–13 by number.

### spec 0029 (`docs/specs/0029-client-entity-model.md`)

- **§5.3 "The cache" bullet:** "A change of the held `permissions` or `settings` tag clears it
  (spec 0025 §10)" -> removed from that bullet; new bullet "Tag moves": a `settings` move clears
  the cache (the tenant's languages decide what each name slot holds); a `permissions` move keeps
  it (its names were read while the user had access). No longer cites spec 0025 §10 as the source.

### spec 0030 (`docs/specs/0030-search-page.md`)

- **§5.4 "Permissions or settings tag moved":** "At the same moment, spec 0025 §10 forgets the
  tenant's page state" -> "The tenant's other page-state entries follow spec 0025 §10."
- **§15.2 step 2:** `isStale` cause "(a save, delete, import or action on the entity, spec 0025
  §10)" -> "(a write on the entity or a permissions move, spec 0025 §10)".

### spec 0032 (`docs/specs/0032-stock-pages.md`)

- **§7.7 steps:** step 3 "A language change … and spec 0025 §10 forgets the tenant's page state"
  -> split in two:
  - new step 3 "Page state. Every tab forgets the tenant's page state once it holds the new
    `settings` tag (spec 0025 §10)". It applies to every settings save, which is where the
    forgetting comes from; the old text tied it to the language change and its `permissions` bump;
  - step 4 "A language change also bumps `permissions` …, and descriptors follow the new shape"
    (page-state clause removed).
  - Later steps renumbered 4–6 -> 5–7. No spec cites §7.7 steps by number.

### spec 0034 (`docs/specs/0034-report-renderers-and-studio.md`)

- **§3.7 "Return":** "State older than 5 minutes renders and also runs, as reloading" -> "State
  older than 5 minutes, or an entry that spec 0025 §10 marks stale (`isStale`), renders and also
  runs, as reloading."
- **§14 host harness bullet:** "the 5-minute run" -> "the run after 5 minutes or on a stale entry".

### spec 0035 (`docs/specs/0035-dashboards-and-home-page.md`)

- **§11.4:** added bullet "A stale entry (`isStale`, spec 0025 §10) renders the same way, then runs
  every tile silently, as a result older than 5 minutes does." A `403`/`404` on that run already
  clears the held data (§11.2, spec 0034 §3.4).
- **§17 Scheduler suite:** "the page-state return" -> "the page-state return, and a stale entry
  running every tile".

### Checked, no change

- spec 0031 §8.3 step 3 and §13 "Stale": already "the held record renders at once, and a read
  refreshes it", which covers a permissions move.
- spec 0030 §12.3 step 3 (`markStale` after a bulk action) and §18 ("a stale one re-reads").
- spec 0024 §9.5, spec 0028 §7.3, spec 0033 (`TmDisplayNames` uses only).

## Open questions

- **A settings move forgets page state on every settings save**, not only a language change (an
  invoice-prefix save also drops every tab's scroll and selection for the tenant). This is the
  unchanged rule; spec 0032 §7.7 now states it plainly. Narrowing it to a change of the tenant's
  languages would need the client to compare the old and new settings documents.

## Requests for other files

None.

## Fix pass

- P-01 -> applied (0025 §10 "A stale entry" first sentence; the second now reads "defines both",
  covering S-08's addition).
- P-02 -> applied (0025 §10 settings bullet; the reason stays in Decision 15).
- P-03 -> applied (0029 §17 "descriptors, permissions" row).
- P-04 -> applied (0030 §15.2 step 2 cites spec 0025 §10 only).
- P-05 -> applied (0031 Decisions record 11).
- P-06 -> applied (0031 §12.2 step 3: "an empty answer or a failed read disables the arrow"). The
  "current record stays" clause was left out: step 1 already says it. The alternative (fall back
  to paging within `ids`) is for Ahmad if he prefers it.
- P-07 -> applied (0034 §14 host harness).
- P-08 -> applied (0035 §11.4 "A stale entry").
- P-09 -> applied (0035 §17 Scheduler row).
- S-08 -> applied, reworded as a host-neutral rule in 0025 §10: a refetch refused with `forbidden`
  or `not-found` leaves none of the refused data in the entry. The literal "forgets the entry"
  would be wrong for the dashboard, where a `403` clears one tile, not the whole entry (0035
  §11.2). Report and dashboard already meet the rule (0034 §3.3 clears the result and §3.7
  remembers on destroy). Search and details forget (S-09, S-10).
- S-09 -> applied (0030 §15.1: two bullets, `forget` on `forbidden`/`not-found`, else `result`
  holds the rows still shown or null; the sketch's `result` comment points at §15.1; §15.2 step 1:
  `result: null` reads the restored query as a first load; §18 harness pins the `403` forget).
- S-10 -> applied (0031 §13: new bullet "Refused", which also covers the draft's stamp-check read,
  not only a stale one).
- S-11 -> applied, with the rule placed in its owner: 0035 §6.2 new bullet "A re-read applies its
  `view`" (a forced refresh or a stale return re-lays out the visible set; a tile that turned
  hidden leaves and its held result is dropped). 0035 §11.4's stale bullet points at it.

For Ahmad (not applied; no finding ID):

- Every settings save forgets the tenant's page state, not only a language change, and
  `Refresh caches` (spec 0013 §6.4) does too, since it rewrites the `settings` tag. Narrowing this
  needs the client to compare the old and new settings documents (verify-pagestate note 1,
  verify-scenarios note 1).
- Spec 0013 §6.4 publishes `cache.changed { tag: 'settings' }` with no value, and spec 0025 §4.4
  handles only the valued form (verify-scenarios note 6; outside D-3).

## Readability pass

- 0025 §10 sketch comment, permissions/settings/display-names bullets: no change.
- 0025 §10 "A stale entry": split into three bullets ("A stale entry", "A refused refetch",
  "Hosts"); "defines both" -> "defines the two rules above".
- 0025 §14 test row, Decision 15, Review flags renumbering: no change.
- 0029 §5.3 "Tag moves", §17 row: no change.
- 0030 §5.4, §15.1 sketch comment, §15.2 steps 1–2, §18 bullet: no change.
- 0030 §15.1 opener: "the snapshot follows the region" -> "the snapshot matches what the results
  region shows"; "calls `forget` instead" -> "calls `forget` instead of `remember`".
- 0031 §12.2 step 3, §13 "Refused", Decision 11: no change.
- 0032 §7.7 steps 3–4: no change.
- 0034 §3.7 "Return": "State older than 5 minutes, or an entry that spec 0025 §10 marks stale
  (`isStale`)" -> "State that is older than 5 minutes or marked stale (`isStale`, spec 0025 §10)".
- 0034 §14 host harness bullet: no change.
- 0035 §6.2 "A re-read applies its `view`": last sentence split ("leaves the grid. Its held result
  is dropped, as for a removed key.").
- 0035 §11.4 "A stale entry": "§6.2 applies the new `view`" -> "the grid applies the new `view`
  (§6.2)".
- 0035 §17 Scheduler row: ", and an `isStale` entry" -> "; an `isStale` entry" (matches the row's
  list).
