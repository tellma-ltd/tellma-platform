// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Caching;

namespace Tellma.Core.Tests.Caching
{
    /// <summary>
    ///     The process's view of the tags and what follows from it: the tenant and user snapshot stores
    ///     (empty, replaced, bounded), the mismatch policies and the re-run dependencies they discard,
    ///     the wire forms clients key by, and the instruments with their once-per-name warning.
    /// </summary>
    public sealed class VersionTagStateTests : IDisposable
    {
        private static readonly VersionTag First = new(new Guid("00000001-0000-0000-0000-000000000000"));

        private static readonly VersionTag Second = new(new Guid("00000002-0000-0000-0000-000000000000"));

        /// <summary>The meter factory and the logs the instruments use.</summary>
        private readonly ServiceProvider _services = new ServiceCollection().AddMetrics().AddFakeLogging().BuildServiceProvider();

        /// <inheritdoc />
        public void Dispose()
        {
            _services.Dispose();
        }

        // ===================== The snapshot stores =====================

        [Fact]
        public void A_tenant_never_read_has_the_empty_snapshot_read_at_the_start_of_time()
        {
            using VersionTagSnapshots snapshots = new(Options(tagSnapshots: 4));

            VersionTagSnapshot empty = snapshots.Current(9);

            Assert.Equal((9, 0, DateTimeOffset.MinValue), (empty.TenantId, empty.Tags.Count, empty.ReadAtUtc));
            Assert.Equal(VersionTag.None, empty[VersionTagNames.Settings]);
        }

        [Fact]
        public void A_replaced_snapshot_is_the_tenants_current_one_and_no_other_tenants()
        {
            using VersionTagSnapshots snapshots = new(Options(tagSnapshots: 4));
            VersionTagSnapshot first = Snapshot(1, First);
            VersionTagSnapshot second = Snapshot(1, Second);

            snapshots.Replace(first);
            Assert.Same(first, snapshots.Current(1));
            snapshots.Replace(second);
            Assert.Same(second, snapshots.Current(1));
            Assert.Empty(snapshots.Current(2).Tags);
        }

        [Fact]
        public void A_full_store_still_replaces_a_tenants_snapshot_and_refuses_a_new_tenant_rather_than_keep_a_stale_one()
        {
            using VersionTagSnapshots snapshots = new(Options(tagSnapshots: 2));
            snapshots.Replace(Snapshot(1, First));
            snapshots.Replace(Snapshot(2, First));

            // Replacing at capacity frees the old entry first, so the new one always lands.
            VersionTagSnapshot replaced = Snapshot(1, Second);
            snapshots.Replace(replaced);
            Assert.Same(replaced, snapshots.Current(1));

            // A third tenant's snapshot may be refused; the tenant then reads as never read, never as stale.
            snapshots.Replace(Snapshot(3, Second));
            VersionTagSnapshot third = snapshots.Current(3);
            Assert.True(third.Tags.Count == 0 || third[VersionTagNames.Settings] == Second);
        }

        [Fact]
        public void A_users_snapshot_is_kept_per_tenant_and_user()
        {
            using UserVersionTagSnapshots snapshots = new(Options(preferencesEntries: 2));
            UserVersionTagSnapshot first = new(1, 7, First, First, DateTimeOffset.UnixEpoch);
            UserVersionTagSnapshot second = new(1, 7, Second, First, DateTimeOffset.UnixEpoch);

            Assert.Null(snapshots.Current(1, 7));
            snapshots.Replace(first);
            snapshots.Replace(new UserVersionTagSnapshot(2, 7, First, First, DateTimeOffset.UnixEpoch));
            snapshots.Replace(second);

            Assert.Same(second, snapshots.Current(1, 7));
            Assert.Null(snapshots.Current(1, 8));
        }

        // ===================== The policies =====================

        [Theory]
        [InlineData(VersionTagNames.Settings, BatchPurpose.Read, VersionTagMismatchPolicy.Refresh)]
        [InlineData(VersionTagNames.Entities, BatchPurpose.Read, VersionTagMismatchPolicy.Refresh)]
        [InlineData("entity:core.Role", BatchPurpose.Read, VersionTagMismatchPolicy.Refresh)]
        [InlineData("currencies", BatchPurpose.Read, VersionTagMismatchPolicy.Refresh)]
        [InlineData(VersionTagNames.Permissions, BatchPurpose.Read, VersionTagMismatchPolicy.Rerun)]
        [InlineData(VersionTagNames.Settings, BatchPurpose.Validate, VersionTagMismatchPolicy.Rerun)]
        [InlineData("entity:core.Role", BatchPurpose.Validate, VersionTagMismatchPolicy.Rerun)]
        [InlineData(VersionTagNames.Settings, BatchPurpose.Persist, VersionTagMismatchPolicy.Rerun)]
        [InlineData(VersionTagNames.Permissions, BatchPurpose.Persist, VersionTagMismatchPolicy.Rerun)]
        public void A_reads_inputs_refresh_except_permissions_and_validation_and_persistence_rerun(string name, BatchPurpose purpose, VersionTagMismatchPolicy expected)
        {
            Assert.Equal(expected, VersionTagPolicies.For(name, purpose));
        }

        [Fact]
        public void A_maintenance_batch_has_no_policy_and_the_callers_preferences_refresh()
        {
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => VersionTagPolicies.For(VersionTagNames.Settings, BatchPurpose.Maintenance));
            Assert.Equal(VersionTagMismatchPolicy.Refresh, VersionTagPolicies.ForPreferences());
        }

        [Fact]
        public void Only_moved_rerun_dependencies_are_stale_and_a_name_the_database_lacks_has_moved()
        {
            VersionTagSnapshot read = new(1, new Dictionary<string, VersionTag> { ["a"] = Second, ["b"] = Second, ["c"] = First }, DateTimeOffset.UnixEpoch);
            VersionTagDependency movedRerun = new("a", First, VersionTagMismatchPolicy.Rerun);
            VersionTagDependency movedRefresh = new("b", First, VersionTagMismatchPolicy.Refresh);
            VersionTagDependency steady = new("c", First, VersionTagMismatchPolicy.Rerun);
            VersionTagDependency missing = new("d", First, VersionTagMismatchPolicy.Rerun);

            Assert.Equal([movedRerun, missing], VersionTagPolicies.StaleReruns([movedRerun, movedRefresh, steady, missing], read));
            Assert.Empty(VersionTagPolicies.StaleReruns([movedRefresh, steady], read));
        }

        // ===================== The wire forms =====================

        [Fact]
        public void Each_item_is_its_format_version_then_the_tag_and_permissions_join_the_tenants_and_the_users()
        {
            Assert.Equal("1.00000001000000000000000000000000", VersionTagWire.Settings(First));
            Assert.Equal("1.00000001000000000000000000000000", VersionTagWire.Entities(First));
            Assert.Equal("1.00000002000000000000000000000000", VersionTagWire.Preferences(Second));
            Assert.Equal("1.0000000100000000000000000000000000000002000000000000000000000000", VersionTagWire.Permissions(First, Second));
        }

        [Fact]
        public void The_header_lists_the_users_items_only_for_a_user()
        {
            VersionTagSnapshot tenant = new(
                1,
                new Dictionary<string, VersionTag> { [VersionTagNames.Settings] = First, [VersionTagNames.Permissions] = Second, [VersionTagNames.Entities] = Second },
                DateTimeOffset.UnixEpoch);
            UserVersionTagSnapshot user = new(1, 7, First, First, DateTimeOffset.UnixEpoch);

            Assert.Equal(
                "settings=1.00000001000000000000000000000000, entities=1.00000002000000000000000000000000",
                VersionTagWire.HeaderValue(tenant, null));
            Assert.Equal(
                "settings=1.00000001000000000000000000000000, permissions=1.0000000200000000000000000000000000000001000000000000000000000000, "
                + "preferences=1.00000001000000000000000000000000, entities=1.00000002000000000000000000000000",
                VersionTagWire.HeaderValue(tenant, user));
        }

        // ===================== The instruments =====================

        [Theory]
        [InlineData(VersionTagNames.Settings, "settings")]
        [InlineData(VersionTagNames.Permissions, "permissions")]
        [InlineData(VersionTagNames.Entities, "entities")]
        [InlineData("entity:core.Role", "entity")]
        [InlineData("currencies", "other")]
        public void Each_name_is_metered_by_its_kind_never_by_itself(string name, string kind)
        {
            Assert.Equal(kind, VersionTagMetrics.KindOf(name));
        }

        [Fact]
        public void The_counters_record_moves_by_kind_stale_results_by_phase_and_missing_rows_by_kind()
        {
            VersionTagMetrics metrics = Metrics();
            using MetricCollector<long> bumps = Collector(CacheTelemetryNames.Bumps);
            using MetricCollector<long> stale = Collector(CacheTelemetryNames.Stale);
            using MetricCollector<long> missing = Collector(CacheTelemetryNames.Missing);

            metrics.RecordBumps([VersionTagNames.Entities, "entity:core.Role", "currencies"], userTags: 3);
            metrics.RecordStale(VersionTagMetrics.WriteGuardPhase);
            metrics.ReportReadRerun(["currencies"], 1);
            metrics.RecordMissing(["entity:core.Role"]);

            Assert.Equal(
                [(1L, "entities"), (1L, "entity"), (1L, "other"), (3L, "user")],
                bumps.GetMeasurementSnapshot().Select(static m => (m.Value, (string)m.Tags[CacheTelemetryNames.TagKindTag]!)));
            Assert.Equal(
                ["write-guard", "read-rerun"],
                stale.GetMeasurementSnapshot().Select(static m => (string)m.Tags[CacheTelemetryNames.StalePhaseTag]!));
            Assert.Equal(["entity"], missing.GetMeasurementSnapshot().Select(static m => (string)m.Tags[CacheTelemetryNames.TagKindTag]!));
        }

        [Fact]
        public void A_registered_name_with_no_row_is_warned_of_once_per_process()
        {
            VersionTagMetrics metrics = Metrics();
            VersionTagSnapshot read = new(1, new Dictionary<string, VersionTag> { [VersionTagNames.Settings] = First }, DateTimeOffset.UnixEpoch);

            metrics.WarnMissing(read, [VersionTagNames.Settings, "currencies"]);
            metrics.WarnMissing(read with { TenantId = 2 }, [VersionTagNames.Settings, "currencies"]);

            FakeLogRecord warning = Assert.Single(_services.GetFakeLogCollector().GetSnapshot(), static record => record.Id.Name == "VersionTagRowMissing");
            Assert.Equal(LogLevel.Warning, warning.Level);
            Assert.Contains("version tag currencies", warning.Message, StringComparison.Ordinal);
        }

        /// <summary>Cache options with the given bounds.</summary>
        /// <param name="tagSnapshots">The tenant snapshot bound.</param>
        /// <param name="preferencesEntries">The user snapshot bound.</param>
        /// <returns>The options.</returns>
        private static IOptions<CacheOptions> Options(int tagSnapshots = 10, int preferencesEntries = 10)
        {
            return Microsoft.Extensions.Options.Options.Create(new CacheOptions { TagSnapshots = tagSnapshots, PreferencesEntries = preferencesEntries });
        }

        /// <summary>A tenant snapshot whose <c>settings</c> tag is the given one.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="settings">The tag.</param>
        /// <returns>The snapshot.</returns>
        private static VersionTagSnapshot Snapshot(int tenantId, VersionTag settings)
        {
            return new VersionTagSnapshot(tenantId, new Dictionary<string, VersionTag> { [VersionTagNames.Settings] = settings }, DateTimeOffset.UnixEpoch);
        }

        /// <summary>The instruments over the suite's meter factory and logs.</summary>
        /// <returns>The instruments.</returns>
        private VersionTagMetrics Metrics()
        {
            return new VersionTagMetrics(_services.GetRequiredService<IMeterFactory>(), _services.GetRequiredService<ILogger<VersionTagMetrics>>());
        }

        /// <summary>A collector of one of the platform meter's counters.</summary>
        /// <param name="instrument">The counter's name.</param>
        /// <returns>The collector.</returns>
        private MetricCollector<long> Collector(string instrument)
        {
            return new MetricCollector<long>(_services.GetRequiredService<IMeterFactory>(), CacheTelemetryNames.MeterName, instrument);
        }
    }
}
