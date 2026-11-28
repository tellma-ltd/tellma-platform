// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;

namespace Tellma.Core.Access
{
    /// <summary>
    ///     The values of the access rows every tenant database is seeded with: the system user, its
    ///     stamp row, the Administrator role with its unfiltered wildcard permission, and the system
    ///     user's permanent membership of it. The ids are <see cref="WellKnownIds" />'s.
    /// </summary>
    internal static class AccessSeeds
    {
        /// <summary>
        ///     The instant every seeded access row was created, modified and, for the system user,
        ///     joined: midnight of 1 January 2000 at offset zero. Fixed, so the migrations never change.
        /// </summary>
        public static DateTimeOffset SeededAt { get; } = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

        /// <summary>The system user's name.</summary>
        public const string SystemUserName = "System";

        /// <summary>The Administrator role's name and code.</summary>
        public const string AdministratorRoleName = "Administrator";

        /// <summary>The system user's seeded permissions tag; any fixed value serves, since the first bump replaces it.</summary>
        public static Guid SystemPermissionsTag { get; } = new("00000000-0000-0000-0000-000000000001");

        /// <summary>The system user's seeded preferences tag; any fixed value serves, since the first bump replaces it.</summary>
        public static Guid SystemPreferencesTag { get; } = new("00000000-0000-0000-0000-000000000002");
    }
}
