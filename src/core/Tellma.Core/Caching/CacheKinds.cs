// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;

namespace Tellma.Core.Caching
{
    /// <summary>
    ///     The kinds of <see cref="VersionedCache{TKey, TValue}" />: one store per kind, and the
    ///     closed set the <c>cache.kind</c> tag of the cache instruments takes.
    /// </summary>
    public static class CacheKinds
    {
        /// <summary>The tenant's settings, one entry per tenant.</summary>
        public const string Settings = "settings";

        /// <summary>A user's preferences, one entry per user of a tenant.</summary>
        public const string Preferences = "preferences";

        /// <summary>A user's permissions, one entry per user of a tenant.</summary>
        public const string Permissions = "permissions";

        /// <summary>The lists of the cacheable entities, sized in rows.</summary>
        public const string Entities = "entities";

        /// <summary>Every kind.</summary>
        public static FrozenSet<string> All { get; } = new[] { Settings, Preferences, Permissions, Entities }.ToFrozenSet(StringComparer.Ordinal);
    }
}
