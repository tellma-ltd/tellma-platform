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
    ///     Makes the key of every <see cref="SiblingAttribute" /> entity a one-to-one foreign key to the
    ///     table of the leaf registered for its owner, <c>FK_&lt;Table&gt;_Id</c>, which cascades the
    ///     owner's delete. A sibling has no sequence (<see cref="SequenceConvention" />): its writer
    ///     inserts the owner's id.
    /// </summary>
    internal sealed class SiblingConvention : IModelFinalizingConvention
    {
        /// <inheritdoc />
        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (IConventionEntityType entityType in ConventionModel.Tables(modelBuilder.Metadata))
            {
                if (EntityShapes.GetAttribute<SiblingAttribute>(entityType.ClrType) is not { } sibling)
                {
                    continue;
                }

                string declaration = ConventionModel.Declaration("Sibling", entityType);
                IConventionProperty key = entityType.FindPrimaryKey()?.Properties is [IConventionProperty single]
                    ? single
                    : throw new InvalidOperationException($"{declaration} decorates an entity without a single-column key; a sibling's key is its owner's id.");

                IConventionEntityType owner = ConventionModel.Leaf(modelBuilder.Metadata, sibling.Owner, declaration);
                _ = ConventionModel.AddForeignKey(entityType, key, owner, DeleteBehavior.Cascade, unique: true, declaration);
            }
        }

        /// <summary>Whether an entity type is a sibling.</summary>
        /// <param name="entityType">The entity type.</param>
        /// <returns>True when its class declares <see cref="SiblingAttribute" />.</returns>
        public static bool IsSibling(IReadOnlyEntityType entityType)
        {
            return EntityShapes.GetAttribute<SiblingAttribute>(entityType.ClrType) is not null;
        }
    }
}
