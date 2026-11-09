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
    ///     Refuses a stored key that is not positive on every keyed table:
    ///     <c>CK_&lt;Table&gt;_Id CHECK ([Id] &gt; 0)</c>.
    /// </summary>
    /// <remarks>
    ///     The wire gives <c>0</c> (a new row) and negative ids (temporary ids) a meaning, so a stored
    ///     non-positive id would be a row no save can address. Sequences start at 1000 and seeded rows
    ///     use 1 to 999, so only a hand-written seed, migration or script could produce one.
    /// </remarks>
    internal sealed class PositiveKeyConvention : IModelFinalizingConvention
    {
        /// <summary>The constraint's own name, appended to <c>CK_&lt;Table&gt;_</c>.</summary>
        public const string ConstraintName = "Id";

        /// <inheritdoc />
        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (IConventionEntityType entityType in ConventionModel.Tables(modelBuilder.Metadata))
            {
                if (!EntityShapes.IsKeyed(entityType.ClrType))
                {
                    continue;
                }

                string column = ConventionModel.Column(entityType.FindProperty(nameof(Entity<>.Id))!);
                _ = entityType.Builder.HasCheckConstraint(
                    ConstraintNames.Check(ConventionModel.Table(entityType), ConstraintName),
                    $"[{column}] > 0",
                    fromDataAnnotation: true);
            }
        }
    }
}
