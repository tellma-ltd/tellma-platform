// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Sharing
{
    /// <summary>
    ///     One row's entry of the <c>shares</c> details extra: its grants, for a caller who may share
    ///     or publish it. The grantees' and the sharers' display projections ride the read's related
    ///     entities.
    /// </summary>
    /// <param name="Id">The shareable row.</param>
    /// <param name="Grants">Its grants: the user grants, then the role grants, then the Everyone grant, each group by grant id.</param>
    public sealed record ShareList(int Id, IReadOnlyList<ShareGrantView> Grants);
}
