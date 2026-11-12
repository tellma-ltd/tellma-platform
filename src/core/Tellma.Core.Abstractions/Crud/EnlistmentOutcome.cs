// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Caching;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>What persisting an open frame came to.</summary>
    /// <param name="Stamp">The persist batch's stamp, which every row it wrote carries.</param>
    /// <param name="VersionTags">The tenant's version tags after the batch's bumps.</param>
    /// <param name="UserVersionTags">The caller's user-level version tags after the batch's bumps; <see langword="null" /> when the batch carried no connected user.</param>
    public sealed record EnlistmentOutcome(
        DateTimeOffset Stamp,
        VersionTagSnapshot VersionTags,
        UserVersionTagSnapshot? UserVersionTags);
}
