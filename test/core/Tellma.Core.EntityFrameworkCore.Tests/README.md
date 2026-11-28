# Tellma.Core.EntityFrameworkCore.Tests

Unit tests of the table-types runtime library — no database, no Design package. The project
deliberately references **only** the runtime project: building it is a standing proof that the
runtime API surface (operations, differ, SQL generation, metadata) is complete without any
design-time package.

| Folder | Covers |
| --- | --- |
| `Conventions/` | Opt-in/out, default naming, attribute inheritance (pack→leaf), fluent-beats-attribute, exclusions, rowversion, computed columns, validations, JSON columns (ToJson/primitive-collection/native-json as varchar(max)/nvarchar(max)), column-order parity |
| `Metadata/` | The metadata API (spec §6): ordered columns, store types and facets, PK mirroring, grants; the column-ordering rule |
| `Diffing/` | Model-pair differ tests: exact operations per change class, deterministic ordering relative to table operations, EnsureSchema for type-only schemas |
| `SqlGeneration/` | Golden SQL for create/drop: PK, collation, grants, memory-optimized pre-flight (53101), drop guard (53102), idempotent-option pass-through |
| `Standalone/` | Standalone types (spec 0001 §5): the ad-hoc fluent and class-derived routes, incl. the platform bulk shapes from `Tellma.Core.Abstractions`; column collations (`UseCollation`, the character-column check, versioning, unchanged canonical JSON without one) |
| `Internal/` | Pinning tests for the internal-API quarantine (spec Rule 1): EF version band, differ ctor signature, the SQL Server annotation provider's base and constructor, service replacement |
| `LockEscalation/` | The `LOCK_ESCALATION` option: each mode read back as its keyword, an undeclared one as null, an undefined mode refused; the option carried onto the create and set by a statement of its own after it, declared on an existing table, dropped (restoring `TABLE`) and unchanged (nothing emitted); a keyword SQL Server does not accept refused at generation |
| `Boundary/` | Rule 3 mechanics: no Design assembly references, a Design-free transitive dependency closure (the publish-output guarantee), a ground-truth output-directory scan, no Tellma app references, internal-API usage confined to the quarantine namespace |

Test models live in `Infrastructure/TestModel.cs`; contexts are built with
`UseSqlServer(...).UseTableTypes()` and **never connect** — tests only read the finalized model
and generate SQL text.
