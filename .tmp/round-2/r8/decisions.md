# Round r8 — Ahmad's answers to the r7 questions

Verbatim:

- Q1 yes, add the Part F amendment
- Q2 the line goes
- Q3 Skip pages the user can no longer access

The questions they answer (from the r7 report):

1. Spec 0012 §11.4 edits compiled Queryex text: it inserts the `[JsonColumn]` projections
   (`[T].[DumpJson]`) at the end of the compiled select list, before the `FROM` line, which depends
   on spec 0008's statement layout. Cleaner: a spec 0010 Part F amendment so that `CompileQuery`
   accepts extra root columns.
2. Spec 0024 §7.6 says "metric-adjusted fallbacks keep the swap shift-free (spec 0002)", but frozen
   spec 0002 defines no metric-adjusted fallbacks and preloads only Latin. Spec 0027 §8.7 no longer
   relies on it (the pinned bar waits for `document.fonts`).
3. Recently visited still shows pages the user can no longer access (spec 0025 §13 doesn't filter
   on access). Spec 0027 §9.3 currently skips only pages that resolve `unavailable`.

## H-1 (Q1). Part F amendment: raw root columns in `CompileQuery`

- Add a new item to spec 0010 Part F, after §F.15 (so §F.16), in exactly the format of the other
  Part F items (Target / Requested by / Why, the change, a sketch, compatibility, SQL block with
  shortened layout). Read §F.0 (shared rules, additivity, goldens) and two neighbouring items
  (e.g. §F.3 and §F.14) first and match them.
- The change: `QuerySpec` (spec 0008 §2.1) gains an optional member naming raw root columns — the
  columns of the root table that the Queryex schema omits (the host's `[JsonColumn]`s). Proposed
  shape: `IReadOnlyList<string> RawColumns` (default empty); pick the name that fits 0008's
  vocabulary and say so in the log. Semantics:
  - each name is a physical column of the root's table, validated as a SQL identifier (bracketed by
    the emitter; a caller error, `ArgumentException`, for an invalid identifier or a duplicate);
    the engine does not check it against the schema (the schema omits it by design) — the host
    owns its existence;
  - the emitter writes `[T].[<name>]` (the root alias of 0008 §12.2/§13.4) after the compiled
    select items, in the given order, and the `CompiledQuery`'s column list gains one column per
    name after the compiled ones: type String, nullable, no path (use 0008's actual column type
    names and members);
  - only for a non-aggregate (row-grain) query; with grouping, `Measures` or aggregates present it
    is a caller error. Check how 0008 distinguishes these and phrase it in its terms;
  - `DiscoverQuery` ignores it (or reports it — decide by what the host needs: 0012 §11.4 runs
    `DiscoverQuery` on the select text first);
  - additive: empty → byte-identical SQL (§F.0).
- Part F's Testing, Definition of done, Decisions record and Review flags (and Part F's intro
  sentence that counts the items, "seven changes … eight more") gain what the item needs — keep
  it proportionate.
- Spec 0012 §11.4: rewrite the "Every mapped property" paragraph to pass the root's `[JsonColumn]`
  column names through the new `QuerySpec` member instead of editing the compiled `Body`; the
  reader and materializer text (`QueryColumn` per column, set by name in §11.5) then follows from
  the compiled columns. Keep the child-query rule (each child query takes the flag and passes its
  own table's `[JsonColumn]`s). Cite spec 0010 §F.16. Drop "before its `FROM` line" and anything
  else that assumed text editing. Update 0012's Context dependency list if it enumerates the Part F
  items it uses.
- Check spec 0012 §13.1 golden-SQL wording still holds.

## H-2 (Q2). Remove the font-swap line

- Spec 0024 §7.6: delete "metric-adjusted fallbacks keep the swap shift-free (spec 0002)"; keep
  "`build` preloads them (§2.6)" as a complete bullet.
- ARCHITECTURE.md (Accessibility & i18n paragraph) makes the same claim: "and metric-adjusted local
  fallback faces for a layout-shift-free swap". Remove that clause so the living document matches
  (it describes the font stylesheet's contents; keep the rest of the parenthetical).
- Grep docs/specs/0010–0035 for any other "metric-adjusted" / "shift-free swap" claim that relied
  on it and fix or report.

## H-3 (Q3). Recents skip pages the user can no longer access

- A recent page whose URL resolves (spec 0027 §8.3's pin resolution) `no-access` or `unavailable`
  is skipped. Decide where it is filtered: once at the source (`TmRecents.pages`, spec 0025 §13) if
  that dependency direction is allowed (check which entry point / layer owns `TmRecents` and the
  §8.3 resolver), else in each consumer — spec 0027 §9.3 (the launcher's Recently visited band and
  the typed-state table that lists recent pages) and spec 0035 §15.3 (the home page). State the
  rule once in the owner; others cross-reference.
- The stored `nav.recent` value is not pruned (access can come back); filtering is on read and
  recomputes when access changes. Say so in one line if the owner doesn't already.
- Update the affected §13 test rows (0027's `TmLauncherHarness` line "a recent page hidden by
  `visibleWhen`", 0025's recents tests, 0035's if any).

## Rules

Specs 0001–0009 are frozen; don't edit them (reading is fine). Don't stage or commit. Touch only
`docs/specs/`, `ARCHITECTURE.md` and `.tmp/round-2/r8/`.
