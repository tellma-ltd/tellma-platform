// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;

namespace Tellma.Core.Abstractions.Sharing
{
    /// <summary>A user grant the <c>share</c> action adds, or whose access it changes.</summary>
    /// <param name="UserId">The grantee: an active individual member other than the row's owner.</param>
    /// <param name="Access">What the grant lets the grantee do.</param>
    public sealed record UserGrant([property: EntityRef("core.User")] int UserId, ShareAccess Access);
}
