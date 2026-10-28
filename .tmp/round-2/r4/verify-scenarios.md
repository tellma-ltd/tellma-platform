# Scenario review: D-2 (collation) and D-3 (page state)

End-to-end walk-throughs over the current files, 27 October 2026. Findings the line-by-line
verifiers already made (`verify-collation.md` C-01–C-14, `verify-pagestate.md` P-01–P-09) are not
repeated. "Fact §n" cites `facts-collation.md`; file:line cites this repository.

## collation

Walk-through outcomes (a finding only where one is listed below):

- **(e) Azure SaaS.** `migrate` creates `tellma-acme-catalog` with §6.7's full statement, then
  `provision` creates the first tenant with the same template. Both statements must run in
  `master` on the target server; no spec says where the migrator connects or what right it needs
  (S-02). Neither database gets `READ_COMMITTED_SNAPSHOT ON` from the migrator; Azure defaults it
  on, so only the other scenarios see the gap (S-01).
- **(f) On-prem, DBA-created.** The database passes §6.7's check and is migrated, but it is never
  "fresh" in §6.1's sense, so RCSI and the query store are skipped (S-03).
- **(g) Fresh clone.** `migrate` on LocalDB creates the catalog in `Latin1_General_100_CI_AS`, the
  in-proc `idsvr` schema lands in it, and the identity seeder's collation check passes. The catalog
  runs without RCSI, where EF's implicit creation turned it on before this round (S-01).
- **(h) Identity.** Standalone: the seeder's own `CREATE DATABASE` replaces EF's, dropping RCSI
  (S-05) and needing a `master` connection and its own copy of the statement (S-07). In-proc: the
  one seeder has no way to know it must fail instead of create (S-04, S-06).
- **(i) CI.** Every fixture creates with the SQL Server statement on LocalDB or the container; the
  checks and the conflict rules hold. **(j) Clone and restore.** The copy keeps its collations and
  the check runs on it; C-05 covers the wording.

### docs/specs/0011-distribution-host-and-multitenancy.md

- [S-01] (must) 0011 §6.7 "Who creates." - "The migrator creates every tenant database (`provision`,
  §6.1) and a missing catalog (`migrate`, before it takes the catalog lock)." -> append "On every
  database it creates, the migrator then runs `ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT
  ON` (a no-op on Azure SQL Database, §6.4)."; in the §6.1 `migrate` row, "Creates a missing catalog
  (§6.7)" -> "Creates a missing catalog with read-committed snapshot on (§6.7)"; in the §10
  `Tellma.Core.IntegrationTests` row, after "creates a missing catalog in `Latin1_General_100_CI_AS`"
  add "with `is_read_committed_snapshot_on = 1`". Why: fact §1.4. Until this round the `migrate` row
  left catalog creation to EF's `Migrate()` (`git diff HEAD`, the row's old text), whose `Create()`
  emits that `ALTER` on every engine but Azure. §6.7 replaces the path and says nothing, so on-prem,
  LocalDB and container catalogs (the session store's CAS refresh, §5.5) now run without RCSI while
  Azure keeps it. Spec 0012 §13.2 and spec 0018 §12 treat RCSI as part of creation.
- [S-02] (should) 0011 §6.7 "The statement." - "On SQL Server and LocalDB it is `CREATE DATABASE
  [<name>] COLLATE Latin1_General_100_CI_AS;`." -> add a bullet before it: "**Where it runs.** The
  migrator opens the target server's `master` with the database's own credential profile (the
  catalog's from `Tellma:Catalog:ConnectionString`; a tenant's from its row's `Server` and
  `CredentialProfile`, §3.5) with `Initial Catalog = master`, reads `SERVERPROPERTY('EngineEdition')`
  there, and runs the statement as its own batch. The migrator's principal holds the server-level
  right to create databases: `CREATE ANY DATABASE` on SQL Server; on Azure SQL Database, membership
  of `master`'s `dbmanager` role or the server administrator." (confirm the two names before writing
  them). Why: scenarios (e) and (g): the catalog string names a database that does not exist yet,
  §3.5's factories open only the catalog or a tenant database, and "owns the databases" (§3.5) is not
  the right to create one. No spec mentions `master`.
- [S-03] (should) 0011 §6.1 `provision` row - "`ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT
  ON` and `SET QUERY_STORE = ON (…)` on the fresh database" (and §6.4's "provisioning only, fresh
  database" comments) -> define "fresh" once, in §6.7's "An existing database" bullet: "A database
  with no `__EFMigrationsHistory` table is fresh, whoever created it; `provision` runs its
  fresh-database statements on it." Why: scenario (f): §6.7 admits a DBA-created database as
  existing, and as written it is never fresh, so an on-prem tenant a DBA pre-created gets neither
  RCSI nor the query store and behaves unlike a migrator-created one.
- [S-04] (should) 0011 §5.7 - "The engine never creates the catalog (spec 0010 §A.11)." ->
  "`AddTellma` turns the engine's database creation off (spec 0010 §A.11's option), so the seeder
  fails on a missing catalog instead of creating it." Why: scenario (h): the engine has no in-proc
  flag (`TellmaIdentityOptions` holds `PathBase` only,
  `src/apps/Tellma.Identity/Options/TellmaIdentityOptions.cs:34`), so the one seeder cannot choose
  between creating and failing unless the host tells it. Pairs with S-06.

### docs/specs/0010-amendments-to-implemented-specs.md

- [S-05] (must) 0010 §A.11 "Standalone, created by the engine." - "It creates a missing one with spec
  0011 §6.7's statement, without edition options, so EF's implicit creation never runs." -> append
  "then runs `ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON`, a no-op on Azure SQL
  Database."; in Part A — Testing's "Database collation (§A.11)" first case, append "and
  `is_read_committed_snapshot_on` reads 1". Why: facts §1.4 and §1.6: today `MigrateAsync`
  (`src/apps/Tellma.Identity/Services/Seeding/IdentitySeeder.cs:56`) lets EF create the database,
  which turns RCSI on everywhere but Azure; the amendment replaces that path and silently drops RCSI
  for every on-prem, LocalDB and container identity database.
- [S-06] (should) 0010 §A.11 "In-proc." - "The engine never creates it: a missing database fails
  seeding, so the host does not start." -> name the switch: a seed option in the
  `TellmaIdentityOptions` sketch (for example `Seed.CreateDatabase`, default true); "Standalone,
  created by the engine" applies with it on, "In-proc" with it off, and spec 0011 §5.7's `AddTellma`
  sets it off. Why: scenario (h): `TellmaIdentityOptions` carries no mode (`PathBase` only), and the
  integration case "An in-proc engine facing a missing database fails seeding and creates nothing"
  has nothing to compose from.
- [S-07] (should) 0010 §A.11 "Standalone, created by the engine." - "the seeder checks that its
  database exists before `MigrateAsync`" -> append "through a connection to the server's `master`
  built from the engine's connection string (`Initial Catalog = master`). The statement is the
  engine's own copy, since `Tellma.Identity` references no Core package (0003 §2.1)." Why: scenario
  (h): "created by spec 0011 §6.7's statement" reads as a shared implementation that does not exist,
  and the engine's connection string names the database it is about to create. Spec 0011 §10 and
  Part A — Testing each pin "the creation statement"; the second pin is of a second copy (Notes 2).

Checked, no finding: spec 0012 §4.4, §13.2; spec 0018 §3.1; `ARCHITECTURE.md` "One collation for
every database" and "Databases" (both say provisioning enables RCSI for tenants only, which is
true today and stays true after S-01 unless the paragraph is widened with it).

## page-state

Walk-through outcomes (a finding only where one is listed below):

- **(a) Role removed, two tabs.** The role edit publishes `cache.changed { tag: 'permissions' }`
  (spec 0013 §2.5); after 0–10 s the leader syncs `me`, the held `permissions` tag of both tabs
  moves through `store.changed`, and each tab's `TmPageState` marks every entry of tenant 17 stale.
  Back on `/17/customers`: the first frame shows the held rows, selection, highlight and scroll; one
  `customers/query` goes out with the plan resolved under the new access (columns beyond
  `Customer`'s projection drop). `403 no-grant` replaces the rows with "You don't have permission
  for this" (spec 0030 §5.3); a narrowed row filter replaces them with fewer rows, and the selection
  keeps ids no longer shown (spec 0030 §12.1). Opening `/17/customers/5`: the held record renders
  with its title from the kept display-name cache; one `customers/get` goes out; `403` replaces the
  body with the error state (spec 0031 §3.5) and the pager stays live. Neither host then forgets
  the stale entry, so the revoked data renders again on every later return (S-08, S-09, S-10). A
  Back before the leader synced restores with no request; spec 0030 §5.4 re-reads when the tag
  lands.
- **(b) Report and dashboard.** Report page: the held result renders; `reports/get` (the stale
  details snapshot) and `reports/run` go out; `403` on the run clears the result (spec 0034 §3.3).
  Dashboard: held tiles render, every tile with a result runs silently, `403` clears that tile; the
  dashboard record re-reads too, but a tile whose `Visible` turned false is not said to leave
  (S-11).
- **(c) Language added.** One sync moves `settings` and `permissions` together: whichever tag
  `TmPageState` sees first, the tenant's entries end forgotten and the display-name cache cleared;
  the saving tab adopts `settings` first and forgets at once; open pages re-read (spec 0030 §5.4).
  No finding. Note 1: `refresh-caches` moves the `settings` tag as well.
- **(d) Customer column after (a).** `Customer.Name` and `Name2` stay reachable with no grant on
  customers (spec 0015 §5.2 step 4; spec 0029 §7), so the invoice grid's Customer column renders
  from its rows in the first frame and after the refetch; the cells stop being links (spec 0030
  §7.2). The kept cache serves `label` for titles and the 404 dialog's row names. No finding.

### docs/specs/0025-client-state-and-cross-tab-sync.md

- [S-08] (must) 0025 §10 "A stale entry" - "Each host in the table below defines this for its own
  state." -> append "A refetch that fails with `forbidden` or `not-found` forgets the entry
  (`forget`), so the next return is a first load." Why: D-3 relies on the refetch to correct a
  stale entry, and on `403` it corrects nothing: `remember` is the only call that clears `isStale`,
  spec 0031 §13 remembers only with `status` `ready`, and `forget` runs only on delete (spec 0031
  §10.2 step 3). The revoked record renders for a frame and re-reads `403` on every later return,
  for the entry's LRU life. Spec 0030 §5.3's `offline` case (held rows kept) is unchanged.

### docs/specs/0030-search-page.md

- [S-09] (should) 0030 §15.1 - "`TmPageState.remember(tmPageStateKey('/17/customers'), snapshot)`
  (spec 0025 §10) runs when the host is destroyed and before it opens a row." -> append "A host
  whose last read failed follows spec 0025 §10: it forgets the entry on `forbidden` or `not-found`,
  and otherwise remembers `result: null`, which §15.2 step 1 treats as a first load of the restored
  query." Why: scenario (a): after the `403` the region holds the error view, and §15.2 step 1 would
  render a snapshot with an equal `canonical` "at once with no request" with nothing to render;
  today `result` is null only "with `searchRead` and for a browse tree".

### docs/specs/0031-details-page.md

- [S-10] (must) 0031 §13 "Stale" - "the held record renders at once, and a read refreshes it." ->
  append "A read that fails with `forbidden` or `not-found` forgets the snapshot (spec 0025 §10)."
  Why: S-08 applied here: "Remembered … with `status` `ready`" leaves the pre-revocation snapshot in
  place after the `403`, and only §10.2 step 3 forgets today.

### docs/specs/0035-dashboards-and-home-page.md

- [S-11] (should) 0035 §11.4 "A stale entry" - "renders the same way, then runs every tile
  silently, as a result older than 5 minutes does." -> append "The dashboard record's own re-read
  (spec 0031 §13) brings a new `view`; a tile whose `Visible` turned false leaves the grid and its
  held result is dropped, as a removed key's is." Why: scenario (b): the `/{t}/dashboards/{id}`
  snapshot is stale as well, §6.2 lays out "only the visible tiles, before the first render" from
  the held `view`, and §8.2 compacts rows when a tile leaves; nothing says the re-read's `view`
  replaces the first render's tile set. P-08 edits the same sentence.

Checked, no finding: spec 0029 §5.3 and §7 (d); spec 0034 §3.3, §3.4, §3.7 (b); spec 0032 §7.7
(c); spec 0024 §10.6 and §11 (the `403` kinds every refetch above hits); spec 0025 §4.4 and §5.3
(how the move reaches a follower tab).

## Notes for Ahmad

1. **`Refresh caches` forgets every tab's page state.** Spec 0013 §6.4 rewrites every
   `core.VersionTags` row, `settings` included, so D-3's "a settings move forgets" also fires on an
   action that changes no setting. The alternative already on the table (forget only when the
   languages changed, by comparing the two documents) would cover this too; your call.
2. **One statement, two copies.** `Tellma.Identity` references no Core package, so §6.7's
   statement lives in `Tellma.Core.Migrator` and again in the identity seeder, each with its own
   unit pin. Accept the duplication, or name a shared home (S-07 assumes duplication).
3. **An engine-created identity database on Azure has no edition options**, so it takes the
   server's default service objective (which one is unverified). §A.11 reserves Bicep for "a
   database that needs a service tier or an elastic pool"; say whether an isolated Azure deployment
   may use the engine path at all.
4. **One `CreateDatabaseTemplate` for every database.** `provision --server` admits a second
   logical server, but the template names one pool, and the catalog now uses it too. Pre-existing;
   a per-call override may be needed once pools are tiered.
5. **Readiness after `CREATE DATABASE` on Azure.** Whether the first connection to the new
   database succeeds as soon as the statement returns is unverified; the SaaS smoke run (editor's
   open question 6) should run the migrator's first `Migrate()` right after creation.
6. **Outside both changes, met on the way:** spec 0013 §6.4 publishes `cache.changed { tag:
   'settings' }` with no value, and spec 0025 §4.4 handles only the valued form, so tabs learn of
   that move from their next header, not from the event.
