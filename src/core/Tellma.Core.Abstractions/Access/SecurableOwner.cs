// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     The owner of a child securable: a caller's grants on the owner resource reach the child's
    ///     rows through <paramref name="Navigation" />, a to-one path from the child's filter root to
    ///     the owner's.
    /// </summary>
    /// <remarks>
    ///     A child exposed as a query root (role memberships under <c>core.User</c>, through
    ///     <c>User</c>) is a securable of its own: a caller holds it through their own grants on it and
    ///     through their grants on the same action of the owner, rebased through the navigation.
    /// </remarks>
    /// <param name="Resource">The owner's resource, such as <c>core.User</c>; a registered resource.</param>
    /// <param name="Navigation">The to-one path from the child's filter root to the owner's, such as <c>User</c>; navigations joined by dots.</param>
    public sealed record SecurableOwner(string Resource, string Navigation);
}
