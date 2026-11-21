// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Notifications;

namespace Tellma.Core.Notifications
{
    /// <summary>
    ///     The notification channels of the composition, in registration order: the platform's
    ///     <c>inbox</c> and <c>email</c> first, then the channels other features contributed, in the
    ///     order the features were realised.
    /// </summary>
    /// <remarks>
    ///     The registry keeps the first channel of a key and skips a channel without a key, so that
    ///     the startup gate, and not a failing constructor, is where such a mistake surfaces.
    /// </remarks>
    public sealed class NotificationChannelRegistry : INotificationChannelRegistry
    {
        private readonly Dictionary<string, NotificationChannelDescriptor> _byKey = new(StringComparer.Ordinal);

        /// <summary>Creates the registry.</summary>
        /// <param name="channels">The contributed channels, in realisation order.</param>
        /// <exception cref="ArgumentNullException"><paramref name="channels" /> is null.</exception>
        public NotificationChannelRegistry(IEnumerable<NotificationChannelDescriptor> channels)
        {
            ArgumentNullException.ThrowIfNull(channels);

            List<NotificationChannelDescriptor> all = [];
            foreach (NotificationChannelDescriptor channel in channels)
            {
                // A keyless channel cannot be found, and a repeated key is the gate's to report.
                if (channel?.Key is not null && _byKey.TryAdd(channel.Key, channel))
                {
                    all.Add(channel);
                }
            }

            All = all;
        }

        /// <inheritdoc />
        public IReadOnlyList<NotificationChannelDescriptor> All { get; }

        /// <inheritdoc />
        public NotificationChannelDescriptor? Find(string key)
        {
            // A null key names no channel, as an unknown one does: callers pass keys read from rows.
            return key is null ? null : _byKey.GetValueOrDefault(key);
        }
    }
}
