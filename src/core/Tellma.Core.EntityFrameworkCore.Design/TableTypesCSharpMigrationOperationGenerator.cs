// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.TableTypes.Operations;

namespace Tellma.Core.EntityFrameworkCore.Design
{
    /// <summary>
    ///     A <see cref="CSharpMigrationOperationGenerator" /> that scaffolds the table-type
    ///     operations into migration files as <c>migrationBuilder.CreateTableType(...)</c> /
    ///     <c>.DropTableType(...)</c> calls. All other operations fall through to EF's generator.
    /// </summary>
    /// <remarks>
    ///     EF dispatches operations via <c>dynamic</c> against its own overload set, so overloads
    ///     added in a derived class are never picked up — the unknown-operation fallback
    ///     (<see cref="Generate(MigrationOperation, IndentedStringBuilder)" />) is the supported
    ///     extension point and the one overridden here.
    /// </remarks>
    /// <param name="dependencies">The dependencies; pass through to EF's generator.</param>
    public class TableTypesCSharpMigrationOperationGenerator(CSharpMigrationOperationGeneratorDependencies dependencies)
        : CSharpMigrationOperationGenerator(dependencies)
    {
        /// <summary>The C# code literal helper.</summary>
        private ICSharpHelper Code => Dependencies.CSharpHelper;

        /// <summary>
        ///     Dispatches the table-type operations; every other (unknown) operation goes to EF's
        ///     fallback, which fails with an unknown-operation error.
        /// </summary>
        /// <param name="operation">The operation.</param>
        /// <param name="builder">The builder the generated code is appended to.</param>
        protected override void Generate(MigrationOperation operation, IndentedStringBuilder builder)
        {
            switch (operation)
            {
                case CreateTableTypeOperation createTableType:
                    Generate(createTableType, builder);
                    break;
                case DropTableTypeOperation dropTableType:
                    Generate(dropTableType, builder);
                    break;
                default:
                    base.Generate(operation, builder);
                    break;
            }
        }

        /// <summary>Scaffolds a <c>.CreateTableType(...)</c> call.</summary>
        /// <param name="operation">The create operation.</param>
        /// <param name="builder">The builder the generated code is appended to.</param>
        protected virtual void Generate(CreateTableTypeOperation operation, IndentedStringBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(builder);

            builder.AppendLine(".CreateTableType(");
            using (builder.Indent())
            {
                builder
                    .Append("name: ")
                    .Append(Code.Literal(operation.Name))
                    .AppendLine(",");

                builder
                    .Append("schema: ")
                    .Append(operation.Schema is null ? "null" : Code.Literal(operation.Schema))
                    .AppendLine(",");

                builder.AppendLine("columns: new[]");
                builder.AppendLine("{");
                using (builder.Indent())
                {
                    foreach (TableTypeColumnDefinition column in operation.Columns)
                    {
                        GenerateColumn(column, builder);
                    }
                }

                builder.Append("}");

                if (operation.PrimaryKey.Length > 0)
                {
                    builder
                        .AppendLine(",")
                        .Append("primaryKey: ")
                        .Append(Code.Literal(operation.PrimaryKey));
                }

                if (operation.IsMemoryOptimized)
                {
                    builder
                        .AppendLine(",")
                        .Append("memoryOptimized: true");
                }

                if (operation.Grants.Length > 0)
                {
                    builder
                        .AppendLine(",")
                        .Append("grants: ")
                        .Append(Code.Literal(operation.Grants));
                }

                builder.Append(")");
            }

            Annotations(operation.GetAnnotations(), builder);
        }

        /// <summary>Scaffolds a <c>.DropTableType(...)</c> call.</summary>
        /// <param name="operation">The drop operation.</param>
        /// <param name="builder">The builder the generated code is appended to.</param>
        protected virtual void Generate(DropTableTypeOperation operation, IndentedStringBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(builder);

            builder.Append(".DropTableType(");
            builder.Append("name: ").Append(Code.Literal(operation.Name));
            if (operation.Schema is not null)
            {
                builder.Append(", schema: ").Append(Code.Literal(operation.Schema));
            }

            if (operation.IsMemoryOptimized)
            {
                builder.Append(", memoryOptimized: true");
            }

            builder.Append(")");

            Annotations(operation.GetAnnotations(), builder);
        }

        /// <summary>
        ///     Scaffolds one <see cref="TableTypeColumnDefinition" /> object initializer, omitting
        ///     members at their default values to keep migration files readable.
        /// </summary>
        private void GenerateColumn(TableTypeColumnDefinition column, IndentedStringBuilder builder)
        {
            builder
                .Append("new TableTypeColumnDefinition { Name = ")
                .Append(Code.Literal(column.Name))
                .Append(", StoreType = ")
                .Append(Code.Literal(column.StoreType));

            if (column.IsNullable)
            {
                builder.Append(", IsNullable = true");
            }

            if (column.MaxLength.HasValue)
            {
                builder.Append(", MaxLength = ").Append(Code.Literal(column.MaxLength.Value));
            }

            if (column.Precision.HasValue)
            {
                builder.Append(", Precision = ").Append(Code.Literal(column.Precision.Value));
            }

            if (column.Scale.HasValue)
            {
                builder.Append(", Scale = ").Append(Code.Literal(column.Scale.Value));
            }

            if (column.Collation is not null)
            {
                builder.Append(", Collation = ").Append(Code.Literal(column.Collation));
            }

            if (column.IsRowVersion)
            {
                builder.Append(", IsRowVersion = true");
            }

            builder.AppendLine(" },");
        }
    }
}
