// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Notifications
{
    /// <summary>
    ///     The notification channels of the composition: every
    ///     <see cref="NotificationChannelDescriptor" /> a feature registered, in registration order. A
    ///     singleton, built once when the composition is realised; the startup gate refuses a
    ///     composition whose channels break the registry's rules.
    /// </summary>
    /// <remarks>
    ///     Every composition holds the platform's two baseline channels, <c>inbox</c> and <c>email</c>,
    ///     first. <c>inbox</c> delivers through the notifier's own rows; every other channel delivers
    ///     through the <see cref="INotificationChannel" /> registered for it, and a channel without one
    ///     keeps its users' preferences and delivers nothing.
    /// </remarks>
    public interface INotificationChannelRegistry
    {
        /// <summary>Every registered channel, in registration order.</summary>
        IReadOnlyList<NotificationChannelDescriptor> All { get; }

        /// <summary>The channel registered under a key, compared ordinally.</summary>
        /// <param name="key">The channel's key.</param>
        /// <returns>The channel, or <see langword="null" /> when no channel has the key.</returns>
        NotificationChannelDescriptor? Find(string key);
    }
}
