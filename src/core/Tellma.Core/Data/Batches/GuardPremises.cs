// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Data.Batches
{
    /// <summary>
    ///     What the connect prologue confirmed for a batch it marks: the tenant-level tags it compared
    ///     and whom the batch runs for. A persist batch's re-checks compare the same tags again inside
    ///     the transaction.
    /// </summary>
    /// <remarks>
    ///     A user's connect prologue declares or binds <see cref="ReservedSqlNames.UserId" /> and binds
    ///     <see cref="ReservedSqlNames.ExpectedUserPermissionsTag" />, which the re-checks name; the
    ///     platform's own (<see cref="ForSystem" />) binds neither for them, so its re-checks have no
    ///     user arm and no caller re-check.
    /// </remarks>
    internal sealed class GuardPremises
    {
        /// <summary>Creates the premises.</summary>
        /// <param name="settings">The <c>settings</c> tag the batch composes under.</param>
        /// <param name="permissions">The <c>permissions</c> tag of the caller's cached set; null for the platform's own.</param>
        private GuardPremises(VersionTag settings, VersionTag? permissions)
        {
            Settings = settings;
            Permissions = permissions;
        }

        /// <summary>The <c>settings</c> tag the batch composes under.</summary>
        public VersionTag Settings { get; }

        /// <summary>The <c>permissions</c> tag the caller's cached set carries; null when the batch runs for the platform.</summary>
        public VersionTag? Permissions { get; }

        /// <summary>Whether the batch runs for the platform's own principal rather than a user.</summary>
        public bool IsSystem => Permissions is null;

        /// <summary>The premises of a batch on a user's behalf.</summary>
        /// <param name="settings">The <c>settings</c> tag the batch composes under.</param>
        /// <param name="permissions">The tenant-level <c>permissions</c> tag of the caller's cached set; <see cref="VersionTag.None" /> when none is cached.</param>
        /// <returns>The premises.</returns>
        public static GuardPremises ForUser(VersionTag settings, VersionTag permissions)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(permissions);
            return new GuardPremises(settings, permissions);
        }

        /// <summary>The premises of a batch on the platform's own behalf: no caller, so no permissions.</summary>
        /// <param name="settings">The <c>settings</c> tag the batch composes under.</param>
        /// <returns>The premises.</returns>
        public static GuardPremises ForSystem(VersionTag settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            return new GuardPremises(settings, null);
        }
    }
}
