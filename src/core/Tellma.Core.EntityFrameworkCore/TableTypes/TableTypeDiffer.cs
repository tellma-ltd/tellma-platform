// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Metadata;
using Tellma.Core.EntityFrameworkCore.TableTypes.Json;
using Tellma.Core.EntityFrameworkCore.TableTypes.Operations;

namespace Tellma.Core.EntityFrameworkCore.TableTypes
{
    /// <summary>
    ///     Computes table-type migration operations by comparing the definition annotations of two
    ///     models (typically the snapshot model and the current model). Pure and public-API only —
    ///     the (internal-API) differ subclass delegates here.
    /// </summary>
    /// <remarks>
    ///     Definitions are compared <b>verbatim, string to string</b>: the canonical JSON makes
    ///     string equality equivalent to definition equality. Nothing is re-derived from either
    ///     model's structure. SQL Server has no <c>ALTER TYPE</c>, so a changed definition becomes
    ///     a drop + create pair within the same migration.
    /// </remarks>
    public static class TableTypeDiffer
    {
        /// <summary>
        ///     Diffs the table-type definitions of two models.
        /// </summary>
        /// <param name="source">The model migrated from, or <see langword="null" /> for an empty database.</param>
        /// <param name="target">The model migrated to, or <see langword="null" /> for a drop-everything migration.</param>
        /// <returns>
        ///     The drop operations (sorted by schema then name) and create operations (sorted
        ///     likewise). A definitional change contributes one of each. Drops are emitted before
        ///     all other migration operations and creates after them — always safe, because types
        ///     depend on no tables and (per the architecture) nothing persisted may depend on types.
        /// </returns>
        public static (IReadOnlyList<DropTableTypeOperation> Drops, IReadOnlyList<CreateTableTypeOperation> Creates) Diff(
            IReadOnlyModel? source,
            IReadOnlyModel? target)
        {
            Dictionary<string, string> sourceDefinitions = GetDefinitionAnnotations(source);
            Dictionary<string, string> targetDefinitions = GetDefinitionAnnotations(target);

            List<DropTableTypeOperation> drops = [];
            List<CreateTableTypeOperation> creates = [];

            foreach ((string key, string sourceJson) in sourceDefinitions.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (!targetDefinitions.TryGetValue(key, out string? targetJson) || targetJson != sourceJson)
                {
                    TableTypeDefinition definition = TableTypeJson.DeserializeDefinition(sourceJson);
                    drops.Add(new DropTableTypeOperation
                    {
                        Name = definition.Name,
                        Schema = definition.Schema,
                        IsMemoryOptimized = definition.IsMemoryOptimized,
                    });
                }
            }

            foreach ((string key, string targetJson) in targetDefinitions.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (!sourceDefinitions.TryGetValue(key, out string? sourceJson) || sourceJson != targetJson)
                {
                    creates.Add(CreateTableTypeOperation.CreateFrom(TableTypeJson.DeserializeDefinition(targetJson)));
                }
            }

            return (drops, creates);
        }

        /// <summary>
        ///     Returns whether the two models differ in any table-type definition. Used so that
        ///     pending-model-changes detection sees type-only changes.
        /// </summary>
        /// <param name="source">The model migrated from.</param>
        /// <param name="target">The model migrated to.</param>
        /// <returns><see langword="true" /> when any definition was added, removed, or changed.</returns>
        public static bool HasDifferences(IReadOnlyModel? source, IReadOnlyModel? target)
        {
            Dictionary<string, string> sourceDefinitions = GetDefinitionAnnotations(source);
            Dictionary<string, string> targetDefinitions = GetDefinitionAnnotations(target);

            return sourceDefinitions.Count != targetDefinitions.Count
                || sourceDefinitions.Any(p => !targetDefinitions.TryGetValue(p.Key, out string? json) || json != p.Value);
        }

        /// <summary>Reads all definition annotations (key → canonical JSON) of a model.</summary>
        private static Dictionary<string, string> GetDefinitionAnnotations(IReadOnlyModel? model)
        {
            return model is null
                ? []
                : model.GetAnnotations()
                    .Where(a => a.Name.StartsWith(TableTypeAnnotationNames.DefinitionPrefix, StringComparison.Ordinal)
                        && a.Value is string)
                    .ToDictionary(a => a.Name, a => (string)a.Value!);
        }
    }
}
