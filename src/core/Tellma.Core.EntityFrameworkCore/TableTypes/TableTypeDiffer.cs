// Copyright (c) Tellma Ltd. All rights reserved.
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
    ///     <para>
    ///         Definitions are compared <b>verbatim, string to string</b>: the canonical JSON makes
    ///         string equality equivalent to definition equality. Nothing is re-derived from either
    ///         model's structure.
    ///     </para>
    ///     <para>
    ///         The differ emits <b>creates only</b> (spec 0001 §3): a definitional change yields a new
    ///         content-addressed physical name, created alongside the old version, and one
    ///         <see cref="CleanupTableTypesOperation" /> carrying the target model's complete
    ///         physical-name keep-list retires stale versions later. It never emits drops. Because the
    ///         rule is direction-agnostic — creates for whatever the target has, keep-list from the
    ///         target model — the scaffolded <c>Down()</c> is automatically correct.
    ///     </para>
    /// </remarks>
    public static class TableTypeDiffer
    {
        /// <summary>
        ///     Diffs the table-type definitions of two models.
        /// </summary>
        /// <param name="source">The model migrated from, or <see langword="null" /> for an empty database.</param>
        /// <param name="target">The model migrated to, or <see langword="null" /> for a drop-everything migration.</param>
        /// <param name="scope">The owning context's sweep scope, stamped on creates and carried by the cleanup.</param>
        /// <returns>
        ///     The create operations (sorted by schema then name; one per definition new or changed on
        ///     the target side, excluding types this context does not own) and, when the definition
        ///     sets differ at all, one cleanup operation carrying the complete keep-list of the target
        ///     model's owned physical names. Creates are safe after all table operations, and the
        ///     cleanup is always the migration's last command.
        /// </returns>
        public static (IReadOnlyList<CreateTableTypeOperation> Creates, CleanupTableTypesOperation? Cleanup) Diff(
            IReadOnlyModel? source,
            IReadOnlyModel? target,
            string scope)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(scope);

            Dictionary<string, string> sourceDefinitions = GetDefinitionAnnotations(source);
            Dictionary<string, string> targetDefinitions = GetDefinitionAnnotations(target);
            HashSet<string> excluded = GetExcludedKeys(target);

            List<CreateTableTypeOperation> creates = [];
            List<string> keepList = [];

            foreach ((string key, string targetJson) in targetDefinitions.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                // Types this context does not own (ExcludeFromMigrations): another context creates
                // and sweeps the physical type. We bind it at runtime but neither create nor keep it.
                if (excluded.Contains(key))
                {
                    continue;
                }

                TableTypeDefinition definition = TableTypeJson.DeserializeDefinition(targetJson);

                // The complete keep-list is every owned current version — changed or not — so the
                // sweep keeps exactly these and orphans everything else in scope.
                keepList.Add(TableTypeNaming.PhysicalName(definition.Name, TableTypeNaming.ComputeHash(targetJson)));

                if (!sourceDefinitions.TryGetValue(key, out string? sourceJson) || sourceJson != targetJson)
                {
                    creates.Add(CreateTableTypeOperation.CreateFrom(definition, targetJson, scope));
                }
            }

            // Emit a cleanup whenever the definition sets differ at all — including pure removals,
            // which produce no create but still change the keep-list. An unchanged model (the
            // snapshot round-trip) emits nothing.
            bool definitionsDiffer = DefinitionsDiffer(sourceDefinitions, targetDefinitions);
            CleanupTableTypesOperation? cleanup = definitionsDiffer
                ? new CleanupTableTypesOperation
                {
                    Scope = scope,
                    KeepList = [.. keepList],
                    GracePeriodHours = CleanupTableTypesOperation.DefaultGracePeriodHours,
                }
                : null;

            return (creates, cleanup);
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
            return DefinitionsDiffer(GetDefinitionAnnotations(source), GetDefinitionAnnotations(target));
        }

        /// <summary>Returns whether two definition maps differ in any key or value.</summary>
        private static bool DefinitionsDiffer(Dictionary<string, string> source, Dictionary<string, string> target)
        {
            return source.Count != target.Count
                || source.Any(p => !target.TryGetValue(p.Key, out string? json) || json != p.Value);
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

        /// <summary>Reads the set of definition keys this context does not own (excluded from migrations).</summary>
        private static HashSet<string> GetExcludedKeys(IReadOnlyModel? model)
        {
            return model?.FindAnnotation(TableTypeAnnotationNames.ExcludedKeys)?.Value is string json
                ? [.. TableTypeJson.DeserializeStringList(json)]
                : [];
        }
    }
}
