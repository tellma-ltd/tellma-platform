// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Sharing;
using Tellma.Core.Queryex;

namespace Tellma.Core.Sharing
{
    // Spec 0033 §5.3: constant trees, so the SQL is byte-identical for every user and only me() and
    // now() bind per request. A criterion alone makes a decision a filtered one (spec 0014's
    // composition rule), so every member reads and saves the rows they own or are granted, and
    // creates rows, with no role grant; a stored grant widens each decision by disjunction.
    /// <summary>
    ///     The bespoke criteria of a shareable entity: its owner reads, saves, deletes, activates and
    ///     shares it, and its grantees read it, or save it under an <c>Edit</c> grant.
    /// </summary>
    /// <typeparam name="TEntity">The shareable entity.</typeparam>
    /// <typeparam name="TShare">The share entity.</typeparam>
    internal sealed class ShareableAccessCriteria<TEntity, TShare> : IAccessCriteriaProvider
        where TEntity : TopLevelEntity, IShareable
        where TShare : Entity<int>, IShareRow
    {
        /// <summary>The rows the caller owns.</summary>
        private static readonly FilterTree Owned = FilterTree.Leaf("OwnerId = me()");

        /// <summary>Builds the criteria once, from the model's names of the two entities.</summary>
        /// <param name="metadata">The model's entity metadata.</param>
        public ShareableAccessCriteria(IEntityMetadataProvider metadata)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            var shape = ShareableShape.Of(metadata, typeof(TEntity), typeof(TShare));
            string memberships = metadata.Get(typeof(RoleMembership)).Name;
            Resource = shape.Entity.Name;

            // --- Read: owned, or granted to the caller; Save: owned, or granted to the caller with Edit ---
            FilterTree grantee = Grantee(memberships);
            var read = FilterTree.Or([Owned, FilterTree.Exists(shape.Share.Name, shape.Row.Navigation, grantee)]);
            var save = FilterTree.Or([
                Owned,
                FilterTree.Exists(shape.Share.Name, shape.Row.Navigation, FilterTree.And([FilterTree.Leaf($"Access = '{nameof(ShareAccess.Edit)}'"), grantee])),
            ]);
            Criteria =
            [
                new(AccessActions.Read, read, ShareableNames.Reason),
                new(AccessActions.Save, save, ShareableNames.Reason),
                new(AccessActions.Delete, Owned, ShareableNames.Reason),
                new(AccessActions.Activate, Owned, ShareableNames.Reason),
                new(AccessActions.Share, Owned, ShareableNames.Reason),
            ];
        }

        /// <inheritdoc />
        public string Resource { get; }

        /// <inheritdoc />
        public IReadOnlyList<AccessCriterion> Criteria { get; }

        /// <summary>
        ///     The grants, rooted at the share entity, that reach the caller: the Everyone grant, a grant
        ///     to the caller, or a grant to an active role that is public or that the caller currently
        ///     belongs to.
        /// </summary>
        /// <param name="memberships">The role memberships' entity name.</param>
        /// <returns>The tree.</returns>
        internal static FilterTree Grantee(string memberships)
        {
            return FilterTree.Or([
                FilterTree.Leaf("UserId is null and RoleId is null"),
                FilterTree.Leaf("UserId = me()"),
                FilterTree.Via(
                    ShareableShape.RoleNavigation,
                    FilterTree.And([
                        FilterTree.Leaf("IsActive = true"),
                        FilterTree.Or([
                            FilterTree.Leaf("IsPublic = true"),
                            FilterTree.Exists(memberships, "Role", FilterTree.And([FilterTree.Leaf("UserId = me()"), AccessFilters.CurrentMembership])),
                        ]),
                    ])),
            ]);
        }
    }
}
