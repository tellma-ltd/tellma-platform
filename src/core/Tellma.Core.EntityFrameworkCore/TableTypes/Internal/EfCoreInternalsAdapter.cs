// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// ---------------------------------------------------------------------------------------------
// INTERNAL-API QUARANTINE (spec 0001 Rule 1). This file is the ONLY place in the library allowed to
// reference EF Core internal (".Internal"-namespace) APIs. Everything here is pinned by tests
// that fail loudly when an EF upgrade changes the internal surface, and the central package pin
// in Directory.Packages.props is the only way the EF version moves.
//
// Inventory of internal usage:
//   1. MigrationsModelDiffer (Microsoft.EntityFrameworkCore.Migrations.Internal) — the base
//      class of the differ, plus the internal types in its constructor signature
//      (IRowIdentityMapFactory, CommandBatchPreparerDependencies).
//   2. SqlServerOptionsExtension (Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal)
//      — used only as a type check to validate that UseTableTypes() accompanies UseSqlServer().
// ---------------------------------------------------------------------------------------------

#pragma warning disable EF1001 // Internal EF Core API usage — quarantined in this file per Rule 1.

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update.Internal;
using Tellma.Core.EntityFrameworkCore.TableTypes.Operations;

namespace Tellma.Core.EntityFrameworkCore.TableTypes.Internal
{
    /// <summary>
    ///     An <see cref="IMigrationsModelDiffer" /> that appends table-type operations to the
    ///     relational differ's output. Installed by <c>UseTableTypes()</c> in place of EF's
    ///     registration (SQL Server registers no differ of its own; the relational
    ///     <see cref="MigrationsModelDiffer" /> is the effective base either way).
    /// </summary>
    /// <remarks>
    ///     The actual comparison is pure public-API code in <see cref="TableTypeDiffer" />; this
    ///     subclass only splices its results after the base differ's (already sorted) operations: an
    ///     <c>EnsureSchema</c> for any type-only schema, then the creates, then the cleanup sweep
    ///     last — all appended after the table operations, which is always safe because table types
    ///     depend on no tables and (per the architecture) nothing persisted may depend on them.
    /// </remarks>
    /// <param name="typeMappingSource">The relational type mapping source; passes through to the base differ.</param>
    /// <param name="migrationsAnnotationProvider">The migrations annotation provider; passes through to the base differ.</param>
    /// <param name="relationalAnnotationProvider">The relational annotation provider; passes through to the base differ.</param>
    /// <param name="rowIdentityMapFactory">The row identity map factory; passes through to the base differ.</param>
    /// <param name="commandBatchPreparerDependencies">The command batch preparer dependencies; pass through to the base differ.</param>
    /// <param name="contextOptions">The context options, used only to read the configured sweep scope.</param>
    public class TableTypesMigrationsModelDiffer(
        IRelationalTypeMappingSource typeMappingSource,
        IMigrationsAnnotationProvider migrationsAnnotationProvider,
        IRelationalAnnotationProvider relationalAnnotationProvider,
        IRowIdentityMapFactory rowIdentityMapFactory,
        CommandBatchPreparerDependencies commandBatchPreparerDependencies,
        IDbContextOptions contextOptions)
        : MigrationsModelDiffer(
            typeMappingSource,
            migrationsAnnotationProvider,
            relationalAnnotationProvider,
            rowIdentityMapFactory,
            commandBatchPreparerDependencies)
    {
        private readonly IDbContextOptions _contextOptions = contextOptions;

        /// <inheritdoc />
        public override IReadOnlyList<MigrationOperation> GetDifferences(IRelationalModel? source, IRelationalModel? target)
        {
            IReadOnlyList<MigrationOperation> operations = base.GetDifferences(source, target);

            string scope = TableTypesOptionsExtension.GetSweepScope(_contextOptions);
            (IReadOnlyList<CreateTableTypeOperation> creates, CleanupTableTypesOperation? cleanup) =
                TableTypeDiffer.Diff(source?.Model, target?.Model, scope);

            if (creates.Count == 0 && cleanup is null)
            {
                return operations;
            }

            // A table type may live in a schema that no table or sequence uses (e.g. a standalone type
            // in its own schema). EF's differ only ensures schemas its relational model references, so
            // a CREATE TYPE there would target a missing schema. Ensure any create schema the base
            // operations did not already cover; idempotent, so a redundant one (e.g. dbo) is harmless.
            HashSet<string> ensuredSchemas = [.. operations.OfType<EnsureSchemaOperation>().Select(o => o.Name)];
            List<EnsureSchemaOperation> schemaOperations = [];
            foreach (CreateTableTypeOperation create in creates)
            {
                if (create.Schema is string schema && ensuredSchemas.Add(schema))
                {
                    schemaOperations.Add(new EnsureSchemaOperation { Name = schema });
                }
            }

            // Schema-ensures then creates after all table operations (types depend on nothing); the
            // cleanup sweep is the migration's last command (spec 0001 §3 — it must run
            // non-transactionally at the end).
            return cleanup is null
                ? [.. operations, .. schemaOperations, .. creates]
                : [.. operations, .. schemaOperations, .. creates, cleanup];
        }

        /// <inheritdoc />
        public override bool HasDifferences(IRelationalModel? source, IRelationalModel? target)
        {
            return base.HasDifferences(source, target) || TableTypeDiffer.HasDifferences(source?.Model, target?.Model);
        }
    }

    /// <summary>
    ///     The non-differ internal-API touchpoints, kept here so the rest of the library stays on
    ///     public APIs only.
    /// </summary>
    internal static class EfCoreInternals
    {
        /// <summary>
        ///     Returns whether the options carry the SQL Server provider — i.e. whether
        ///     <c>UseSqlServer()</c> was called.
        /// </summary>
        /// <param name="options">The context options.</param>
        /// <returns><see langword="true" /> when the SQL Server provider extension is present.</returns>
        public static bool IsSqlServerConfigured(IDbContextOptions options)
        {
            return options.Extensions.OfType<SqlServerOptionsExtension>().Any();
        }
    }
}
