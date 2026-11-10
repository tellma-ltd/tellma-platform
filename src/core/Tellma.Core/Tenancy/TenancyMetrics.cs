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
    ///     snapshot's age and tenants, and the membership-hint writes. No instrument carries a tenant
    ///     or a user.
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
