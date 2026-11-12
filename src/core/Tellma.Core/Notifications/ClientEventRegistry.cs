// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Realtime;

namespace Tellma.Core.Notifications
{
    /// <summary>
    ///     The client events of the composition, in registration order: the names the features
    ///     contributed, in the order the features were realised.
    /// </summary>
    /// <remarks>
    ///     The registry keeps a repeated name once and skips a missing one, so that the startup gate,
    ///     and not a failing constructor, is where such a mistake surfaces.
    /// </remarks>
    public sealed class ClientEventRegistry : IClientEventRegistry
    {
        private readonly HashSet<string> _names = new(StringComparer.Ordinal);

        /// <summary>Creates the registry.</summary>
        /// <param name="events">The contributed events, in realisation order.</param>
        /// <exception cref="ArgumentNullException"><paramref name="events" /> is null.</exception>
        public ClientEventRegistry(IEnumerable<ClientEventContributionItem> events)
        {
            ArgumentNullException.ThrowIfNull(events);

            List<string> names = [];
            foreach (ClientEventContributionItem item in events)
            {
                if (item?.Name is not null && _names.Add(item.Name))
                {
                    names.Add(item.Name);
                }
            }

            Names = names;
        }

        /// <inheritdoc />
        public IReadOnlyList<string> Names { get; }

        /// <inheritdoc />
        public bool IsRegistered(string name)
        {
            return name is not null && _names.Contains(name);
        }
    }
}
