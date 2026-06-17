// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.TableTypes.Json;

namespace Tellma.Core.EntityFrameworkCore.Design
{
    /// <summary>
    ///     A <see cref="CSharpSnapshotGenerator" /> that renders every derived table-type
    ///     definition as a readable, multi-line <c>HasTableTypeDefinition(...)</c> fluent call
    ///     instead of one opaque JSON <c>HasAnnotation</c> string — so a column change shows up in
    ///     snapshot diffs as a per-line change.
    /// </summary>
    /// <remarks>
    ///     Replaying the fluent call rebuilds the definition annotation byte-for-byte (same record,
    ///     same canonical serializer), which is what keeps the differ's verbatim annotation
    ///     comparison working — the snapshot round-trip test pins this. The raw JSON annotations
    ///     are excluded from the generic annotation output by
    ///     <see cref="TableTypesSqlServerAnnotationCodeGenerator" />; both services are registered
    ///     together by <see cref="TableTypesDesignTimeServices" />.
    /// </remarks>
    /// <param name="dependencies">The dependencies; pass through to EF's generator.</param>
    public class TableTypesCSharpSnapshotGenerator(CSharpSnapshotGeneratorDependencies dependencies)
        : CSharpSnapshotGenerator(dependencies)
    {
        /// <summary>The C# code literal helper.</summary>
        private ICSharpHelper Code => Dependencies.CSharpHelper;

        /// <inheritdoc />
        public override void Generate(string modelBuilderName, IModel model, IndentedStringBuilder stringBuilder)
        {
            base.Generate(modelBuilderName, model, stringBuilder);

            foreach (IAnnotation annotation in model.GetAnnotations()
                .Where(a => a.Name.StartsWith(TableTypeAnnotationNames.DefinitionPrefix, StringComparison.Ordinal)
                    && a.Value is string)
                .OrderBy(a => a.Name, StringComparer.Ordinal))
            {
                GenerateTableTypeDefinition(
                    modelBuilderName, TableTypeJson.DeserializeDefinition((string)annotation.Value!), stringBuilder);
            }
        }

        /// <summary>Renders one definition as a <c>HasTableTypeDefinition(...)</c> call.</summary>
        private void GenerateTableTypeDefinition(
            string modelBuilderName,
            TableTypeDefinition definition,
            IndentedStringBuilder stringBuilder)
        {
            stringBuilder
                .AppendLine()
                .Append(modelBuilderName)
                .Append(".HasTableTypeDefinition(")
                .Append(Code.Literal(definition.Name))
                .Append(", ")
                .Append(definition.Schema is null ? "null" : Code.Literal(definition.Schema))
                .AppendLine(", type => type");

            using (stringBuilder.Indent())
            {
                List<string> calls = [];

                if (definition.TableName is not null)
                {
                    calls.Add(definition.TableSchema is null
                        ? $".ForTable({Code.Literal(definition.TableName)})"
                        : $".ForTable({Code.Literal(definition.TableName)}, {Code.Literal(definition.TableSchema)})");
                }

                foreach (TableTypeColumnDefinition column in definition.Columns)
                {
                    calls.Add(FormatColumn(column));
                }

                if (definition.PrimaryKey.Count > 0)
                {
                    calls.Add($".HasKey({string.Join(", ", definition.PrimaryKey.Select(Code.Literal))})");
                }

                if (definition.Grants.Count > 0)
                {
                    calls.Add($".HasGrants({string.Join(", ", definition.Grants.Select(Code.Literal))})");
                }

                if (definition.IsMemoryOptimized)
                {
                    calls.Add(".IsMemoryOptimized()");
                }

                for (int i = 0; i < calls.Count; i++)
                {
                    stringBuilder.Append(calls[i]);
                    if (i < calls.Count - 1)
                    {
                        stringBuilder.AppendLine();
                    }
                }

                stringBuilder.AppendLine(");");
            }
        }

        /// <summary>Formats one <c>.Column(...)</c> call, omitting arguments at their default values.</summary>
        private string FormatColumn(TableTypeColumnDefinition column)
        {
            List<string> arguments =
            [
                $"name: {Code.Literal(column.Name)}",
                $"storeType: {Code.Literal(column.StoreType)}",
            ];
            if (column.IsNullable)
            {
                arguments.Add("nullable: true");
            }

            if (column.MaxLength.HasValue)
            {
                arguments.Add($"maxLength: {Code.Literal(column.MaxLength.Value)}");
            }

            if (column.Precision.HasValue)
            {
                arguments.Add($"precision: {Code.Literal(column.Precision.Value)}");
            }

            if (column.Scale.HasValue)
            {
                arguments.Add($"scale: {Code.Literal(column.Scale.Value)}");
            }

            if (column.Collation is not null)
            {
                arguments.Add($"collation: {Code.Literal(column.Collation)}");
            }

            if (column.IsRowVersion)
            {
                arguments.Add("rowVersion: true");
            }

            if (column.IsJson)
            {
                arguments.Add("json: true");
            }

            return $".Column({string.Join(", ", arguments)})";
        }
    }
}
