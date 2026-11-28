// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;

namespace Tellma.Core.Users
{
    // Spec 0014 §3.7: core.Permissions has no unique index (Filter exceeds the key limit), so exact
    // duplicates (Resource, Action, Filter) within a role are collapsed silently at save, before the
    // role's rules count and stamp its permissions. The comparison is ordinal, over the values the
    // pipeline's normalisation left (strings trimmed). A stored row survives over a new one, so its
    // id and its filter's stamp are kept; a dropped stored duplicate is deleted by the save.
    /// <summary>Collapses the exact duplicate permissions of a role, each to one row.</summary>
    internal static class PermissionCollapse
    {
        /// <summary>
        ///     Keeps one permission of each exact (resource, action, filter) triple of a role: the
        ///     first stored one, or the first when none is stored. The survivors keep their order.
        /// </summary>
        /// <param name="role">The role, its permissions as preprocessing left them.</param>
        /// <returns>True when a duplicate was dropped.</returns>
        public static bool Collapse(Role role)
        {
            ArgumentNullException.ThrowIfNull(role);
            if (role.Permissions is not { Count: > 1 } permissions)
            {
                return false;
            }

            // --- The survivor of each triple: the first stored row, else the first row ---
            Dictionary<(string Resource, string Action, string? Filter), Permission> survivors = [];
            foreach (Permission permission in permissions)
            {
                (string, string, string?) key = (permission.Resource, permission.Action, permission.Filter);
                if (!survivors.TryGetValue(key, out Permission? survivor) || (survivor.Id <= 0 && permission.Id > 0))
                {
                    survivors[key] = permission;
                }
            }

            if (survivors.Count == permissions.Count)
            {
                return false;
            }

            // --- The survivors, in the order they were sent ---
            role.Permissions = [.. permissions.Where(permission => ReferenceEquals(survivors[(permission.Resource, permission.Action, permission.Filter)], permission))];
            return true;
        }
    }
}
