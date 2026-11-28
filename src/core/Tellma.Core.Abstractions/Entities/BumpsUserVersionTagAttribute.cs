// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Caching;

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     Makes every write to the entity's table bump one of the user-level version tags of each user
    ///     the written rows name, which invalidates those users' cached permissions or preferences.
    /// </summary>
    /// <remarks>
    ///     Repeatable, one rule per declaration, and inherited by a leaf. A role membership, for
    ///     example, bumps its user's permissions tag:
    ///     <c>[BumpsUserVersionTag(UserVersionTagNames.Permissions)]</c>; the user row itself bumps its
    ///     own preferences tag: <c>[BumpsUserVersionTag(UserVersionTagNames.Preferences, "Id")]</c>.
    /// </remarks>
    /// <param name="column">The user-level tag the write bumps.</param>
    /// <param name="userIdProperty">The property of the written rows that names the affected user.</param>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class BumpsUserVersionTagAttribute(UserVersionTagNames column, string userIdProperty = "UserId") : Attribute
    {
        /// <summary>The user-level tag the write bumps.</summary>
        public UserVersionTagNames Column { get; } = column;

        /// <summary>The property of the written rows that names the affected user.</summary>
        public string UserIdProperty { get; } = userIdProperty;
    }
}
