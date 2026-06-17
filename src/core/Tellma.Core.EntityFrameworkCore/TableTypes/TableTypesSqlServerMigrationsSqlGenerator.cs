// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Tellma.Core.EntityFrameworkCore.TableTypes.Json;
using Tellma.Core.EntityFrameworkCore.TableTypes.Operations;

namespace Tellma.Core.EntityFrameworkCore.TableTypes
{
    /// <summary>
    ///     A <see cref="SqlServerMigrationsSqlGenerator" /> that additionally generates SQL for the
    ///     table-type operations (create / drop / cleanup). Installed by <c>UseTableTypes()</c> in
    ///     place of the provider's generator; all other operations fall through to the base class.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         All generated statements are deliberately legal inside the per-command
    ///         <c>IF NOT EXISTS (...) BEGIN ... END</c> wrapper of idempotent scripts
    ///         (<c>dotnet ef migrations script --idempotent</c>): <c>CREATE TYPE</c>, <c>DECLARE</c>,
    ///         control-of-flow, cursors and <c>THROW</c> carry no batch-position restrictions.
    ///     </para>
    ///     <para>
    ///         Injection safety (spec 0001 §3): <c>migrations script</c> emits a static file, so no
    ///         command parameters are used. Caller-supplied <i>values</i> (scope, logical name, hash,
    ///         comparison literals, message text) go through the relational type mapping's safe
    ///         literal generation; <i>identifiers</i> (type/schema names, grant principals) go through
    ///         <c>DelimitIdentifier</c>/<c>QUOTENAME</c>.
    ///     </para>
    /// </remarks>
    /// <param name="dependencies">The dependencies of the migrations SQL generator; passes through to the provider's generator.</param>
    /// <param name="commandBatchPreparer">The command batch preparer; passes through to the provider's generator.</param>
    public class TableTypesSqlServerMigrationsSqlGenerator(
        MigrationsSqlGeneratorDependencies dependencies,
        ICommandBatchPreparer commandBatchPreparer)
        : SqlServerMigrationsSqlGenerator(dependencies, commandBatchPreparer)
    {
        /// <summary>
        ///     Dispatches the table-type operations; every other operation goes to the SQL Server
        ///     provider's generator. This override of the base class's unknown-operation fallback is
        ///     the supported extension point for custom operations.
        /// </summary>
        /// <param name="operation">The operation.</param>
        /// <param name="model">The target model, when available.</param>
        /// <param name="builder">The command builder.</param>
        protected override void Generate(MigrationOperation operation, IModel? model, MigrationCommandListBuilder builder)
        {
            switch (operation)
            {
                case CreateTableTypeOperation createTableType:
                    Generate(createTableType, builder);
                    break;
                case DropTableTypeOperation dropTableType:
                    Generate(dropTableType, builder);
                    break;
                case CleanupTableTypesOperation cleanup:
                    Generate(cleanup, model, builder);
                    break;
                default:
                    base.Generate(operation, model, builder);
                    break;
            }
        }

        /// <summary>
        ///     Generates the idempotent, content-addressed <c>CREATE TYPE ... AS TABLE</c>: when the
        ///     physical name is absent, create it (with the memory-optimized pre-flight when relevant),
        ///     grant, and stamp it; when present, complete the stamps of an aborted prior create, or
        ///     fail with <see cref="TableTypeErrorNumbers.TableTypeContentMismatch" /> /
        ///     <see cref="TableTypeErrorNumbers.TableTypeOwnedByAnotherScope" /> (spec 0001 §3).
        /// </summary>
        /// <param name="operation">The create operation.</param>
        /// <param name="builder">The command builder.</param>
        protected virtual void Generate(CreateTableTypeOperation operation, MigrationCommandListBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(builder);

            if (operation.Columns.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Cannot create table type {DisplayName(operation.Name, operation.Schema)} without columns.");
            }

            if (operation.IsMemoryOptimized && operation.PrimaryKey.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Cannot create memory-optimized table type {DisplayName(operation.Name, operation.Schema)} without a " +
                    "primary key: SQL Server requires at least one index on memory-optimized table types.");
            }

            string typeName = Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.PhysicalName, operation.Schema);
            string terminator = Dependencies.SqlGenerationHelper.StatementTerminator;

            // T-SQL variables are batch-scoped, and an idempotent script concatenates the
            // (non-suppressed) create commands into one batch — so the variable names must be unique
            // per create. The suffix is the FULL definition hash (not the 8-char physical-name
            // suffix): its name space is then as wide as the content space, so even two creates that
            // collide on the 8-char prefix get distinct variables (a clean THROW 53103 at apply time,
            // never a "variable already declared" parse error). 64 hex chars keeps the name well
            // under SQL Server's 128-character identifier limit.
            string sfx = operation.DefinitionHash;
            string vSchema = $"@schema_{sfx}";
            string vPhysical = $"@physical_{sfx}";
            string vFq = $"@fq_{sfx}";
            string vHash = $"@existingHash_{sfx}";
            string vScope = $"@existingScope_{sfx}";

            // Resolve the schema and physical name into variables so the extended-property stamps work
            // whether the schema is explicit or the database user's default (SCHEMA_NAME()).
            builder
                .Append($"DECLARE {vSchema} sysname = ")
                .Append(operation.Schema is null ? "SCHEMA_NAME()" : SqlLiteral(operation.Schema))
                .AppendLine(terminator)
                .Append($"DECLARE {vPhysical} sysname = ")
                .Append(SqlLiteral(operation.PhysicalName))
                .AppendLine(terminator)
                .AppendLine($"DECLARE {vFq} nvarchar(520) = QUOTENAME({vSchema}) + N'.' + QUOTENAME({vPhysical})" + terminator)
                .AppendLine($"IF TYPE_ID({vFq}) IS NULL")
                .AppendLine("BEGIN");

            if (operation.IsMemoryOptimized)
            {
                AppendMemoryOptimizedPreflight(operation, builder);
            }

            AppendCreateType(operation, typeName, builder);
            AppendGrants(operation.Grants, typeName, builder);
            AppendStampUpsert(builder, TableTypeStampNames.LogicalName, SqlLiteral(operation.Name), vFq, vSchema, vPhysical);
            AppendStampUpsert(builder, TableTypeStampNames.Scope, SqlLiteral(operation.Scope), vFq, vSchema, vPhysical);
            AppendStampUpsert(builder, TableTypeStampNames.DefinitionHash, SqlLiteral(operation.DefinitionHash), vFq, vSchema, vPhysical);

            builder
                .AppendLine("END")
                .AppendLine("ELSE")
                .AppendLine("BEGIN")
                .AppendLine($"    DECLARE {vHash} nvarchar(max) = CONVERT(nvarchar(max), (")
                .Append($"        SELECT [value] FROM [sys].[extended_properties] WHERE [class] = 6 AND [major_id] = TYPE_ID({vFq}) AND [name] = ")
                .Append(SqlLiteral(TableTypeStampNames.DefinitionHash))
                .AppendLine("))" + terminator)
                .AppendLine($"    DECLARE {vScope} nvarchar(max) = CONVERT(nvarchar(max), (")
                .Append($"        SELECT [value] FROM [sys].[extended_properties] WHERE [class] = 6 AND [major_id] = TYPE_ID({vFq}) AND [name] = ")
                .Append(SqlLiteral(TableTypeStampNames.Scope))
                .AppendLine("))" + terminator)
                .AppendLine($"    IF {vHash} IS NULL")
                .AppendLine("    BEGIN")
                .AppendLine("        -- Aborted prior create (e.g. the non-transactional memory-optimized path committed")
                .AppendLine("        -- CREATE TYPE but not the stamps): complete the stamps and converge.");
            AppendStampUpsert(builder, TableTypeStampNames.LogicalName, SqlLiteral(operation.Name), vFq, vSchema, vPhysical, indent: "        ");
            AppendStampUpsert(builder, TableTypeStampNames.Scope, SqlLiteral(operation.Scope), vFq, vSchema, vPhysical, indent: "        ");
            AppendStampUpsert(builder, TableTypeStampNames.DefinitionHash, SqlLiteral(operation.DefinitionHash), vFq, vSchema, vPhysical, indent: "        ");

            string contentMismatch = SqlLiteral(
                $"Table type {DisplayName(operation.PhysicalName, operation.Schema)} already exists with a different " +
                "definition hash than expected. The content-addressed name does not match its content — an out-of-band " +
                "type is squatting on the name, or (astronomically unlikely) a truncated-hash collision occurred.");
            string ownershipConflict = SqlLiteral(
                $"Table type {DisplayName(operation.PhysicalName, operation.Schema)} is already owned by another sweep " +
                $"scope. Own it in a single context, or declare it with ExcludeFromMigrations() in this context (scope " +
                $"'{operation.Scope}').");

            builder
                .AppendLine("    END")
                .Append($"    ELSE IF {vHash} <> ")
                .Append(SqlLiteral(operation.DefinitionHash))
                .AppendLine()
                .Append("        THROW ")
                .Append(TableTypeNaming.Invariant(TableTypeErrorNumbers.TableTypeContentMismatch))
                .Append(", ")
                .Append(contentMismatch)
                .AppendLine(", 1" + terminator)
                .Append($"    ELSE IF {vScope} IS NULL OR {vScope} <> ")
                .Append(SqlLiteral(operation.Scope))
                .AppendLine()
                .Append("        THROW ")
                .Append(TableTypeNaming.Invariant(TableTypeErrorNumbers.TableTypeOwnedByAnotherScope))
                .Append(", ")
                .Append(ownershipConflict)
                .AppendLine(", 1" + terminator)
                .AppendLine("END");

            // Memory-optimized DDL cannot run inside the migration's transaction.
            builder.EndCommand(suppressTransaction: operation.IsMemoryOptimized);
        }

        /// <summary>
        ///     Generates the drop of one table type by physical name, preceded by a guard that
        ///     resolves all persisted SQL modules referencing it
        ///     (<c>sys.sql_expression_dependencies</c>, <c>referenced_class = 6</c>) and fails with
        ///     their names. Authored manually only — the differ never emits drops (spec 0001 §3).
        /// </summary>
        /// <param name="operation">The drop operation (its name is the physical name).</param>
        /// <param name="builder">The command builder.</param>
        protected virtual void Generate(DropTableTypeOperation operation, MigrationCommandListBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(builder);

            string typeName = Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema);
            string terminator = Dependencies.SqlGenerationHelper.StatementTerminator;

            // A null schema means the database user's default schema, mirrored by SCHEMA_NAME().
            string schemaPredicate = operation.Schema is null ? "SCHEMA_NAME()" : SqlLiteral(operation.Schema);
            string messagePrefix =
                $"Cannot drop table type {DisplayName(operation.Name, operation.Schema)}: it is referenced by persisted " +
                "SQL module(s) ";
            string messageSuffix =
                ". Per the Tellma architecture, no persisted SQL module may reference a generated table type; all " +
                "consumers must be dynamic SQL composed in C#.";

            builder
                .AppendLine("DECLARE @dependents nvarchar(max) = (")
                .AppendLine("    SELECT STRING_AGG(QUOTENAME(OBJECT_SCHEMA_NAME(d.[referencing_id])) + N'.' + QUOTENAME(OBJECT_NAME(d.[referencing_id])), N', ')")
                .AppendLine("    FROM [sys].[sql_expression_dependencies] AS d")
                .AppendLine("    INNER JOIN [sys].[table_types] AS tt ON tt.[user_type_id] = d.[referenced_id]")
                .Append("    WHERE d.[referenced_class] = 6 AND tt.[name] = ")
                .Append(SqlLiteral(operation.Name))
                .Append(" AND SCHEMA_NAME(tt.[schema_id]) = ")
                .Append(schemaPredicate)
                .Append(")")
                .AppendLine(terminator)
                .AppendLine("IF @dependents IS NOT NULL")
                .AppendLine("BEGIN")
                .Append("    DECLARE @error nvarchar(2048) = ")
                .Append(SqlLiteral(messagePrefix))
                .Append(" + @dependents + ")
                .Append(SqlLiteral(messageSuffix))
                .AppendLine(terminator)
                .Append("    THROW ")
                .Append(TableTypeNaming.Invariant(TableTypeErrorNumbers.DroppedTypeHasDependents))
                .Append(", @error, 1")
                .AppendLine(terminator)
                .Append("END")
                .AppendLine(terminator)
                .Append("DROP TYPE ")
                .Append(typeName)
                .AppendLine(terminator);

            builder.EndCommand(suppressTransaction: operation.IsMemoryOptimized);
        }

        /// <summary>
        ///     Generates the cleanup sweep over one scope (spec 0001 §3 → Versioning): clears the
        ///     orphan mark of kept types, marks newly stale types, and collects orphans past the grace
        ///     period (skipping and surfacing — never throwing — any with a persisted dependent). It
        ///     is always emitted with the transaction suppressed, since its drop set is discovered at
        ///     apply time and may include memory-optimized types.
        /// </summary>
        /// <param name="operation">The cleanup operation.</param>
        /// <param name="model">The target model, used to resolve the keep-list when the operation carries none.</param>
        /// <param name="builder">The command builder.</param>
        protected virtual void Generate(CleanupTableTypesOperation operation, IModel? model, MigrationCommandListBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(builder);

            IReadOnlyList<string> keepList = operation.KeepList ?? (IReadOnlyList<string>)ResolveKeepListFromModel(model);
            string terminator = Dependencies.SqlGenerationHelper.StatementTerminator;

            builder
                .Append("DECLARE @scope nvarchar(450) = ")
                .Append(SqlLiteral(operation.Scope))
                .AppendLine(terminator)
                .Append("DECLARE @grace int = ")
                .Append(TableTypeNaming.Invariant(operation.GracePeriodHours))
                .AppendLine(terminator)
                .AppendLine("DECLARE @now datetime2 = SYSUTCDATETIME()" + terminator)
                .AppendLine("DECLARE @keep TABLE ([name] sysname PRIMARY KEY)" + terminator);

            if (keepList.Count > 0)
            {
                // Keep-list physical names are SQL Server identifiers, but here they are compared as
                // values, so they are emitted as escaped literals.
                builder.Append("INSERT INTO @keep ([name]) VALUES ");
                builder.Append(string.Join(", ", keepList.Select(n => "(" + SqlLiteral(n) + ")")));
                builder.AppendLine(terminator);
            }

            builder
                .AppendLine("DECLARE @schema sysname, @name sysname, @orphanedAt datetime2, @inKeep bit, @sql nvarchar(max), @deps nvarchar(max)" + terminator)
                .AppendLine("DECLARE tellma_tt_cleanup CURSOR LOCAL FAST_FORWARD FOR")
                .AppendLine("    SELECT SCHEMA_NAME(tt.[schema_id]), tt.[name], TRY_CONVERT(datetime2, op.[value]),")
                .AppendLine("           CASE WHEN k.[name] IS NULL THEN 0 ELSE 1 END")
                .AppendLine("    FROM [sys].[table_types] AS tt")
                .Append("    INNER JOIN [sys].[extended_properties] AS sc ON sc.[class] = 6 AND sc.[major_id] = tt.[user_type_id] AND sc.[name] = ")
                .Append(SqlLiteral(TableTypeStampNames.Scope))
                .AppendLine(" AND CONVERT(nvarchar(max), sc.[value]) = @scope")
                .Append("    LEFT JOIN [sys].[extended_properties] AS op ON op.[class] = 6 AND op.[major_id] = tt.[user_type_id] AND op.[name] = ")
                .Append(SqlLiteral(TableTypeStampNames.OrphanedAtUtc))
                .AppendLine()
                .AppendLine("    LEFT JOIN @keep AS k ON k.[name] = tt.[name]" + terminator)
                .AppendLine("OPEN tellma_tt_cleanup" + terminator)
                .AppendLine("FETCH NEXT FROM tellma_tt_cleanup INTO @schema, @name, @orphanedAt, @inKeep" + terminator)
                .AppendLine("WHILE @@FETCH_STATUS = 0")
                .AppendLine("BEGIN")
                .AppendLine("    IF @inKeep = 1")
                .AppendLine("    BEGIN")
                .AppendLine("        IF @orphanedAt IS NOT NULL")
                .Append("            EXEC [sys].[sp_dropextendedproperty] @name = ")
                .Append(SqlLiteral(TableTypeStampNames.OrphanedAtUtc))
                .AppendLine(", @level0type = N'SCHEMA', @level0name = @schema, @level1type = N'TYPE', @level1name = @name" + terminator)
                .AppendLine("    END")
                .AppendLine("    ELSE IF @orphanedAt IS NULL")
                .Append("        EXEC [sys].[sp_addextendedproperty] @name = ")
                .Append(SqlLiteral(TableTypeStampNames.OrphanedAtUtc))
                .AppendLine(", @value = @now, @level0type = N'SCHEMA', @level0name = @schema, @level1type = N'TYPE', @level1name = @name" + terminator)
                .AppendLine("    ELSE IF DATEADD(HOUR, @grace, @orphanedAt) <= @now")
                .AppendLine("    BEGIN")
                .AppendLine("        SET @deps = (")
                .AppendLine("            SELECT STRING_AGG(QUOTENAME(OBJECT_SCHEMA_NAME(d.[referencing_id])) + N'.' + QUOTENAME(OBJECT_NAME(d.[referencing_id])), N', ')")
                .AppendLine("            FROM [sys].[sql_expression_dependencies] AS d")
                .AppendLine("            WHERE d.[referenced_class] = 6 AND d.[referenced_id] = TYPE_ID(QUOTENAME(@schema) + N'.' + QUOTENAME(@name)))" + terminator)
                .AppendLine("        IF @deps IS NOT NULL")
                .AppendLine("        BEGIN")
                .AppendLine("            -- Skip and surface (do not throw): GC of a version nothing uses must not block deployments.")
                .AppendLine("            DECLARE @skip nvarchar(max) = N'Skipped collecting orphaned table type [' + @schema + N'].[' + @name + N'] (scope ' + @scope + N'): still referenced by ' + @deps + N'.'" + terminator)
                .AppendLine("            RAISERROR(@skip, 10, 1) WITH NOWAIT" + terminator)
                .AppendLine("        END")
                .AppendLine("        ELSE")
                .AppendLine("        BEGIN")
                .AppendLine("            SET @sql = N'DROP TYPE ' + QUOTENAME(@schema) + N'.' + QUOTENAME(@name)" + terminator)
                .AppendLine("            EXEC [sys].[sp_executesql] @sql" + terminator)
                .AppendLine("        END")
                .AppendLine("    END")
                .AppendLine("    FETCH NEXT FROM tellma_tt_cleanup INTO @schema, @name, @orphanedAt, @inKeep" + terminator)
                .AppendLine("END")
                .AppendLine("CLOSE tellma_tt_cleanup" + terminator)
                .AppendLine("DEALLOCATE tellma_tt_cleanup" + terminator);

            // The sweep is the migration's last command and always runs non-transactionally: its drop
            // set is discovered here and may include memory-optimized types (spec 0001 §3).
            builder.EndCommand(suppressTransaction: true);
        }

        /// <summary>Appends the In-Memory OLTP support pre-flight, throwing on unsupported tiers with no silent fallback.</summary>
        private void AppendMemoryOptimizedPreflight(CreateTableTypeOperation operation, MigrationCommandListBuilder builder)
        {
            string message =
                $"Cannot create memory-optimized table type {DisplayName(operation.Name, operation.Schema)}: In-Memory " +
                "OLTP is not supported on this database tier or edition (DATABASEPROPERTYEX 'IsXTPSupported' <> 1). " +
                "Use a Premium/Business Critical tier or a memory-optimized filegroup, or remove the memory-optimized " +
                "configuration from the table type.";
            builder
                .AppendLine("    IF DATABASEPROPERTYEX(DB_NAME(), 'IsXTPSupported') <> 1")
                .Append("        THROW ")
                .Append(TableTypeNaming.Invariant(TableTypeErrorNumbers.MemoryOptimizedNotSupported))
                .Append(", ")
                .Append(SqlLiteral(message))
                .Append(", 1")
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        }

        /// <summary>Appends the <c>CREATE TYPE ... AS TABLE</c> body (columns, primary key, memory-optimized clause).</summary>
        private void AppendCreateType(CreateTableTypeOperation operation, string typeName, MigrationCommandListBuilder builder)
        {
            builder.Append("    CREATE TYPE ").Append(typeName).AppendLine(" AS TABLE (");

            for (int i = 0; i < operation.Columns.Count; i++)
            {
                TableTypeColumnDefinition column = operation.Columns[i];

                // Column names are identifiers → delimited. StoreType and Collation are SQL fragments
                // taken from the model (the same trust boundary as HasColumnType()/UseCollation()),
                // emitted verbatim by design — not user-input values, so not literal-escaped.
                builder
                    .Append("        ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(column.Name))
                    .Append(" ")
                    .Append(column.StoreType);
                if (column.Collation is not null)
                {
                    builder.Append(" COLLATE ").Append(column.Collation);
                }

                builder.Append(column.IsNullable ? " NULL" : " NOT NULL");

                if (i < operation.Columns.Count - 1 || operation.PrimaryKey.Length > 0)
                {
                    builder.Append(",");
                }

                builder.AppendLine();
            }

            if (operation.PrimaryKey.Length > 0)
            {
                // Memory-optimized types cannot have a clustered index; on-disk types mirror the
                // table's clustered primary key.
                builder
                    .Append("        PRIMARY KEY ")
                    .Append(operation.IsMemoryOptimized ? "NONCLUSTERED" : "CLUSTERED")
                    .Append(" (")
                    .Append(string.Join(", ", operation.PrimaryKey.Select(Dependencies.SqlGenerationHelper.DelimitIdentifier)))
                    .AppendLine(")");
            }

            builder.Append("    )");
            if (operation.IsMemoryOptimized)
            {
                builder.Append(" WITH (MEMORY_OPTIMIZED = ON)");
            }

            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        }

        /// <summary>
        ///     Appends one <c>GRANT EXECUTE ON TYPE</c> statement per principal. Grants do not
        ///     survive a drop, so they are part of every version create by construction.
        /// </summary>
        private void AppendGrants(string[] grants, string typeName, MigrationCommandListBuilder builder)
        {
            foreach (string principal in grants)
            {
                builder
                    .Append("    GRANT EXECUTE ON TYPE::")
                    .Append(typeName)
                    .Append(" TO ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(principal))
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            }
        }

        /// <summary>
        ///     Appends an idempotent add-or-update of one extended-property stamp on the type named by
        ///     the given <paramref name="fqVar" />/<paramref name="schemaVar" />/<paramref name="physicalVar" />
        ///     variables declared by the create. The upsert form makes stamping safe to re-enter after
        ///     a partial (non-transactional) create.
        /// </summary>
        private void AppendStampUpsert(
            MigrationCommandListBuilder builder,
            string stampName,
            string valueSql,
            string fqVar,
            string schemaVar,
            string physicalVar,
            string indent = "    ")
        {
            string nameLiteral = SqlLiteral(stampName);
            string terminator = Dependencies.SqlGenerationHelper.StatementTerminator;
            string level = $", @level0type = N'SCHEMA', @level0name = {schemaVar}, @level1type = N'TYPE', @level1name = {physicalVar}";
            builder
                .Append(indent)
                .Append($"IF NOT EXISTS (SELECT 1 FROM [sys].[extended_properties] WHERE [class] = 6 AND [major_id] = TYPE_ID({fqVar}) AND [name] = ")
                .Append(nameLiteral)
                .AppendLine(")")
                .Append(indent)
                .Append("    EXEC [sys].[sp_addextendedproperty] @name = ")
                .Append(nameLiteral)
                .Append(", @value = ")
                .Append(valueSql)
                .AppendLine(level + terminator)
                .Append(indent)
                .AppendLine("ELSE")
                .Append(indent)
                .Append("    EXEC [sys].[sp_updateextendedproperty] @name = ")
                .Append(nameLiteral)
                .Append(", @value = ")
                .Append(valueSql)
                .AppendLine(level + terminator);
        }

        /// <summary>
        ///     Resolves the keep-list (physical names of this context's owned types) from the target
        ///     model, for the no-list <c>CleanupTableTypes</c> form used by hand-written migrations.
        /// </summary>
        private static List<string> ResolveKeepListFromModel(IModel? model)
        {
            if (model is null)
            {
                return [];
            }

            HashSet<string> excluded = model.FindAnnotation(TableTypeAnnotationNames.ExcludedKeys)?.Value is string json
                ? [.. TableTypeJson.DeserializeStringList(json)]
                : [];

            List<string> keep = [];
            foreach (IAnnotation annotation in model.GetAnnotations())
            {
                if (annotation.Name.StartsWith(TableTypeAnnotationNames.DefinitionPrefix, StringComparison.Ordinal)
                    && annotation.Value is string definitionJson
                    && !excluded.Contains(annotation.Name))
                {
                    TableTypeDefinition definition = TableTypeJson.DeserializeDefinition(definitionJson);
                    keep.Add(TableTypeNaming.PhysicalName(definition.Name, TableTypeNaming.ComputeHash(definitionJson)));
                }
            }

            return keep;
        }

        /// <summary>Renders a string as an escaped <c>N'...'</c> SQL literal.</summary>
        private string SqlLiteral(string value)
        {
            return Dependencies.TypeMappingSource.GetMapping(typeof(string)).GenerateSqlLiteral(value);
        }

        /// <summary>The bracket-delimited display name used in error messages.</summary>
        private static string DisplayName(string name, string? schema)
        {
            return schema is null ? $"[{name}]" : $"[{schema}].[{name}]";
        }
    }
}
