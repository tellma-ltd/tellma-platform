// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Notifications;

namespace Tellma.Core.Notifications
{
    /// <summary>
    ///     The notification types of the composition, in registration order: the types the features
    ///     contributed, in the order the features were realised.
    /// </summary>
    /// <remarks>
    ///     The registry keeps the first type of a key and skips a type without a key, so that the
    ///     startup gate, and not a failing constructor, is where such a mistake surfaces.
    /// </remarks>
    public sealed class NotificationTypeRegistry : INotificationTypeRegistry
    {
        private readonly Dictionary<string, NotificationTypeDescriptor> _byKey = new(StringComparer.Ordinal);

        /// <summary>Creates the registry.</summary>
        /// <param name="types">The contributed types, in realisation order.</param>
        /// <exception cref="ArgumentNullException"><paramref name="types" /> is null.</exception>
        public NotificationTypeRegistry(IEnumerable<NotificationTypeDescriptor> types)
        {
            ArgumentNullException.ThrowIfNull(types);

            List<NotificationTypeDescriptor> all = [];
            List<string> categories = [];
            HashSet<string> seenCategories = new(StringComparer.Ordinal);
            foreach (NotificationTypeDescriptor type in types)
            {
                // A keyless type cannot be found, and a repeated key is the gate's to report.
                if (type?.Key is null || !_byKey.TryAdd(type.Key, type))
                {
                    continue;
                }

                all.Add(type);

                // Each category once, in the order of its first type.
                if (type.Category is not null && seenCategories.Add(type.Category))
                {
                    categories.Add(type.Category);
                }
            }

            All = all;
            Categories = categories;
        }

        /// <inheritdoc />
        public IReadOnlyList<NotificationTypeDescriptor> All { get; }

        /// <inheritdoc />
        public IReadOnlyList<string> Categories { get; }

        /// <inheritdoc />
        public NotificationTypeDescriptor? Find(string key)
        {
            // A null key names no type, as an unknown one does: callers pass keys read from rows.
            return key is null ? null : _byKey.GetValueOrDefault(key);
        }
    }
}
