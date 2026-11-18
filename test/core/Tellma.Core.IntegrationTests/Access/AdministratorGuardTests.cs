// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.IntegrationTests.Access
{
    /// <summary>
    ///     The security guards inside real transactions: the tenant's lock serialising two removals
    ///     of its last two administrators, a refusal rolling back the whole save, the platform's own
    ///     scope standing aside, a time-bounded administrator not counting, and the public-role and
    ///     administrator-role invariants refusing a raw write; each refusal counted.
    /// </summary>
    /// <param name="fixture">The two tenant databases and their composition.</param>
    [Trait("Category", "Integration")]
    public sealed class AdministratorGuardTests(ConnectDatabase fixture) : IClassFixture<ConnectDatabase>, IDisposable
    {
        /// <summary>Takes the tenant's security lock for the side transaction.</summary>
        private const string TakeSecurityLock = "EXEC sp_getapplock @Resource = N'tellma.access', @LockMode = N'Exclusive', @LockOwner = N'Transaction';";

        /// <summary>The table the raw writes declare.</summary>
        private static readonly HashSet<TableName> RolesTable = [new("core", "Roles")];

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

        // --- Fixtures ---

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
