// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Tenancy;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Tenancy
{
    /// <summary>
    ///     The tenant registry's snapshot semantics over a fake catalog and clock: probe and reload,
    ///     inequality of versions, single flight, listeners, the staleness bound, refused rows and
    ///     the startup load.
    /// </summary>
    public class TenantRegistryTests
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task The_first_load_serves_every_sound_row_by_id()
        {
            using RegistryHarness harness = new();

            await harness.LoadAsync(FakeCatalogReader.Row(2), FakeCatalogReader.Row(1));

            Assert.Equal([1, 2], harness.TenantIds);
            Assert.True(harness.Registry.IsAvailable);
            Assert.Equal(harness.Catalog.Version, harness.Registry.SnapshotVersion);
            Assert.Equal(harness.Time.GetUtcNow(), harness.Registry.SnapshotLoadedAt);
            Assert.Equal("Tenant 1", harness.Registry.Get(1).Descriptor.Name);
            Assert.Null(harness.Registry.Find(3));
            Assert.Equal(3, Assert.Throws<TenantNotFoundException>(() => harness.Registry.Get(3)).TenantId);
        }

        [Fact]
        public async Task An_unchanged_version_is_probed_and_confirms_the_snapshot_without_a_reload()
        {
            using RegistryHarness harness = new();
            await harness.LoadAsync(FakeCatalogReader.Row(1));
            TenantInfo before = harness.Registry.Get(1);

            harness.Time.Advance(TimeSpan.FromSeconds(15));
            await harness.Registry.RefreshAsync(force: false, Token);

            Assert.Equal(1, harness.Catalog.Probes);
            Assert.Equal(1, harness.Catalog.Reloads);
            Assert.Same(before, harness.Registry.Get(1));
            Assert.Equal(harness.Time.GetUtcNow(), harness.Registry.SnapshotLoadedAt);
        }

        [Fact]
        public async Task A_changed_version_reloads_the_snapshot()
        {
            using RegistryHarness harness = new();
            await harness.LoadAsync(FakeCatalogReader.Row(1));

            harness.Catalog.Write(FakeCatalogReader.Row(1), FakeCatalogReader.Row(2, "Suspended"));
            await harness.Registry.RefreshAsync(force: false, Token);

            Assert.Equal(2, harness.Catalog.Reloads);
            Assert.Equal([1, 2], harness.TenantIds);
            Assert.Equal(TenantState.Suspended, harness.Registry.Get(2).Descriptor.State);
        }

        [Fact]
        public async Task An_older_version_reloads_too_because_versions_compare_for_inequality()
        {
            // A catalog restored from a backup carries an older stamp; it must still reload.
            using RegistryHarness harness = new();
            await harness.LoadAsync(FakeCatalogReader.Row(1));
            Guid older = harness.Catalog.Version;

            harness.Catalog.Write(FakeCatalogReader.Row(1), FakeCatalogReader.Row(2));
            await harness.Registry.RefreshAsync(force: false, Token);
            harness.Catalog.Write(FakeCatalogReader.Row(1));
            harness.Catalog.Version = older;
            await harness.Registry.RefreshAsync(force: false, Token);

            Assert.Equal(3, harness.Catalog.Reloads);
            Assert.Equal(older, harness.Registry.SnapshotVersion);
            Assert.Equal([1], harness.TenantIds);
        }

        [Fact]
        public async Task A_forced_refresh_reloads_even_when_the_version_is_unchanged()
        {
            using RegistryHarness harness = new();
            await harness.LoadAsync(FakeCatalogReader.Row(1));

            await harness.Registry.RefreshAsync(force: true, Token);

            Assert.Equal(0, harness.Catalog.Probes);
            Assert.Equal(2, harness.Catalog.Reloads);
        }

        [Fact]
        public async Task Concurrent_refreshes_join_the_one_in_flight()
        {
            using RegistryHarness harness = new();
            await harness.LoadAsync(FakeCatalogReader.Row(1));
            harness.Catalog.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            Task first = harness.Registry.RefreshAsync(force: false, Token);
            await harness.Catalog.AtGate.Task.WaitAsync(Token);
            Task second = harness.Registry.RefreshAsync(force: false, Token);
            Task third = harness.Registry.RefreshAsync(force: false, Token);
            harness.Catalog.Gate.SetResult();
            await Task.WhenAll(first, second, third);

            Assert.Equal(1, harness.Catalog.Probes);
        }

        [Fact]
        public async Task A_forced_refresh_asked_for_during_one_runs_after_it_and_coalesces_with_others()
        {
            using RegistryHarness harness = new();
            await harness.LoadAsync(FakeCatalogReader.Row(1));
            TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Catalog.Gate = gate;

            Task probing = harness.Registry.RefreshAsync(force: false, Token);
            await harness.Catalog.AtGate.Task.WaitAsync(Token);

            // A write commits while the probe is held; forced refreshes asked for now must not join
            // the probe, which may have read the catalog before the write. Two of them and a plain one
            // share a single queued reload.
            harness.Catalog.Write(FakeCatalogReader.Row(1), FakeCatalogReader.Row(2));
            Task forced = harness.Registry.RefreshAsync(force: true, Token);
            Task alsoForced = harness.Registry.RefreshAsync(force: true, Token);
            Task joining = harness.Registry.RefreshAsync(force: false, Token);
            harness.Catalog.Gate = null;
            gate.SetResult();
            await Task.WhenAll(probing, forced, alsoForced, joining).WaitAsync(Token);

            // The first load, the probe's own reload (the version moved), and one queued forced reload.
            Assert.Equal(1, harness.Catalog.Probes);
            Assert.Equal(3, harness.Catalog.Reloads);
            Assert.Equal([1, 2], harness.TenantIds);
        }

        [Fact]
        public async Task Listeners_hear_only_of_changed_descriptors_with_the_state_before()
        {
            RecordingListener listener = new();
            using RegistryHarness harness = new(listeners: listener);
            TenantRow unchanged = FakeCatalogReader.Row(1);
            TenantRow suspended = FakeCatalogReader.Row(2);
            TenantRow relocated = FakeCatalogReader.Row(3);
            await harness.LoadAsync(unchanged, suspended, relocated);
            Assert.Empty(listener.Calls);

            // One state change, one relocation (version moves, descriptor does not), one new tenant,
            // one deleted tenant.
            harness.Catalog.Update(suspended with { State = "Suspended" });
            harness.Catalog.Update(relocated with { Database = "tenant-3-moved" });
            harness.Catalog.Update(FakeCatalogReader.Row(4, "Provisioning"));
            harness.Catalog.Remove(1);
            await harness.Registry.RefreshAsync(force: false, Token);
            await harness.DeliveredAsync();

            (TenantDescriptor Tenant, TenantState? Previous)[] calls = [.. listener.Calls];
            Assert.Equal(2, calls.Length);
            Assert.Equal((2, TenantState.Suspended, (TenantState?)TenantState.Active), (calls[0].Tenant.Id, calls[0].Tenant.State, calls[0].Previous));
            Assert.Equal((4, TenantState.Provisioning, null), (calls[1].Tenant.Id, calls[1].Tenant.State, calls[1].Previous));
            Assert.Equal("tenant-3-moved", harness.Registry.Get(3).Location.Database);
            Assert.Equal(1, Assert.Single(listener.Removals).Id);
            LogContains(harness, "TenantSnapshotReloaded", LogLevel.Information, "3 descriptor(s) changed");
        }

        [Fact]
        public async Task A_failing_listener_is_logged_and_the_others_still_hear()
        {
            RecordingListener failing = new(throws: true);
            RecordingListener healthy = new();
            using RegistryHarness harness = new(listeners: [failing, healthy]);
            TenantRow row = FakeCatalogReader.Row(1);
            await harness.LoadAsync(row);

            harness.Catalog.Update(row with { State = "ReadOnly" });
            await harness.Registry.RefreshAsync(force: false, Token);
            await harness.DeliveredAsync();

            Assert.Single(failing.Calls);
            Assert.Single(healthy.Calls);
            Assert.Equal(LogLevel.Error, Assert.Single(harness.Events("TenantStateListenerFailed")).Level);
        }

        [Fact]
        public async Task A_listener_that_returns_no_task_is_logged_and_the_others_still_hear()
        {
            ScriptedListener empty = new(static () => null!);
            RecordingListener healthy = new();
            using RegistryHarness harness = new(listeners: [empty, healthy]);
            TenantRow row = FakeCatalogReader.Row(1);
            await harness.LoadAsync(row);

            harness.Catalog.Update(row with { State = "ReadOnly" });
            await harness.Registry.RefreshAsync(force: false, Token);
            await harness.DeliveredAsync();

            Assert.Equal(1, empty.Calls);
            Assert.Single(healthy.Calls);
            Assert.Equal(LogLevel.Error, Assert.Single(harness.Events("TenantStateListenerFailed")).Level);
        }

        [Fact]
        public async Task A_listener_failing_with_its_own_timeout_is_logged_as_failed_not_timed_out()
        {
            ScriptedListener timing = new(static () => Task.FromException(new TimeoutException("The listener's own call timed out.")));
            RecordingListener healthy = new();
            using RegistryHarness harness = new(listeners: [timing, healthy]);
            TenantRow row = FakeCatalogReader.Row(1);
            await harness.LoadAsync(row);

            harness.Catalog.Update(row with { State = "ReadOnly" });
            await harness.Registry.RefreshAsync(force: false, Token);
            await harness.DeliveredAsync();

            Assert.Single(healthy.Calls);
            Assert.IsType<TimeoutException>(Assert.Single(harness.Events("TenantStateListenerFailed")).Exception);
            Assert.Empty(harness.Events("TenantStateListenerTimedOut"));
        }

        [Fact]
        public async Task The_listener_bound_starts_before_the_listener_runs()
        {
            // The listener spends its whole bound before it hands back a task that never ends. The
            // bound expires only if its timer already existed when the clock moved.
            RegistryHarness? built = null;
            TaskCompletionSource never = new(TaskCreationOptions.RunContinuationsAsynchronously);
            ScriptedListener slow = new(() =>
            {
                built!.Time.Advance(TimeSpan.FromSeconds(5));
                return never.Task;
            });
            RecordingListener after = new();
            using RegistryHarness harness = new(listenerTimeout: TimeSpan.FromSeconds(5), listeners: [slow, after]);
            built = harness;
            TenantRow row = FakeCatalogReader.Row(1);
            await harness.LoadAsync(row);

            harness.Catalog.Update(row with { State = "Suspended" });
            await harness.Registry.RefreshAsync(force: false, Token);
            await harness.DeliveredAsync();

            Assert.Single(after.Calls);
            Assert.Equal(LogLevel.Error, Assert.Single(harness.Events("TenantStateListenerTimedOut")).Level);
            never.SetResult();
        }

        [Fact]
        public async Task A_tenant_that_leaves_the_snapshot_by_breaking_a_rule_is_announced_as_removed()
        {
            RecordingListener listener = new();
            using RegistryHarness harness = new(listeners: listener);
            TenantRow row = FakeCatalogReader.Row(1);
            await harness.LoadAsync(row, FakeCatalogReader.Row(2));

            // The operator cuts the tenant's host off: the row stays, the tenant leaves.
            harness.Catalog.Update(row with { Server = "retired.example.com" });
            await harness.Registry.RefreshAsync(force: false, Token);
            await harness.DeliveredAsync();

            Assert.Equal([2], harness.TenantIds);
            TenantDescriptor removed = Assert.Single(listener.Removals);
            Assert.Equal((1, TenantState.Active), (removed.Id, removed.State));
            Assert.Empty(listener.Calls);
        }

        [Fact]
        public async Task A_hung_listener_never_holds_the_refresh_and_times_out_into_the_next_notification()
        {
            HangingListener hung = new();
            RecordingListener after = new();
            using RegistryHarness harness = new(listenerTimeout: TimeSpan.FromSeconds(5), listeners: [hung, after]);
            TenantRow row = FakeCatalogReader.Row(1);
            await harness.LoadAsync(row);

            harness.Catalog.Update(row with { State = "Suspended" });
            await harness.Registry.RefreshAsync(force: false, Token);
            await hung.Entered.Task.WaitAsync(Token);

            // The flight has ended: the next probe confirms the snapshot while the listener hangs.
            harness.Time.Advance(TimeSpan.FromSeconds(1));
            await harness.Registry.RefreshAsync(force: false, Token).WaitAsync(TimeSpan.FromSeconds(10), Token);
            Assert.Equal(harness.Time.GetUtcNow(), harness.Registry.SnapshotLoadedAt);
            Assert.Empty(after.Calls);

            // Past the bound the call is logged and the next listener hears.
            harness.Time.Advance(TimeSpan.FromSeconds(5));
            await harness.DeliveredAsync();
            Assert.Single(after.Calls);
            Assert.Equal(LogLevel.Error, Assert.Single(harness.Events("TenantStateListenerTimedOut")).Level);
            hung.Release.SetResult();
        }

        [Fact]
        public async Task A_listener_that_refreshes_the_registry_does_not_deadlock()
        {
            RegistryHarness? built = null;
            ReentrantListener reentrant = new(() => built!.Registry);
            using RegistryHarness harness = new(listeners: reentrant);
            built = harness;
            TenantRow row = FakeCatalogReader.Row(1);
            await harness.LoadAsync(row);

            harness.Catalog.Update(row with { State = "ReadOnly" });
            await harness.Registry.RefreshAsync(force: false, Token);

            await reentrant.Refreshed.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            await harness.DeliveredAsync();
            Assert.Equal(3, harness.Catalog.Reloads);
        }

        [Fact]
        public async Task Every_listener_call_runs_in_a_scope_of_its_own()
        {
            List<ScopedListener> seen = [];
            using RegistryHarness harness = new(configureServices: services => services
                .AddSingleton(seen)
                .AddScoped<ITenantStateListener, ScopedListener>());
            TenantRow first = FakeCatalogReader.Row(1);
            TenantRow second = FakeCatalogReader.Row(2);
            await harness.LoadAsync(first, second);

            harness.Catalog.Update(first with { State = "ReadOnly" });
            harness.Catalog.Update(second with { State = "ReadOnly" });
            await harness.Registry.RefreshAsync(force: false, Token);
            await harness.DeliveredAsync();

            Assert.Equal(2, seen.Count);
            Assert.NotSame(seen[0], seen[1]);
        }

        [Fact]
        public async Task A_reloaded_profile_that_breaks_the_connection_rules_carries_no_tenant()
        {
            using RegistryHarness harness = new();
            await harness.LoadAsync(FakeCatalogReader.Row(1));

            // A configuration reload delivers an unencrypted profile outside Development.
            TellmaSqlOptions changed = new();
            changed.Profiles["default"] = "Authentication=Active Directory Managed Identity;Encrypt=False;Max Pool Size=20";
            changed.AllowedServers.Add(".database.windows.net");
            harness.Sql.Change(changed);

            // The reader counts a reload when it is asked, before the new snapshot is swapped in;
            // the reload's log line comes after the swap.
            await WaitUntilAsync(() => harness.Events("TenantSnapshotReloaded").Count == 2);

            Assert.Equal(2, harness.Catalog.Reloads);
            Assert.Empty(harness.TenantIds);
            Assert.Contains(harness.Events("TenantProfileRefused"), static record => record.Level == LogLevel.Error);
        }

        [Fact]
        public async Task A_failed_refresh_keeps_serving_within_the_staleness_bound_and_fails_closed_beyond_it()
        {
            using RegistryHarness harness = new();
            await harness.LoadAsync(FakeCatalogReader.Row(1));
            harness.Catalog.Failure = new InvalidOperationException("The catalog is unreachable.");

            // Within the bound: served, logged at Warning.
            harness.Time.Advance(TimeSpan.FromMinutes(4));
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Registry.RefreshAsync(force: false, Token));
            Assert.NotNull(harness.Registry.Find(1));
            Assert.True(harness.Registry.IsAvailable);
            Assert.Equal(LogLevel.Warning, Assert.Single(harness.Events("TenantSnapshotRefreshFailed")).Level);

            // Beyond it: every lookup refuses, logged at Error.
            harness.Time.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Registry.RefreshAsync(force: false, Token));
            TenantUnavailableException refused = Assert.Throws<TenantUnavailableException>(() => harness.Registry.Find(1));
            Assert.Equal(TenantProblemCodes.CatalogUnavailable, refused.Code);
            Assert.Equal(TimeSpan.FromSeconds(15), refused.RetryAfter);
            Assert.Null(refused.State);
            _ = Assert.Throws<TenantUnavailableException>(() => harness.Registry.Get(1));
            Assert.False(harness.Registry.IsAvailable);
            Assert.Equal(LogLevel.Error, Assert.Single(harness.Events("TenantSnapshotRefreshFailedStale")).Level);

            // The catalog answers again: served again.
            harness.Catalog.Failure = null;
            await harness.Registry.RefreshAsync(force: false, Token);
            Assert.NotNull(harness.Registry.Find(1));
        }

        [Fact]
        public void A_snapshot_never_loaded_refuses_every_lookup()
        {
            using RegistryHarness harness = new();

            Assert.Equal(TenantProblemCodes.CatalogUnavailable, Assert.Throws<TenantUnavailableException>(() => harness.Registry.Find(1)).Code);
            Assert.Empty(harness.Registry.Tenants);
            Assert.Equal(Guid.Empty, harness.Registry.SnapshotVersion);
            Assert.Null(harness.Registry.SnapshotLoadedAt);
            Assert.False(harness.Registry.IsAvailable);
        }

        [Fact]
        public async Task A_row_outside_the_allowed_servers_stays_out_of_the_snapshot_and_is_logged_at_error()
        {
            using RegistryHarness harness = new();

            await harness.LoadAsync(
                FakeCatalogReader.Row(1),
                FakeCatalogReader.Row(2, server: "attacker.example.com"),
                FakeCatalogReader.Row(3, server: "evil.database.windows.net.attacker.example.com"),
                FakeCatalogReader.Row(4, server: "database.windows.net"));

            Assert.Equal([1], harness.TenantIds);
            Assert.Null(harness.Registry.Find(2));
            Assert.Equal(3, Assert.Throws<TenantNotFoundException>(() => harness.Registry.Get(3)).TenantId);
            List<Microsoft.Extensions.Logging.Testing.FakeLogRecord> refused = harness.Events("TenantServerRefused");
            Assert.Equal(3, refused.Count);
            Assert.All(refused, static record => Assert.Equal(LogLevel.Error, record.Level));
            Assert.Contains("attacker.example.com", refused[0].Message, StringComparison.Ordinal);
            Assert.Contains("Tenant 2", refused[0].Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Rows_with_an_unknown_profile_a_malformed_database_or_an_unknown_state_stay_out()
        {
            using RegistryHarness harness = new();

            await harness.LoadAsync(
                FakeCatalogReader.Row(1),
                FakeCatalogReader.Row(2, profile: "missing"),
                FakeCatalogReader.Row(3, database: "tenant;Password=x"),
                FakeCatalogReader.Row(4, state: "Hibernating"),
                FakeCatalogReader.Row(5, category: "2"));

            Assert.Equal([1], harness.TenantIds);
            Assert.Single(harness.Events("TenantProfileRefused"));
            Assert.Single(harness.Events("TenantDatabaseRefused"));
            Assert.Equal(2, harness.Events("TenantRowUnknown").Count);
            Assert.Equal([2, 3, 4, 5], harness.Registry.Refused.Select(static refused => refused.Row.Id));
        }

        [Fact]
        public async Task In_Development_an_empty_allowed_servers_list_allows_every_host()
        {
            using RegistryHarness harness = new(static sql => sql.AllowedServers.Clear(), isDevelopment: true);

            await harness.LoadAsync(FakeCatalogReader.Row(1, server: "(localdb)\\MSSQLLocalDB"));

            Assert.Equal([1], harness.TenantIds);
        }

        [Fact]
        public async Task Outside_Development_an_empty_allowed_servers_list_allows_no_host()
        {
            using RegistryHarness harness = new(static sql => sql.AllowedServers.Clear());

            await harness.LoadAsync(FakeCatalogReader.Row(1));

            Assert.Empty(harness.TenantIds);
        }

        [Fact]
        public async Task More_warm_tenants_than_the_bound_warn_after_the_reload_without_failing_it()
        {
            using RegistryHarness harness = new(static sql =>
            {
                sql.Profiles["warm"] = "Authentication=Active Directory Managed Identity;Encrypt=True;Max Pool Size=20;Min Pool Size=4";
                sql.MaxWarmTenants = 1;
            });

            await harness.LoadAsync(FakeCatalogReader.Row(1, profile: "warm"), FakeCatalogReader.Row(2, profile: "warm"), FakeCatalogReader.Row(3));

            Assert.Equal([1, 2, 3], harness.TenantIds);
            Microsoft.Extensions.Logging.Testing.FakeLogRecord warning = Assert.Single(harness.Events("WarmTenantsExceeded"));
            Assert.Equal(LogLevel.Warning, warning.Level);
            Assert.Contains("2 tenants", warning.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_changed_profile_recomposes_every_string_at_once()
        {
            using RegistryHarness harness = new();
            await harness.LoadAsync(FakeCatalogReader.Row(1));
            Assert.Contains("Max Pool Size=20", harness.Registry.Get(1).ConnectionString, StringComparison.Ordinal);

            TellmaSqlOptions changed = new();
            changed.Profiles["default"] = "Authentication=Active Directory Managed Identity;Encrypt=True;Max Pool Size=12";
            changed.AllowedServers.Add(".database.windows.net");
            harness.Sql.Change(changed);
            await WaitUntilAsync(() => harness.Catalog.Reloads == 2 && harness.Registry.Get(1).ConnectionString.Contains("Max Pool Size=12", StringComparison.Ordinal));
        }

        [Fact]
        public async Task A_catalog_behind_the_platform_fails_startup_naming_the_migrate_command()
        {
            using RegistryHarness harness = new();
            harness.Catalog.History = CatalogHistoryState.Behind;

            TellmaCompositionException failure = await Assert.ThrowsAsync<TellmaCompositionException>(() => harness.Registry.InitializeAsync(Token));

            CompositionProblem problem = Assert.Single(failure.Problems);
            Assert.Equal("core.catalog", problem.Source);
            Assert.Contains(CatalogMigrations.Latest, problem.Problem, StringComparison.Ordinal);
            Assert.Contains("dotnet run --project distributions/acme/src/Tellma.Distro.Acme.Migrator -- migrate", problem.Fix, StringComparison.Ordinal);
            Assert.Equal(0, harness.Catalog.Reloads);
        }

        [Fact]
        public async Task A_catalog_never_migrated_fails_startup()
        {
            using RegistryHarness harness = new();
            harness.Catalog.History = CatalogHistoryState.MissingSchema;

            TellmaCompositionException failure = await Assert.ThrowsAsync<TellmaCompositionException>(() => harness.Registry.InitializeAsync(Token));

            Assert.Contains("never migrated", Assert.Single(failure.Problems).Problem, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_missing_catalog_database_fails_startup_in_Development_with_the_migrate_command()
        {
            using RegistryHarness harness = new(isDevelopment: true);
            harness.Catalog.History = CatalogHistoryState.MissingDatabase;

            TellmaCompositionException failure = await Assert.ThrowsAsync<TellmaCompositionException>(() => harness.Registry.InitializeAsync(Token));

            Assert.Contains("-- migrate", Assert.Single(failure.Problems).Fix, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_startup_without_the_first_load_reads_nothing_until_a_forced_refresh()
        {
            using RegistryHarness harness = new(loadOnStart: false);
            harness.Catalog.Write(FakeCatalogReader.Row(1));

            await harness.Registry.InitializeAsync(Token);

            Assert.Equal(0, harness.Catalog.HistoryReads);
            Assert.Equal(0, harness.Catalog.Reloads);
            Assert.False(harness.Registry.IsAvailable);

            await harness.Registry.RefreshAsync(force: true, Token);

            Assert.True(harness.Registry.IsAvailable);
            Assert.Equal([1], harness.TenantIds);
        }

        [Fact]
        public async Task A_catalog_database_that_cannot_be_opened_outside_Development_starts_the_host_not_ready()
        {
            using RegistryHarness harness = new();
            harness.Catalog.History = CatalogHistoryState.MissingDatabase;

            await harness.Registry.InitializeAsync(Token);

            Assert.False(harness.Registry.IsAvailable);
            Assert.Equal(LogLevel.Error, Assert.Single(harness.Events("CatalogDatabaseUnavailable")).Level);
        }

        [Fact]
        public async Task An_unreachable_catalog_starts_the_host_not_ready_and_a_later_refresh_verifies_and_loads()
        {
            using RegistryHarness harness = new();
            harness.Catalog.Failure = new InvalidOperationException("The catalog is unreachable.");

            await harness.Registry.InitializeAsync(Token);
            Assert.False(harness.Registry.IsAvailable);
            Assert.Single(harness.Events("TenantSnapshotFirstLoadFailed"));

            harness.Catalog.Failure = null;
            harness.Catalog.Write(FakeCatalogReader.Row(1));
            await harness.Registry.RefreshAsync(force: false, Token);

            Assert.Equal(2, harness.Catalog.HistoryReads);
            Assert.Equal([1], harness.TenantIds);
        }

        [Fact]
        public async Task A_catalog_found_behind_after_startup_serves_nothing_and_says_how_to_migrate()
        {
            using RegistryHarness harness = new();
            harness.Catalog.Failure = new InvalidOperationException("The catalog is unreachable.");
            await harness.Registry.InitializeAsync(Token);

            harness.Catalog.Failure = null;
            harness.Catalog.History = CatalogHistoryState.Behind;
            _ = await Assert.ThrowsAsync<CatalogNotMigratedException>(() => harness.Registry.RefreshAsync(force: false, Token));

            Assert.False(harness.Registry.IsAvailable);
            Assert.Contains("-- migrate", Assert.Single(harness.Events("CatalogBehind")).Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task The_first_load_is_bounded_and_the_host_starts_without_it()
        {
            using RegistryHarness harness = new();
            harness.Catalog.Write(FakeCatalogReader.Row(1));
            harness.Catalog.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            Task initializing = harness.Registry.InitializeAsync(Token);
            await harness.Catalog.AtGate.Task.WaitAsync(Token);
            harness.Time.Advance(TenantRegistryStartupOptions.DefaultFirstLoadTimeout);
            await initializing.WaitAsync(Token);

            Assert.False(harness.Registry.IsAvailable);
            Assert.Single(harness.Events("TenantSnapshotFirstLoadFailed"));
            harness.Catalog.Gate.SetResult();
        }

        [Fact]
        public async Task The_migrators_registry_skips_the_catalog_verification()
        {
            using RegistryHarness harness = new(verifyCatalog: false);
            harness.Catalog.History = CatalogHistoryState.MissingSchema;

            await harness.LoadAsync(FakeCatalogReader.Row(1));
            await harness.Registry.RefreshAsync(force: false, Token);

            Assert.Equal(0, harness.Catalog.HistoryReads);
            Assert.Equal([1], harness.TenantIds);
        }

        [Fact]
        public async Task Refreshes_are_measured_by_outcome_and_the_snapshot_by_category_and_state()
        {
            using RegistryHarness harness = new();
            using MetricCollector<double> durations = new(harness.MeterFactory, TenancyTelemetryNames.MeterName, TenancyTelemetryNames.CatalogRefreshDuration);
            using MetricCollector<int> tenants = new(harness.MeterFactory, TenancyTelemetryNames.MeterName, TenancyTelemetryNames.CatalogTenants);
            using MetricCollector<double> age = new(harness.MeterFactory, TenancyTelemetryNames.MeterName, TenancyTelemetryNames.CatalogSnapshotAge);

            await harness.LoadAsync(FakeCatalogReader.Row(1), FakeCatalogReader.Row(2), FakeCatalogReader.Row(3, "Suspended", category: "Sandbox"));
            await harness.Registry.RefreshAsync(force: false, Token);
            harness.Catalog.Failure = new InvalidOperationException("The catalog is unreachable.");
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Registry.RefreshAsync(force: false, Token));

            string[] outcomes = [.. durations.GetMeasurementSnapshot().Select(static m => (string)m.Tags[TenancyTelemetryNames.OutcomeTag]!)];
            Assert.Equal(["reloaded", "unchanged", "failed"], outcomes);

            tenants.RecordObservableInstruments();
            List<(string, string, int)> counted = [.. tenants.GetMeasurementSnapshot().Select(static m => ((string)m.Tags[TenancyTelemetryNames.CategoryTag]!, (string)m.Tags[TenancyTelemetryNames.StateTag]!, m.Value))];
            Assert.Equal([("Live", "Active", 2), ("Sandbox", "Suspended", 1)], counted);

            harness.Time.Advance(TimeSpan.FromSeconds(42));
            age.RecordObservableInstruments();
            Assert.Equal(42, age.LastMeasurement!.Value);
            Assert.All(durations.GetMeasurementSnapshot(), static m => Assert.Single(m.Tags));
        }

        [Fact]
        public async Task No_log_line_carries_a_composed_string_or_a_profile_value()
        {
            using RegistryHarness harness = new(static sql =>
            {
                sql.AllowSqlPassword = true;
                sql.Profiles["sql-login"] = "User Id=tellma_web;Password=hunter2-secret;Encrypt=True;Max Pool Size=20";
                sql.MaxWarmTenants = 0;
                sql.Profiles["warm"] = "Integrated Security=true;Encrypt=True;Max Pool Size=20;Min Pool Size=1";
            });

            await harness.LoadAsync(
                FakeCatalogReader.Row(1, profile: "sql-login"),
                FakeCatalogReader.Row(2, profile: "warm"),
                FakeCatalogReader.Row(3, server: "attacker.example.com", profile: "sql-login"));
            harness.Catalog.Failure = new InvalidOperationException("The catalog is unreachable.");
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Registry.RefreshAsync(force: false, Token));

            Assert.Contains("hunter2-secret", harness.Registry.Get(1).ConnectionString, StringComparison.Ordinal);
            Assert.NotEmpty(harness.Logger.Collector.GetSnapshot());
            Assert.DoesNotContain("hunter2", harness.AllLogText, StringComparison.Ordinal);
            Assert.DoesNotContain("Password", harness.AllLogText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hunter2", harness.Registry.Get(1).ToString(), StringComparison.Ordinal);
        }

        /// <summary>Waits until a condition holds, for work that finishes on another thread.</summary>
        /// <param name="condition">The condition.</param>
        /// <returns>A task that completes when it holds.</returns>
        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (!condition())
            {
                await Task.Delay(10, timeout.Token);
            }
        }

        /// <summary>Asserts that one record of an event was logged at a level and carries a text.</summary>
        /// <param name="harness">The harness.</param>
        /// <param name="eventName">The event.</param>
        /// <param name="level">The level.</param>
        /// <param name="text">The text.</param>
        private static void LogContains(RegistryHarness harness, string eventName, LogLevel level, string text)
        {
            Microsoft.Extensions.Logging.Testing.FakeLogRecord record = harness.Events(eventName)[^1];
            Assert.Equal(level, record.Level);
            Assert.Contains(text, record.Message, StringComparison.Ordinal);
        }
    }
}
