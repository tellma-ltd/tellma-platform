// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Collections.Concurrent;
using Tellma.Core.EntityFrameworkCore.TableTypes.Json;

namespace Tellma.Core.EntityFrameworkCore.TableTypes
{
    /// <summary>
    ///     The metadata API of the table-types extension: every aspect of the generated types is
    ///     queryable from the EF model. This is the surface dynamic SQL generation, runtime TVP
    ///     binding (<c>SqlDataRecord</c>/<c>DataTable</c>) and tests consume — binding MUST be
    ///     driven by <see cref="TableTypeDefinition.Columns" /> order from here, never by
    ///     hard-coded ordinals.
    /// </summary>
    public static class TableTypeModelExtensions
    {
        /// <summary>
        ///     Parsed definitions, cached by their canonical JSON. Annotation values repeat across
        ///     model instances (snapshots, design-time copies), so the cache is keyed by content;
        ///     it grows only with distinct definitions ever observed in the process.
        /// </summary>
        private static readonly ConcurrentDictionary<string, TableTypeDefinition> Cache = new(StringComparer.Ordinal);

        /// <summary>
        ///     Returns all table types of the model — table-derived and standalone — sorted by schema
        ///     then name.
        /// </summary>
        /// <param name="model">The model.</param>
        /// <returns>The table-type definitions.</returns>
        public static IReadOnlyList<TableTypeDefinition> GetTableTypes(this IReadOnlyModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            return [.. model.GetAnnotations()
                .Where(a => a.Name.StartsWith(TableTypeAnnotationNames.DefinitionPrefix, StringComparison.Ordinal)
                    && a.Value is string)
                .Select(a => Parse((string)a.Value!))
                .OrderBy(d => d.Schema, StringComparer.Ordinal)
                .ThenBy(d => d.Name, StringComparer.Ordinal)];
        }

        /// <summary>
        ///     Returns the table type derived from this entity type's table, or <see langword="null" />
        ///     when the table has none.
        /// </summary>
        /// <param name="entityType">The entity type.</param>
        /// <returns>The table-type definition, or <see langword="null" />.</returns>
        public static TableTypeDefinition? GetTableType(this IReadOnlyEntityType entityType)
        {
            ArgumentNullException.ThrowIfNull(entityType);

            string? tableName = entityType.GetTableName();
            if (tableName is null)
            {
                return null;
            }

            string? tableSchema = entityType.GetSchema();
            return entityType.Model.GetTableTypes()
                .FirstOrDefault(d => d.TableName == tableName && d.TableSchema == tableSchema);
        }

        /// <summary>Returns whether this entity type's table has a paired table type.</summary>
        /// <param name="entityType">The entity type.</param>
        /// <returns><see langword="true" /> when the table has a table type.</returns>
        public static bool HasTableType(this IReadOnlyEntityType entityType)
        {
            return GetTableType(entityType) is not null;
        }

        /// <summary>Parses a definition from canonical JSON through the content-keyed cache.</summary>
        private static TableTypeDefinition Parse(string json)
        {
            return Cache.GetOrAdd(json, static j => TableTypeJson.DeserializeDefinition(j));
        }
    }
}
