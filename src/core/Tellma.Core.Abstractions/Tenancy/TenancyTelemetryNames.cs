// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     The names of the tenancy runtime's meter, instruments and tags. Dashboards and alert
    ///     queries key on them, so they never change.
    /// </summary>
    /// <remarks>
    ///     No instrument carries a tenant or user tag: per-tenant identity belongs on traces and logs,
    ///     never on metrics.
    /// </remarks>
    public static class TenancyTelemetryNames
    {
        /// <summary>The runtime's meter, shared with the other runtime instruments.</summary>
        public const string MeterName = "Tellma.Core";

        /// <summary>Counter: the access guard's verdicts, by <see cref="OutcomeTag" />.</summary>
        public const string Resolutions = "tellma.tenancy.resolutions";

        /// <summary>
        ///     Histogram, in seconds: one refresh of the tenant snapshot, by <see cref="OutcomeTag" />
        ///     (<c>unchanged</c>, <c>reloaded</c> or <c>failed</c>).
        /// </summary>
        public const string CatalogRefreshDuration = "tellma.tenancy.catalog.refresh.duration";

        /// <summary>Observable gauge, in seconds: how long ago the snapshot was last confirmed against the catalog.</summary>
        public const string CatalogSnapshotAge = "tellma.tenancy.catalog.snapshot.age";

        /// <summary>Observable gauge: the tenants in the snapshot, by <see cref="CategoryTag" /> and <see cref="StateTag" />.</summary>
        public const string CatalogTenants = "tellma.tenancy.catalog.tenants";

        /// <summary>
        ///     Counter: membership-hint writes, by <see cref="OutcomeTag" /> (<c>succeeded</c> or
        ///     <c>failed</c>) and <see cref="SourceTag" /> (<c>post_commit</c>, <c>reconcile</c> or
        ///     <c>provision</c>).
        /// </summary>
        public const string MembershipWrites = "tellma.tenancy.membership.writes";

        /// <summary>Counter: tenant scopes created, by <see cref="KindTag" />.</summary>
        public const string Scopes = "tellma.tenancy.scopes";

        /// <summary>The outcome of the measured operation.</summary>
        public const string OutcomeTag = "outcome";

        /// <summary>A tenant category, <c>Live</c> or <c>Sandbox</c>.</summary>
        public const string CategoryTag = "category";

        /// <summary>A tenant state, such as <c>Active</c>.</summary>
        public const string StateTag = "state";

        /// <summary>The kind of a scope, <c>request</c> or <c>background</c>.</summary>
        public const string KindTag = "kind";

        /// <summary>Which writer wrote a membership hint.</summary>
        public const string SourceTag = "source";
    }
}
