# D-2 verification: collation edits

Scope: the collation text of specs 0010, 0011, 0012, 0018, 0019 and `ARCHITECTURE.md` against
`decisions.md` D-2, `facts-collation.md` and `applied-collation.md`. Specs 0001–0009 are unchanged
(`git diff HEAD --stat` lists none). Spec 0018 and `ARCHITECTURE.md` raised no finding.

## docs/specs/0011-distribution-host-and-multitenancy.md

- [C-01] (must) 0011 §5.9 - "Spec 0010 Part A (§A.1–§A.10) specifies the identity-server changes
  this spec and its siblings depend on, and ships before them:" -> replace "(§A.1–§A.10)" with
  "(§A.1–§A.11)", and add a last bullet "- the identity database's collation (§6.7)." Why: §A.11
  is new, and the Non-goals cite this list ("spec 0010 Part A's (listed in §5.9)"). The range is
  now stale.
- [C-02] (should) 0011 §6.7 opener - "Every database the platform creates uses the collation
  `Latin1_General_100_CI_AS`: the catalog," -> "Every database the platform creates uses one
  collation, `Latin1_General_100_CI_AS` (the platform collation): the catalog,". Why: spec 0010
  Goals, spec 0010 §F.15 ("agrees with the platform collation (spec 0011 §6.7)") and
  `ARCHITECTURE.md` use the term, but §6.7 never defines it (style guide, "Define, then use").
- [C-03] (should) 0011 §6.7 "The catalog clause." - "It gives `sys.*` names the data's collation.
  Without it they keep `SQL_Latin1_General_CP1_CI_AS`," -> delete the bullet. Add the reason
  to Decision 32 as one more sentence: "On Azure SQL Database `WITH CATALOG_COLLATION =
  DATABASE_DEFAULT` gives `sys.*` names the data's collation, without which spec 0001's table-type
  sweep fails with Msg 468." Why: the bullet holds rationale only. decisions.md and docs/AGENTS.md
  allow rationale only in the Decisions record. The SQL Server form already shows no clause in "The
  statement".
- [C-04] (should) 0011 §6.7 "An existing database." - "Any other value fails that database's
  command, naming the database and both values." -> "A collation other than
  `Latin1_General_100_CI_AS`, or on Azure SQL Database a catalog collation other than
  `DATABASE_DEFAULT`, fails that database's command, naming the database and both values." Why:
  "other" has no stated reference value. Fact §2.2 lists the two `catalog_collation_type_desc`
  values.
- [C-05] (should) 0011 §6.7 "Copies." - "A sandbox clone (§6.3) carries no collation clause,
  because `CREATE DATABASE … AS COPY OF` accepts none." -> append "The check above runs on every
  clone before its first migration, whether copied on Azure or restored on-prem." Then delete the
  separate sentence "The check above runs on the copy before its first migration." Why: §6.3 names
  two clone paths, "`AS COPY OF` on Azure, backup-restore on-prem", and the bullet covers only the
  Azure one. Fact §2.4 verifies `AS COPY OF` only, so name no restore property.
- [C-06] (should) 0011 §3.8 sketch, `TenantMembershipRecord` - "public string Subject { get; set;
  }                     // varchar(255)" -> "// varchar(255) COLLATE Latin1_General_100_BIN2
  (§7.3)". Why: spec 0012 §4.4's new paragraph names `TenantMembershipList.Subject` as declaring
  `Latin1_General_100_BIN2`. §7.3 states the collation, and spec 0018 §3.1's sibling sketch now
  carries it. This comment shows the column without it.
- [C-07] (should) 0011 §10, `Tellma.Distro.Acme.IntegrationTests` row - "the catalog and every
  tenant database report `Latin1_General_100_CI_AS`;" -> append "`provision` naming an existing
  database in another collation fails without migrating it;". Why: §6.7's "An existing database"
  covers every database the migrator migrates. Only the catalog case is pinned, in the
  `Tellma.Core.IntegrationTests` row.
- [C-08] (should) 0011 §5.7 - "The engine never creates the catalog (spec 0010 §A.11)." -> append
  "§3.4's catalog check runs before the engine's seeding, so a missing catalog still prints the
  `migrate` command (§6.6)." Why: spec 0010 §A.11 In-proc says "a missing database fails seeding,
  so the host does not start". §6.6 promises "A missing catalog at web startup prints that exact
  `migrate` command instead of a stack trace". Neither text says which failure wins.

## docs/specs/0010-amendments-to-implemented-specs.md

- [C-09] (must) 0010 §A.11 Compatibility - "A developer drops it, and the next start re-creates and
  re-seeds it (0003 §10.4)." -> "A developer drops it. A standalone engine re-creates and re-seeds
  it on the next start (0003 §10.4); an in-proc store is re-created by the migrator's `migrate`
  (spec 0011 §6.6)." Why: the In-proc bullet of the same section says "The engine never creates
  it". Spec 0011 §6.7 makes the migrator the catalog's only creator.
- [C-10] (should) 0010 §A.11 opener - "The identity database uses `Latin1_General_100_CI_AS`,
  created by spec 0011 §6.7's statement." -> "The identity database uses the platform collation as
  spec 0011 §6.7 states it: its `CREATE DATABASE` statement, or its two Bicep properties." Why:
  the first bullet's Bicep path creates the database without the statement, which contradicts the
  opener's "created by … statement".
- [C-11] (should) 0010 §A.11 "Standalone, created by infrastructure." - "Bicep that declares the
  database sets spec 0011 §6.7's two collation properties." -> "Infrastructure that creates the
  database uses spec 0011 §6.7's statement. Bicep sets its two collation properties." Why: 0003 §2
  allows an isolated standalone deployment "on Azure or on-prem", and an on-prem database has no
  Bicep. Neither path covers an operator's script.
- [C-12] (should) 0010 §A.11 closing labels - "**Tests:** Part A — Testing." -> delete it and keep
  **Compatibility:**. Why: §A.1–§A.10 carry no Tests label. Part A — Testing holds every Part A
  case, so the pointer adds nothing.

## docs/specs/0012-data-access-layer.md

- [C-13] (should) 0012 §4.4 - "A standalone type's string column that a statement compares with an
  explicitly collated column declares the same collation." -> "A standalone type's or a table
  variable's string column that a statement compares with an explicitly collated column declares
  the same collation." Why: facts "Conflict risks" states the rule for table types and table
  variables. Probe P5 shows that a table variable fails against an explicitly collated column with
  Msg 468. Spec 0012 §1.4 owns batch-local table variables.

## docs/specs/0019-excel-codec.md

- [C-14] (should) 0019 §10.3 - "(a collation equivalence beyond case: accent- or
  width-insensitivity)" -> "(a collation equivalence beyond case)". Why: under D-2 every tenant
  database is `Latin1_General_100_CI_AS`, which is accent-sensitive (the `_AS` suffix; 0008 §18.3
  calls the fixture's collation "case-insensitive, accent-sensitive"). The accent example cannot
  occur. Width behaviour is not among the Verified facts, so name no replacement example.

## Notes for Ahmad

1. **The editor's open questions all stand.** (1) Who creates the SaaS catalog: the migrator only,
   or Bicep too? (2) Who creates and migrates the shared identity database in production? (3)
   Should an identity database in another collation be refused or only warned about (Part A flag
   9)? (4) Should the Queryex fixture keep its `_SC` columns, which conflict with any `StringList`
   restriction (Msg 468, P5)? (5) Spec 0012 §8.1–§8.2's `EXCEPT` misses case-only edits (not
   caused by D-2). (6) Run one live Azure smoke of `COLLATE … (SERVICE_OBJECTIVE = ELASTIC_POOL(…))
   WITH CATALOG_COLLATION = DATABASE_DEFAULT`.
2. **Key name.** `Tellma:Provisioning:CreateDatabaseTemplate` now holds an options fragment with no
   substitution, and it also applies to the catalog. Rename it (for example
   `Tellma:Sql:CreateDatabaseOptions`) or keep it.
3. **How a class-derived standalone type declares a collation.** Spec 0001 §5 lists the honoured
   annotations, and none of them is a collation. The code offers one only through the fluent
   `TableTypeBuilder.Column(…, collation)` (`StandaloneColumnConfiguration.Collation`). Both
   `TenantMembershipRecord` and `UserInvitationOutcome` are class-derived and must carry
   `Latin1_General_100_BIN2`. Choose the mechanism. If it needs a spec 0001 change, spec 0010 line
   27 ("Specs 0001, 0007 and 0009 need no amendment") no longer holds.
4. **Implementation order versus deployment.** Part A says its section order is the implementation
   order, with §A.11 last, yet §A.11 "ships before any identity database is deployed". If the
   shared server deploys with §A.1–§A.10 first, its database takes the server default (ties to
   questions 2 and 3).
5. **Who migrates in-proc `idsvr`** is still unsettled (inventory Gap 5). Both the web process
   (spec 0011 §5.7) and `migrate` (§6.1, "`idsvr` too in in-proc mode") migrate it.
6. **`dotnet ef database update`** through a design-time factory (`AcmeDesignTimeFactory`,
   `TellmaIdentityDesignTimeDbContextFactory`, the MigrationsHost tool) still creates a missing
   database in the server default. §6.7 says nothing relies on EF's implicit creation but does not
   forbid this path. Say "use `migrate`", or accept the exception for these tools.
7. **The widened refusal has no escape hatch.** The migrator now refuses a database in another
   collation on every `migrate` of every tenant, not only at `provision`. That is harmless while no
   tenant database exists. An escape hatch is needed only if one already does (inventory Gap 3).
