// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Settings;
using Tellma.Core.Tenancy;

namespace Tellma.Core.IntegrationTests.Access
{
    /// <summary>
    ///     The connect prologue, its caches and the guarded runner against real tenant databases:
    ///     a cold connect and the warm batches after it, each stale part refreshed in the round trip
    ///     that found it, the refusals, the bookkeeping writes, and two tenants that reuse each
    ///     other's ids yet never share an entry.
    /// </summary>
    /// <param name="fixture">The two tenant databases and their composition.</param>
    [Trait("Category", "Integration")]
    public sealed class UserConnectionTests(ConnectDatabase fixture) : IClassFixture<ConnectDatabase>, IDisposable
    {
        /// <summary>The prologue round trips, one measurement each, tagged by variant.</summary>
        private readonly MetricCollector<double> _prologues = new(
            fixture.Services.GetRequiredService<IMeterFactory>(), AccessTelemetryNames.MeterName, AccessTelemetryNames.ConnectPrologueDuration);

        /// <summary>The connect results, tagged by result.</summary>
        private readonly MetricCollector<long> _results = new(
            fixture.Services.GetRequiredService<IMeterFactory>(), AccessTelemetryNames.MeterName, AccessTelemetryNames.ConnectResults);

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public void Dispose()
        {
            _prologues.Dispose();
            _results.Dispose();
        }

        // --- Cold, then warm ---

        [Fact]
        public async Task A_cold_connect_reads_the_user_in_one_round_trip_and_a_warm_batch_costs_none_more()
        {
            (int userId, string subject) = await SeedAsync("Ada");

            // --- Cold: one prologue-only round trip returns the user, the rows, the profile and the settings ---
            await using (AsyncServiceScope cold = await ScopeAsync(subject))
            {
                ConnectedUser user = await CurrentAsync(cold);
                Assert.Equal(userId, user.UserId);
                Assert.Equal(UserKind.Individual, user.Kind);
                Assert.Equal(UserState.Joined, user.State);
                Assert.Equal("Ada", user.Profile.Name);
                Assert.Equal(await TagAsync("permissions"), user.Access.Tag);
                Assert.Equal(await UserTagAsync(userId, "PermissionsTag"), user.Access.UserTag);
                Assert.Equal(await UserTagAsync(userId, "PreferencesTag"), user.PreferencesTag);
            }

            Assert.Equal([AccessTelemetryNames.VariantSubject], Variants());
            Assert.Equal([AccessTelemetryNames.ResultCold], Results());

            // --- Warm: the next request connects from the cache, and its batch carries the prologue ---
            await using AsyncServiceScope warm = await ScopeAsync(subject);
            _ = Assert.Single(Variants());
            int roundTrips = await ReadAsync(warm, static (_, outcome) => outcome.RoundTrips);

            Assert.Equal(1, roundTrips);
            Assert.Equal([AccessTelemetryNames.VariantSubject, AccessTelemetryNames.VariantSubject], Variants());
            Assert.Equal([AccessTelemetryNames.ResultCold, AccessTelemetryNames.ResultWarm], Results());
        }

        // --- Stale parts ---

        [Fact]
        public async Task A_moved_tenant_permissions_tag_fails_the_guard_once_and_the_batch_recomposes_under_the_fresh_set()
        {
            (_, string subject) = await SeedAsync("Bea");
            await WarmAsync(subject);
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, "UPDATE [core].[VersionTags] SET [Tag] = NEWID() WHERE [Name] = N'permissions';");

            await using AsyncServiceScope scope = await ScopeAsync(subject);
            ConnectedUser user = await ReadAsync(scope, static (user, _) => user);

            Assert.Equal(await TagAsync("permissions"), user.Access.Tag);
            Assert.Equal(2, Variants().Count - 2);
            Assert.Equal([AccessTelemetryNames.ResultStalePermissions, AccessTelemetryNames.ResultWarm], Results().Skip(2));
        }

        [Fact]
        public async Task A_moved_user_permissions_tag_fails_the_guard_once_and_the_batch_recomposes_under_the_fresh_set()
        {
            (int userId, string subject) = await SeedAsync("Cyd");
            await WarmAsync(subject);
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, Sql($"UPDATE [core].[UserStamps] SET [PermissionsTag] = NEWID() WHERE [UserId] = {userId};"));

            await using AsyncServiceScope scope = await ScopeAsync(subject);
            ConnectedUser user = await ReadAsync(scope, static (user, _) => user);

            Assert.Equal(await UserTagAsync(userId, "PermissionsTag"), user.Access.UserTag);
            Assert.Equal([AccessTelemetryNames.ResultStalePermissions, AccessTelemetryNames.ResultWarm], Results().Skip(2));
        }

        [Fact]
        public async Task A_stale_profile_is_refreshed_in_the_round_trip_that_found_it_and_the_body_runs()
        {
            (int userId, string subject) = await SeedAsync("Dee");
            await WarmAsync(subject);
            await fixture.ExecuteAsync(
                ConnectDatabase.TenantA,
                Sql($"UPDATE [core].[Users] SET [Name] = N'Deirdre' WHERE [Id] = {userId}; UPDATE [core].[UserStamps] SET [PreferencesTag] = NEWID() WHERE [UserId] = {userId};"));

            await using AsyncServiceScope scope = await ScopeAsync(subject);
            (ConnectedUser user, int roundTrips) = await ReadAsync(scope, static (user, outcome) => (user, outcome.RoundTrips));

            Assert.Equal("Deirdre", user.Profile.Name);
            Assert.Equal(1, roundTrips);
            Assert.Equal([AccessTelemetryNames.ResultStaleProfile], Results().Skip(2));
        }

        [Fact]
        public async Task Stale_settings_are_served_to_a_read_with_the_fresh_rows_and_refused_to_a_validation_until_recomposed()
        {
            (_, string subject) = await SeedAsync("Eve");
            await WarmAsync(subject);
            TenantSettingsStore store = fixture.Services.GetRequiredService<TenantSettingsStore>();

            // --- A read runs under the settings it composed with; the fresh rows ride the same round trip ---
            await BumpSettingsAsync("Renamed once");
            await using (AsyncServiceScope read = await ScopeAsync(subject))
            {
                await BumpSettingsAsync("Renamed twice");
                int roundTrips = await ReadAsync(read, static (_, outcome) => outcome.RoundTrips);

                Assert.Equal(1, roundTrips);
                Assert.True(store.IsCurrent(ConnectDatabase.TenantA, new VersionTag(await TagAsync("settings"))));
                Assert.Equal(AccessTelemetryNames.ResultStaleSettings, Results()[^1]);
            }

            // --- A validation refuses stale settings: one recompose, under the fresh ones ---
            await using AsyncServiceScope validate = await ScopeAsync(subject);
            await BumpSettingsAsync("Renamed thrice");
            int before = Variants().Count;
            _ = await RunAsync(validate, BatchPurpose.Validate, static (_, outcome) => outcome.RoundTrips);

            Assert.Equal(2, Variants().Count - before);
            Assert.True(store.IsCurrent(ConnectDatabase.TenantA, new VersionTag(await TagAsync("settings"))));
        }

        // --- Refusals ---

        [Fact]
        public async Task An_unknown_subject_finds_no_tenant()
        {
            _ = await Assert.ThrowsAsync<TenantNotFoundException>(() => ScopeAsync("nobody-" + Guid.NewGuid().ToString("N")));

            Assert.Equal([AccessTelemetryNames.ResultUnknownSubject], Results());
        }

        [Fact]
        public async Task A_user_deactivated_after_connecting_finds_no_tenant_on_the_next_batch_and_on_the_next_connect()
        {
            (int userId, string subject) = await SeedAsync("Fay");
            await using AsyncServiceScope connected = await ScopeAsync(subject);
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, Sql($"UPDATE [core].[Users] SET [IsActive] = 0 WHERE [Id] = {userId};"));

            _ = await Assert.ThrowsAsync<TenantNotFoundException>(() => ReadAsync(connected, static (user, _) => user));
            _ = await Assert.ThrowsAsync<TenantNotFoundException>(() => ScopeAsync(subject));

            Assert.Equal([AccessTelemetryNames.ResultCold, AccessTelemetryNames.ResultDeactivated, AccessTelemetryNames.ResultDeactivated], Results());
        }

        // --- The other variants ---

        [Fact]
        public async Task A_background_scope_connects_its_run_as_user_by_id_and_stamps_nothing()
        {
            (int userId, _) = await SeedAsync("Gil");

            await using AsyncServiceScope scope = await ScopeAsync(subject: null, userId: userId);
            ConnectedUser user = await ReadAsync(scope, static (user, _) => user);

            Assert.Equal(userId, user.UserId);
            Assert.Equal("Gil", user.Profile.Name);
            Assert.Equal([AccessTelemetryNames.VariantUser, AccessTelemetryNames.VariantUser], Variants());
            Assert.Null(await StampAsync(userId));
        }

        [Fact]
        public async Task The_system_connects_without_a_user_row_and_validates_in_one_round_trip()
        {
            await using AsyncServiceScope scope = await ScopeAsync(subject: "system", kind: PrincipalKind.System);
            (ConnectedUser user, int roundTrips) = await RunAsync(scope, BatchPurpose.Validate, static (user, outcome) => (user, outcome.RoundTrips));

            Assert.Equal(WellKnownIds.SystemUserId, user.UserId);
            Assert.True(user.Access.IsSystem);
            Assert.Equal(1, roundTrips);
            Assert.All(Variants(), static variant => Assert.Equal(AccessTelemetryNames.VariantSystem, variant));
        }

        [Fact]
        public async Task A_service_account_connects_by_its_client_id()
        {
            int userId = fixture.NextUserId();
            string clientId = string.Create(CultureInfo.InvariantCulture, $"client-{userId}");
            await fixture.SeedUserAsync(ConnectDatabase.TenantA, userId, clientId, "Robot", UserKind.Service);

            await using AsyncServiceScope scope = await ScopeAsync(clientId, kind: PrincipalKind.ServiceAccount);
            ConnectedUser user = await CurrentAsync(scope);

            Assert.Equal(userId, user.UserId);
            Assert.Equal(UserKind.Service, user.Kind);
        }

        // --- The bookkeeping writes ---

        [Fact]
        public async Task An_invited_users_first_requests_flip_the_state_exactly_once_however_many_arrive_together()
        {
            (int userId, string subject) = await SeedAsync("Hal", joined: false);

            // --- Concurrent first requests, then more after the connect cache forgot the user ---
            AsyncServiceScope[] scopes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ScopeAsync(subject)));
            ConnectedUser[] users = await Task.WhenAll(scopes.Select(CurrentAsync));
            foreach (AsyncServiceScope scope in scopes)
            {
                await scope.DisposeAsync();
            }

            fixture.Services.GetRequiredService<ConnectCache>().Remove(ConnectDatabase.TenantA, subject);
            await using (AsyncServiceScope again = await ScopeAsync(subject))
            {
                Assert.Equal(UserState.Joined, (await CurrentAsync(again)).State);
            }

            Assert.All(users, static user => Assert.Equal(UserState.Joined, user.State));
            Assert.Equal(1, await fixture.ScalarAsync<int>(ConnectDatabase.TenantA, Sql($"SELECT COUNT(*) FROM [core].[UsersHistory] WHERE [Id] = {userId};")));
            Assert.Equal(1, await fixture.ScalarAsync<int>(ConnectDatabase.TenantA, Sql($"SELECT COUNT(*) FROM [core].[Users] WHERE [Id] = {userId} AND [JoinedAt] IS NOT NULL;")));
        }

        [Fact]
        public async Task The_activity_stamp_is_written_once_per_interval_however_many_batches_run_and_adds_no_history()
        {
            (int userId, string subject) = await SeedAsync("Ida");

            await using AsyncServiceScope scope = await ScopeAsync(subject);
            DateTimeOffset? first = await StampAsync(userId);
            for (int i = 0; i < 1000; i++)
            {
                _ = await ReadAsync(scope, static (_, outcome) => outcome.RoundTrips);
            }

            // A slow run may outlast the interval, which then admits exactly the stamps it allows:
            // a later stamp is never within the interval of the first.
            DateTimeOffset? last = await StampAsync(userId);
            Assert.NotNull(first);
            Assert.NotNull(last);
            Assert.True(last == first || last - first >= TimeSpan.FromSeconds(60), $"Stamped at {first:O}, then again at {last:O}.");
            Assert.Equal(0, await fixture.ScalarAsync<int>(ConnectDatabase.TenantA, Sql($"SELECT COUNT(*) FROM [core].[UsersHistory] WHERE [Id] = {userId};")));
        }

        [Fact]
        public async Task A_background_request_stamps_no_activity()
        {
            (int userId, string subject) = await SeedAsync("Jo");

            await using AsyncServiceScope scope = await ScopeAsync(subject, background: true);
            _ = await ReadAsync(scope, static (_, outcome) => outcome.RoundTrips);

            Assert.Null(await StampAsync(userId));
        }

        [Fact]
        public async Task A_frozen_tenant_stamps_nothing_and_flips_nothing_and_a_read_only_one_does_both()
        {
            (int frozenId, string frozen) = await SeedAsync("Kit", joined: false);
            (int readOnlyId, string readOnly) = await SeedAsync("Lou", joined: false);

            await using (AsyncServiceScope scope = await ScopeAsync(frozen, state: TenantState.Frozen))
            {
                Assert.Equal(UserState.Invited, (await CurrentAsync(scope)).State);
            }

            await using (AsyncServiceScope scope = await ScopeAsync(readOnly, state: TenantState.ReadOnly))
            {
                Assert.Equal(UserState.Joined, (await CurrentAsync(scope)).State);
            }

            Assert.Null(await StampAsync(frozenId));
            Assert.NotNull(await StampAsync(readOnlyId));
        }

        // --- Two tenants ---

        [Fact]
        public async Task Two_tenants_that_reuse_an_id_and_a_subject_never_share_a_cache_entry_or_a_snapshot()
        {
            int userId = fixture.NextUserId();
            string subject = string.Create(CultureInfo.InvariantCulture, $"shared-{userId}");
            await fixture.SeedUserAsync(ConnectDatabase.TenantA, userId, subject, "In A");
            await fixture.SeedUserAsync(ConnectDatabase.TenantB, userId, subject, "In B");

            var users = new ConnectedUser[2];
            for (int round = 0; round < 2; round++)
            {
                await using AsyncServiceScope a = await ScopeAsync(subject, tenantId: ConnectDatabase.TenantA);
                await using AsyncServiceScope b = await ScopeAsync(subject, tenantId: ConnectDatabase.TenantB);
                users[0] = await ReadAsync(a, static (user, _) => user);
                users[1] = await ReadAsync(b, static (user, _) => user);
            }

            Assert.Equal("In A", users[0].Profile.Name);
            Assert.Equal("In B", users[1].Profile.Name);
            Assert.Equal(ConnectDatabase.TenantA, users[0].Access.TenantId);
            Assert.Equal(ConnectDatabase.TenantB, users[1].Access.TenantId);
            IVersionTagSnapshots snapshots = fixture.Services.GetRequiredService<IVersionTagSnapshots>();
            Assert.Equal(new VersionTag(await TagAsync("permissions", ConnectDatabase.TenantA)), snapshots.Current(ConnectDatabase.TenantA)[VersionTagNames.Permissions]);
            Assert.Equal(new VersionTag(await TagAsync("permissions", ConnectDatabase.TenantB)), snapshots.Current(ConnectDatabase.TenantB)[VersionTagNames.Permissions]);
            Assert.NotEqual(users[0].Access.Tag, users[1].Access.Tag);
        }

        // --- Helpers ---

        /// <summary>Formats a statement in the invariant culture.</summary>
        /// <param name="sql">The statement.</param>
        /// <returns>The text.</returns>
        private static string Sql(FormattableString sql)
        {
            return sql.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The scope's connected user, with no round trip.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The user.</returns>
        private static Task<ConnectedUser> CurrentAsync(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<IScopeConnection>().CurrentAsync(Token);
        }

        /// <summary>Runs a guarded batch of one trivial statement.</summary>
        /// <typeparam name="T">The read's type.</typeparam>
        /// <param name="scope">The scope.</param>
        /// <param name="purpose">The purpose.</param>
        /// <param name="read">Reads the result.</param>
        /// <returns>The read.</returns>
        private static Task<T> RunAsync<T>(AsyncServiceScope scope, BatchPurpose purpose, Func<ConnectedUser, BatchOutcome, T> read)
        {
            return scope.ServiceProvider.GetRequiredService<IGuardedBatchRunner>().RunAsync(
                purpose,
                static (user, batch) =>
                {
                    _ = batch.Sql($"SELECT 1 AS [One];", new SqlOptions(resultSets: 1));
                    return ValueTask.CompletedTask;
                },
                read,
                Token);
        }

        /// <summary>Runs a guarded read of one trivial statement.</summary>
        /// <typeparam name="T">The read's type.</typeparam>
        /// <param name="scope">The scope.</param>
        /// <param name="read">Reads the result.</param>
        /// <returns>The read.</returns>
        private static Task<T> ReadAsync<T>(AsyncServiceScope scope, Func<ConnectedUser, BatchOutcome, T> read)
        {
            return RunAsync(scope, BatchPurpose.Read, read);
        }

        /// <summary>
        ///     A scope of a tenant whose context names a caller, completed by the initializers as a
        ///     request's is: the connection at 100, then the locale negotiation.
        /// </summary>
        /// <param name="subject">The caller's subject; null for a background scope that carries a user id.</param>
        /// <param name="userId">The run-as user of a background scope; null for a request.</param>
        /// <param name="kind">The principal's kind.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="state">The state the request sees the tenant in.</param>
        /// <param name="background">Whether the request is a background one that stamps no activity.</param>
        /// <returns>The scope.</returns>
        private async Task<AsyncServiceScope> ScopeAsync(
            string? subject,
            int? userId = null,
            PrincipalKind kind = PrincipalKind.User,
            int tenantId = ConnectDatabase.TenantA,
            TenantState state = TenantState.Active,
            bool background = false)
        {
            AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
            try
            {
                IRequestContextHolder holder = scope.ServiceProvider.GetRequiredService<IRequestContextHolder>();
                RequestContext context = holder.Current with { Tenant = ConnectDatabase.Descriptor(tenantId, state), Kind = kind, Subject = subject, UserId = userId };
                holder.Set(context);
                _ = await RequestContextInitialization.RunAsync(
                    holder,
                    scope.ServiceProvider.GetServices<IRequestContextInitializer>(),
                    context,
                    new RequestContextInputs(null, null, null, null, null, null, SuppressActivityStamp: background),
                    Token);
                return scope;
            }
            catch
            {
                await scope.DisposeAsync();
                throw;
            }
        }

        /// <summary>Seeds a user of the first tenant under a fresh id and subject.</summary>
        /// <param name="name">The user's name.</param>
        /// <param name="joined">Whether the user has joined.</param>
        /// <returns>The id and the subject.</returns>
        private async Task<(int UserId, string Subject)> SeedAsync(string name, bool joined = true)
        {
            int userId = fixture.NextUserId();
            string subject = string.Create(CultureInfo.InvariantCulture, $"sub-{userId}");
            await fixture.SeedUserAsync(ConnectDatabase.TenantA, userId, subject, name, joined: joined);
            return (userId, subject);
        }

        /// <summary>Connects a subject and runs one batch for it, so the next scope starts warm.</summary>
        /// <param name="subject">The subject.</param>
        /// <returns>A task that completes when the caches are warm.</returns>
        private async Task WarmAsync(string subject)
        {
            await using AsyncServiceScope scope = await ScopeAsync(subject);
            _ = await ReadAsync(scope, static (_, outcome) => outcome.RoundTrips);
        }

        /// <summary>Renames the first tenant and moves its settings tag, as a settings save does.</summary>
        /// <param name="name">The new name.</param>
        /// <returns>A task that completes when the save committed.</returns>
        private Task BumpSettingsAsync(string name)
        {
            return fixture.ExecuteAsync(
                ConnectDatabase.TenantA,
                $"UPDATE [core].[Settings] SET [Name] = N'{name}'; UPDATE [core].[VersionTags] SET [Tag] = NEWID() WHERE [Name] = N'settings';");
        }

        /// <summary>A tenant-level tag as stored.</summary>
        /// <param name="name">The tag's name.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The tag.</returns>
        private async Task<Guid> TagAsync(string name, int tenantId = ConnectDatabase.TenantA)
        {
            return await fixture.ScalarAsync<Guid>(tenantId, $"SELECT [Tag] FROM [core].[VersionTags] WHERE [Name] = N'{name}';");
        }

        /// <summary>A user's own tag as stored.</summary>
        /// <param name="userId">The user.</param>
        /// <param name="column">The stamp column.</param>
        /// <returns>The tag.</returns>
        private async Task<Guid> UserTagAsync(int userId, string column)
        {
            return await fixture.ScalarAsync<Guid>(ConnectDatabase.TenantA, Sql($"SELECT [{column}] FROM [core].[UserStamps] WHERE [UserId] = {userId};"));
        }

        /// <summary>A user's activity stamp as stored.</summary>
        /// <param name="userId">The user.</param>
        /// <returns>The stamp; null when never stamped.</returns>
        private Task<DateTimeOffset?> StampAsync(int userId)
        {
            return fixture.ScalarAsync<DateTimeOffset?>(ConnectDatabase.TenantA, Sql($"SELECT [LastActiveAt] FROM [core].[UserStamps] WHERE [UserId] = {userId};"));
        }

        /// <summary>The variant of every prologue round trip measured so far, in order.</summary>
        /// <returns>The variants.</returns>
        private List<string> Variants()
        {
            return [.. _prologues.GetMeasurementSnapshot().Select(static measurement => (string)measurement.Tags[AccessTelemetryNames.VariantTag]!)];
        }

        /// <summary>Every connect result counted so far, in order.</summary>
        /// <returns>The results.</returns>
        private List<string> Results()
        {
            return [.. _results.GetMeasurementSnapshot().Select(static measurement => (string)measurement.Tags[AccessTelemetryNames.ResultTag]!)];
        }
    }
}
