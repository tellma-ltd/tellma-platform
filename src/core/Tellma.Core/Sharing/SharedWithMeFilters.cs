// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Sharing;
using Tellma.Core.Queryex;

namespace Tellma.Core.Sharing
{
    // Spec 0033 §5.5: the "Shared with me" tab's filter, a constant tree with no value from the
    // caller: rows someone else owns, or no one (a standard row shared with the caller), that a grant
    // reaching the caller names. The query joins it to its own filters and the access filter.
    /// <summary>The <c>shared-with-me</c> named filter of a shareable stack: the rows shared with the caller that the caller does not own.</summary>
    /// <typeparam name="TEntity">The shareable entity.</typeparam>
    /// <typeparam name="TShare">The share entity.</typeparam>
    internal sealed class SharedWithMeFilters<TEntity, TShare> : INamedFilterContributor<TEntity>
        where TEntity : TopLevelEntity, IShareable
        where TShare : Entity<int>, IShareRow
    {
        /// <summary>Builds the filter once, from the model's names of the two entities.</summary>
        /// <param name="metadata">The model's entity metadata.</param>
        public SharedWithMeFilters(IEntityMetadataProvider metadata)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            var shape = ShareableShape.Of(metadata, typeof(TEntity), typeof(TShare));
            FilterTree grantee = ShareableAccessCriteria<TEntity, TShare>.Grantee(metadata.Get(typeof(RoleMembership)).Name);
            NamedFilters = new Dictionary<string, FilterTree>(StringComparer.Ordinal)
            {
                [ShareableNames.SharedWithMeFilter] = FilterTree.And([
                    FilterTree.Or([FilterTree.Leaf("OwnerId is null"), FilterTree.Leaf("OwnerId != me()")]),
                    FilterTree.Exists(shape.Share.Name, shape.Row.Navigation, grantee),
                ]),
            };
        }

        /// <inheritdoc />
        public IReadOnlyDictionary<string, FilterTree> NamedFilters { get; }
    }
}
