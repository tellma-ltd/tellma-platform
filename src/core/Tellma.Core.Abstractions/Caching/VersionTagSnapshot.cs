// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;

namespace Tellma.Core.Abstractions.Caching
{
    /// <summary>
    ///     A tenant's tenant-level version tags as the last batch read them at its head, overlaid with
    ///     that batch's own bumps. It holds no user-level tag.
    /// </summary>
    /// <remarks>
    ///     A name absent from the snapshot was absent from the database when it was read: the cache
    ///     kind behind it is uncacheable on this instance until the migrator seeds its row, and every
    ///     read of it loads.
    /// </remarks>
    /// <param name="TenantId">The tenant.</param>
    /// <param name="Tags">The tags by name, compared ordinally; copied, so the caller's dictionary may change afterwards.</param>
    /// <param name="ReadAtUtc">The instance clock when the batch read the tags.</param>
    public sealed record VersionTagSnapshot(int TenantId, IReadOnlyDictionary<string, VersionTag> Tags, DateTimeOffset ReadAtUtc)
    {
        /// <summary>The tags by name, compared ordinally.</summary>
        public IReadOnlyDictionary<string, VersionTag> Tags { get; } =
            (Tags ?? throw new ArgumentNullException(nameof(Tags))).ToFrozenDictionary(StringComparer.Ordinal);

        /// <summary>The tag of a name; <see cref="VersionTag.None" /> when the snapshot lacks it.</summary>
        /// <param name="name">The name.</param>
        /// <returns>The tag.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="name" /> is null.</exception>
        public VersionTag this[string name]
        {
            get
            {
                ArgumentNullException.ThrowIfNull(name);
                return Tags.TryGetValue(name, out VersionTag? tag) ? tag : VersionTag.None;
            }
        }

        /// <summary>Whether the snapshot holds a name: false means the kind behind it is uncacheable on this instance.</summary>
        /// <param name="name">The name.</param>
        /// <returns>True when it does.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="name" /> is null.</exception>
        public bool Has(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            return Tags.ContainsKey(name);
        }
    }
}
