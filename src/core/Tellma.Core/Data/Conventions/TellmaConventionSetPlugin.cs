// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Tellma.Core.Data.Conventions
{
    /// <summary>
    ///     Installs the platform's model conventions, in order, at the head of the model-finalizing
    ///     conventions: they run over the finished model (every entity, every model configuration),
    ///     before the provider's finalizing conventions settle key generation, temporal periods and
    ///     redundant indexes, and before the table-type extension derives each type.
    /// </summary>
    /// <remarks>
    ///     The provider's own convention that indexes every foreign key is removed: the platform's
    ///     conventions name every index the model has, so a foreign key carries one only where a
    ///     convention declares it (a child's parent key, a reference, a tree's parent, a job or blob
    ///     column's unique index), and never under a name the provider chose. The audit columns, whose
    ///     user references change on every save, are the foreign keys this leaves unindexed.
    /// </remarks>
    internal sealed class TellmaConventionSetPlugin : IConventionSetPlugin
    {
        /// <summary>
        ///     The conventions, in the order they run. Each reads only what the ones before it settled:
        ///     enum sizing reads the string conversion, the table type reads every exclusion the tree and
        ///     temporal conventions add, and uniqueness reads the parent key pairing.
        /// </summary>
        /// <returns>A fresh instance of each convention.</returns>
        public static IReadOnlyList<IModelFinalizingConvention> Conventions()
        {
            return
            [
                new EnumStringConvention(),
                new EnumStringLengthConvention(),
                new JsonColumnConvention(),
                new AuditConvention(),
                new CollationConvention(),
                new TableTypeConvention(),
                new TemporalConvention(),
                new ChildConvention(),
                new ReferenceConvention(),
                new SiblingConvention(),
                new TableIndexConvention(),
                new TreeConvention(),
                new ActivatableConvention(),
                new JobEntityConvention(),
                new UniqueConvention(),
                new BlobReferenceConvention(),
                new SequenceConvention(),
                new PositiveKeyConvention(),
                new MultilingualConvention(),
            ];
        }

        /// <inheritdoc />
        public ConventionSet ModifyConventions(ConventionSet conventionSet)
        {
            ArgumentNullException.ThrowIfNull(conventionSet);

            conventionSet.Remove(typeof(ForeignKeyIndexConvention));
            conventionSet.ModelFinalizingConventions.InsertRange(0, Conventions());
            return conventionSet;
        }
    }
}
