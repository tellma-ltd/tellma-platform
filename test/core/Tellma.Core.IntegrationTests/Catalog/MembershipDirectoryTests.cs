// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.IntegrationTests.Infrastructure;
using Tellma.Core.Tenancy;

namespace Tellma.Core.IntegrationTests.Catalog
{
    /// <summary>
    ///     The membership hints against a real catalog, bound through the catalog's table type: the
    ///     list, the best-effort upsert, and the per-tenant reconciliation that never touches another
    ///     tenant's hints.
    /// </summary>
    [Trait("Category", "Integration")]
    public class MembershipDirectoryTests(SqlServerFixture sql)
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Recorded_hints_list_by_tenant_id_with_their_descriptors_and_retired_tenants_left_out()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "members_list");
            int[] ids = await RegisterAsync(host, 3);
            await host.Catalog.RenameAsync(ids[1], new TenantDisplayNames("Second", "الثاني", null, ["en", "ar"]), Token);

            await host.Directory.RecordAsync(
                [Hint(ids[2], "alice", true), Hint(ids[0], "alice", true), Hint(ids[1], "alice", false), Hint(ids[0], "bob", true)],
                Token);
            _ = await host.Catalog.SetStateAsync(ids[2], TenantState.Retired, null, "migrator", Token);

            IReadOnlyList<TenantMembership> alice = await host.Directory.ListAsync("alice", Token);

            Assert.Equal([ids[0], ids[1]], alice.Select(static membership => membership.Tenant.Id));
            Assert.Equal([true, false], alice.Select(static membership => membership.IsActive));
            Assert.Equal(new TenantDescriptor(ids[1], "Second", "الثاني", null, ["en", "ar"], TenantCategory.Live, TenantState.Provisioning, null), alice[1].Tenant);
            Assert.All(alice, static membership => Assert.NotEqual(default, membership.UpdatedAt));
            Assert.Empty(await host.Directory.ListAsync("carol", Token));
        }

        [Fact]
        public async Task A_tenants_members_list_its_hints_alone_in_subject_order()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "members_of");
            int[] ids = await RegisterAsync(host, 2);
            await host.Directory.RecordAsync(
                [Hint(ids[0], "carol", true), Hint(ids[0], "alice", false), Hint(ids[1], "mallory", true), Hint(ids[0], "bob", true)],
                Token);

            IReadOnlyList<TenantMemberHint> members = await host.Directory.ListMembersAsync(ids[0], Token);

            Assert.Equal(["alice", "bob", "carol"], members.Select(static member => member.Subject));
            Assert.Equal([false, true, true], members.Select(static member => member.IsActive));
            Assert.All(members, static member => Assert.NotEqual(default, member.UpdatedAt));
            Assert.Empty(await host.Directory.ListMembersAsync(4242, Token));
        }

        [Fact]
        public async Task Subjects_compare_byte_for_byte_so_one_never_sees_anothers_tenants()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "members_case");
            int[] ids = await RegisterAsync(host, 2);
            await host.Directory.RecordAsync([Hint(ids[0], "alice", true), Hint(ids[1], "Alice", true)], Token);

            Assert.Equal([ids[0]], (await host.Directory.ListAsync("alice", Token)).Select(static m => m.Tenant.Id));
            Assert.Equal([ids[1]], (await host.Directory.ListAsync("Alice", Token)).Select(static m => m.Tenant.Id));
            Assert.Empty(await host.Directory.ListAsync("ALICE", Token));

            // SQL Server pads trailing spaces away when it compares, so a subject with spaces is
            // refused rather than matched to the one without.
            _ = await Assert.ThrowsAsync<ArgumentException>(() => host.Directory.ListAsync("alice ", Token));
        }

        [Fact]
        public async Task A_second_record_updates_the_activity_and_duplicates_in_one_call_collapse()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "members_upsert");
            int[] ids = await RegisterAsync(host, 1);

            await host.Directory.RecordAsync([Hint(ids[0], "alice", true)], Token);
            await host.Directory.RecordAsync([Hint(ids[0], "alice", true), Hint(ids[0], "alice", false)], Token);

            TenantMembership membership = Assert.Single(await host.Directory.ListAsync("alice", Token));
            Assert.False(membership.IsActive);
            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(host.ConnectionString, "SELECT COUNT(*) FROM [catalog].[TenantMemberships];"));
        }

        [Fact]
        public async Task A_failed_write_is_logged_and_metered_and_never_thrown()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "members_fail");
            using MetricCollector<long> writes = new(
                host.Services.GetRequiredService<IMeterFactory>(), TenancyTelemetryNames.MeterName, TenancyTelemetryNames.MembershipWrites);

            // A hint for a tenant the catalog does not hold breaks the foreign key; a malformed subject
            // never reaches the catalog.
            await host.Directory.RecordAsync([Hint(4242, "alice", true)], Token);
            await host.Directory.RecordAsync([Hint(4242, "ålice", true)], Token);

            List<Microsoft.Extensions.Logging.Testing.FakeLogRecord> failures = [.. host.Logs.GetSnapshot().Where(static record => record.Id.Name == "MembershipHintWriteFailed")];
            Assert.Equal(2, failures.Count);
            Assert.All(failures, static record => Assert.Equal(LogLevel.Warning, record.Level));
            Assert.All(writes.GetMeasurementSnapshot(), static m => Assert.Equal(("failed", "post_commit"), ((string)m.Tags["outcome"]!, (string)m.Tags["source"]!)));
            Assert.Equal(2, writes.GetMeasurementSnapshot().Count);
            Assert.Equal(0, await SqlServerFixture.ScalarAsync<int>(host.ConnectionString, "SELECT COUNT(*) FROM [catalog].[TenantMemberships];"));
        }

        [Fact]
        public async Task Reconciliation_rebuilds_one_tenants_hints_and_leaves_every_other_tenants_alone()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "members_reconcile");
            int[] ids = await RegisterAsync(host, 2);
            await host.Directory.RecordAsync([Hint(ids[0], "alice", true), Hint(ids[0], "bob", true), Hint(ids[1], "bob", true)], Token);

            int changed = await host.Directory.ReconcileAsync(ids[0], [Hint(ids[0], "alice", false), Hint(ids[0], "carol", true)], Token);

            // alice updated, carol inserted, bob deleted from the first tenant only.
            Assert.Equal(3, changed);
            Assert.Equal([(ids[0], false)], (await host.Directory.ListAsync("alice", Token)).Select(static m => (m.Tenant.Id, m.IsActive)));
            Assert.Equal([ids[0]], (await host.Directory.ListAsync("carol", Token)).Select(static m => m.Tenant.Id));
            Assert.Equal([ids[1]], (await host.Directory.ListAsync("bob", Token)).Select(static m => m.Tenant.Id));
        }

        [Fact]
        public async Task Reconciling_a_tenant_with_no_users_clears_its_hints_alone()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "members_clear");
            int[] ids = await RegisterAsync(host, 2);
            await host.Directory.RecordAsync([Hint(ids[0], "alice", true), Hint(ids[1], "alice", true)], Token);

            Assert.Equal(1, await host.Directory.ReconcileAsync(ids[0], [], Token));

            Assert.Equal([ids[1]], (await host.Directory.ListAsync("alice", Token)).Select(static m => m.Tenant.Id));
        }

        [Fact]
        public async Task A_reconciliation_carrying_another_tenants_record_is_refused_before_it_writes()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "members_cross");
            int[] ids = await RegisterAsync(host, 2);
            await host.Directory.RecordAsync([Hint(ids[1], "alice", true)], Token);

            _ = await Assert.ThrowsAsync<ArgumentException>(() => host.Directory.ReconcileAsync(ids[0], [Hint(ids[1], "mallory", true)], Token));

            Assert.Equal([ids[1]], (await host.Directory.ListAsync("alice", Token)).Select(static m => m.Tenant.Id));
            Assert.Empty(await host.Directory.ListAsync("mallory", Token));
        }

        /// <summary>Registers tenants.</summary>
        /// <param name="host">The host.</param>
        /// <param name="count">How many.</param>
        /// <returns>Their ids, in order.</returns>
        private static async Task<int[]> RegisterAsync(CatalogHost host, int count)
        {
            List<int> ids = [];
            for (int i = 0; i < count; i++)
            {
                TenantDescriptor tenant = await host.Catalog.RegisterAsync(
                    new TenantRegistration($"Tenant {i}", TenantCategory.Live, null, null), "migrator", cancellationToken: Token);
                ids.Add(tenant.Id);
            }

            return [.. ids];
        }

        /// <summary>A membership hint.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="subject">The subject.</param>
        /// <param name="isActive">Whether the user is active.</param>
        /// <returns>The hint.</returns>
        private static TenantMembershipRecord Hint(int tenantId, string subject, bool isActive)
        {
            return new TenantMembershipRecord { TenantId = tenantId, Subject = subject, IsActive = isActive };
        }
    }
}
