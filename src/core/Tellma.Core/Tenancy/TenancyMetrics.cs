// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     The tenancy runtime's instruments on the runtime meter: the catalog refresh's duration, the
    ///     snapshot's age and tenants, the membership-hint writes, the outcome of resolving a request
    ///     to a tenant and the tenant scopes bound. No instrument carries a tenant or a user.
    /// </summary>
    internal sealed class TenancyMetrics
    {
        /// <summary>The refresh outcome of a probe that found the version unchanged.</summary>
        public const string Unchanged = "unchanged";

        /// <summary>The refresh outcome of a reload.</summary>
        public const string Reloaded = "reloaded";

        /// <summary>The refresh outcome of a failure.</summary>
        public const string Failed = "failed";

        /// <summary>The membership-write outcome of a success.</summary>
        public const string Succeeded = "succeeded";

        private const string SecondUnit = "s";

        private readonly Meter _meter;
        private readonly Histogram<double> _refreshDuration;
        private readonly Counter<long> _membershipWrites;
        private readonly Counter<long> _resolutions;
        private readonly Counter<long> _scopes;

        /// <summary>Creates the instruments.</summary>
        /// <param name="meterFactory">The host's meter factory, which owns the meter's lifetime.</param>
        public TenancyMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);
            _meter = meterFactory.Create(TenancyTelemetryNames.MeterName);

            _refreshDuration = _meter.CreateHistogram<double>(
                TenancyTelemetryNames.CatalogRefreshDuration,
                SecondUnit,
                "Duration of one refresh of the tenant snapshot, by outcome.");

            _membershipWrites = _meter.CreateCounter<long>(
                TenancyTelemetryNames.MembershipWrites,
                description: "Membership-hint writes, by outcome and source.");

            _resolutions = _meter.CreateCounter<long>(
                TenancyTelemetryNames.Resolutions,
                description: "Tenant requests resolved, by outcome.");

            _scopes = _meter.CreateCounter<long>(
                TenancyTelemetryNames.Scopes,
                description: "Service scopes bound to a tenant, by kind.");
        }

        /// <summary>The tag value of a membership writer.</summary>
        /// <param name="source">The writer.</param>
        /// <returns>The tag value.</returns>
        public static string SourceValue(TenantMembershipWriteSource source)
        {
            return source switch
            {
                TenantMembershipWriteSource.PostCommit => "post_commit",
                TenantMembershipWriteSource.Reconcile => "reconcile",
                TenantMembershipWriteSource.Provision => "provision",
                _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Not a membership writer."),
            };
        }

        /// <summary>The tag value of a resolution outcome.</summary>
        /// <param name="outcome">The outcome.</param>
        /// <returns>The tag value.</returns>
        public static string OutcomeValue(TenantResolutionOutcome outcome)
        {
            return outcome switch
            {
                TenantResolutionOutcome.Served => "served",
                TenantResolutionOutcome.NotFound => "not_found",
                TenantResolutionOutcome.Provisioning => "provisioning",
                TenantResolutionOutcome.ReadOnly => "read_only",
                TenantResolutionOutcome.Frozen => "frozen",
                TenantResolutionOutcome.Suspended => "suspended",
                TenantResolutionOutcome.NotMember => "not_member",
                TenantResolutionOutcome.CatalogUnavailable => "catalog_unavailable",
                _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Not a resolution outcome."),
            };
        }

        /// <summary>The tag value of a scope kind.</summary>
        /// <param name="kind">The kind.</param>
        /// <returns>The tag value.</returns>
        public static string KindValue(TenantScopeKind kind)
        {
            return kind switch
            {
                TenantScopeKind.Request => "request",
                TenantScopeKind.Background => "background",
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a scope kind."),
            };
        }

        /// <summary>Records how one tenant request was resolved.</summary>
        /// <param name="outcome">The outcome.</param>
        public void RecordResolution(TenantResolutionOutcome outcome)
        {
            _resolutions.Add(1, new KeyValuePair<string, object?>(TenancyTelemetryNames.OutcomeTag, OutcomeValue(outcome)));
        }

        /// <summary>Records one service scope bound to a tenant.</summary>
        /// <param name="kind">Whether a request or background work bound it.</param>
        public void RecordScope(TenantScopeKind kind)
        {
            _scopes.Add(1, new KeyValuePair<string, object?>(TenancyTelemetryNames.KindTag, KindValue(kind)));
        }

        /// <summary>Records one refresh.</summary>
        /// <param name="outcome"><see cref="Unchanged" />, <see cref="Reloaded" /> or <see cref="Failed" />.</param>
        /// <param name="seconds">How long it took.</param>
        public void RecordRefresh(string outcome, double seconds)
        {
            _refreshDuration.Record(seconds, new KeyValuePair<string, object?>(TenancyTelemetryNames.OutcomeTag, outcome));
        }

        /// <summary>Records one membership-hint write.</summary>
        /// <param name="succeeded">Whether it succeeded.</param>
        /// <param name="source">Which writer wrote it.</param>
        public void RecordMembershipWrite(bool succeeded, TenantMembershipWriteSource source)
        {
            _membershipWrites.Add(
                1,
                new KeyValuePair<string, object?>(TenancyTelemetryNames.OutcomeTag, succeeded ? Succeeded : Failed),
                new KeyValuePair<string, object?>(TenancyTelemetryNames.SourceTag, SourceValue(source)));
        }

        /// <summary>Creates the snapshot's observable gauges over the registry's state.</summary>
        /// <param name="age">The snapshot's age, in seconds; none before the first load.</param>
        /// <param name="tenants">The served tenants, counted by category and state.</param>
        public void ObserveSnapshot(Func<IEnumerable<Measurement<double>>> age, Func<IEnumerable<Measurement<int>>> tenants)
        {
            _ = _meter.CreateObservableGauge(
                TenancyTelemetryNames.CatalogSnapshotAge,
                age,
                SecondUnit,
                "How long ago the tenant snapshot was last confirmed against the catalog.");

            _ = _meter.CreateObservableGauge(
                TenancyTelemetryNames.CatalogTenants,
                tenants,
                description: "The tenants the snapshot serves, by category and state.");
        }
    }
}
