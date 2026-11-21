// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     Where a sign-in to the root lands: the user's one live tenant, when there is exactly one.
    ///     The server's post-sign-in landing and the client's launch redirect apply the same rule.
    /// </summary>
    public static class TenantLanding
    {
        /// <summary>
        ///     The id of the one tenant among the memberships that is live, whose membership is active,
        ///     and whose state serves reads (<see cref="TenantState.Active" />,
        ///     <see cref="TenantState.ReadOnly" /> or <see cref="TenantState.Frozen" />). Sandboxes
        ///     never count.
        /// </summary>
        /// <param name="memberships">The user's memberships.</param>
        /// <returns>The tenant's id; null when no tenant or more than one qualifies.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="memberships" /> or one of its entries is null.</exception>
        public static int? SingleLiveTenant(IReadOnlyList<TenantMembership> memberships)
        {
            ArgumentNullException.ThrowIfNull(memberships);

            // Distinct ids, so a tenant listed twice still counts as one.
            int? found = null;
            foreach (TenantMembership membership in memberships)
            {
                ArgumentNullException.ThrowIfNull(membership, nameof(memberships));

                TenantDescriptor tenant = membership.Tenant;
                bool qualifies = membership.IsActive
                    && tenant.Category == TenantCategory.Live
                    && tenant.State is TenantState.Active or TenantState.ReadOnly or TenantState.Frozen;

                if (!qualifies || found == tenant.Id)
                {
                    continue;
                }

                if (found is not null)
                {
                    return null;
                }

                found = tenant.Id;
            }

            return found;
        }
    }
}
