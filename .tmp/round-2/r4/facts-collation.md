# D-2 facts: database collations

Research for decision D-2 ("every database on `Latin1_General_100_CI_AS`"), 27 October 2026.
Every fact carries a marker:

- **[Verified: URL]** — Microsoft Learn, read on 27 October 2026.
- **[Verified: file:line]** — source in the EF Core checkout or this repository.
- **[Verified: probe Pn]** — observed by running the probe named in "Sources and probes".
- **[Unverified]** — inference or memory; not confirmed by a source or a run.

## Sources and probes

- **EF Core checkout** — `C:\Users\ahmad\workspace\efcore`, branch `release/10.0`, describe
  `v10.0.9-58-g9d1b795935` (commit `9d1b7959357`, 9 June 2026). Every EF file cited below is
  identical to tag `v10.0.9` (`git diff --stat v10.0.9 HEAD` on them is empty). The platform pins
  EF Core 10.0.9 and Microsoft.Data.SqlClient 6.1.1 (`Directory.Packages.props:41`, `:55`).
- **Engines probed** — LocalDB 15.0.4382.1 (SQL Server 2019 Express, `MSSQLLocalDB`), LocalDB
  17.0.4025.3 (SQL Server 2025 Express, `MSSQLLocalDB17`), and throwaway containers
  `mcr.microsoft.com/mssql/server:2022-latest` (16.0.4255.1) and `2025-latest` (17.0.4045.5).
  All probe databases and containers were dropped afterwards.
- **Probe scripts** (scratchpad
  `C:\Users\ahmad\AppData\Local\Temp\claude\C--Users-ahmad-workspace-tellma-tellma-platform\ab119a81-92e3-4ada-8784-ec682d808960\scratchpad\`):
  - **P1** `coll1.sql` — database `COLLATE Latin1_General_100_CI_AS` on a server and `tempdb` of
    `SQL_Latin1_General_CP1_CI_AS`; joins each construct of §3 to a user column. LocalDB 15.0.
  - **P2** `coll2.sql` — the same database created `WITH CATALOG_COLLATION =
    SQL_Latin1_General_CP1_CI_AS` (Azure's default); the table-type sweep's join shape. LocalDB
    15.0, LocalDB 17.0, container 2022.
  - **P3** `coll3.sql` — Ethiopic equality, ordering and grouping per collation; supplementary
    characters under `_SC` and non-`_SC`. LocalDB 15.0 and 17.0, identical results.
  - **P4** `coll4.sql` — `OPENJSON` columns against a user column. LocalDB 15.0.
  - **P5** `coll5.sql` — a `Latin1_General_100_CI_AS_SC` column against a table variable, a TVP
    and a parameter in a `Latin1_General_100_CI_AS` database. LocalDB 15.0.
  - **P6** `efprobe\Program.cs` — EF Core 10.0.9 with SqlClient 6.1.1 against LocalDB 15.0: the
    differ, the generator, `EnsureCreated()` and `ALTER DATABASE … COLLATE` with and without a
    second connection.
  - **P7** — `SERVERPROPERTY('Collation')` and `tempdb`/`model` collations on both container
    images, with no collation environment variable.
  - **P8** — `CREATE DATABASE … COLLATE Latin1_General_100_CI_AS [WITH CATALOG_COLLATION =
    DATABASE_DEFAULT]` on LocalDB 15.0 and 17.0, then `sys.databases`.

## 1. EF Core 10: `UseCollation`, migrations and database creation

### 1.1 What `UseCollation` sets

- **`modelBuilder.UseCollation(name)` stores the collation on the model** (`Model.SetCollation`).
  [Verified: `efcore/src/EFCore.Relational/Extensions/RelationalModelBuilderExtensions.cs:514-523`]
- **The relational model reads it back**: `IRelationalModel.Collation => Model.GetCollation()`.
  It is not a relational-model annotation. [Verified:
  `efcore/src/EFCore.Relational/Metadata/IRelationalModel.cs:58-59`]

### 1.2 What the differ emits

- **A later migration emits `AlterDatabaseOperation`.** When both models exist and their
  collations differ, the differ emits `AlterDatabaseOperation { Collation = target, OldDatabase =
  { Collation = source } }`. [Verified:
  `efcore/src/EFCore.Relational/Migrations/Internal/MigrationsModelDiffer.cs:365-377`; probe P6
  case B: `AlterDatabaseOperation Collation=Latin1_General_100_CI_AS Old=<null>`]
- **The first migration emits no collation operation.** With no snapshot the source model is
  `null` (`efcore/src/EFCore.Design/Migrations/Design/MigrationsScaffolder.cs:145-148`). The
  differ then takes the `Add(target)` path, whose
  `AlterDatabaseOperation` carries only relational-model annotations, never `Collation`.
  [Verified: `MigrationsModelDiffer.cs:389-395`, `:401-416`, `:447-452`; probe P6 case A: the
  only operation for a model with `UseCollation` is `CreateTableOperation`]
- **So an initial migration never sets the database collation.** Only `CREATE DATABASE` (§1.4)
  or a later migration (above) does. [Verified: the two facts above]

### 1.3 The SQL `SqlServerMigrationsSqlGenerator` writes for it

- **To a named collation** (probe P6 case B prints exactly this):

  ```sql
  BEGIN
  DECLARE @db_name nvarchar(max) = QUOTENAME(DB_NAME());
  EXEC(N'ALTER DATABASE ' + @db_name + ' COLLATE Latin1_General_100_CI_AS;');
  END
  ```

  [Verified: `efcore/src/EFCore.SqlServer/Migrations/SqlServerMigrationsSqlGenerator.cs:1130-1149`]
- **Back to the server default** (target `null`): a second variable
  `DECLARE @defaultCollation1 nvarchar(max) = CAST(SERVERPROPERTY('Collation') AS nvarchar(max));`
  is spliced into the same `EXEC`. [Verified: same file `:1138-1143`;
  `efcore/test/EFCore.SqlServer.FunctionalTests/Migrations/SqlServerMigrationsSqlGeneratorTest.cs:583-597`]
- **It always runs outside the migration transaction** (`EndCommand(suppressTransaction:
  true)`). [Verified: same file `:1151-1155`; probe P6: "transaction suppressed: True"]

### 1.4 Creating a missing database

- **`Migrate()` creates a missing database first.** It calls `_databaseCreator.Exists()` and, when
  false, `_databaseCreator.Create()`; `MigrateAsync` does the same. [Verified:
  `efcore/src/EFCore.Relational/Migrations/Internal/Migrator.cs:102-105`, `:230-233`]
- **`EnsureCreated()` uses the same `Create()`**, then creates the tables. [Verified:
  `efcore/src/EFCore.Relational/Storage/RelationalDatabaseCreator.cs:241-246`]
- **`Create()` carries the model's collation.** It builds one `SqlServerCreateDatabaseOperation`
  with `Name` and `FileName` from the connection string and `Collation` from the design-time
  model's relational model. [Verified:
  `efcore/src/EFCore.SqlServer/Storage/Internal/SqlServerDatabaseCreator.cs:53-64`, `:150-163`]
- **The statement it writes**, logged by probe P6 case C:

  ```sql
  CREATE DATABASE [EfCollProbe_aa242069]
  COLLATE Latin1_General_100_CI_AS;
  ```

  `COLLATE` appears only when the model has a collation. A second command follows: `IF
  SERVERPROPERTY('EngineEdition') <> 5` (not Azure SQL Database) `ALTER DATABASE … SET
  READ_COMMITTED_SNAPSHOT ON`. [Verified: `SqlServerMigrationsSqlGenerator.cs:989-1052`; probe
  P6 case C: database and new column both `Latin1_General_100_CI_AS`]
- **It never writes Azure options.** No `EDITION`, `SERVICE_OBJECTIVE`, `ELASTIC_POOL` or `WITH
  CATALOG_COLLATION` is emitted. [Verified: same lines; the operation has no such members]

### 1.5 An existing database whose collation differs

- **`Migrate()` never compares collations.** An existing database skips `Create()`; the history
  table and migrations then run in whatever collation the database has. [Verified:
  `Migrator.cs:102-105`; no collation check in the migrate path]
- **New columns without an explicit collation take the database's collation**, so a model that
  says `UseCollation(X)` builds its tables in the server default when someone else created the
  database without `COLLATE`. [Verified: §1.2 (no operation) and §3.1 (column default)]
- **Only a later migration with a collation change emits `ALTER DATABASE … COLLATE`** (§1.2).

### 1.6 Adding `UseCollation` to a model that already has migrations

- **The next scaffolded migration contains `AlterDatabase`** with the new collation and `null` as
  the old one (§1.2, probe P6 case B).
- **Skipping that migration is not possible**: `Migrate()` checks for pending model changes, and
  `PendingModelChangesWarning` throws by default. A collation difference is a model difference.
  [Verified: `Migrator.cs:378-395`;
  `efcore/src/EFCore.Relational/Infrastructure/RelationalOptionsExtension.cs:466`]
- **The platform's identity model sets no collation today**: `OnModelCreating` only sets the
  default schema, and the snapshot has no collation. [Verified:
  `src/apps/Tellma.Identity/Data/TellmaIdentityDbContext.cs:23-28`;
  `src/apps/Tellma.Identity.Migrations/Tellma/Identity/Migrations/TellmaIdentityDbContextModelSnapshot.cs:20`
  (only `HasDefaultSchema("idsvr")`); grep for `Collation` in that project: none]
- **The identity seeder lets EF create its database**: with `Seed.ApplyMigrations` it calls
  `context.Database.MigrateAsync`, which creates a missing database in the server default (no
  model collation). [Verified: `src/apps/Tellma.Identity/Services/Seeding/IdentitySeeder.cs:53-57`
  and §1.4]

### 1.7 `ALTER DATABASE … COLLATE` on SQL Server

- **Documented conditions**: "You're the only one currently using the database" and no
  schema-bound object depends on the database collation. Schema-bound views and functions,
  computed columns, `CHECK` constraints, and table-valued functions returning character columns
  that inherit the default each block it with an error. [Verified:
  https://learn.microsoft.com/en-us/sql/t-sql/statements/alter-database-transact-sql?view=sql-server-ver17
  "Change the database collation"]
- **It cannot run inside a transaction** ("must run in auto-commit mode"). [Verified: same page,
  Remarks]
- **Existing columns keep their collation.** "This statement does not change the collation of
  the columns in any existing user-defined tables." It does change system-table columns,
  procedure and function parameters, and the string system types. [Verified:
  https://learn.microsoft.com/en-us/sql/relational-databases/collations/set-or-change-the-database-collation;
  probe P6 case E: database becomes `Latin1_General_100_CI_AI`, `dbo.Items.Name` stays
  `Latin1_General_100_CI_AS`]
- **A second open connection makes it fail**: Msg 5030 "The database could not be exclusively
  locked to perform the operation", then "ALTER DATABASE failed. The default collation of database
  '…' cannot be set to …". [Verified: probe P6 case D]
- **Altering to the collation the database already has succeeds** even with a second connection
  open. [Verified: probe P6 case D2, LocalDB 15.0 only]
- **Pooled connections break after a real change**: the next command on a pooled connection
  failed with "Resetting the connection results in a different state than the initial login. The
  login fails." Clearing the pool fixed it. [Verified: probe P6, first run of case E, SqlClient
  6.1.1]

## 2. Azure SQL Database

### 2.1 `CREATE DATABASE` syntax and clause order

- **Order: name, `COLLATE`, the parenthesised edition options, then `WITH`.**

  ```sql
  CREATE DATABASE database_name [ COLLATE collation_name ]
  { (<edition_options> [ , ...n ] ) }
  [ WITH <with_options> [ , ..n ] ]
  -- <with_options>: CATALOG_COLLATION = { DATABASE_DEFAULT | SQL_Latin1_General_CP1_CI_AS }
  --               | BACKUP_STORAGE_REDUNDANCY = … | LEDGER = …
  -- <edition_options>: MAXSIZE = … | EDITION = … | SERVICE_OBJECTIVE = { … | ELASTIC_POOL(name = …) }
  ```

  [Verified:
  https://learn.microsoft.com/en-us/sql/t-sql/statements/create-database-transact-sql?view=azuresqldb-current
  "Syntax"]
- **Microsoft's own examples of the order**: `CREATE DATABASE hito COLLATE
  Japanese_Bushu_Kakusu_100_CS_AS_KS_WS ( MAXSIZE = 500 MB, EDITION = 'GeneralPurpose',
  SERVICE_OBJECTIVE = 'GP_Gen5_8' );` and `CREATE DATABASE TestDB3 COLLATE Japanese_XJIS_140
  (MAXSIZE = 100 MB, EDITION = 'Basic') WITH CATALOG_COLLATION = DATABASE_DEFAULT;`. [Verified:
  same page, Examples]
- **The pool example names no edition**: `CREATE DATABASE db1 (SERVICE_OBJECTIVE = ELASTIC_POOL
  (NAME = S3M100));`. [Verified: same page] Whether `EDITION = …` beside `ELASTIC_POOL(…)` is
  accepted, as spec 0011 §6.1 writes it
  (`docs/specs/0011-distribution-host-and-multitenancy.md:1682`), is [Unverified].
- **The full platform statement is therefore** `CREATE DATABASE [name] COLLATE
  Latin1_General_100_CI_AS (SERVICE_OBJECTIVE = ELASTIC_POOL(name = pool)) WITH CATALOG_COLLATION
  = DATABASE_DEFAULT;`. [Unverified: not run on a live Azure server; each clause and the order are
  verified above]

### 2.2 Defaults and the catalog collation

- **The data collation defaults to `SQL_Latin1_General_CP1_CI_AS`** when `COLLATE` is omitted.
  [Verified: CREATE DATABASE (Azure SQL Database) page, `collation_name`]
- **The catalog collation defaults to `SQL_Latin1_General_CP1_CI_AS` too.** "By default, the
  metadata catalog for system object names is collated to SQL_Latin1_General_CP1_CI_AS collation.
  This is the default setting on Azure SQL Database if CATALOG_COLLATION is unspecified."
  `DATABASE_DEFAULT` makes system views and tables follow the data collation. [Verified: same page,
  `CATALOG_COLLATION`]
- **So `COLLATE Latin1_General_100_CI_AS` alone gives `sys.*` names a different collation from
  data.** [Verified: the two facts above; probe P2 reproduces the effect, §3.5]
- **Both are fixed at creation.** "The CATALOG_COLLATION argument is only available during
  database creation and can't be changed after creation." [Verified: same page] "The database
  collation and catalog collation can be set on database creation and can't be updated."
  [Verified:
  https://learn.microsoft.com/en-us/sql/relational-databases/databases/contained-database-collations]
- **The logical server collation cannot be set or changed**, and `tempdb`'s collation is the
  server collation. [Verified:
  https://learn.microsoft.com/en-us/sql/relational-databases/collations/collation-and-unicode-support?view=sql-server-ver17
  "Collations in Azure SQL Database";
  https://learn.microsoft.com/en-us/sql/relational-databases/databases/tempdb-database?view=sql-server-ver17
  "Limitations"]
- **`sys.databases.catalog_collation_type_desc` reports it** (`DATABASE_DEFAULT` or
  `SQL_Latin_1_General_CP1_CI_AS`), documented as Azure SQL Database only. [Verified:
  https://learn.microsoft.com/en-us/sql/relational-databases/system-catalog-views/sys-databases-transact-sql?view=sql-server-ver17]

### 2.3 `ALTER DATABASE … COLLATE` is not supported

- "The ALTER DATABASE COLLATE statement is not supported on Azure SQL Database." [Verified:
  https://learn.microsoft.com/en-us/sql/relational-databases/collations/set-or-change-the-database-collation]
- "Collation can't be changed after database has been created on Azure SQL Database." The Azure
  SQL Database `ALTER DATABASE` syntax has no `COLLATE` branch. [Verified:
  https://learn.microsoft.com/en-us/sql/t-sql/statements/alter-database-transact-sql?view=sql-server-ver17
  `COLLATE` argument note and "Syntax" for SQL Database]
- **What EF's §1.3 batch does there** (an error, or a no-op when the collation already matches) is
  [Unverified]; the statement is outside the documented syntax.

### 2.4 `CREATE DATABASE … AS COPY OF`

- **The copy syntax takes no `COLLATE` or `CATALOG_COLLATION`**: only `SERVICE_OBJECTIVE` (or
  `ELASTIC_POOL`) and `BACKUP_STORAGE_REDUNDANCY`. [Verified: CREATE DATABASE (Azure SQL Database)
  page, "Copy a database"]
- **A copy is "a transactionally consistent snapshot of the source database"**, seeded by
  geo-replication. [Verified:
  https://learn.microsoft.com/en-us/azure/azure-sql/database/database-copy]
- **Neither page mentions collation.** That the copy keeps the source's data and catalog
  collations follows from the two facts above but is [Unverified] as a stated guarantee.

### 2.5 Bicep and ARM: `Microsoft.Sql/servers/databases`

- **`properties.collation`** — "The collation of the database." (string). [Verified:
  https://learn.microsoft.com/en-us/azure/templates/microsoft.sql/servers/databases?pivots=deployment-language-bicep]
- **`properties.catalogCollation`** — "Collation of the metadata catalog.", values
  `'DATABASE_DEFAULT'` or `'SQL_Latin1_General_CP1_CI_AS'`. [Verified: same page]
- **The newest API version listed is `2026-08-01-preview`.** [Verified: same page]
- **The reference states no default and no immutability for either property.** That an omitted
  value means `SQL_Latin1_General_CP1_CI_AS` (as in T-SQL), and what a redeploy with a changed
  value does, are [Unverified].

## 3. Collation conflicts when a database differs from its server

### 3.1 The rule that decides every case

- **Labels.** A column reference is *Implicit X*. A variable, parameter, literal, or the output of
  a catalog built-in function is *Coercible-default*: the current database's collation in a batch,
  the module's database inside a procedure, function or trigger. [Verified:
  https://learn.microsoft.com/en-us/sql/t-sql/statements/collation-precedence-transact-sql?view=sql-server-ver17
  "Collation labels"]
- **Implicit X with Implicit Y gives No-collation.** Implicit beats Coercible-default, so a column
  never conflicts with a variable, parameter or literal. [Verified: same page, "Collation rules"]
- **Collation-sensitive operations then fail at compile time**: comparisons, `IN`, `LIKE`,
  `BETWEEN`, `MIN`, `MAX`, `UNION`, and string concatenation. `UNION ALL` and `CASE` are
  insensitive but their result cannot be selected or compared. [Verified: same page, "Operators and
  collation"]
- **The error is Msg 468** "Cannot resolve the collation conflict between "X" and "Y" in the equal
  to operation." (or "in the UNION operation"). The whole batch fails to compile. [Verified: probe
  P1; in P1 the failing batches' own `PRINT` statements never ran]

### 3.2 Each construct, in a non-contained database

Probe P1: database `Latin1_General_100_CI_AS`, server and `tempdb` `SQL_Latin1_General_CP1_CI_AS`,
user column `dbo.T.Name nvarchar(100)` with no explicit collation.

| Construct | Its string column's collation | Joined to `dbo.T.Name` |
|---|---|---|
| `CREATE TABLE #t (Name nvarchar(100))` | `tempdb`'s: `SQL_Latin1_General_CP1_CI_AS` | **Msg 468** |
| `#temp` column `COLLATE DATABASE_DEFAULT` | `Latin1_General_100_CI_AS` | works |
| `SELECT Name INTO #t FROM dbo.T` | copied from the source column | works |
| `DECLARE @tv TABLE (Name nvarchar(100))` | the current database's | works |
| TVP of `CREATE TYPE … AS TABLE (Id nvarchar(450))` | the database's, fixed at `CREATE TYPE` | works |
| variable, parameter, `N'…'` literal | Coercible-default | works |
| `sys.objects.name` (user database) | the database's | works |
| `sys.databases.name` | `master`'s: `SQL_Latin1_General_CP1_CI_AS` | **Msg 468** |

- **Every row is observed in probe P1.** [Verified: probe P1, cases 1–8]
- **Further P1 cases**: `#temp` joined to a table variable fails (468); `SELECT … FROM dbo.T UNION
  SELECT … FROM #t` fails (468, "UNION operation"); `sys.databases.name = sys.objects.name` fails
  (468); `sys.databases.name` against a `sysname` variable or a literal works. [Verified: probe P1,
  cases 9, 10, 12, 13]
- **Explicitly collated columns behave the same way.** A `Latin1_General_100_BIN2` column joined
  to a `#temp` column fails (468); against a variable it works. [Verified: probe P1, case 14] A
  `Latin1_General_100_CI_AS_SC` column against a table variable or a TVP column of the database
  default fails (468, "Latin1_General_100_CI_AS" vs "…_SC"); against a parameter it works.
  [Verified: probe P5]
- **Table-variable columns take the current database's collation** unless declared otherwise:
  "If not specified, the column is assigned either the collation of the user-defined data type …
  or the collation of the current database." [Verified:
  https://learn.microsoft.com/en-us/sql/t-sql/language-elements/declare-local-variable-transact-sql?view=sql-server-ver17
  `COLLATE collation_name`]
- **`#temp` columns take `tempdb`'s collation**, which "usually matches the collation for the
  instance", and Microsoft shows the Msg 468 join with the `COLLATE DATABASE_DEFAULT` fix.
  [Verified:
  https://learn.microsoft.com/en-us/sql/relational-databases/databases/contained-database-collations
  "Non-contained databases", Example 2]
- **`OPENJSON` does not conflict** with a user column, with or without a `WITH` schema. [Verified:
  probe P4]

### 3.3 Contained databases

- **Temp data takes the database collation in a contained database.** The documented table:
  user data `DATABASE_DEFAULT` in both; temp data `tempdb` collation (non-contained) versus
  `DATABASE_DEFAULT` (contained); metadata `DATABASE_DEFAULT`/`CATALOG_DEFAULT` versus
  `CATALOG_DEFAULT`. [Verified: contained-database-collations page, table under "Contained
  databases"]
- **A contained database's catalog collation is fixed at `Latin1_General_100_CI_AS_WS_KS_SC`** on
  SQL Server. [Verified: same page; ALTER DATABASE page, `COLLATE` argument]
- **The "Variables: Instance collation" row concerns variable names**, as its `@i`/`@I` example
  shows; a variable's value follows §3.1. [Verified: same page, "Example 2" function `f`; collation
  precedence page]

### 3.4 Azure SQL Database and `#temp`

- **No Microsoft Learn page states which collation a `#temp` column gets on Azure SQL Database.**
  The `tempdb` page documents per-database `tempdb` and database-scoped global temp tables only.
  [Verified: tempdb-database page, "tempdb in Azure SQL Database"]
- **That it follows the database collation**, as a Microsoft Q&A answer claims, is [Unverified].

### 3.5 Azure's catalog collation, reproduced on box SQL Server

- **Box SQL Server accepts `WITH CATALOG_COLLATION`.** `CREATE DATABASE … COLLATE
  Latin1_General_100_CI_AS WITH CATALOG_COLLATION = SQL_Latin1_General_CP1_CI_AS` succeeds;
  `catalog_collation_type_desc` reads `SQL_Latin1_General_CP1_CI_AS` and `sys.table_types.name`
  then has that collation. The SQL Server syntax on Learn does not list the option. [Verified:
  probe P2 on LocalDB 15.0, LocalDB 17.0 and container 2022; CREATE DATABASE page, SQL Server
  syntax]
- **The platform's table-type sweep fails there.** `DECLARE @keep TABLE ([name] sysname PRIMARY
  KEY)` then `LEFT JOIN @keep AS k ON k.[name] = tt.[name]` over `sys.table_types` raises Msg 468
  between `SQL_Latin1_General_CP1_CI_AS` and `Latin1_General_100_CI_AS`. A `sysname` variable
  against `sys.table_types.name` works. [Verified: probe P2 on all three engines]
- **That is the sweep's exact shape.** [Verified:
  `src/core/Tellma.Core.EntityFrameworkCore/TableTypes/TableTypesSqlServerMigrationsSqlGenerator.cs:274`,
  `:288-297`]
- **The box default is `DATABASE_DEFAULT`**, and `WITH CATALOG_COLLATION = DATABASE_DEFAULT` is
  accepted. [Verified: probe P8 on LocalDB 15.0 and 17.0]
- **On box SQL Server the sweep works** because `sys.*` names follow the database collation in a
  non-contained database. [Verified: probe P1 case 11; contained-database-collations page:
  "metadata is collated in the database collation"]

## 4. Ethiopic (Amharic) text and the `_SC` suffix

### 4.1 What Microsoft documents

- **Older collation versions lack sort weights for some code points.** "Depending on the version
  of the collation, some code points might not have sort weights, or uppercase/lowercase mappings
  defined." "When working with some languages, it can be critical to avoid the older collations.
  For example, this is true for Telegu." [Verified:
  https://learn.microsoft.com/en-us/sql/t-sql/statements/windows-collation-name-transact-sql?view=sql-server-ver17
  "Remarks"]
- **`SQL_*` collations sort Unicode data by "a particular version of Windows sorting rules"**;
  the setup default for "English (United States)" is `SQL_Latin1_General_CP1_CI_AS`, whose
  "closest Windows collation counterpart" is `Latin1_General_100_CI_AS_SC`. [Verified:
  collation-and-unicode-support page, "SQL Server collations" and "Server-level collations"]
- **Microsoft does not list which characters a collation leaves without weights**, nor say what
  such characters do in comparisons. [Unverified: no such list found on the pages above]
- **Both collations use code page 1252 for `varchar`.** [Verified: windows-collation-name page
  (`Latin1_General_100_CI_AS` "maps to code page 1252"); the `CP1` in the SQL collation's name is
  code page 1252 per the collation-and-unicode-support page's en-US example]

### 4.2 What the engine does (probe P3, LocalDB 15.0 and 17.0)

Sample: ሀ U+1200, ለ U+1208, መ U+1218, ሰ U+1230, and the words አዲስ, በነክ, መስከረም.

| Behaviour | `SQL_Latin1_General_CP1_CI_AS` | `Latin1_General_CI_AS` (v80) | `Latin1_General_100_CI_AS` and `_SC` |
|---|---|---|---|
| `NCHAR(0x1200) = NCHAR(0x1208)` | equal | equal | different |
| `N'አዲስ' = N''` | equal | not run | different |
| `COUNT(DISTINCT …)` over the 7 strings | 1 | not run | 7 |
| `ORDER BY` | no order (ties throughout) | not run | ሀ, ለ, መ, መስከረም, ሰ, በነክ, አዲስ |

- **The server default treats Ethiopic as weightless**: different letters compare equal, a word
  equals the empty string, and `GROUP BY`, `DISTINCT`, unique indexes and `ORDER BY` collapse
  Amharic text. [Verified: probe P3]
- **The version-100 collation orders Ethiopic syllables in code-point order**, which follows the
  traditional fidel order (ሀ ለ … ሰ … በ … አ). `_SC` gives the same order for these BMP letters.
  [Verified: probe P3; the fidel-order claim is [Unverified]]

### 4.3 What `_SC` changes

- **The `_SC` family covers the full Unicode range for `nchar`, `nvarchar` and `sql_variant`.** All
  version-100 collations already sort supplementary characters linguistically; `_SC` cannot be
  applied to `SQL_*`, `BIN`/`BIN2` or version-140 collations. [Verified:
  collation-and-unicode-support page, "Supplementary characters"]
- **Documented function differences** (with `_SC` versus without):
  - `CHARINDEX`, `LEN`, `PATINDEX`: a surrogate pair counts as one code point versus two.
  - `LEFT`, `REPLACE`, `REVERSE`, `RIGHT`, `SUBSTRING`, `STUFF`: work per code point versus "might
    split any surrogate pairs".
  - `NCHAR(> 0xFFFF)`: the character versus `NULL`. `UNICODE`: up to 0x10FFFF versus a UCS-2 unit.
  - `_` and `[^…]` wildcards: supplementary characters supported versus not.

  [Verified: same page, the comparison table]
- **Observed on `N'a' + U+1F600 + N'b'`**: `LEN` 4 versus 3; `SUBSTRING(…, 2, 1)` returns the high
  surrogate 0xD83D versus U+1F600; `CHARINDEX(N'b', …)` 4 versus 3 (non-`_SC` first). [Verified:
  probe P3]
- **Amharic is in the BMP.** Ethiopic (U+1200–), Ethiopic Supplement (U+1380–), Ethiopic
  Extended (U+2D80–) and Extended-A (U+AB00–) are BMP; only Ethiopic Extended-B
  (U+1E7E0–U+1E7FE, Gurage syllables) is supplementary. [Verified: Python `unicodedata` 15.1.0 names U+1200, U+1380,
  U+2D80, U+AB01 and U+1E7E0 as Ethiopic syllables] So `_SC` changes nothing for Amharic text.

### 4.4 Spec 0008 under a non-`_SC` database

- **The Queryex fixture states `_SC` per column.** `LedgerDdl.Collation =
  "Latin1_General_100_CI_AS_SC"`, appended as `COLLATE` to every text column. [Verified:
  `test/shared/Tellma.Queryex.Testing/Schema/LedgerDdl.cs:28`, `:108`]
- **Spec 0008 delegates every text rule to the backend.**
  - Comparison "follows the column's backend collation", and "`length` reports the backend's own
    count". [Verified: `docs/specs/0008-queryex.md:1398-1405`]
  - `contains`, `startsWith` and `endsWith` emit `CHARINDEX`-style matching whose sensitivity
    "follows the backend collation". [Verified: `0008-queryex.md:1750-1759`]
  - Literal adoption into `VarChar` is limited to ASCII values. [Verified:
    `0008-queryex.md:2074-2080`]
  - Fixture text stays in ASCII, where the interpreter and the collation provably agree.
    [Verified: `0008-queryex.md:2479-2484`, `:2523-2526`]
  - The C# evaluation subset refuses text comparisons and text functions with QX4010. [Verified:
    `docs/specs/0010-amendments-to-implemented-specs.md:3424`, `:3427`]
- **So a non-`_SC` database changes no rule of spec 0008.** It changes only the values `length`,
  `substring`, `left`, `right` and the `CHARINDEX` matches produce for supplementary characters
  (UTF-16 code units, §4.3), which spec 0008 already leaves to the backend, and which the ASCII
  fixture never exercises. [Verified: the facts above]
- **The `_SC` column collation can conflict inside the fixture database.** Its columns differ from
  any string column that takes the database default (Msg 468, probe P5). Today no table variable
  or TVP exists in the Queryex test projects. [Verified: grep of
  `test/shared/Tellma.Queryex.Testing` and `test/core/Tellma.Core.Queryex.IntegrationTests` for
  `TABLE (`, `READONLY`, `Structured`, `StringList`: none] Spec 0010 §F.1's key-set restriction
  emits `<column> IN (SELECT [Id] FROM <TableSource>)`; a String key over the fixture would hit
  the conflict. [Verified: `0010-amendments-to-implemented-specs.md:2701-2725`]

## 5. LocalDB and the SQL Server container images

- **LocalDB's server collation is fixed.** "The instance collation for LocalDB is set to
  SQL_Latin1_General_CP1_CI_AS and can't be changed. Database-level, column-level, and
  expression-level collations are supported normally." [Verified:
  https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/sql-server-express-localdb?view=sql-server-ver17;
  observed on LocalDB 15.0 and 17.0 with `master`, `tempdb`, `model` and `msdb` all
  `SQL_Latin1_General_CP1_CI_AS`]
- **LocalDB databases have `AUTO_CLOSE` on**, so `sys.databases.collation_name` can read `NULL`
  while a database is closed; `DATABASEPROPERTYEX(name, 'Collation')` reads it reliably.
  [Verified: probe P8 (`is_auto_close_on = 1`, `collation_name` `NULL` right after creation);
  sys-databases page, `collation_name`]
- **Both container images default to `SQL_Latin1_General_CP1_CI_AS`** for the server, `tempdb`
  and `model`. [Verified: probe P7, images 2022-latest (16.0.4255.1) and 2025-latest (17.0.4045.5)]
- **`MSSQL_COLLATION` overrides the default** at setup: "Sets the default collation for SQL
  Server. This setting overrides the default mapping of language ID (LCID) to collation."
  [Verified:
  https://learn.microsoft.com/en-us/sql/linux/sql-server-linux-configure-environment-variables?view=sql-server-ver17]
- **Testcontainers.MsSql 4.13.0 sets no collation.** The only variables in its assembly are
  `ACCEPT_EULA`, `MSSQL_SA_PASSWORD` and the `SQLCMD*` set; the generic
  `ContainerBuilder.WithEnvironment(name, value)` exists. [Verified: UTF-16 strings of
  `~/.nuget/packages/testcontainers.mssql/4.13.0/lib/*/Testcontainers.MsSql.dll`;
  `~/.nuget/packages/testcontainers/4.13.0/lib/netstandard2.1/Testcontainers.xml:625`]
- **The caveat for a database created with another collation** is §3.2: `tempdb` and `master`
  stay on the server collation, so `#temp` columns and `sys.databases` names conflict with the
  database's columns. On box SQL Server, `sys.*` names in the database follow it. [Verified: probe
  P1]
- **Today's fixtures create databases with no `COLLATE`**, so they take the server default:
  - `test/apps/Tellma.Identity.IntegrationTests/Infrastructure/SqlServerFixture.cs:73`
  - `test/apps/Tellma.Identity.E2E/Infrastructure/IdentityServerFixture.cs:498`
  - `test/core/Tellma.Core.EntityFrameworkCore.IntegrationTests/Infrastructure/SqlServerFixture.cs:72`
  - `test/core/Tellma.Core.Queryex.IntegrationTests/SqlServerFixture.cs:68`
  - `eng/queryex-inspection/Tellma.Queryex.Inspection/Fixture.cs:74`

  [Verified: grep for `CREATE DATABASE` in `src`, `test`, `eng`]

## Implications for the specs

### Every database gets `Latin1_General_100_CI_AS`

- **Name the collation at every creation point; never inherit it.** Every server the platform
  meets defaults to `SQL_Latin1_General_CP1_CI_AS` (§2.2, §5), and EF's implicit creation adds
  `COLLATE` only when the model has one (§1.4).
- **On Azure, also state `WITH CATALOG_COLLATION = DATABASE_DEFAULT`, after the edition options.**
  Without it `sys.*` names keep `SQL_Latin1_General_CP1_CI_AS` (§2.2) and the table-type sweep
  fails with Msg 468 on every migration that touches types, in tenant databases and in the catalog
  (§3.5). Spec 0011 §6.1's `provision` statement and `ARCHITECTURE.md`'s "Migrations & seeding"
  omit it today. Box SQL Server accepts the same clause, so one statement can serve every target
  (§3.5, P8).
- **Bicep that declares a database sets `collation: 'Latin1_General_100_CI_AS'` and
  `catalogCollation: 'DATABASE_DEFAULT'`** (§2.5).
- **Fix the collation at creation, never by migration.** Azure cannot alter it (§2.3). On SQL
  Server, `ALTER DATABASE … COLLATE` needs exclusive access and leaves every existing column on
  the old collation (§1.7).
- **The identity amendment (spec 0010 Part A) must not add `UseCollation` alone.** The next
  migration would carry `ALTER DATABASE … COLLATE` (§1.6), and `Migrate()` refuses to skip it.
  The options that work:
  - the host creates the database with the platform statement before `MigrateAsync`, so EF finds
    it existing (§1.4, §1.5); or
  - `UseCollation` plus a migration whose `Up` drops the `AlterDatabase` operation: EF's own
    `Create()` then writes `COLLATE` for a missing database, and the snapshot stays in step. This
    never carries the Azure catalog clause, so it suits development and tests only.
  - In in-proc mode the store is the catalog database, so the catalog's creation statement covers
    it.
- **Say what happens to an existing database with another collation.** EF never checks (§1.5).
  A creator that wants the guarantee reads `DATABASEPROPERTYEX(DB_NAME(), 'Collation')`, and on
  Azure `sys.databases.catalog_collation_type_desc`, then refuses or warns. Azure offers no in-place
  conversion (§2.3).
- **Copies keep the source's collations** (§2.4, inference). A sandbox clone of a correctly
  created tenant needs no clause.
- **Test databases use the production statement; test servers keep their default.** LocalDB
  cannot change its server collation and containers default to the Azure-like server collation
  (§5), so `tempdb` mismatches surface in tests as in production. The five fixtures listed in §5
  create databases without `COLLATE` today.
- **A test can reproduce Azure's catalog default** by creating a database `WITH CATALOG_COLLATION
  = SQL_Latin1_General_CP1_CI_AS` on LocalDB or the container (§3.5). This is undocumented for SQL
  Server, so a spec should treat it as a test device only.

### Conflict risks that need a rule (only constructs the platform uses)

- **`sys.*` names joined to user values: the table-type sweep.** This is the one known instance,
  found by reading the generator (`TableTypesSqlServerMigrationsSqlGenerator.cs:274`, `:297`) and
  reproduced in probe P2. Every other `sys.*` use in that file compares with literals or variables
  (`:118-186`, `:200-245`), which never conflict (§3.1). The Azure catalog clause above prevents
  it. Declaring the `@keep` column `COLLATE CATALOG_DEFAULT` would also harden it for databases
  created elsewhere; that is a code change under frozen spec 0001.
- **Table variables and TVPs against explicitly collated columns.** Spec 0012 makes table
  variables the batch-local tables and `OUTPUT` targets
  (`docs/specs/0012-data-access-layer.md:1463`), and list types such as `StringList (Id
  nvarchar(450))` take the database default (`0012-data-access-layer.md:766`;
  `src/core/Tellma.Core.Abstractions/TableTypes/BulkLists.cs:45-50`).
  They are safe against database-default columns (§3.2). Against an explicit collation (the
  `Latin1_General_100_BIN2` `Subject`, `Key` and `Handle` columns, or the Queryex fixture's `_SC`
  columns) they raise Msg 468 (P1 case 14, P5). The rule: a table type or table variable compared
  with an explicitly collated column declares the same collation, as `TenantMembershipList.Subject`
  already does (`docs/specs/0011-distribution-host-and-multitenancy.md:1862-1865`).
- **`#temp` tables.** None appear in specs 0010–0035 or in `src` (grep for `#`-prefixed table
  names, `INTO #` and `tempdb`). A `#temp` column conflicts on every engine probed (§3.2), and
  Azure's behaviour is undocumented (§3.4). The rule: no `#temp` tables, or every string column
  `COLLATE DATABASE_DEFAULT`.
- **`sys.databases` joined to user data.** None found. Spec 0011 §6.1 states the "already exists
  with another collation" check without its SQL; written as a comparison with a variable or a
  literal it works (§3.2, P1 case 10). The rule, if stated: compare `sys.databases` columns only
  with variables or literals.
- **Not a risk**: variables, parameters and literals (§3.1); `SELECT INTO` copies (§3.2);
  `OPENJSON` (§3.2, P4); the UTF-8 JSON wire columns, which are assigned into table columns and
  never compared (assignment is collation-insensitive, §3.1).
