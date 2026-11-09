// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Data.Conventions
{
    /// <summary>
    ///     Makes every <see cref="TemporalAttribute" /> table system-versioned, with its history in
    ///     <c>&lt;schema&gt;.&lt;Table&gt;History</c> and the <c>ValidFrom</c>/<c>ValidTo</c> period
    ///     columns as shadow properties left out of the table type. The children of a temporal table
    ///     follow it, at every depth, unless they declare <c>[Temporal(false)]</c>.
    /// </summary>
    /// <remarks>
    ///     A temporal table-per-type root or leaf cannot be built, since the history of a row split
    ///     across tables would split with it; the convention fails the model with the entity named.
    /// </remarks>
    internal sealed class TemporalConvention : IModelFinalizingConvention
    {
        /// <summary>The period start column.</summary>
        public const string PeriodStart = "ValidFrom";

        /// <summary>The period end column.</summary>
        public const string PeriodEnd = "ValidTo";

        /// <summary>The suffix of a history table's name.</summary>
        public const string HistorySuffix = "History";

        /// <inheritdoc />
        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            // --- No temporal table-per-type leaf (the loop below sees only roots) ---
            foreach (IConventionEntityType derived in modelBuilder.Metadata.GetEntityTypes())
            {
                if (derived.BaseType is not null
                    && HasOwnTable(derived)
                    && EntityShapes.GetAttribute<TemporalAttribute>(derived.ClrType) is { Enabled: true })
                {
                    throw new InvalidOperationException(
                        $"{ConventionModel.Declaration("Temporal", derived)} makes a table-per-type leaf temporal. A temporal table cannot be split across tables; remove [Temporal] or map the hierarchy to one table.");
                }
            }

            Dictionary<IConventionEntityType, ChildPairing> pairs = ChildConvention.Pairs(modelBuilder.Metadata);
            foreach (IConventionEntityType entityType in ConventionModel.Tables(modelBuilder.Metadata))
            {
                if (!IsTemporal(entityType, pairs, depth: 0))
                {
                    continue;
                }

                // --- No table-per-type: a version of a row would split across its tables ---
                if (entityType.GetDerivedTypes().Any(HasOwnTable))
                {
                    throw new InvalidOperationException(
                        $"{ConventionModel.Declaration("Temporal", entityType)} makes a table-per-type root temporal. A temporal table cannot be split across tables; remove [Temporal] or map the hierarchy to one table.");
                }

                // --- The shadow period columns, created here so they can be left out of the table type ---
                // The provider's temporal convention would create them in response to the period
                // annotations, but only once the finalizing batch completes, after this convention
                // returns; created first, they are found and reused by it.
                IConventionEntityTypeBuilder builder = entityType.Builder;
                foreach (string period in (string[])[PeriodStart, PeriodEnd])
                {
                    IConventionPropertyBuilder property = builder.Property(typeof(DateTime), period, fromDataAnnotation: true)
                        ?? throw new InvalidOperationException(
                            $"{ConventionModel.Declaration("Temporal", entityType)} needs the shadow period column {period}, but a member or a model configuration of the entity already uses the name.");
                    TableTypeConvention.Exclude(property.Metadata);
                }

                // --- System versioning ---
                _ = builder.IsTemporal(true, fromDataAnnotation: true);
                _ = builder.UseHistoryTableName(ConventionModel.Table(entityType) + HistorySuffix, fromDataAnnotation: true);
                _ = builder.UseHistoryTableSchema(entityType.GetSchema(), fromDataAnnotation: true);
                _ = builder.HasPeriodStart(PeriodStart, fromDataAnnotation: true);
                _ = builder.HasPeriodEnd(PeriodEnd, fromDataAnnotation: true);
            }
        }

        /// <summary>Whether a derived entity type maps a table of its own (table-per-type) rather than its base's.</summary>
        /// <param name="derived">The derived entity type.</param>
        /// <returns>True for a table-per-type leaf.</returns>
        private static bool HasOwnTable(IConventionEntityType derived)
        {
            IConventionEntityType? baseType = derived.BaseType;
            return baseType is not null
                && derived.GetTableName() is not null
                && (derived.GetTableName() != baseType.GetTableName() || derived.GetSchema() != baseType.GetSchema());
        }

        /// <summary>
        ///     Whether an entity type is temporal: its own <see cref="TemporalAttribute" /> decides, and a
        ///     child without one follows its parent.
        /// </summary>
        /// <param name="entityType">The entity type.</param>
        /// <param name="pairs">The model's child pairings.</param>
        /// <param name="depth">How many parents have been followed, which bounds a cycle of collections.</param>
        /// <returns>True when the table is system-versioned.</returns>
        private static bool IsTemporal(IConventionEntityType entityType, Dictionary<IConventionEntityType, ChildPairing> pairs, int depth)
        {
            if (EntityShapes.GetAttribute<TemporalAttribute>(entityType.ClrType) is { } temporal)
            {
                return temporal.Enabled;
            }

            // A child owned through its own descendants would recurse forever; the metadata
            // validation reports the cycle, and the child simply does not follow.
            const int MaxDepth = 64;
            return depth < MaxDepth
                && pairs.TryGetValue(entityType, out ChildPairing? pairing)
                && IsTemporal(pairing.Parent, pairs, depth + 1);
        }
    }
}
