// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Sharing
{
    /// <summary>
    ///     A top-level entity whose rows belong to a user and are shared with users, roles or everyone
    ///     through a share entity beside it: a stored report, a dashboard, or a pack's or a
    ///     distribution's own entity.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The entity's service derives from <see cref="ShareableService{TEntity, TShare}" />, and
    ///         its feature composes it with <see cref="ShareableContributionExtensions.Shareable{TEntity, TShare}" />.
    ///     </para>
    ///     <para>
    ///         A row with no owner is a standard row, seeded by the platform: read-only outside a
    ///         system scope, yet shareable like any other.
    ///     </para>
    /// </remarks>
    public interface IShareable
    {
        /// <summary>
        ///     The user the row belongs to, a <c>[ServerOwned]</c> column: the creator, which an update
        ///     keeps from the stored row and only the <c>transfer</c> action changes; null for a
        ///     standard row.
        /// </summary>
        int? OwnerId { get; set; }

        /// <summary>The row a "Save as" copied, a <c>[WriteOnce]</c> column with no foreign key; null for an original.</summary>
        int? BasedOnId { get; set; }
    }
}
