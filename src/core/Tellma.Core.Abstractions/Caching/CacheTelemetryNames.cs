// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Caching
{
    /// <summary>
    ///     The names of the cache, version-tag and settings instruments and their tags, on the
    ///     platform's <see cref="MeterName" /> meter. Dashboards and alerts key on these strings, so
    ///     they never change. Every tag takes a closed set of values; no tag carries a tenant, a user,
    ///     an entity or a key name, which are structured log properties instead.
    /// </summary>
    public static class CacheTelemetryNames
    {
        /// <summary>The meter every Core instrument is created on.</summary>
        public const string MeterName = "Tellma.Core";

        /// <summary>Counter (<c>{request}</c>): cache reads. Tagged <see cref="KindTag" /> and <see cref="OutcomeTag" />.</summary>
        public const string Requests = "tellma.cache.requests";

        /// <summary>Histogram (<c>s</c>): how long a cache load took. Tagged <see cref="KindTag" />.</summary>
        public const string LoadDuration = "tellma.cache.load.duration";

        /// <summary>Observable gauge (<c>{entry}</c>): the entries a cache holds. Tagged <see cref="KindTag" />.</summary>
        public const string Entries = "tellma.cache.entries";

        /// <summary>Observable gauge (<c>{unit}</c>): a cache's size in its kind's unit. Tagged <see cref="KindTag" />.</summary>
        public const string Size = "tellma.cache.size";

        /// <summary>Observable gauge (<c>{unit}</c>): a cache's size limit, in the same unit as <see cref="Size" />. Tagged <see cref="KindTag" />.</summary>
        public const string Limit = "tellma.cache.limit";

        /// <summary>Counter (<c>{entry}</c>): entries leaving a cache. Tagged <see cref="KindTag" /> and <see cref="ReasonTag" />.</summary>
        public const string Evictions = "tellma.cache.evictions";

        /// <summary>Counter (<c>{bump}</c>): version tags bumped. Tagged <see cref="TagKindTag" />.</summary>
        public const string Bumps = "tellma.versiontags.bumps";

        /// <summary>Counter (<c>{event}</c>): batches that found a cached input moved. Tagged <see cref="StalePhaseTag" />.</summary>
        public const string Stale = "tellma.versiontags.stale";

        /// <summary>Counter (<c>{name}</c>): a registered tag name missing from the database. Tagged <see cref="TagKindTag" />.</summary>
        public const string Missing = "tellma.versiontags.missing";

        /// <summary>Counter (<c>{entry}</c>): a stored setting read as its default because it was invalid or unreadable. Untagged.</summary>
        public const string InvalidEntries = "tellma.settings.entries.invalid";

        /// <summary>The cache kind tag: <c>settings</c>, <c>preferences</c>, <c>permissions</c> or <c>entities</c>.</summary>
        public const string KindTag = "cache.kind";

        /// <summary>The read outcome tag: <c>hit</c>, <c>miss</c>, <c>stale</c>, <c>oversized</c> or <c>uncached</c>.</summary>
        public const string OutcomeTag = "cache.outcome";

        /// <summary>The eviction reason tag: <c>capacity</c>, <c>replaced</c>, <c>removed</c> or <c>age</c>.</summary>
        public const string ReasonTag = "cache.reason";

        /// <summary>
        ///     The tag kind tag: <c>settings</c>, <c>permissions</c>, <c>entities</c>, <c>entity</c>
        ///     (any one entity's), <c>other</c> (a pack's name) or <c>user</c> (a user-level tag).
        /// </summary>
        public const string TagKindTag = "tag.kind";

        /// <summary>The stale phase tag: <c>read-rerun</c>, <c>write-guard</c> or <c>exhausted</c>.</summary>
        public const string StalePhaseTag = "stale.phase";
    }
}
