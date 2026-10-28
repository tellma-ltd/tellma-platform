# Review round 1, follow-up 2 (27 October 2026): binding decisions

Ahmad's words are quoted; the interpretation under each is binding for every agent in this round.

All spec work happens in `C:\Users\ahmad\workspace\tellma\tellma-platform`. Specs 0001–0009 are
frozen: never edit them; a change to what they specify goes into spec 0010 (Part A amends 0003,
Part B 0002, Part C 0004, Part D 0005, Part E 0006, Part F 0008). Specs 0010–0035 and
`ARCHITECTURE.md` are edited in place. Follow `docs/AGENTS.md` ("Revising specs across review
rounds": replace, don't append; no history narration; each fact once, in the section that owns
it; rationale only in the Decisions record) and the readability rules in
`.tmp/round-2/style-guide.md`.

## D-1. Text comparisons in measures stay refused

> "Keep text comparisons inside measures and outside aggregation functions refused with QX4010"

No edit. Spec 0010 §F.15's subset table already refuses them. Nothing may claim otherwise.

## D-2. Every database on `Latin1_General_100_CI_AS`

> "Yes everything on the same collation `Latin1_General_100_CI_AS`"

Context: the previous step (already applied) made every **tenant** database
`COLLATE Latin1_General_100_CI_AS` at `CREATE DATABASE` (spec 0011 §6.1 `provision`, Decision
32; spec 0012 §13.2's fixture database; `ARCHITECTURE.md` "Migrations & seeding"). The reason:
the server default `SQL_Latin1_General_CP1_CI_AS` defines no order on Amharic text.

Now the same collation applies to **every database the platform creates**:

- the tenant databases (done);
- the **catalog** database of every distribution (spec 0011), wherever it is created: the
  migrator, the development path, Azure infrastructure, test fixtures;
- the **identity** server's database (spec 0003, implemented and frozen, so the change is an
  amendment in spec 0010 Part A), in standalone and in in-proc mode (where its `idsvr` schema
  lives in the catalog database);
- development and test databases that stand in for those (fixtures, LocalDB, containers), so
  tests run under the production collation.

Explicit column collations that exist for a reason (for example `Latin1_General_100_BIN2` on
`Subject` and session keys, the UTF-8 JSON wire columns of spec 0001, the Queryex fixture's
per-column collation) stay as they are unless research shows they conflict.

State the rule once, in its owner, and cross-reference it elsewhere. Spec 0011 owns the rule for
the catalog and tenant databases; spec 0010 Part A owns the identity database's amendment.
Decision 32 of spec 0011 widens from tenant databases to every database.

## D-3. Page state survives a permissions move

> "I also reviewed spec 0025 and I have one change in Section 10, when permissions move, don't
> delete the tenant entries, the data was queried when the user had access."

Spec 0025 §10 today forgets every entry of tenant `t` when `t`'s held `permissions` or
`settings` tag changes; its Review flag 8 names the alternative "keep them and let the refetch
correct them". Ahmad chose that alternative for permissions:

- **A `permissions` move marks every entry of the tenant stale** (the existing `markStale` with
  no `entity`): the entry restores query, scroll and selection, renders its rows at once, and
  refetches them under the new permissions.
- **A `settings` move still forgets** the tenant's entries, because a settings change can change
  the rows' shape (the tenant's languages). Ahmad did not ask to change this.
- **Spec 0029 §5.3's display-name cache follows the same principle**: a `permissions` move keeps
  it (its names were read when the user had access); a `settings` move still clears it. Today it
  says it clears on either tag "on spec 0025 §10's rule", which would otherwise become a false
  citation.
- **Every host that keeps page state must define what a stale entry does**, since a permissions
  move now marks entries of every kind stale, not only search and details entries after a write.
  Search (spec 0030 §15) and details (spec 0031 §13) already define it. The report page (spec
  0034 §3.7) and the dashboard (spec 0035 §11.4) must say it too; the natural rule is "renders the
  held result and also runs", as their existing "older than 5 minutes" rule does.
- Spec 0025's Review flag 8 is resolved, so it leaves the flags (renumber the later flags and
  every reference to them), and a Decisions record entry states the rule with its why.
- Every other mention of the old rule (for example spec 0030 §5 "spec 0025 §10 forgets the
  tenant's page state", spec 0032's settings page, spec 0025's test table) is corrected.
