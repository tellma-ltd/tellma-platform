// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Blobs;
using Tellma.Core.Data.Conventions;
using Tellma.Core.Jobs;

namespace Tellma.Core.Data.Metadata
{
    /// <summary>
    ///     The shadow properties the platform declares, which are the only columns of the tenant model that
    ///     no property of a class carries: the system-versioning period columns of a temporal entity, the
    ///     node of a tree, and the model-only columns Core's own configurations give the job and blob
    ///     machinery tables. Every other shadow property is implicit, the column EF Core adds for a
    ///     navigation that has no foreign-key property, and the entity metadata refuses it.
    /// </summary>
    internal static class PlatformShadowColumns
    {
        /// <summary>
        ///     The model-only columns of Core's machinery tables, by the class each configuration maps. Each
        ///     is written and read only by the fixed-text statements of the feature that owns it, so no
        ///     member, query or response may carry it.
        /// </summary>
        /// <remarks>
        ///     Only Core writes model configurations, so this list is closed: a pack or a distribution
        ///     cannot add to it. Each class is sealed and replaced by no leaf, so the class identifies the
        ///     table.
        /// </remarks>
        private static readonly Dictionary<Type, string[]> MachineryColumns = new()
        {
            // spec 0020 §2.3: the job's checkpoint, the lease's fencing token and the confirmed warnings
            // are the worker's, read from the claim's result set.
            [typeof(Job)] = [JobConfiguration.StateJson, JobConfiguration.LeaseToken, JobConfiguration.ConfirmedKeys],

            // spec 0017 §2.1-§2.2: the storage name and the content hash never reach a client.
            [typeof(Blob)] = [BlobConfiguration.StorageKey, BlobConfiguration.Sha256],
        };

        /// <summary>The model-only columns of Core's machinery tables, by the class each configuration maps.</summary>
        public static IReadOnlyDictionary<Type, string[]> Machinery => MachineryColumns;

        /// <summary>Whether the platform declares a shadow property of an entity type.</summary>
        /// <param name="entityType">The entity type that declares the property.</param>
        /// <param name="property">The property's name.</param>
        /// <returns>
        ///     True for <c>ValidFrom</c> and <c>ValidTo</c> on a temporal entity, <c>Node</c> on a tree, and
        ///     a model-only column of a machinery table.
        /// </returns>
        public static bool IsDeclared(IReadOnlyEntityType entityType, string property)
        {
            ArgumentNullException.ThrowIfNull(entityType);
            ArgumentNullException.ThrowIfNull(property);

            // --- The temporal convention's period columns ---
            if (property is TemporalConvention.PeriodStart or TemporalConvention.PeriodEnd)
            {
                return entityType.IsTemporal();
            }

            // --- The tree convention's node ---
            if (property == TreeConvention.NodeColumn)
            {
                return EntityShapes.IsTree(entityType.ClrType);
            }

            // --- The machinery's model-only columns ---
            return MachineryColumns.TryGetValue(entityType.ClrType, out string[]? columns)
                && columns.Contains(property, StringComparer.Ordinal);
        }
    }
}
