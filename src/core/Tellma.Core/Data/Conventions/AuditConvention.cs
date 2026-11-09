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
    ///     Shapes the four audit columns of every <see cref="TopLevelEntity{TKey}" />: the two user
    ///     columns become foreign keys to <c>core.Users</c> with no cascade, and the two timestamps
    ///     <c>datetimeoffset(7)</c>.
    /// </summary>
    /// <remarks>
    ///     The audit columns carry no index: <c>ModifiedById</c> changes on every save, so an index
    ///     on it would cost every write and serve no query the platform runs.
    /// </remarks>
    internal sealed class AuditConvention : IModelFinalizingConvention
    {
        /// <summary>The fractional-second digits of both audit timestamps.</summary>
        public const int TimestampPrecision = 7;

        /// <inheritdoc />
        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (IConventionEntityType entityType in ConventionModel.Tables(modelBuilder.Metadata))
            {
                if (!EntityShapes.IsTopLevel(entityType.ClrType))
                {
                    continue;
                }

                // --- The user columns: foreign keys to core.Users ---
                IConventionEntityType users = ConventionModel.FindCoreTable(
                    modelBuilder.Metadata, CoreTable.Users, $"The audit columns of {entityType.DisplayName()}");

                foreach (string column in (string[])[nameof(TopLevelEntity.CreatedById), nameof(TopLevelEntity.ModifiedById)])
                {
                    IConventionProperty property = entityType.FindProperty(column)!;
                    _ = ConventionModel.AddForeignKey(entityType, property, users, DeleteBehavior.NoAction, unique: false, $"The audit column {entityType.DisplayName()}.{column}");
                }

                // --- The timestamps: seven fractional digits ---
                foreach (string column in (string[])[nameof(TopLevelEntity.CreatedAt), nameof(TopLevelEntity.ModifiedAt)])
                {
                    _ = entityType.FindProperty(column)!.Builder.HasPrecision(TimestampPrecision, fromDataAnnotation: true);
                }
            }
        }
    }
}
