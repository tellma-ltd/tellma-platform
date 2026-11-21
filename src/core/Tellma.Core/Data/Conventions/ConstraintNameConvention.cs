// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Tellma.Core.Data.Conventions
{
    /// <summary>
    ///     Names every primary key <c>PK_&lt;Table&gt;</c> and every foreign key without a name of its
    ///     own <c>FK_&lt;Table&gt;_&lt;Columns&gt;</c>, Core's configured keys included, so the pipeline's
    ///     mapping of duplicate-key and reference errors by constraint name always finds its key and
    ///     no name is left to the provider.
    /// </summary>
    /// <remarks>
    ///     Runs last. A name a model configuration gives explicitly is kept; every key the other
    ///     conventions add is already named this way.
    /// </remarks>
    internal sealed class ConstraintNameConvention : IModelFinalizingConvention
    {
        /// <inheritdoc />
        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (IConventionEntityType entityType in ConventionModel.Tables(modelBuilder.Metadata))
            {
                string table = ConventionModel.Table(entityType);

                // --- The primary key ---
                _ = entityType.FindPrimaryKey()?.Builder.HasName(ConstraintNames.PrimaryKey(table), fromDataAnnotation: true);

                // --- Every foreign key without an explicit name ---
                foreach (IConventionForeignKey foreignKey in entityType.GetDeclaredForeignKeys().ToList())
                {
                    _ = foreignKey.Builder.HasConstraintName(
                        ConstraintNames.ForeignKey(table, foreignKey.Properties.Select(ConventionModel.Column)),
                        fromDataAnnotation: true);
                }
            }
        }
    }
}
