// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Abstractions.Sharing
{
    /// <summary>Who can see a shareable row, and why.</summary>
    /// <param name="Total">The distinct audience users who can see the row, exact.</param>
    /// <param name="Capped">Whether <paramref name="Members" /> was cut at the configured maximum, so it lists fewer than <paramref name="Total" />.</param>
    /// <param name="CannotRead">Parallel to the query's data securables: the distinct audience users who can see the row but lack each one, exact.</param>
    /// <param name="Members">
    ///     Each audience user with every reason and the highest access, by display name in the
    ///     reader's language; null unless the caller may read the roles.
    /// </param>
    /// <param name="Grants">One row per grant that can reach the row, for the share dialog: the owner, the user grants, the role grants, the Everyone grant, the stored permissions, then each row seen through.</param>
    /// <param name="Related">
    ///     The display projections of the users (with their kind) and roles the grants and members
    ///     name, and of the rows seen through that the caller may read.
    /// </param>
    public sealed record ShareAudience(
        int Total,
        bool Capped,
        IReadOnlyList<int> CannotRead,
        IReadOnlyList<AudienceMember>? Members,
        IReadOnlyList<AudienceGrant> Grants,
        RelatedEntities Related);
}
