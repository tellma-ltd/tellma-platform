// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Tenancy;

namespace Tellma.Core.IntegrationTests.Access
{
    /// <summary>
    ///     What the access write side's suites share over the connect fixture: administrators and a
    ///     clerk who may save users seeded raw, the users' and the roles' services of a scope, the
    ///     tags a write moves, and a wait for the sessions a held lock blocks.
    /// </summary>
    internal static class WriteSideRig
    {
        /// <summary>The administrator role.</summary>
        public const int Administrators = WellKnownIds.AdministratorRoleId;

        /// <summary>The longest a test waits for the sessions it expects a held lock to block.</summary>
        private static readonly TimeSpan WaiterBound = TimeSpan.FromSeconds(30);

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <summary>
        ///     A caller's scope of the first tenant whose session stepped up a moment ago, as the users'
        ///     and the roles' saves, sensitive securables, require.
        /// </summary>
        /// <param name="fixture">The databases.</param>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="services">The instance the scope is of; the fixture's own when null.</param>
        /// <returns>The scope.</returns>
        public static Task<AsyncServiceScope> SteppedUpScopeAsync(this ConnectDatabase fixture, string subject, IServiceProvider? services = null)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return fixture.ScopeAsync(subject, services: services, assurance: new AuthenticationAssurance(AssuranceTiers.Aal2, now, now));
        }

        /// <summary>Removes every administrator membership of the first tenant, raw, so a test counts only its own.</summary>
        /// <param name="fixture">The databases.</param>
        /// <returns>A task that completes when they are removed.</returns>
        public static Task ResetAdministratorsAsync(this ConnectDatabase fixture)
        {
            return fixture.ExecuteAsync(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"DELETE FROM [core].[RoleMemberships] WHERE [RoleId] = {Administrators};"));
        }

        /// <summary>Seeds a joined individual of the first tenant with an administrator membership over a window.</summary>
        /// <param name="fixture">The databases.</param>
        /// <param name="name">The user's name.</param>
        /// <param name="startsAt">The window's start as a SQL expression; null since always.</param>
        /// <param name="endsAt">The window's end as a SQL expression; null for none.</param>
        /// <returns>The id and the subject.</returns>
        public static async Task<(int UserId, string Subject)> SeedAdministratorAsync(this ConnectDatabase fixture, string name, string? startsAt = null, string? endsAt = null)
        {
            (int userId, string subject) = await fixture.SeedCallerAsync(name);
            await fixture.SeedMembershipAsync(userId, Administrators, startsAt, endsAt);
            return (userId, subject);
        }

        /// <summary>Seeds a joined individual of the first tenant who may read and save users, and nothing more.</summary>
        /// <param name="fixture">The databases.</param>
        /// <returns>The id and the subject.</returns>
        public static async Task<(int UserId, string Subject)> SeedClerkAsync(this ConnectDatabase fixture)
        {
            (int userId, string subject) = await fixture.SeedCallerAsync("Clerk");
            int role = await fixture.SeedRoleAsync((CoreResources.User, AccessActions.Read, null), (CoreResources.User, AccessActions.Save, null));
            await fixture.SeedMembershipAsync(userId, role);
            return (userId, subject);
        }

        /// <summary>The number of administrator memberships of the first tenant.</summary>
        /// <param name="fixture">The databases.</param>
        /// <returns>The count.</returns>
        public static Task<int> AdministratorsAsync(this ConnectDatabase fixture)
        {
            return fixture.ScalarAsync<int>(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM [core].[RoleMemberships] WHERE [RoleId] = {Administrators};"));
        }

        /// <summary>A tenant-level tag of the first tenant.</summary>
        /// <param name="fixture">The databases.</param>
        /// <param name="name">The tag's name.</param>
        /// <returns>The tag.</returns>
        public static Task<Guid> TenantTagAsync(this ConnectDatabase fixture, string name)
        {
            return fixture.ScalarAsync<Guid>(ConnectDatabase.TenantA, $"SELECT [Tag] FROM [core].[VersionTags] WHERE [Name] = N'{name}';");
        }

        /// <summary>A user's two stamped tags in the first tenant.</summary>
        /// <param name="fixture">The databases.</param>
        /// <param name="userId">The user.</param>
        /// <returns>The permissions tag and the preferences tag.</returns>
        public static async Task<(Guid? Permissions, Guid? Preferences)> UserTagsAsync(this ConnectDatabase fixture, int userId)
        {
            string where = string.Create(CultureInfo.InvariantCulture, $"WHERE [UserId] = {userId}");
            return (
                await fixture.ScalarAsync<Guid?>(ConnectDatabase.TenantA, $"SELECT [PermissionsTag] FROM [core].[UserStamps] {where};"),
                await fixture.ScalarAsync<Guid?>(ConnectDatabase.TenantA, $"SELECT [PreferencesTag] FROM [core].[UserStamps] {where};"));
        }

        /// <summary>The users' service of a scope.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        public static EntityService<User, int> Users(this AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<User, int>>();
        }

        /// <summary>The roles' service of a scope.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        public static EntityService<Role, int> Roles(this AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<Role, int>>();
        }

        /// <summary>Loads one user with its memberships.</summary>
        /// <param name="scope">The scope.</param>
        /// <param name="userId">The user.</param>
        /// <returns>The user.</returns>
        public static async Task<User> LoadUserAsync(this AsyncServiceScope scope, int userId)
        {
            EntitiesResult<User> loaded = await scope.Users().GetByIdAsync(userId, DetailsRequest.None, Token);
            return Assert.Single(loaded.Entities);
        }

        /// <summary>Saves users without returning them.</summary>
        /// <param name="scope">The scope.</param>
        /// <param name="users">The users.</param>
        /// <returns>The ids.</returns>
        public static async Task<IReadOnlyList<long>> SaveUsersAsync(this AsyncServiceScope scope, params User[] users)
        {
            return (await scope.Users().SaveAsync(users, new SaveOptions { ReturnEntities = false }, Token)).Ids;
        }

        /// <summary>Loads a user and saves it without its administrator memberships.</summary>
        /// <param name="scope">The scope.</param>
        /// <param name="userId">The user.</param>
        /// <returns>A task that completes when the save committed.</returns>
        public static async Task RemoveAdministratorAsync(this AsyncServiceScope scope, int userId)
        {
            User user = await scope.LoadUserAsync(userId);
            user.RoleMemberships = [.. (user.RoleMemberships ?? []).Where(static membership => membership.RoleId != Administrators)];
            _ = await scope.SaveUsersAsync(user);
        }

        /// <summary>The codes of a refused save, in order.</summary>
        /// <param name="refused">The refusal.</param>
        /// <returns>The codes.</returns>
        public static IReadOnlyList<string> Codes(this ValidationException refused)
        {
            return [.. refused.Errors.Select(static error => error.Code)];
        }

        /// <summary>Holds a lock on a side connection until the sessions it should block are waiting on it, then releases it.</summary>
        /// <param name="fixture">The databases.</param>
        /// <param name="take">The statement that takes the lock inside the side transaction.</param>
        /// <param name="waiters">How many other sessions must wait on a lock of the resource type before the release.</param>
        /// <param name="resourceType">The waited lock's resource type, such as <c>APPLICATION</c> or <c>KEY</c>.</param>
        /// <param name="start">Starts the work the lock should block.</param>
        /// <returns>The started work, once the lock is released.</returns>
        public static async Task<IReadOnlyList<Task>> HoldWhileAsync(this ConnectDatabase fixture, string take, int waiters, string resourceType, Func<IReadOnlyList<Task>> start)
        {
            // --- The lock, held by a transaction the work cannot see past ---
            await using SqlConnection holder = new(fixture.ConnectionString(ConnectDatabase.TenantA));
            await holder.OpenAsync(Token);
            await using var transaction = (SqlTransaction)await holder.BeginTransactionAsync(Token);
            await using (SqlCommand command = new(take, holder, transaction))
            {
                _ = await command.ExecuteNonQueryAsync(Token);
            }

            IReadOnlyList<Task> work = start();

            // --- The work's sessions, each blocked on the held lock; a failure ends the wait early ---
            var waited = Stopwatch.StartNew();
            await using SqlConnection watcher = new(fixture.ConnectionString(ConnectDatabase.TenantA));
            await watcher.OpenAsync(Token);
            while (await WaitersAsync(watcher, resourceType) < waiters && !work.Any(static task => task.IsCompleted))
            {
                Assert.True(waited.Elapsed < WaiterBound, $"Fewer than {waiters} sessions waited on the held lock within {WaiterBound}.");
                await Task.Delay(TimeSpan.FromMilliseconds(25), Token);
            }

            Assert.DoesNotContain(work, static task => task.IsCompleted);
            await transaction.CommitAsync(Token);
            return work;
        }

        /// <summary>The sessions waiting on a lock of a resource type in the connection's database.</summary>
        /// <param name="connection">The connection.</param>
        /// <param name="resourceType">The resource type.</param>
        /// <returns>The count.</returns>
        private static async Task<int> WaitersAsync(SqlConnection connection, string resourceType)
        {
            await using SqlCommand command = new(
                "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE [resource_database_id] = DB_ID() AND [resource_type] = @type AND [request_status] = N'WAIT';",
                connection);
            _ = command.Parameters.AddWithValue("@type", resourceType);
            return (int)await command.ExecuteScalarAsync(Token);
        }
    }
}
