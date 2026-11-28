// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     The ids of the access rows every tenant database is seeded with. Ids 1 to
    ///     <see cref="ReservedIdBandEnd" /> of every table are reserved for seeded rows; every table's
    ///     sequence starts above the band.
    /// </summary>
    public static class WellKnownIds
    {
        /// <summary>
        ///     The system user: the author of every seeded row, the identity of built-in work, and an
        ///     administrator by membership. It has no subject, so it can never sign in.
        /// </summary>
        public const int SystemUserId = 1;

        /// <summary>The Administrator role, whose unfiltered wildcard grant is guaranteed.</summary>
        public const int AdministratorRoleId = 1;

        /// <summary>The Administrator role's unfiltered permission over every resource and action.</summary>
        public const int AdministratorPermissionId = 1;

        /// <summary>The system user's permanent membership of the Administrator role.</summary>
        public const int SystemAdministratorMembershipId = 1;

        /// <summary>The last id of the band reserved for seeded rows.</summary>
        public const int ReservedIdBandEnd = 999;
    }
}
