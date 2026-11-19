// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Access
{
    /// <summary>
    ///     The security guards inside real transactions: the tenant's lock serialising two removals
    ///     of its last two administrators, a save that cannot take it in time unavailable after its
    ///     retries, a refusal rolling back the whole save, the platform's own scope standing aside, a
    ///     time-bounded administrator not counting, a deactivation of the last administrator refused
    ///     in the action's persist, the public-role and administrator-role invariants refusing a raw
    ///     write, and another stack's save and delete whose own statements write a security table
    ///     refused at append under the stacks' table ownership and held to the guards without it;
    ///     each refusal counted.
    /// </summary>
    /// <param name="fixture">The two tenant databases and their composition.</param>
    [Trait("Category", "Integration")]
    public sealed class AdministratorGuardTests(ConnectDatabase fixture) : IClassFixture<ConnectDatabase>, IDisposable
    {
        /// <summary>Takes the tenant's security lock for the side transaction.</summary>
        private const string TakeSecurityLock = "EXEC sp_getapplock @Resource = N'tellma.access', @LockMode = N'Exclusive', @LockOwner = N'Transaction';";

        /// <summary>The table the raw writes declare.</summary>
        private static readonly HashSet<TableName> RolesTable = [new("core", "Roles")];

        /// <summary>The table a caller's raw removal of memberships declares.</summary>
        private static readonly HashSet<TableName> MembershipsTable = [new("core", "RoleMemberships")];

        /// <summary>The guards counter's measurements.</summary>
        private readonly MetricCollector<long> _guards = new(
            fixture.Services.GetRequiredService<IMeterFactory>(), AccessTelemetryNames.MeterName, AccessTelemetryNames.GuardsTriggered);

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public void Dispose()
        {
            _guards.Dispose();
        }

        // --- The last administrator ---

        [Fact]
        public async Task Two_concurrent_removals_of_the_last_two_administrators_commit_exactly_one()
        {
            await fixture.ResetAdministratorsAsync();
            (int ann, _) = await fixture.SeedAdministratorAsync("Ann");
            (int ben, _) = await fixture.SeedAdministratorAsync("Ben");
            (_, string clerk) = await fixture.SeedClerkAsync();

            // A longer lock wait than the default, so neither save times out while the lock is held.
            await using ServiceProvider instance = fixture.CreateInstance(static services => services.PostConfigure<AccessOptions>(static options => options.SecurityLockTimeout = TimeSpan.FromSeconds(20)));
            using MetricCollector<long> guards = new(instance.GetRequiredService<IMeterFactory>(), AccessTelemetryNames.MeterName, AccessTelemetryNames.GuardsTriggered);

            // --- Both saves pass their validation, each leaving the other administrator, and wait on the lock together ---
            IReadOnlyList<Task> removals = await fixture.HoldWhileAsync(TakeSecurityLock, 2, "APPLICATION", () => [RemoveAsync(instance, clerk, ann), RemoveAsync(instance, clerk, ben)]);
            Exception?[] outcomes = [await CaptureAsync(removals[0]), await CaptureAsync(removals[1])];

            // --- The second to take the lock found no administrator left, and nothing of it committed ---
            ValidationException refused = Assert.IsType<ValidationException>(Assert.Single(outcomes, static outcome => outcome is not null));
            Assert.Equal([AccessCodes.LastAdministrator], refused.Codes());
            Assert.Equal(1, await fixture.AdministratorsAsync());
            Assert.Equal([AccessTelemetryNames.GuardLastAdministrator], Tags(guards));
        }

        [Fact]
        public async Task A_refused_save_rolls_back_its_whole_transaction()
        {
            await fixture.ResetAdministratorsAsync();
            (int ann, _) = await fixture.SeedAdministratorAsync("Ann");
            (_, string clerk) = await fixture.SeedClerkAsync();
            (Guid? permissions, Guid? preferences) = await fixture.UserTagsAsync(ann);

            // --- One save renames the administrator and removes her membership ---
            await using (AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(clerk))
            {
                User user = await scope.LoadUserAsync(ann);
                user.Name = "Renamed";
                user.RoleMemberships = [.. user.RoleMemberships!.Where(static membership => membership.RoleId != WriteSideRig.Administrators)];

                ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => scope.SaveUsersAsync(user));
                Assert.Equal([AccessCodes.LastAdministrator], refused.Codes());
            }

            // --- Neither change, nor either tag the save would have moved ---
            Assert.Equal("Ann", await fixture.ScalarAsync<string>(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"SELECT [Name] FROM [core].[Users] WHERE [Id] = {ann};")));
            Assert.Equal(1, await fixture.AdministratorsAsync());
            Assert.Equal((permissions, preferences), await fixture.UserTagsAsync(ann));
            Assert.Equal([AccessTelemetryNames.GuardLastAdministrator], Tags(_guards));
        }

        [Fact]
        public async Task A_save_that_cannot_take_the_security_lock_in_time_is_unavailable_after_its_retries_and_each_attempt_is_counted()
        {
            await fixture.ResetAdministratorsAsync();
            _ = await fixture.SeedAdministratorAsync("Ann");
            (int ben, _) = await fixture.SeedAdministratorAsync("Ben");
            (_, string clerk) = await fixture.SeedClerkAsync();

            // A short lock wait, so every attempt times out while the side transaction holds the lock.
            await using ServiceProvider instance = fixture.CreateInstance(static services => services.PostConfigure<AccessOptions>(static options => options.SecurityLockTimeout = TimeSpan.FromMilliseconds(200)));
            using MetricCollector<long> guards = new(instance.GetRequiredService<IMeterFactory>(), AccessTelemetryNames.MeterName, AccessTelemetryNames.GuardsTriggered);

            // --- The lock, held by a side transaction until the save gave up ---
            DataAccessRetryExhaustedException exhausted;
            await using (SqlConnection holder = new(fixture.ConnectionString(ConnectDatabase.TenantA)))
            {
                await holder.OpenAsync(Token);
                await using var transaction = (SqlTransaction)await holder.BeginTransactionAsync(Token);
                await using (SqlCommand take = new(TakeSecurityLock, holder, transaction))
                {
                    _ = await take.ExecuteNonQueryAsync(Token);
                }

                DependencyUnavailableException unavailable = await Assert.ThrowsAsync<DependencyUnavailableException>(() => RemoveAsync(instance, clerk, ben));
                exhausted = Assert.IsType<DataAccessRetryExhaustedException>(unavailable.InnerException);
                await transaction.RollbackAsync(Token);
            }

            // --- Every attempt timed out on the lock and was counted once; nothing committed ---
            Assert.True(exhausted.Attempts > 1, exhausted.ToString());
            Assert.Equal(Enumerable.Repeat<string?>(AccessTelemetryNames.GuardLockTimeout, exhausted.Attempts), Tags(guards));
            Assert.Equal(2, await fixture.AdministratorsAsync());
        }

        [Fact]
        public async Task Another_administrator_counts_only_while_their_membership_is_permanent_and_started()
        {
            await fixture.ResetAdministratorsAsync();
            (int ann, _) = await fixture.SeedAdministratorAsync("Ann");
            (int ben, _) = await fixture.SeedAdministratorAsync("Ben", endsAt: "DATEADD(day, 30, SYSDATETIMEOFFSET())");
            (int cal, _) = await fixture.SeedAdministratorAsync("Cal", startsAt: "DATEADD(day, 1, SYSDATETIMEOFFSET())");
            (_, string clerk) = await fixture.SeedClerkAsync();

            // --- Ben's ends and Cal's has not started: neither stands in for Ann's ---
            await using (AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(clerk))
            {
                ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => scope.RemoveAdministratorAsync(ann));
                Assert.Equal([AccessCodes.LastAdministrator], refused.Codes());
            }

            // --- Once Ben's is permanent, Ann's may go ---
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[RoleMemberships] SET [EndsAt] = NULL WHERE [UserId] = {ben} AND [RoleId] = 1;"));
            await using (AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(clerk))
            {
                await scope.RemoveAdministratorAsync(ann);
            }

            Assert.Equal(2, await fixture.AdministratorsAsync());
            Assert.Equal(1, await fixture.ScalarAsync<int>(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM [core].[RoleMemberships] WHERE [UserId] = {cal} AND [RoleId] = 1;")));
        }

        [Fact]
        public async Task The_platform_s_own_scope_stands_aside_from_the_guards()
        {
            await fixture.ResetAdministratorsAsync();
            (int ann, _) = await fixture.SeedAdministratorAsync("Ann");

            // --- The platform may leave the tenant without an administrator, as a bootstrap does before it runs ---
            await using (AsyncServiceScope scope = await fixture.ScopeAsync("system", kind: PrincipalKind.System))
            {
                await scope.RemoveAdministratorAsync(ann);
            }

            Assert.Equal(0, await fixture.AdministratorsAsync());
            Assert.Empty(Tags(_guards));
            await fixture.SeedMembershipAsync(ann, WriteSideRig.Administrators);
        }

        [Fact]
        public async Task A_non_administrator_deactivating_the_last_administrator_is_refused_in_the_action_s_persist_and_nothing_changes()
        {
            await fixture.ResetAdministratorsAsync();
            (int ann, _) = await fixture.SeedAdministratorAsync("Ann");
            (int activator, string subject) = await fixture.SeedCallerAsync("Activator");
            int role = await fixture.SeedRoleAsync((CoreResources.User, AccessActions.Read, null), (CoreResources.User, AccessActions.Activate, null));
            await fixture.SeedMembershipAsync(activator, role);

            // --- The action's own rules refuse only the caller's own row: the guards refuse this one ---
            await using (AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(subject))
            {
                ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => scope.Users().DeactivateAsync([ann], cancellationToken: Token));
                Assert.Equal([AccessCodes.LastAdministrator], refused.Codes());
            }

            // --- Ann stays active, and the refusal was counted ---
            Assert.True(await fixture.ScalarAsync<bool>(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"SELECT [IsActive] FROM [core].[Users] WHERE [Id] = {ann};")));
            Assert.Equal(1, await fixture.AdministratorsAsync());
            Assert.Equal([AccessTelemetryNames.GuardLastAdministrator], Tags(_guards));
        }

        // --- The public role and the administrator role ---

        [Fact]
        public async Task A_raw_write_that_gives_a_public_role_a_member_or_damages_the_administrator_role_is_refused_and_rolled_back()
        {
            await fixture.ResetAdministratorsAsync();
            (int ann, string subject) = await fixture.SeedAdministratorAsync("Ann");
            int role = await fixture.SeedRoleAsync((CoreResources.User, AccessActions.Read, null));
            await fixture.SeedMembershipAsync(ann, role);

            // --- Each write runs between the guards, as the pipeline places them ---
            await using AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(subject);
            BatchAssertionFailedException publicRole = await Assert.ThrowsAsync<BatchAssertionFailedException>(() => GuardedAsync(
                scope, string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Roles] SET [IsPublic] = 1 WHERE [Id] = {role};")));
            BatchAssertionFailedException damaged = await Assert.ThrowsAsync<BatchAssertionFailedException>(() => GuardedAsync(
                scope, "UPDATE [core].[Roles] SET [IsActive] = 0 WHERE [Id] = 1;"));

            Assert.Equal((AccessCodes.GuardPublicRoleHasMembers, AccessCodes.AdministratorRoleDamaged), (publicRole.Code, damaged.Code));
            Assert.False(await fixture.ScalarAsync<bool>(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"SELECT [IsPublic] FROM [core].[Roles] WHERE [Id] = {role};")));
            Assert.True(await fixture.ScalarAsync<bool>(ConnectDatabase.TenantA, "SELECT [IsActive] FROM [core].[Roles] WHERE [Id] = 1;"));
            Assert.Equal([AccessTelemetryNames.GuardPublicRoleMembers, AccessTelemetryNames.GuardAdministratorDamaged], Tags(_guards));
        }

        // --- A security write from another stack's persist ---

        [Fact]
        public async Task A_save_of_another_stack_whose_own_statement_removes_the_last_administrator_is_refused_and_rolled_back()
        {
            await fixture.ResetAdministratorsAsync();
            (int ann, string subject) = await fixture.SeedAdministratorAsync("Ann");
            int exports = await fixture.ScalarAsync<int>(ConnectDatabase.TenantA, "SELECT COUNT(*) FROM [fixture].[Exports];");
            SaveOptions options = new() { ReturnEntities = false, OnPersist = batch => RemoveMemberships(batch, ann) };

            // --- An export's save, whose caller statement declares a write of the memberships: the
            // users' stack owns them, so the statement is refused at append ---
            await using (AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(subject))
            {
                InvalidOperationException unowned = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => scope.ServiceProvider.GetRequiredService<EntityService<Export, int>>().SaveAsync([new Export { Name = "beside" }], options, Token));
                Assert.Contains("'core.User'", unowned.Message, StringComparison.Ordinal);
            }

            // --- Without the ownership check the statement reaches the persist, and the guards refuse it there ---
            await using ServiceProvider instance = fixture.CreateInstance(Unowned);
            using MetricCollector<long> guards = new(instance.GetRequiredService<IMeterFactory>(), AccessTelemetryNames.MeterName, AccessTelemetryNames.GuardsTriggered);
            await using (AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(subject, instance))
            {
                ValidationException refused = await Assert.ThrowsAsync<ValidationException>(
                    () => scope.ServiceProvider.GetRequiredService<EntityService<Export, int>>().SaveAsync([new Export { Name = "beside" }], options, Token));
                Assert.Equal([AccessCodes.LastAdministrator], refused.Codes());
            }

            // --- Neither the export nor the removal committed ---
            Assert.Equal(1, await fixture.AdministratorsAsync());
            Assert.Equal(exports, await fixture.ScalarAsync<int>(ConnectDatabase.TenantA, "SELECT COUNT(*) FROM [fixture].[Exports];"));
            Assert.Equal([AccessTelemetryNames.GuardLastAdministrator], Tags(guards));
        }

        [Fact]
        public async Task A_delete_of_another_stack_whose_effect_removes_the_last_administrator_is_refused_and_rolled_back()
        {
            await fixture.ResetAdministratorsAsync();
            (int ann, string subject) = await fixture.SeedAdministratorAsync("Ann");
            int export;
            await using (AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(subject))
            {
                EntitiesResult<Export> saved = await scope.ServiceProvider.GetRequiredService<EntityService<Export, int>>()
                    .SaveAsync([new Export { Name = "doomed" }], new SaveOptions { ReturnEntities = false }, Token);
                export = checked((int)saved.Ids[0]);
            }

            // --- An instance whose effect on the exports' persists removes Ann's memberships, as a raw
            // write: the users' stack owns them, so the effect's statement is refused at append ---
            await using (ServiceProvider owned = fixture.CreateInstance(
                services => services.AddSingleton(new MembershipRemoval(ann)),
                static tellma => tellma.AddFeature(new MembershipRemovalFeature())))
            {
                await using AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(subject, owned);
                InvalidOperationException unowned = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => scope.ServiceProvider.GetRequiredService<EntityService<Export, int>>().DeleteByIdsAsync([export], null, Token));
                Assert.Contains("'core.User'", unowned.Message, StringComparison.Ordinal);
            }

            // --- Without the ownership check the statement reaches the persist, and the guards refuse it there ---
            await using ServiceProvider instance = fixture.CreateInstance(
                services =>
                {
                    _ = services.AddSingleton(new MembershipRemoval(ann));
                    Unowned(services);
                },
                static tellma => tellma.AddFeature(new MembershipRemovalFeature()));
            using MetricCollector<long> guards = new(instance.GetRequiredService<IMeterFactory>(), AccessTelemetryNames.MeterName, AccessTelemetryNames.GuardsTriggered);
            await using (AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(subject, instance))
            {
                ValidationException refused = await Assert.ThrowsAsync<ValidationException>(
                    () => scope.ServiceProvider.GetRequiredService<EntityService<Export, int>>().DeleteByIdsAsync([export], null, Token));
                Assert.Equal([AccessCodes.LastAdministrator], refused.Codes());
            }

            // --- Neither the delete nor the removal committed ---
            Assert.Equal(1, await fixture.AdministratorsAsync());
            Assert.Equal(1, await fixture.ScalarAsync<int>(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM [fixture].[Exports] WHERE [Id] = {export};")));
            Assert.Equal([AccessTelemetryNames.GuardLastAdministrator], Tags(guards));
        }

        // --- Fixtures ---

        /// <summary>Appends a caller's raw removal of a user's memberships, declaring the table it writes and the user whose tag it moves.</summary>
        /// <param name="batch">The persist batch.</param>
        /// <param name="userId">The user.</param>
        private static void RemoveMemberships(IDataBatch batch, int userId)
        {
            // The removal stands for a participant of a stack that owns no security table writing one
            // raw: the test writes the platform's memberships table from a class that owns no stack,
            // deliberately, as such a caller's OnPersist would. The stacks' table ownership refuses it at
            // append; an instance without that check lets it reach the guards.
            SqlIdentifier users = batch.Tvp([new IdList { Id = userId }]);
#pragma warning disable TELLMA0006
            _ = batch.Sql($"DELETE FROM [core].[RoleMemberships] WHERE [UserId] = {userId};", new SqlOptions(writes: MembershipsTable, userIds: users));
#pragma warning restore TELLMA0006
        }

        /// <summary>
        ///     Lifts an instance's write-authority check at append: no stack owns any table, so a raw
        ///     write of a security table from another stack's statement reaches the persist and its guards.
        /// </summary>
        /// <param name="services">The instance's services, after its composition.</param>
        private static void Unowned(IServiceCollection services)
        {
            _ = services.Replace(ServiceDescriptor.Singleton<IStackTableOwners, NoStackTableOwners>());
        }

        /// <summary>The user whose memberships the exports' effect removes.</summary>
        /// <param name="UserId">The user.</param>
        private sealed record MembershipRemoval(int UserId);

        /// <summary>
        ///     An effect of the exports, a stack that owns no security table, whose statement removes a
        ///     user's memberships raw, as a careless participant would.
        /// </summary>
        /// <param name="removal">The user.</param>
        private sealed class MembershipRemovalEffect(MembershipRemoval removal) : IPersistEffect<Export>
        {
            /// <inheritdoc />
            public Task ContributeAsync(PersistContext<Export> context)
            {
                ArgumentNullException.ThrowIfNull(context);
                RemoveMemberships(context.Batch, removal.UserId);
                return Task.CompletedTask;
            }
        }

        /// <summary>The feature that attaches <see cref="MembershipRemovalEffect" /> to the fixture's exports.</summary>
        private sealed class MembershipRemovalFeature : ITellmaFeature
        {
            /// <inheritdoc />
            public string Name => "membership-removal";

            /// <inheritdoc />
            public void Declare(FeatureDeclaration declaration)
            {
            }

            /// <inheritdoc />
            public void Contribute(FeatureContribution contribution)
            {
                ArgumentNullException.ThrowIfNull(contribution);
                _ = contribution.PersistEffect<Export, MembershipRemovalEffect>();
            }
        }

        /// <summary>The guard tag of each measurement, in order.</summary>
        /// <param name="guards">The collector.</param>
        /// <returns>The tags.</returns>
        private static IReadOnlyList<string?> Tags(MetricCollector<long> guards)
        {
            return [.. guards.GetMeasurementSnapshot().Select(static measurement => measurement.Tags[AccessTelemetryNames.GuardTag] as string)];
        }

        /// <summary>Waits for a task, capturing what it threw.</summary>
        /// <param name="task">The task.</param>
        /// <returns>The exception; null when it succeeded.</returns>
        private static async Task<Exception?> CaptureAsync(Task task)
        {
            try
            {
                await task;
                return null;
            }
            catch (Exception exception) when (exception is ValidationException or DataAccessException)
            {
                return exception;
            }
        }

        /// <summary>Removes a user's administrator membership in a caller's scope of an instance.</summary>
        /// <param name="instance">The instance.</param>
        /// <param name="subject">The caller.</param>
        /// <param name="userId">The user.</param>
        /// <returns>A task that completes when the save committed.</returns>
        private async Task RemoveAsync(IServiceProvider instance, string subject, int userId)
        {
            await using AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(subject, instance);
            await scope.RemoveAdministratorAsync(userId);
        }

        /// <summary>Runs one raw write of the security tables between the security lock and the invariants, as a caller.</summary>
        /// <param name="scope">The caller's scope.</param>
        /// <param name="statement">The write.</param>
        /// <returns>A task that completes when the batch committed.</returns>
        private static async Task GuardedAsync(AsyncServiceScope scope, string statement)
        {
            IAccessGuards guards = scope.ServiceProvider.GetRequiredService<IAccessGuards>();
            _ = await scope.ServiceProvider.GetRequiredService<IGuardedBatchRunner>().RunAsync(
                BatchPurpose.Persist,
                (_, batch) =>
                {
                    // The write is opaque to the batch, which the data layer's own channel allows; it
                    // declares the security table it writes.
                    var data = (DataBatch)batch;
                    guards.ContributeLock(batch);
                    data.Append(data.Begin(), statement, writes: RolesTable);
                    guards.ContributeInvariants(batch);
                    return ValueTask.CompletedTask;
                },
                static (_, outcome) => outcome.RoundTrips,
                Token);
        }
    }
}
