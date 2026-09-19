# Research: Entity contract and data access (theme `data-access`, future spec 0011)

Verified on 2026-09-01 unless a finding says otherwise. Every finding is tagged **Verified** (read in a
primary source: vendor docs, release notes, source code, issue tracker) or **Inferred** (a conclusion
drawn from verified facts, not itself stated by a source). Items nobody could confirm are collected in
§10. Versions pinned in this repo today (`Directory.Packages.props`): EF Core 10.0.9,
`Microsoft.Data.SqlClient` 6.1.1, `OpenTelemetry.*` 1.16.0, no `Microsoft.SqlServer.Types`, no
`Microsoft.EntityFrameworkCore.SqlServer.HierarchyId`.

---

## 1. EF Core 10 and SQL Server `hierarchyid`

### 1.1 Packaging

**Verified.** `Microsoft.EntityFrameworkCore.SqlServer.HierarchyId` is still a separate NuGet package;
it has not been folded into `Microsoft.EntityFrameworkCore.SqlServer`. Its source lives in
`dotnet/efcore` (`src/EFCore.SqlServer.HierarchyId`) since EF 8; the old community repository
`efcore/EFCore.SqlServer.HierarchyId` states it was merged into `dotnet/efcore`. Latest stable:
**10.0.11, released 2026-08-11** (10.0.9 on 2026-06-09 matches the repo's EF pin). Dependencies for
`net10.0`: `Microsoft.EntityFrameworkCore.SqlServer >= 10.0.11`,
`Microsoft.EntityFrameworkCore.SqlServer.Abstractions >= 10.0.11` (this is where the `HierarchyId` CLR
type lives; the package has no dependencies of its own), `Microsoft.SqlServer.Types >= 160.1000.6`,
`Microsoft.Data.SqlClient >= 6.1.6`. Activation is `options.UseSqlServer(cs, x => x.UseHierarchyId())`.

Sources: https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.SqlServer.HierarchyId ;
https://learn.microsoft.com/en-us/ef/core/providers/sql-server/hierarchyid ;
https://github.com/efcore/EFCore.SqlServer.HierarchyId (README: "merged into dotnet/efcore").

### 1.2 The `HierarchyId` .NET type and LINQ translation

**Verified.** Members and their SQL translations (docs "Function mappings" table):

| .NET | SQL |
|---|---|
| `GetAncestor(int n)` | `@h.GetAncestor(@n)` |
| `GetDescendant(child)` / `GetDescendant(child1, child2)` | `@h.GetDescendant(@c1, NULL)` / `@h.GetDescendant(@c1, @c2)` |
| `GetLevel()` | `@h.GetLevel()` |
| `GetReparentedValue(oldRoot, newRoot)` | `@h.GetReparentedValue(@o, @n)` |
| `HierarchyId.GetRoot()` | `hierarchyid::GetRoot()` |
| `IsDescendantOf(parent)` (true for itself) | `@h.IsDescendantOf(@p)` |
| `HierarchyId.Parse(string)` | `hierarchyid::Parse(@s)` |
| `ToString()` | `@h.ToString()` |
| operators `== != < <= > >=` | native comparison (depth-first order) |

`HierarchyId` may be a key property. Parameters are sent as `SqlDbType.Udt` with
`UdtTypeName = "hierarchyid"` in the compact binary form; literals are emitted as
`hierarchyid::Parse('...')` (`SqlServerHierarchyIdTypeMapping.ConfigureParameter` in `dotnet/efcore`).
Migrations emit the store type `hierarchyid`; nothing special is needed for `CreateTable`/`AddColumn`.
No open `dotnet/efcore` issue is tagged for hierarchyid beyond a test-suite merge (#30301, verified via
`gh search issues`).

Sources: https://learn.microsoft.com/en-us/ef/core/providers/sql-server/hierarchyid ;
https://raw.githubusercontent.com/dotnet/efcore/main/src/EFCore.SqlServer.HierarchyId/Storage/Internal/SqlServerHierarchyIdTypeMapping.cs

### 1.3 SQL Server semantics and indexing

**Verified.** A `hierarchyid` column "doesn't automatically represent a tree": uniqueness and parent
existence are not enforced unless the application adds a unique index or an FK on a persisted computed
`GetAncestor(1)` column; concurrency of node generation is the application's job. Two index strategies:
depth-first (unique index on the `hierarchyid` column: subtree reads are colocated) and breadth-first
(index on `(GetLevel() persisted computed column, hierarchyid)`: direct children colocated). Moving a
non-leaf node costs `n` row updates (`GetReparentedValue` over `IsDescendantOf`); the docs' `MoveOrg`
sample does it in a SERIALIZABLE transaction. Storage is compact (about 5 bytes for 100k nodes at fanout
6).

Source: https://learn.microsoft.com/en-us/sql/relational-databases/hierarchical-data-sql-server

### 1.4 Binding `hierarchyid` in a TVP

**Verified.** `SqlMetaData` has UDT constructors, e.g. `SqlMetaData(string name, SqlDbType dbType, Type
userDefinedType, string serverTypeName)`; the `userDefinedType` must carry
`SqlUserDefinedTypeAttribute`, i.e. `Microsoft.SqlServer.Types.SqlHierarchyId` (from
`Microsoft.SqlServer.Types`, latest **170.1000.7, 2025-11-19**, targets `netstandard2.0` and
`net472+`; 160.1000.6 targeted `netstandard2.1`). The EF `HierarchyId` type is *not* a UDT and cannot be
used in `SqlDataRecord` directly. Cross-platform (Linux) behaviour of `SqlHierarchyId` in
`Microsoft.SqlServer.Types` 160/170 is **not documented** on NuGet; `dotnet/SqlClient#322` ("Implement
SqlHierarchyId type for .NET Core") was closed as *External* on 2023-02-01 without a doc statement. The
repo already sidesteps UDT binding for scalar parameters: the Queryex integration tests bind
`QxHierarchyId` parameters as `SqlDbType.NVarChar` text (string → hierarchyid implicit conversion),
see `test/core/Tellma.Core.Queryex.IntegrationTests/SqlBinding.cs:172`.

**Implication for the design.** Keep `hierarchyid` out of the UDTT row image (spec 0001 supports
per-property exclusions) and recompute `Node` in SQL in the same batch (`GetReparentedValue` /
`GetDescendant` over the affected subtrees), or carry it in the TVP as `nvarchar` and convert on insert.
Either avoids a hard dependency on `Microsoft.SqlServer.Types` in the save path. The EF package is still
required for LINQ/migrations, and it pulls `Microsoft.Data.SqlClient >= 6.1.6` (bump the pin).
Queryex's missing `level()` maps 1:1 onto `GetLevel()`; `descendantOf`/`ancestorOf` already map onto
`IsDescendantOf`.

---

## 2. EF Core 10 temporal tables

### 2.1 Mapping and conventions

**Verified.** `modelBuilder.Entity<T>().ToTable("X", b => b.IsTemporal())`. Defaults: period columns
`PeriodStart`/`PeriodEnd` (`datetime2 GENERATED ALWAYS AS ROW START/END NOT NULL`), history table
`<Table>History` created in the same schema as the table (EF emits `DECLARE @historyTableSchema
sysname = SCHEMA_NAME()` and `HISTORY_TABLE = [<schema>].[XHistory]`). Customisation:
`b.IsTemporal(t => { t.HasPeriodStart("ValidFrom"); t.HasPeriodEnd("ValidTo");
t.UseHistoryTable("XHistory", "schema"); })`. In EF 10 the period columns are **shadow properties**
(read via `EF.Property<DateTime>(e, "ValidFrom")`); mapping them to CLR properties
(`HasPeriodStart(e => e.ValidFrom)`, configured `ValueGenerated.OnAddOrUpdate`) arrives in **EF 11
(preview)**. Query operators: `TemporalAsOf`, `TemporalAll`, `TemporalFromTo`, `TemporalBetween`,
`TemporalContainedIn`; temporal queries are no-tracking by default. All period values are UTC.
Only the root of an inheritance hierarchy can be temporal (issue #33119 error text "Only root entity
type should be marked as temporal"); TPT/entity splitting with temporal is unsupported (#26457, open).

Sources: https://learn.microsoft.com/en-us/ef/core/providers/sql-server/temporal-tables ;
https://github.com/dotnet/efcore/issues/26457 ; https://github.com/dotnet/efcore/issues/33119

### 2.2 What SQL Server does on bulk INSERT/UPDATE/DELETE/MERGE

**Verified** (SQL Server docs, updated 2026-08-18):
- Period timestamps are the **begin time of the transaction** (UTC); all rows touched in one
  transaction carry the same `ValidFrom`/`ValidTo`.
- UPDATE and DELETE each write one history row per affected row, **even if no column value changed**.
  DELETE is logical (row moves to history with `ValidTo` = txn begin).
- Multiple updates of the same PK in one transaction create zero-duration history rows
  (`ValidFrom = ValidTo`); `FOR SYSTEM_TIME` filters those out, direct history queries do not.
- MERGE "behaves exactly as if up to three statements (INSERT, UPDATE and/or DELETE) executed" and is
  supported "with the same limitations that INSERT and UPDATE statements have regarding PERIOD columns".
- INSERT/UPDATE cannot reference period columns (error 13537 "Cannot update GENERATED ALWAYS columns")
  except as `DEFAULT` in INSERT; `TRUNCATE` is not allowed while versioning is on; `INSTEAD OF`
  triggers are not permitted on either table; `AFTER` triggers only on the current table; the history
  table cannot have constraints, a PK, or FKs; the current table must have a PK.
- Schema changes (`ALTER TABLE ADD/ALTER/DROP COLUMN`) propagate to the history table automatically
  while holding a schema lock on both tables; adding a `NOT NULL` column with a default also creates the
  default on the history table (docs suggest dropping it afterwards); computed, IDENTITY, SPARSE (with
  page compression, the history default), COLUMN_SET and ROWGUIDCOL additions need
  `SYSTEM_VERSIONING = OFF`; `ALTER COLUMN` is never online on a temporal table.
- Default history table gets a clustered rowstore index on (period end, period start) and PAGE
  compression; docs recommend a clustered columnstore for large histories.

Sources: https://learn.microsoft.com/en-us/sql/relational-databases/tables/temporal-tables ;
https://learn.microsoft.com/en-us/sql/relational-databases/tables/temporal/modify-data ;
https://learn.microsoft.com/en-us/sql/relational-databases/tables/temporal-table-considerations-and-limitations ;
https://learn.microsoft.com/en-us/sql/relational-databases/tables/temporal/change-schema

### 2.3 OUTPUT with temporal tables

**Verified (by absence).** Neither the OUTPUT clause reference (updated 2024-09-06) nor the temporal
limitations page lists any temporal-specific restriction on `OUTPUT`. The general rules apply: `OUTPUT`
without `INTO` fails if the target has an enabled trigger for that action; an `OUTPUT INTO` target
cannot have enabled triggers, participate in an FK, or have CHECK constraints; a nested
`INSERT ... SELECT FROM (MERGE ... OUTPUT $action ...)` has the same target restrictions; OUTPUT rows
are returned to the client even if the statement later fails, so the result must not be used after an
error; an `OUTPUT` that returns rows to the client always runs a serial plan.
**Inferred:** `OUTPUT inserted.ValidFrom` is legal (period columns are ordinary columns to OUTPUT), so
the emitter can read back the new period start for concurrency stamping.

Source: https://learn.microsoft.com/en-us/sql/t-sql/queries/output-clause-transact-sql

### 2.4 MERGE into a temporal table (still broken)

**Verified.** Hugo Kornelis re-tested the MERGE bug list on SQL Server 2022 CU7 (2023-09-29): a MERGE
targeting a temporal table whose **history table has a nonclustered index** fails with "Attempting to
set a non-NULL-able column's value to NULL"; his workaround is "never create a nonclustered index on the
history table", and his verdict is "Do not use MERGE to target a temporal table." No Microsoft fix is
referenced. See §4.

Source: https://sqlserverfast.com/blog/hugo/2023/09/an-update-on-merge/

### 2.5 Open `dotnet/efcore` issues, label `area-temporal-tables` (listed with `gh`, 2026-09-01)

**Verified.** 30 open issues. The ones that touch this design:
- #38836 (2026-08-21) named default-constraint removal also targets the history table → migration
  fails with error 3728. Relevant because ARCHITECTURE.md mandates explicit constraint names and EF 10
  added named default constraints / `UseNamedDefaultConstraints()`.
- #38601 (2026-07-11, regression) `KeyNotFoundException` when a temporal table is renamed into the
  model's default schema.
- #36161, #36029 invalid or inconsistent migrations for complex temporal schema manipulation.
- #38851 (2026-08-24) compiled query with a parameterized `TemporalAsOf` throws.
- #37288 `GroupJoin` returns a single row for temporal tables (EF 9/10).
- #33243 `UnexpectedTrailingResultSetWhenSaving` with temporal tables + concurrency token when saving
  several entity types in one `SaveChanges` (SaveChanges path only; irrelevant to a custom emitter).
- #28368 no API to configure history-table indexes; #26041 no "all tables temporal" switch; #25743 no
  mapping attribute; #26457 no TPT/entity-splitting support.

**Implication for the design.** Temporal is safe as an *additive per-table capability* for top-level
entities whose current table is leaf-mapped (no TPT root), with the emitter writing separate
UPDATE/INSERT/DELETE statements rather than MERGE, and with churn columns kept in a sibling
non-temporal table (every UPDATE doubles the write and adds a history row even when nothing changed).
Migrations involving renames, schema moves or named default constraints on temporal tables need a test
in the migrator suite because the EF differ has open regressions there. `ValidFrom` is a valid
concurrency stamp candidate only if the platform never runs bookkeeping UPDATEs on the temporal table.

---

## 3. `Microsoft.Data.SqlClient` 6.x

### 3.1 Versions

**Verified.** Repo pins 6.1.1 (2025-08-14). 6.1 line: 6.1.0 (2025-07-25), 6.1.1 (2025-08-14), 6.1.2
(2025-10-07), 6.1.3 (2025-11-12), 6.1.4 (2026-01-15; fixes a `SqlDataAdapter` batch NRE), 6.1.5
(2026-04-27; **fixes `ExecuteScalar` silently swallowing an error token that followed data, which
zombied transactions**; fixes SPN lookups on SQL auth), **6.1.6 (2026-06-24)**. 7.0 line: 7.0.0
(2026-03-17), 7.0.1 (2026-04-23), **7.0.2 (2026-06-25, latest stable)**; 7.1.0-preview3 (2026-08-27).
7.0 removes the Azure dependencies from the core package (Entra auth needs
`Microsoft.Data.SqlClient.Extensions.Azure`), adds `SspiContextProvider`, enhanced routing, and emits
the strongly typed diagnostic events on .NET Framework too. EF Core SqlServer 10.0.11 requires
`Microsoft.Data.SqlClient >= 6.1.6`.

Sources: https://www.nuget.org/packages/Microsoft.Data.SqlClient ;
https://github.com/dotnet/SqlClient/blob/main/release-notes/6.1/README.md ;
https://github.com/dotnet/SqlClient/blob/main/release-notes/6.1/6.1.5.md ;
https://github.com/dotnet/SqlClient/blob/main/release-notes/7.0/7.0.0.md ;
https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.SqlServer

### 3.2 `SqlBatch` / `DbBatch`

**Verified.**
- `SqlBatch : DbBatch` (since 5.2). Members: `BatchCommands`/`Commands`, `Connection`, `Transaction`,
  `Timeout`, `ExecuteReader[Async]`, `ExecuteNonQuery[Async]` (returns the total rows affected across
  commands), `ExecuteScalar[Async]` (first column of first row of first result set), `Prepare[Async]`,
  `Cancel`. `SqlBatchCommand : DbBatchCommand` has `CommandText`, `CommandType`, `CommandBehavior`,
  `Parameters` (its own `SqlParameterCollection`), `RecordsAffected` (per command),
  `ColumnEncryptionSetting` (not implemented).
- **There is no `RetryLogicProvider` on `SqlBatch`** (member list) — `SqlCommand` has one, but the
  batch delegates to a *private* internal `SqlCommand` (`_batchCommand`) placed in batch-RPC mode
  (`SetBatchRPCMode(true, count)` then `AddBatchCommand(cmd)` per command); each `SqlBatchCommand`
  becomes its own RPC (`sp_executesql`-style) with its own parameter set, sent in one TDS message.
- Multiple result sets: yes. The docs example reads the results of all batch commands with one
  `SqlDataReader` and `NextResult()`.
- TVPs: parameters are ordinary `SqlParameter`s (`SqlDbType.Structured` + `TypeName`); **no documented
  restriction** for batch commands and no issue reports; not verified by an actual test here.
- Error semantics are under-documented (issue #3273, closed 2025-11-21): without `Transaction`, the
  non-failing DML commands of a batch commit (each RPC autocommits); with a `Transaction`, nothing is
  committed until the caller commits. Whether commands after a failing one still execute is **not
  documented**.
- Diagnostics: the batch's execution raises the normal `SqlCommand` events, but `AddBatchCommand`
  overwrites `CommandText`, so subscribers (OpenTelemetry) see **only the last statement's text** and
  cannot learn the batch size (issue #4545, open, 2026-08-16).

Sources: https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlbatch?view=sqlclient-dotnet-core-6.1 ;
https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlbatchcommand?view=sqlclient-dotnet-core-6.1 ;
https://raw.githubusercontent.com/dotnet/SqlClient/main/src/Microsoft.Data.SqlClient/src/Microsoft/Data/SqlClient/SqlBatch.cs ;
https://github.com/dotnet/SqlClient/issues/4545 ; https://github.com/dotnet/SqlClient/issues/3273

**Implication for the design.** For the batch executor, concatenated text with engine-namespaced
parameters (`@qx{b}_p{n}`, spec 0008 §13) plus `NextResult()` gives everything `SqlBatch` gives and
more: one TVP referenced by several statements is sent once (with `SqlBatch` each command carries its
own parameter collection, so a shared TVP would be attached and transmitted per command), one
`SqlCommand.RetryLogicProvider` applies, one diagnostic span shows the whole text, and `SET XACT_ABORT
ON; BEGIN TRAN ... COMMIT` can wrap the text. `SqlBatch` adds per-command `RecordsAffected` and
`CommandBehavior`, nothing the design needs. Recommend concatenated text; keep `SqlBatch` as a
non-goal.

### 3.3 `SqlRetryLogicBaseProvider` and the transient error list

**Verified.**
- Providers: `SqlConfigurableRetryFactory.CreateExponentialRetryProvider`,
  `CreateIncrementalRetryProvider`, `CreateFixedRetryProvider`, `CreateNoneRetryProvider`, all taking
  `SqlRetryLogicOption { NumberOfTries, DeltaTime, MinTimeInterval, MaxTimeInterval, TransientErrors,
  AuthorizedSqlCondition }`. Assign to `SqlConnection.RetryLogicProvider` (retries `Open`) and/or
  `SqlCommand.RetryLogicProvider` (retries execution). Retry is off unless a provider is assigned.
- **"A built-in provider doesn't retry a command when the connection has an active transaction. If a
  transient failure invalidates a transaction, roll back and retry the entire transaction."** (docs,
  updated 2026-08-14). `AuthorizedSqlCondition` gates retry by command text (docs sample: only
  `SELECT`).
- Built-in transient list (class remarks; the `BaselineTransientErrors` property warns the list "may
  change at any time" and is not an API contract): 1204, 1205 (deadlock victim), 1222 (lock timeout),
  49918, 49919, 49920, 4060, 4221, 40143, 40613, 40501, 40540, 40197, 10929, 10928, 10060, 10054,
  10053, 997, 233. Setting `TransientErrors` replaces the list (extend by concatenation).

Sources: https://learn.microsoft.com/en-us/sql/connect/ado-net/configurable-retry-logic-sqlclient-introduction?view=sql-server-ver17 ;
https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlconfigurableretryfactory?view=sqlclient-dotnet-core-6.1 ;
https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlretrylogicoption?view=sqlclient-dotnet-core-6.1

**Implication for the design.** The driver's command retry never applies inside an explicit
transaction and never to a `SqlBatch`, so the per-statement `MayRetry` flag must be enforced by the
executor: a round trip is retryable only if every statement in it is (`MayRetry` is AND-ed) and the
whole round trip (its transaction included) is re-executed from the start. Deadlock (1205) and lock
timeout (1222) are in the driver's own transient list, which is the right list to reuse (subscribe to
`BaselineTransientErrors` at startup rather than hard-coding numbers).

### 3.4 Streaming TVPs with `IEnumerable<SqlDataRecord>`

**Verified.** `SqlParameter` with `SqlDbType.Structured` accepts `DataTable`, `DbDataReader` or
`IEnumerable<SqlDataRecord>`; `TypeName` must name a compatible server type. The driver source
(`ParameterPeekAheadValue.cs`) documents the streaming contract: the enumerator is obtained once and the
first record is read during metadata generation ("metadata is stored in the first record"), then the
remaining records are streamed while writing the RPC — the enumerable is **not** materialised. TVPs are
read-only in T-SQL, cannot be OUTPUT, have no statistics (only PK/UNIQUE indexes), "do not acquire
locks for the initial population", and are cached like temp tables for parameterized queries. Column
binding is positional (spec 0001's ordinal-binding rule).

Sources: https://learn.microsoft.com/en-us/sql/connect/ado-net/sql/table-valued-parameters ;
https://learn.microsoft.com/en-us/sql/relational-databases/tables/use-table-valued-parameters-database-engine ;
https://github.com/dotnet/SqlClient/blob/main/src/Microsoft.Data.SqlClient/src/Microsoft/Data/SqlClient/ParameterPeekAheadValue.cs

### 3.5 `SqlBulkCopy` versus TVPs for tens of thousands of rows

**Verified.** Database Engine docs: "Compared to bulk operations that have a greater startup cost than
table-valued parameters, table-valued parameters perform well for inserting less than 1,000 rows",
while "using table-valued parameters frequently can be faster for large data sets" and reused TVPs
benefit from temp-table caching. `SqlBulkCopy` writes only to a single SQL Server table, cannot return
rows, supports an external `SqlTransaction` (ctor `SqlBulkCopy(SqlConnection, SqlBulkCopyOptions,
SqlTransaction)`), `BatchSize`, `EnableStreaming` from an `IDataReader`, `TableLock` etc.
**Inferred.** The 1,000-row figure is a docs heuristic, not a measured crossover for this schema. A save
of tens of thousands of rows that must *upsert and synchronise* needs set-based UPDATE/INSERT/DELETE
against a source table anyway; a TVP is that source with one round trip and no extra object. Reserve
`SqlBulkCopy` for import-scale inserts (hundreds of thousands of rows) into a staging table inside the
same transaction, followed by the same set statements. Benchmark before choosing a threshold.

Sources: as in §3.4 plus
https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlbulkcopy?view=sqlclient-dotnet-core-6.1

---

## 4. SQL Server MERGE in 2026

**Verified.**
- Microsoft's MERGE reference (updated 2026-01-27) now carries a "Concurrency considerations for
  MERGE" section: MERGE uses different locking than discrete statements; "It might be more efficient to
  write discrete INSERT, UPDATE, and DELETE statements"; "At scale, MERGE might introduce complicated
  concurrency issues"; "When heavy concurrency is expected, separate INSERT, UPDATE, and DELETE logic
  might perform better, with less blocking"; specify `HOLDLOCK` (= SERIALIZABLE) when the same keys may
  be inserted and updated concurrently. Also: `@@ROWCOUNT` in any AFTER trigger reflects the whole
  MERGE; `INSTEAD OF` triggers must exist for every action used; index the join columns of source and
  target; MERGE is incompatible with queued-updating replication.
- Aaron Bertrand's living page (sqlblog.org/merge): "Please don't"; if you must, "ALWAYS use HOLDLOCK on
  the target"; prefers separate INSERT/UPDATE; points to Kornelis's re-test.
- Hugo Kornelis (2023-09-29, SQL Server 2022 CU7): of the twelve historical issues, two remain real bugs
  — **#8 temporal target with a nonclustered index on the history table (error)** and **#9 MERGE with
  UPDATE+DELETE actions on a table used by an indexed view (silently wrong view data)**; filtered unique
  index bug fixed in 2019; several others not reproducible; race conditions and trigger behaviour are
  not MERGE-specific. Verdict: "Do not use MERGE with a DELETE action. Do not use MERGE to target a
  temporal table" — otherwise safe.

Sources: https://learn.microsoft.com/en-us/sql/t-sql/statements/merge-transact-sql ;
https://sqlblog.org/merge ; https://sqlserverfast.com/blog/hugo/2023/09/an-update-on-merge/

**Implication for the design.** Tellma's save is "upsert top-level rows + synchronise children
(deletes)" against tables that are temporal (`User`, `Role`, `RoleMembership`, `Permission`) — exactly
the two surviving MERGE bug classes. Emit separate statements per table inside one transaction:
`UPDATE t SET ... FROM t JOIN @tvp`, `INSERT ... SELECT FROM @tvp WHERE NOT EXISTS`, and for children
`DELETE c WHERE c.ParentId IN (SELECT Id FROM @parents) AND c.Id NOT IN (SELECT Id FROM @children)`.
**Inferred:** because ids are app-assigned and unique by construction, the classic upsert race on the
key ("two sessions insert the same key") cannot occur, so `HOLDLOCK` is unnecessary for surrogate keys;
races on natural unique keys (`Code`) surface as unique-constraint violations and belong to validation
error mapping, not to locking.

---

## 5. `sp_sequence_get_range`, CACHE and `NEXT VALUE FOR`

**Verified.**
- `sys.sp_sequence_get_range @sequence_name, @range_size (bigint), @range_first_value OUTPUT,
  [@range_last_value, @range_cycle_count (int), @sequence_increment, @sequence_min_value,
  @sequence_max_value] OUTPUT`; OUTPUT values are `sql_variant` with the sequence's base type. For a
  NO CYCLE sequence, a range that exceeds the remaining values raises error 11732 and **deducts
  nothing**. **Permission: `UPDATE` on the sequence object or its schema** (same for `NEXT VALUE FOR`;
  members of `db_owner`/`db_datawriter` can generate numbers).
- CACHE: default is `CACHE` with an engine-chosen size ("users shouldn't rely upon the selection being
  consistent"). Only the current value and the remaining count are kept in memory; the last value of the
  cache is persisted. Normal shutdown persists the next value (no gap); **abnormal shutdown loses the
  unissued cached values (gap ≤ cache size)**; `NO CACHE` writes every use. Restart/`ALTER SEQUENCE`
  flush the cache first, so no numbers are skipped. Sequence values are generated **outside the current
  transaction** and consumed on rollback; unused ranges are gaps. Uniqueness is not enforced by the
  sequence — put a unique constraint on the column.
- `NEXT VALUE FOR` is allowed in `INSERT ... SELECT` (one value per row; with `OVER (ORDER BY ...)` the
  values follow that order; `OVER` is not allowed in UPDATE/MERGE). Not allowed: in subqueries/CTEs/
  derived tables, with DISTINCT/UNION/EXCEPT/INTERSECT, with TOP/OFFSET/ROWCOUNT, with ORDER BY unless
  `OVER` is used, in WHERE/OUTPUT/GROUP BY/HAVING, in CASE/COALESCE/IIF, in views/UDFs/computed
  columns, in MERGE (except via a default constraint), when the database is read-only.
- Availability-group/Azure failover gap behaviour is **not stated** in Microsoft docs; **inferred**
  from the abnormal-shutdown rule: a new primary reads the persisted value, so the gap is bounded by
  the cache size.
- EF Core cannot emit `CACHE` for `HasSequence`/`CreateSequence` (dotnet/efcore #11261, open, Backlog,
  last updated 2025-06-15); a raw `ALTER SEQUENCE ... CACHE n` in a migration is the only route.

Sources: https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-sequence-get-range-transact-sql ;
https://learn.microsoft.com/en-us/sql/t-sql/statements/create-sequence-transact-sql ;
https://learn.microsoft.com/en-us/sql/t-sql/functions/next-value-for-transact-sql ;
https://learn.microsoft.com/en-us/sql/relational-databases/sequence-numbers/sequence-numbers ;
https://github.com/dotnet/efcore/issues/11261

**Implication for the design.** The reservation is a procedure call with OUTPUT parameters, so it can
ride any round trip as one more statement (`DECLARE @f sql_variant; EXEC sys.sp_sequence_get_range
N'core.sq_Users', @n, @f OUTPUT; SELECT CAST(@f AS int);`) — the "hitch a ride on the validation
read" idea is mechanically sound. Gap size is the sum of the app's unconsumed buffer on restart and the
engine cache on crash; both are tunable (small app ranges, explicit `CACHE` via raw migration SQL).
`NEXT VALUE FOR ... OVER (ORDER BY)` inside `INSERT ... SELECT FROM @tvp` is a valid server-side
alternative for standalone inserts, but not for this stack, where children need parent ids before the
batch is built. The app identity needs `UPDATE` on every `sq_<Table>` (confirms the ARCHITECTURE.md
open question).

---

## 6. SQL Server 2025 / Azure SQL: `json`, JSON aggregates, regex, and EF 10

**Verified.**
- `json` data type: GA on **Azure SQL Database** and **Azure SQL Managed Instance** with the *SQL Server
  2025* or *Always-up-to-date* update policy (announcement 2025-05-19, together with `JSON_OBJECTAGG`
  and `JSON_ARRAYAGG`, which are ANSI-compatible). SQL Server 2025 (17.x) went GA on 2025-11-18, but
  the `json` type reference (updated 2026-01-14) still says the type "is in preview for SQL Server 2025
  (17.x) and SQL database in Fabric"; `modify()` is preview and 2025-only. Available under **all
  compatibility levels**. Stored as UTF-8 binary; **TDS clients see it as `varchar(max)` with
  `Latin1_General_100_BIN2_UTF8`** (TDS ≥ 7.4) and `sp_describe_first_result_set` reports varchar —
  which is why spec 0001 carries json UDTT columns as `varchar(max)` UTF-8. `json` cannot be an index
  key column (included/filtered only), has no implicit conversions (explicit CAST to/from `(n)varchar`
  only), cannot be `sql_variant`, cannot be an alias type; `varchar(max)` → `json` is allowed via
  `ALTER TABLE`, the reverse is not. **JSON index** (`CREATE JSON INDEX`) is preview and SQL Server
  2025-only; needs a clustered PK; offline only.
- Regex functions GA 2025-11-18 on SQL Server 2025 and Azure SQL: `REGEXP_LIKE`, `REGEXP_COUNT`,
  `REGEXP_INSTR`, `REGEXP_REPLACE`, `REGEXP_SUBSTR`, `REGEXP_MATCHES`, `REGEXP_SPLIT_TO_TABLE`;
  `REGEXP_LIKE`, `REGEXP_MATCHES`, `REGEXP_SPLIT_TO_TABLE` require compatibility level **170**.
- Default compatibility level for new databases: Azure SQL Database **170**; Managed Instance 160 (SQL
  Server 2022 policy) or 170 (Always-up-to-date / 2025 policy); SQL Server 2025 170; SQL Server 2022
  160.
- EF Core 10: with `UseAzureSql()` **or** `UseCompatibilityLevel(170)` (or higher) EF maps primitive
  collections, `ToJson()` owned types and JSON complex properties to `json`; existing `nvarchar(max)`
  JSON columns get an `ALTER COLUMN` migration; opt out with `HasColumnType("nvarchar(max)")` or a
  lower compatibility level; `UseSqlServer()` defaults to compatibility level **150**, so it keeps
  `nvarchar(max)`. `ExecuteUpdate` on JSON complex properties translates to `.modify()` on 2025. Two
  more EF 10 changes matter for a raw-SqlClient data layer: EF injects `Application Name` into
  connection strings that lack one (a different pool key from raw `SqlConnection`s using the original
  string — set `Application Name` explicitly), and parameterized collections now translate to multiple
  scalar parameters with padding (`ParameterTranslationMode`).
- Repo today: every `UseSqlServer(...)` call is plain (no `UseAzureSql`, no `UseCompatibilityLevel`).

Sources: https://devblogs.microsoft.com/azure-sql/announcing-the-general-availability-ga-of-json-data-type-json-aggregates/ ;
https://learn.microsoft.com/en-us/sql/t-sql/data-types/json-data-type ;
https://learn.microsoft.com/en-us/sql/t-sql/statements/create-json-index-transact-sql ;
https://devblogs.microsoft.com/azure-sql/general-availability-announcement-regex-support-in-sql-server-2025-azure-sql/ ;
https://learn.microsoft.com/en-us/sql/sql-server/what-s-new-in-sql-server-2025 ;
https://learn.microsoft.com/en-us/sql/t-sql/statements/alter-database-transact-sql-compatibility-level ;
https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew ;
https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes

**Implication for the design.** Whether a JSON-shaped column (`UserSettings` values, `ImageFit`,
notification preferences) is declared `json` or `nvarchar(max)` is a **platform floor decision**: `json`
requires SQL Server 2025 on-prem (still labelled preview there) and compatibility level 170 for EF to
pick it automatically, while `nvarchar(max)` runs everywhere and is what `UseSqlServer()` produces
today. The safe first-release rule is `nvarchar(max)` (or `varchar(max)` UTF-8) with an explicit
`HasColumnType`, and one deliberate `UseCompatibilityLevel`/`UseAzureSql` decision per host so the
model does not silently flip when a distribution switches to `UseAzureSql()`. Regex and JSON
aggregates are SQL-side conveniences; nothing in this theme needs them.

---

## 7. EF Core 10 enum-as-string conventions and max length

**Verified.**
- Pre-convention configuration: `configurationBuilder.Properties<Enum>().HaveConversion<string>()` is
  the EF team's recommended global enum-to-string setting (Andriy Svyryd on dotnet/efcore#25929,
  2023-09-08: "that's indeed the recommended way"); the docs state the type argument "can be a base
  type, an interface or a generic type definition" and list the precedence (interface → base type →
  generic definition → non-nullable value type → exact type). The EF 8 breaking-changes page shows the
  per-enum form `Properties<StatusEnum>().HaveConversion<string>()` (EF 8+ stores enums in JSON as
  ints unless converted).
- Facets: `PropertiesConfigurationBuilder` exposes `HaveMaxLength(n)`, `AreUnicode(false)` (docs
  example on `Properties<string>()`), and facets configured alongside a conversion "will apply to the
  converted database type" (`HasConversion<string>().HasMaxLength(20).IsUnicode(false)` → `varchar(20)`);
  a `ConverterMappingHints(size:, unicode:)` on a custom converter gives defaults that explicit facets
  override. Without a length the enum column is `nvarchar(max)` (the string default; #25929 reports it).
- Precedence trap: "Pre-convention configuration is equivalent to explicit configuration ... It will
  override all conventions and Data Annotations" — a global `HaveMaxLength` beats `[MaxLength]` on a
  property. A finalizing convention (`IModelFinalizingConvention` using `property.Builder.HasMaxLength`)
  only sets what attributes/fluent config did not, and can compute the length from the longest enum
  member name (the docs' `DiscriminatorLengthConvention3` pattern).
- Nulls are never passed to converters; nullable enum properties share the converter. Whether
  `Properties<Enum>()` matches `TEnum?` is **not stated** in docs (an EF comment notes the "Non-nullable
  value type" matching level) — confirm in a unit test.

Sources: https://learn.microsoft.com/en-us/ef/core/modeling/bulk-configuration ;
https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions ;
https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-8.0/breaking-changes ;
https://github.com/dotnet/efcore/issues/25929 ; https://github.com/dotnet/efcore/issues/26890

**Implication for the design.** One platform-owned `ConfigureConventions` line (`Properties<Enum>()
.HaveConversion<string>()`) plus a finalizing convention that sets a bounded non-unicode length from
the enum's member names gives `CenterType = 'Service'` in Queryex for free, keeps distro code at zero
lines, and still lets a distro attribute override the length. Queryex declares enum properties by store
type, so the schema adapter reads the converted store type (`varchar(n)`), not the CLR enum.

---

## 8. Isolation defaults: Azure SQL versus on-prem, and validation reads before a write

**Verified.**
- `READ COMMITTED` is the default isolation level everywhere. `READ_COMMITTED_SNAPSHOT` is **OFF by
  default on SQL Server** (shared locks protect each read statement) and **ON by default on Azure SQL
  Database** and Fabric SQL database (row versioning; "Locks aren't used to protect the data from
  updates by other transactions"). Azure SQL Database also enables snapshot isolation
  (`ALLOW_SNAPSHOT_ISOLATION ON`) for new databases; both can be changed with `ALTER DATABASE`, and
  the blocking guide notes RCSI "might be disabled". Managed Instance is not called out in these pages;
  **inferred** (and consistent with community/Microsoft support posts) that MI keeps the SQL Server
  default (OFF).
- Turning `READ_COMMITTED_SNAPSHOT` on requires that no other connection be open in the database
  during the `ALTER DATABASE`.
- Isolation does not affect write locks: "A transaction always gets an exclusive lock on any data it
  modifies"; an UPDATE at READ COMMITTED takes update locks on the rows it selects (it evaluates the
  latest committed version, not the statement snapshot). `READCOMMITTEDLOCK` forces locking reads per
  statement under RCSI; `HOLDLOCK` = SERIALIZABLE.

Sources: https://learn.microsoft.com/en-us/sql/t-sql/statements/set-transaction-isolation-level-transact-sql ;
https://learn.microsoft.com/en-us/azure/azure-sql/database/understand-resolve-blocking?view=azuresql

**Implication for the design (inferred from the verified statements).**
- SaaS (Azure SQL DB): validation reads neither block writers nor are blocked; but a read-then-write
  pair in one transaction is not protected — a competing commit between the read and the write goes
  unnoticed (write skew). The RCSI-compatible protections are (a) the concurrency-stamp check executed
  *inside* the persist statements (`UPDATE ... WHERE ModifiedAt = @expected`, U/X locks on the latest
  committed row), (b) DB constraints (unique, FK) as the final arbiter of invariants, and (c) `WITH
  (UPDLOCK, HOLDLOCK)` on the few validation reads whose rows must not change before the write
  (e.g. tree ancestors during cycle validation) — never on the broad context loads.
- On-prem (locking RC): the same validation reads take shared locks and can wait on writers' X locks;
  behaviour diverges from SaaS unless the migrator sets `READ_COMMITTED_SNAPSHOT ON` per tenant DB at
  provisioning (single-connection moment). Recommend that the migrator does so, so the platform has one
  isolation story; document it as a tenant-DB invariant.
- Deadlocks (1205) remain possible between concurrent saves touching the same tables in different
  order; a deterministic table order in the emitter (parents before children, fixed schema order) and
  the `MayRetry` round-trip retry cover it.

---

## 9. OpenTelemetry SqlClient instrumentation 1.16 and counting round trips

**Verified.**
- Package `OpenTelemetry.Instrumentation.SqlClient`: repo pins **1.16.0 (2026-06-24)**; 1.17.0
  (2026-07-17, signed assemblies), **1.18.0 (2026-08-21, latest)** moves to semconv v1.44 and adds
  experimental `db.response.returned_rows`. 1.16.0 is "based on **v1.33** of database semantic
  conventions"; since 1.14.0-rc.1 (2026-01-13) **only the stable conventions are emitted** (the
  `OTEL_SEMCONV_STABILITY_OPT_IN` switch is gone; schema URL v1.33.0). 1.16.0 itself: bug fixes
  (sanitizer, options leaking across providers), native AOT support, no attribute changes.
- Span attributes (1.16.0 README): `db.system.name`, `db.namespace`, `db.query.text` (always sanitized;
  literals become `?`; the `SetDbStatementForText` option was removed in 1.12.0-beta.3),
  `db.query.summary`, `db.operation.name`, `db.stored_procedure.name` (for `CommandType.StoredProcedure`
  the text/operation/collection attributes are dropped in favour of the procedure name),
  `db.response.status_code`, `error.type`, `server.address`, `server.port`; opt-in experimental
  `db.query.parameter.<key>` (env `OTEL_DOTNET_EXPERIMENTAL_SQLCLIENT_ENABLE_TRACE_DB_QUERY_PARAMETERS`)
  and trace-context propagation via `SET CONTEXT_INFO`
  (`OTEL_DOTNET_EXPERIMENTAL_SQLCLIENT_ENABLE_TRACE_CONTEXT_PROPAGATION`). Activities are
  `ActivityKind.Client`, named after the procedure or the query summary. Options: `EnrichWithSqlCommand`,
  `Filter`, `RecordException` (default false), `EnableTraceContextPropagation` (.NET only).
- Metric: `db.client.operation.duration` histogram (seconds) with the same attributes minus
  `db.query.text`. No batch-size or per-request count attribute (`db.operation.batch.size` is not
  emitted; SqlClient cannot expose it, dotnet/SqlClient#4545).
- Mechanism on .NET: `DiagnosticListener` named `SqlClientDiagnosticListener`, events
  `Microsoft.Data.SqlClient.WriteCommandBefore` / `WriteCommandAfter` / `WriteCommandError` (plus
  connection open/close and transaction commit/rollback events) with strongly typed payloads
  (`SqlClientCommandBefore` etc. in `Microsoft.Data.SqlClient.Diagnostics`, exposing `Command`);
  raised by every `SqlCommand.Execute*` path, including the internal command behind `SqlBatch`.
- `SqlConnection.StatisticsEnabled = true` + `RetrieveStatistics()` returns per-connection counters
  including **`ServerRoundtrips`** ("the number of times the connection sent commands to the server and
  got a reply back"; a multi-packet command still counts once), `SumResultSets`, `IduCount`,
  `SelectCount`, `NetworkServerTime`, `ExecutionTime`; `ResetStatistics()` zeroes them.

Sources: https://raw.githubusercontent.com/open-telemetry/opentelemetry-dotnet-contrib/main/src/OpenTelemetry.Instrumentation.SqlClient/CHANGELOG.md ;
https://raw.githubusercontent.com/open-telemetry/opentelemetry-dotnet-contrib/Instrumentation.SqlClient-1.16.0/src/OpenTelemetry.Instrumentation.SqlClient/README.md ;
https://raw.githubusercontent.com/open-telemetry/opentelemetry-dotnet-contrib/main/src/OpenTelemetry.Instrumentation.SqlClient/Implementation/SqlClientDiagnosticListener.cs ;
https://github.com/dotnet/SqlClient/blob/main/src/Microsoft.Data.SqlClient/src/Microsoft/Data/SqlClient/Diagnostics/SqlDiagnosticListener.cs ;
https://learn.microsoft.com/en-us/sql/connect/ado-net/sql/provider-statistics-sql-server ;
https://github.com/dotnet/SqlClient/issues/4545

**Implication for the design (recommended way to count round trips per request).** Count in the batch
executor itself: every `ExecuteReaderAsync` it issues is one round trip by construction, so it can
increment a per-request accumulator (an `AsyncLocal`/request-scoped counter reachable through the
request context) and record `tellma.data.roundtrips` (histogram, unit `{roundtrip}`, tags: operation
kind, `retried`) plus a `tellma.data.roundtrips` tag on the request `Activity` when it ends. This is
exact, works for concatenated text (which the OTel instrumentation would show as one span anyway), and
needs no reflection on driver internals. Use `RetrieveStatistics()["ServerRoundtrips"]` in the LocalDB
test tier to assert the budget of each standard operation (one read, two for a save) independently of
the executor's own bookkeeping. A `BaseProcessor<Activity>` counting SqlClient client spans under the
request span is a third, driver-agnostic option but double-counts nothing only if EF Core's own command
interceptors are not also instrumented. `db.client.operation.duration` remains the fleet-level
count/latency series; the semconv has no per-request round-trip metric.

---

## 10. Unverified or partially verified

- Whether a `SqlDbType.Structured` parameter behaves identically on a `SqlBatchCommand` (no doc, no
  issue; not tested here). Moot if concatenated text is chosen.
- Whether `SqlBatch` continues executing later commands after one fails (docs silent; maintainers
  acknowledged the gap in dotnet/SqlClient#3273).
- Linux behaviour of `Microsoft.SqlServer.Types.SqlHierarchyId` 160/170 (not documented on NuGet; the
  design above avoids needing it).
- `OUTPUT inserted.ValidFrom` on a temporal table (inferred legal; test it).
- Managed Instance `READ_COMMITTED_SNAPSHOT` default (inferred OFF; Microsoft docs consulted do not
  state it).
- Sequence cache loss on Azure SQL / AG failover (inferred from the abnormal-shutdown rule).
- `Properties<Enum>()` matching nullable enum properties (confirm in a unit test).
- The `json` type's "preview" label for on-prem SQL Server 2025 as of the 2026-01-14 doc revision may
  have changed since; re-check before relying on `json` on-prem.
- The "< 1,000 rows" TVP-versus-bulk guidance is a docs heuristic; no benchmark on this schema.
