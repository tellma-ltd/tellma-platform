// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Notifications
{
    /// <summary>
    ///     One of a user's notification preferences: whether a channel delivers a type to them. A row
    ///     shape, not an entity.
    /// </summary>
    /// <remarks>
    ///     A user's preferences are stored as deviations only: a type and channel with no preference
    ///     takes the channel's <see cref="NotificationChannelDescriptor.DefaultEnabled" />. The inbox
    ///     channel cannot be turned off for a type that is not
    ///     <see cref="NotificationTypeDescriptor.Mutable" />.
    /// </remarks>
    public sealed class NotificationPreference
    {
        /// <summary>The key of a registered notification type.</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>The key of a registered notification channel.</summary>
        public string Channel { get; set; } = string.Empty;

        /// <summary>Whether the channel delivers the type to the user.</summary>
        public bool Enabled { get; set; }
    }
}
