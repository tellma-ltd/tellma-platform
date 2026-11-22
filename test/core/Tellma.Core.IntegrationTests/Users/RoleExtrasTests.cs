// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.IntegrationTests.Access;

namespace Tellma.Core.IntegrationTests.Users
{
    /// <summary>
    ///     The role page's details extras against a real tenant database: the members a caller may
    ///     read with their windows, ended memberships left out, at most 500 rows, absent when the
    ///     caller may read no user; and the drifted permissions with why; both in the details read's
    ///     one round trip. The class has its own databases, so it alone seeds their users and roles.
    /// </summary>
    /// <param name="fixture">The tenant databases and their composition.</param>
    [Trait("Category", "Integration")]
    public sealed class RoleExtrasTests(ConnectDatabase fixture) : IClassFixture<ConnectDatabase>
    {
        /// <summary>The members extra's name.</summary>
        private const string Members = "members";

        /// <summary>The problems extra's name.</summary>
        private const string Problems = "problems";

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task The_role_page_s_details_read_answers_its_members_and_its_drifted_permissions_in_one_round_trip()
        {
            // --- A role with a granting permission and three drifted ones, and two members ---
            int role = await fixture.SeedRoleAsync(
                (CoreResources.Job, AccessActions.Read, null),
                ("core.Gone", AccessActions.Read, null),
                (CoreResources.Job, "Fly", null),
                (CoreResources.User, AccessActions.Read, "Nothing = 1"));
            (int bea, _) = await fixture.SeedCallerAsync("Page member B");
            (int ada, _) = await fixture.SeedCallerAsync("Page member A");
            await fixture.SeedMembershipAsync(bea, role);
            await fixture.SeedMembershipAsync(ada, role);
            (_, string subject) = await RoleReaderAsync("Page reader", usersFilter: null);
            await WarmAsync(subject, role);

            // --- One round trip for the role, its members and its problems ---
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int start = access.RoundTrips;
            EntitiesResult<Role> read = await scope.Roles().GetByIdAsync(role, new DetailsRequest { Include = [Members, Problems] }, Token);
            Assert.Equal(1, access.RoundTrips - start);

            // --- The members by name ---
            Assert.Equal([Members, Problems], read.Extras!.Keys);
            JsonElement members = read.Extras[Members];
            Assert.Equal([ada, bea], Column(members, "UserId").Select(static value => value.GetInt32()));
            Assert.Equal(["Page member A", "Page member B"], Column(members, "User.Name").Select(static value => value.GetString()));
            Assert.All(Column(members, "RoleId"), value => Assert.Equal(role, value.GetInt32()));

            // --- The drifted permissions in permission order, the invalid filter with its diagnostic ---
            int[] permissions = [.. Assert.Single(read.Entities).Permissions!.Select(static permission => permission.Id)];
            JsonElement[] problems = [.. read.Extras[Problems].EnumerateArray()];
            Assert.Equal(
                [(permissions[1], "UnknownResource"), (permissions[2], "UnknownAction"), (permissions[3], "FilterInvalid")],
                problems.Select(static problem => (problem.GetProperty("permissionId").GetInt32(), problem.GetProperty("code").GetString())));
            JsonElement diagnostic = Assert.Single(problems[2].GetProperty("diagnostics").EnumerateArray());
            Assert.Equal("filters[0]", diagnostic.GetProperty("clause").GetString());
            Assert.Equal(0, problems[0].GetProperty("diagnostics").GetArrayLength());
        }

        [Fact]
        public async Task The_members_under_a_filtered_grant_list_no_hidden_user_carry_their_windows_and_leave_out_ended_memberships()
        {
            int role = await fixture.SeedRoleAsync((CoreResources.Schedule, AccessActions.Read, null));
            (int vis, _) = await fixture.SeedCallerAsync("Window visible");
            (int hid, _) = await fixture.SeedCallerAsync("Window hidden");
            (int ended, _) = await fixture.SeedCallerAsync("Window ended");
            await fixture.SeedMembershipAsync(vis, role, endsAt: "DATEADD(day, 1, SYSDATETIMEOFFSET())");
            await fixture.SeedMembershipAsync(vis, role, startsAt: "DATEADD(day, 2, SYSDATETIMEOFFSET())");
            await fixture.SeedMembershipAsync(hid, role);
            await fixture.SeedMembershipAsync(ended, role, startsAt: "DATEADD(day, -2, SYSDATETIMEOFFSET())", endsAt: "DATEADD(day, -1, SYSDATETIMEOFFSET())");
            (_, string subject) = await RoleReaderAsync("Window reader", usersFilter: "Name != 'Window hidden'");

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            EntitiesResult<Role> read = await scope.Roles().GetByIdAsync(role, new DetailsRequest { Include = [Members] }, Token);

            // --- The visible user's current window, then the scheduled one; the hidden and the ended left out ---
            JsonElement members = read.Extras![Members];
            Assert.Equal([vis, vis], Column(members, "UserId").Select(static value => value.GetInt32()));
            JsonElement[] starts = [.. Column(members, "StartsAt")];
            JsonElement[] ends = [.. Column(members, "EndsAt")];
            Assert.Equal((JsonValueKind.Null, JsonValueKind.String), (starts[0].ValueKind, ends[0].ValueKind));
            Assert.Equal((JsonValueKind.String, JsonValueKind.Null), (starts[1].ValueKind, ends[1].ValueKind));
            Assert.True(ends[0].GetDateTimeOffset() < starts[1].GetDateTimeOffset());
        }

        [Fact]
        public async Task The_members_stop_at_500_rows_by_name()
        {
            int role = await fixture.SeedRoleAsync((CoreResources.Notification, AccessActions.Read, null));
            await SeedMembersAsync(role, 501);
            (_, string subject) = await RoleReaderAsync("Many reader", usersFilter: null);

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            EntitiesResult<Role> read = await scope.Roles().GetByIdAsync(role, new DetailsRequest { Include = [Members] }, Token);

            string?[] names = [.. Column(read.Extras![Members], "User.Name").Select(static value => value.GetString())];
            Assert.Equal(500, names.Length);
            Assert.Equal(Name(0), names[0]);
            Assert.Equal(Name(499), names[^1]);
        }

        [Fact]
        public async Task A_caller_who_may_read_no_user_gets_the_problems_without_the_members()
        {
            // --- An instance without the own-row criterion, which lets every member read their own user ---
            await using ServiceProvider instance = fixture.CreateInstance(static services => services.RemoveAll<IAccessCriteriaProvider>());
            int role = await fixture.SeedRoleAsync(("core.Gone", AccessActions.Read, null));
            (int member, _) = await fixture.SeedCallerAsync("Unseen member");
            await fixture.SeedMembershipAsync(member, role);
            (int reader, string subject) = await fixture.SeedCallerAsync("Role-only reader");
            int roles = await fixture.SeedRoleAsync((CoreResources.Role, AccessActions.Read, null));
            await fixture.SeedMembershipAsync(reader, roles);

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject, services: instance);
            EntitiesResult<Role> read = await scope.Roles().GetByIdAsync(role, new DetailsRequest { Include = [Members, Problems] }, Token);

            Assert.Equal([Problems], read.Extras!.Keys);
            Assert.Equal(1, read.Extras[Problems].GetArrayLength());
        }

        // --- Helpers ---

        /// <summary>The values of one column of a row set's JSON, in row order.</summary>
        /// <param name="rows">The row set.</param>
        /// <param name="name">The column's name.</param>
        /// <returns>The values.</returns>
        private static IEnumerable<JsonElement> Column(JsonElement rows, string name)
        {
            string?[] columns = [.. rows.GetProperty("columns").EnumerateArray().Select(static column => column.GetProperty("name").GetString())];
            int index = Array.IndexOf(columns, name);
            Assert.True(index >= 0, $"The row set has no column {name}.");
            return [.. rows.GetProperty("rows").EnumerateArray().Select(row => row[index])];
        }

        /// <summary>The name of the bulk member at a position, which sorts in position order.</summary>
        /// <param name="position">The position.</param>
        /// <returns>The name.</returns>
        private static string Name(int position)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Bulk member {position:D4}");
        }

        /// <summary>Seeds a caller who may read the roles and, under a filter or none, the users.</summary>
        /// <param name="name">The caller's name.</param>
        /// <param name="usersFilter">The filter of the caller's read of the users; null for every user.</param>
        /// <returns>The caller's id and subject.</returns>
        private async Task<(int UserId, string Subject)> RoleReaderAsync(string name, string? usersFilter)
        {
            (int userId, string subject) = await fixture.SeedCallerAsync(name);
            int role = await fixture.SeedRoleAsync((CoreResources.Role, AccessActions.Read, null), (CoreResources.User, AccessActions.Read, usersFilter));
            await fixture.SeedMembershipAsync(userId, role);
            return (userId, subject);
        }

        /// <summary>Seeds active users in one batch, each a member of a role, named in position order.</summary>
        /// <param name="roleId">The role.</param>
        /// <param name="count">How many.</param>
        /// <returns>A task that completes when they are seeded.</returns>
        private Task SeedMembersAsync(int roleId, int count)
        {
            // --- One statement per table, rows in position order ---
            int system = WellKnownIds.SystemUserId;
            int[] ids = [.. Enumerable.Range(0, count).Select(_ => fixture.NextUserId())];
            StringBuilder users = new("INSERT INTO [core].[Users] ([Id], [Kind], [Subject], [Email], [InvitedAt], [JoinedAt], [Name], [IsActive], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]) VALUES ");
            StringBuilder stamps = new("INSERT INTO [core].[UserStamps] ([UserId]) VALUES ");
            StringBuilder memberships = new("INSERT INTO [core].[RoleMemberships] ([Id], [UserId], [RoleId]) SELECT NEXT VALUE FOR [core].[sq_RoleMemberships], [U].[Id], ");
            _ = memberships.Append(CultureInfo.InvariantCulture, $"{roleId} FROM [core].[Users] AS [U] WHERE [U].[Id] IN (");
            for (int position = 0; position < count; position++)
            {
                string separator = position == 0 ? string.Empty : ", ";
                int id = ids[position];
                _ = users.Append(CultureInfo.InvariantCulture, $"{separator}({id}, 'Individual', 'sub-{id}', N'sub-{id}@example.com', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'{Name(position)}', 1, SYSDATETIMEOFFSET(), {system}, SYSDATETIMEOFFSET(), {system})");
                _ = stamps.Append(CultureInfo.InvariantCulture, $"{separator}({id})");
                _ = memberships.Append(CultureInfo.InvariantCulture, $"{separator}{id}");
            }

            _ = memberships.Append(");");
            return fixture.ExecuteAsync(ConnectDatabase.TenantA, users.Append(';').ToString(), stamps.Append(';').ToString(), memberships.ToString());
        }

        /// <summary>Reads a role once in a scope of the caller's own, so the scopes after it find the connection and the plans cached.</summary>
        /// <param name="subject">The caller.</param>
        /// <param name="role">The role.</param>
        /// <returns>A task that completes when the read ran.</returns>
        private async Task WarmAsync(string subject, int role)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            _ = await scope.Roles().GetByIdAsync(role, new DetailsRequest { Include = [Members, Problems] }, Token);
        }
    }
}
