// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;

namespace Tellma.Core.Access
{
    // The escalation test is spec 0014 §8.4. Read through the wildcard resource follows the set's
    // own implied read (§5.2 step 2): (*, A') implies reading R only where R registers A'.
    /// <summary>
    ///     What a caller may hand out: a permission on a pair is delegable only by a caller whose own
    ///     set holds an unrestricted grant, current at the instant, that covers the pair. A filtered
    ///     grant never qualifies, since whether one filter implies another cannot be decided.
    /// </summary>
    internal static class AccessDelegation
    {
        /// <summary>
        ///     Whether the caller is exempt from the test: the platform's own set, or a caller holding
        ///     the unrestricted wildcard pair now.
        /// </summary>
        /// <param name="caller">The caller's set.</param>
        /// <param name="now">The instant judged.</param>
        /// <returns>True when every pair is delegable.</returns>
        public static bool IsExempt(UserAccess caller, DateTimeOffset now)
        {
            ArgumentNullException.ThrowIfNull(caller);
            return caller.IsSystem || Holds(caller, CoreResources.Wildcard, AccessActions.Wildcard, now);
        }

        /// <summary>
        ///     Whether the caller may hand out a permission on a pair: a concrete pair is covered by
        ///     itself, the resource's wildcard action, the action on the wildcard resource or the
        ///     wildcard pair, and reading also by any action on the resource, or any action on the
        ///     wildcard resource that the resource registers; the resource's wildcard action only by
        ///     itself or the wildcard pair; an action on the wildcard resource only by itself or the
        ///     wildcard pair.
        /// </summary>
        /// <param name="caller">The caller's set.</param>
        /// <param name="resource">The permission's resource, as registered, or the wildcard.</param>
        /// <param name="action">The permission's action, or the wildcard.</param>
        /// <param name="now">The instant judged.</param>
        /// <returns>True when the caller holds a covering grant.</returns>
        public static bool Covers(UserAccess caller, string resource, string action, DateTimeOffset now)
        {
            ArgumentNullException.ThrowIfNull(caller);
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(action);
            if (IsExempt(caller, now))
            {
                return true;
            }

            bool wildcardResource = resource == CoreResources.Wildcard;
            bool wildcardAction = action == AccessActions.Wildcard;

            // --- A wildcard on either side is covered only by the same wildcard (or the pair, above) ---
            if (wildcardResource || wildcardAction)
            {
                return !(wildcardResource && wildcardAction) && Holds(caller, resource, action, now);
            }

            // --- A concrete pair: itself, either wildcard, and for reading any action that implies it ---
            return Holds(caller, resource, action, now)
                || Holds(caller, resource, AccessActions.Wildcard, now)
                || Holds(caller, CoreResources.Wildcard, action, now)
                || (Same(action, AccessActions.Read) && Unrestricted(caller, now).Any(grant =>
                    Same(grant.Resource, resource)
                    || (grant.Resource == CoreResources.Wildcard && caller.ActionResources.Registers(resource, grant.Action))));
        }

        /// <summary>Whether the caller holds one pair exactly, unrestricted and current.</summary>
        /// <param name="caller">The caller's set.</param>
        /// <param name="resource">The resource, or the wildcard.</param>
        /// <param name="action">The action, or the wildcard.</param>
        /// <param name="now">The instant judged.</param>
        /// <returns>True when a grant holds it.</returns>
        private static bool Holds(UserAccess caller, string resource, string action, DateTimeOffset now)
        {
            return Unrestricted(caller, now).Any(grant => Same(grant.Resource, resource) && Same(grant.Action, action));
        }

        /// <summary>The caller's grants that count: unrestricted, and current at the instant.</summary>
        /// <param name="caller">The caller's set.</param>
        /// <param name="now">The instant judged.</param>
        /// <returns>The grants.</returns>
        private static IEnumerable<RoleAccessGrant> Unrestricted(UserAccess caller, DateTimeOffset now)
        {
            return caller.Grants.Where(grant => grant.Filter is null && grant.IsCurrentAt(now));
        }

        /// <summary>Whether two securable names are the same; names compare ignoring case.</summary>
        /// <param name="left">One name.</param>
        /// <param name="right">The other.</param>
        /// <returns>True when they are.</returns>
        private static bool Same(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
