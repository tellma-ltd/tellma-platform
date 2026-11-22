// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Notifications;

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     The arguments of an administrator's save of another user's notification preferences: that
    ///     user's complete set, as the user's own save takes it. A type and channel absent from the
    ///     set revert to the channel's default.
    /// </summary>
    /// <param name="Preferences">The user's complete set, each type and channel once.</param>
    public sealed record NotificationPreferencesArguments(IReadOnlyList<NotificationPreference> Preferences)
    {
        /// <summary>The user's complete set, each type and channel once.</summary>
        /// <exception cref="System.Text.Json.JsonException">The request carries no preferences.</exception>
        public IReadOnlyList<NotificationPreference> Preferences { get; } = WireMembers.Required(Preferences, "preferences");
    }
}
