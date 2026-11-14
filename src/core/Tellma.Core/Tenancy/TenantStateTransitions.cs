// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Tenancy
{
    /// <summary>The tenant state table: which state may follow which.</summary>
    /// <remarks>
    ///     <list type="bullet">
    ///         <item><see cref="TenantState.Active" /> to and from <see cref="TenantState.ReadOnly" />.</item>
    ///         <item>
    ///             <see cref="TenantState.Active" />, <see cref="TenantState.ReadOnly" /> or
    ///             <see cref="TenantState.Suspended" /> to <see cref="TenantState.Frozen" />, and back
    ///             to any of the three.
    ///         </item>
    ///         <item>
    ///             <see cref="TenantState.Active" /> or <see cref="TenantState.ReadOnly" /> to
    ///             <see cref="TenantState.Suspended" />, and <see cref="TenantState.Suspended" /> to
    ///             <see cref="TenantState.Active" />.
    ///         </item>
    ///         <item><see cref="TenantState.Provisioning" /> to <see cref="TenantState.Active" />.</item>
    ///         <item>Any state but <see cref="TenantState.Retired" /> to <see cref="TenantState.Retired" />, which is terminal.</item>
    ///     </list>
    ///     A state never transitions to itself: a repeated request is refused rather than recorded as
    ///     a change.
    /// </remarks>
    internal static class TenantStateTransitions
    {
        /// <summary>Whether a tenant may move from one state to another.</summary>
        /// <param name="from">The current state.</param>
        /// <param name="to">The requested state.</param>
        /// <returns>True when the state table allows the transition.</returns>
        public static bool IsLegal(TenantState from, TenantState to)
        {
            return (from, to) switch
            {
                // Retired is terminal, and nothing moves to the state it is in.
                (TenantState.Retired, _) => false,
                _ when from == to => false,
                (_, TenantState.Retired) => true,

                // The commercial soft lock.
                (TenantState.Active, TenantState.ReadOnly) => true,
                (TenantState.ReadOnly, TenantState.Active) => true,

                // The operator's freeze, from and back to any of the three serving states.
                (TenantState.Active or TenantState.ReadOnly or TenantState.Suspended, TenantState.Frozen) => true,
                (TenantState.Frozen, TenantState.Active or TenantState.ReadOnly or TenantState.Suspended) => true,

                // Suspension, and the reactivation that ends it.
                (TenantState.Active or TenantState.ReadOnly, TenantState.Suspended) => true,
                (TenantState.Suspended, TenantState.Active) => true,

                // The migrator's activation at the end of provisioning.
                (TenantState.Provisioning, TenantState.Active) => true,

                _ => false,
            };
        }
    }
}
