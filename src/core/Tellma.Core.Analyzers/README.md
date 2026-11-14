# Tellma.Core.Analyzers

The platform's Roslyn analyzers. Each rule refuses, at build time, a way of reaching the database
or the service pipeline that would go around the platform's run-time checks. Every rule is an error
by default.

The project is never a package of its own. `Tellma.Core.Abstractions` packs the analyzer and the
T-SQL parser it loads under `analyzers/dotnet/cs`, so every project that references the contract
package runs the rules. Inside the repository, the root `Directory.Build.targets` hands the same
two assemblies from this project's output folder to every project that reaches
`Tellma.Core.Abstractions` through project references, directly or transitively.

## The rules

| Id | Refuses |
|---|---|
| `TELLMA0001` | Constructing a `SqlDataRecord`, or calling its `Set*` methods or `GetOrdinal`, outside a type marked `[TableTypeBinder]`. Table-valued parameter rows bind from the model by column name. |
| `TELLMA0002` | An EF query (`Set<T>()`, `Find`, an entity set), raw EF SQL through `Database`, or `SaveChanges`/`SaveChangesAsync` over `TellmaDbContext`, outside `Tellma.Core` and the assemblies whose name ends in `.Migrator`. |
| `TELLMA0003` | An `IDataBatch.Sql` call that cannot be checked or breaks the batch's rules (see below). |
| `TELLMA0004` | A call to a method marked `[ApiAction]` or `[EntityAction]` from outside its declaring type and `Tellma.Core`. A derived type may call one on its own instance. |
| `TELLMA0005` | `SaveAsync`, `DeleteByIdsAsync`, `DeleteByQueryAsync`, `ExecuteActionAsync`, `ActivateAsync`, `DeactivateAsync` or `DeleteWithDescendantsAsync` of an `EntityService<,>` called from a pipeline participant: a hook override, an `[EntityAction]` method, or any member of an `IEntityValidator<>` or an `IPersistEffect<>`. |
| `TELLMA0006` | A write into the `[Table]` of a mapped entity, or of one of its child collections, from outside `Tellma.Core` by a type that is not the table's owner. The write is an `IDataBatch.Save<T>`, `Update<T>` or `Delete<T>`, or a raw statement whose text writes the table. |

### What `TELLMA0003` checks

The text is read from the interpolated string at the call and parsed with ScriptDom's SQL Server
2025 parser. Each hole is replaced by a stand-in first: the quoted name for an identifier whose name
the code spells out, a bracketed stand-in for one it does not, and a variable for every other hole.

- The text is an interpolated string written at the call. Text held in a variable, built by
  concatenation, or passed through `IDataBatch.Sql` taken as a delegate is refused, because it
  cannot be parsed.
- Every hole binds. A hole is a scalar value, a `SqlIdentifier`, or a list of table-type rows. A
  hole with an alignment or a format string is refused, and so is a bare `nameof`. Write a name as
  `{new SqlIdentifier(nameof(...))}`.
- No hole sits inside a string literal, a comment or a quoted name.
- The text parses, as one batch.
- No `MERGE`. No `EXEC` other than `sys.sp_sequence_get_range` and `sys.sp_getapplock`, and no
  dynamic SQL. No `EXECUTE AS`.
- No `SET TRANSACTION ISOLATION LEVEL`, no transaction statement (`BEGIN`, `COMMIT`, `ROLLBACK`,
  `SAVE`), and no `SET XACT_ABORT` or `SET IMPLICIT_TRANSACTIONS`. The executor owns all of these.
- No reserved name (`@tm_`, and `@qx` or `@tb` followed by digits and `_`) is declared or assigned
  outside `Tellma.Core`. Reading one, such as `@tm_CallerId`, passes. No value hole is assigned.
- `THROW` uses a number from 50400 to 50699 when the text fixes it. `RAISERROR` is kept for
  informational messages at a constant severity of 10 or less. The legacy
  `RAISERROR number 'message'` form does not parse, so it is refused as well.
- A permanent table written by `INSERT`, `UPDATE`, `DELETE`, `TRUNCATE`, `SELECT INTO` or
  `OUTPUT INTO` is declared in `SqlOptions.Writes`. The target is resolved through `FROM` aliases
  and common table expressions. Table variables, temporary tables and batch tables need no
  declaration. When the set is spelled out at the call, every written table must be in it.
- A write into a table whose entity carries `[BumpsUserVersionTag]` names the affected users,
  through `SqlOptions.UserIds` or `SqlOptions.ForCaller`.

### What the rules cannot see

The rules see only the code at the call site, so a few writes are left to the batch's run-time
checks:

- an identifier hole whose name the code does not spell out;
- a type parameter whose constraints name no entity;
- a written-tables set held in a field or built by a helper.

Ownership under `TELLMA0006` is read from the writing code's enclosing types. An owner derives
from `EntityService<,>`, or implements `IEntityValidator<>` or `IPersistEffect<>`, over the
entity whose table it writes, or over that table's root. A companion is generic over its entity,
so its writes name no table and pass. Stack registration happens at composition, which the
analyzer cannot see, so every mapped entity's table counts as stack-owned.

## Layout

| Path | Contents |
|---|---|
| [TellmaDiagnostics.cs](TellmaDiagnostics.cs) | The six descriptors. |
| [KnownNames.cs](KnownNames.cs) | Every type, namespace and assembly name the rules match. The analyzer suite pins each to the real type. |
| [SymbolExtensions.cs](SymbolExtensions.cs) | The name-based symbol tests the rules share. |
| `*Analyzer.cs` | One analyzer per rule. [DataBatchAnalyzer](DataBatchAnalyzer.cs) carries both `TELLMA0003` and `TELLMA0006`. |
| [Sql/](Sql) | Reading the raw statement: the text and its holes, the ScriptDom inspector, the options' shape, and the reserved-name rule. |
| [Entities/](Entities) | The index of mapped entity tables and their owners. |

The rules match every type by namespace and metadata name, never by reference. That keeps this
netstandard2.0 assembly free of the platform's own assemblies, so it loads in any compiler host.
The suite is `test/core/Tellma.Core.Analyzers.Tests`.
