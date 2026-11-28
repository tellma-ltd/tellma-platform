// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Sharing
{
    /// <summary>One audience user of a shareable row: every reason they can see it, and the highest access those reasons give.</summary>
    /// <param name="UserId">The user.</param>
    /// <param name="Access">The highest access among the reasons: owner, then edit, then view.</param>
    /// <param name="Reasons">Every reason the user can see the row, in the order of the audience's grants.</param>
    /// <param name="CannotRead">The positions, among the query's data securables, of those the user lacks.</param>
    public sealed record AudienceMember(int UserId, AudienceAccess Access, IReadOnlyList<AudienceReason> Reasons, IReadOnlyList<int> CannotRead);
}
