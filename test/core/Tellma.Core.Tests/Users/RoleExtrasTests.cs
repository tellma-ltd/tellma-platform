// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Crud.Query;
using Tellma.Core.Tests.Crud.Scripted;
using Tellma.Core.Users;

namespace Tellma.Core.Tests.Users
{
    /// <summary>
    ///     The roles' details extras over the scripted batch: the members as one query over the
    ///     visible roles under the caller's read of the users, absent when that read is denied, and
    ///     the drifted permissions computed from the permissions the read loads, all in the details
    ///     read's one round trip.
    /// </summary>
    public sealed class RoleExtrasTests : IAsyncDisposable
    {
        /// <summary>The role every test reads.</summary>
        private const int RoleId = 5;

        /// <summary>The filter the caller's read of the users holds.</summary>
        private const string UsersFilter = "Name != 'hidden'";

        /// <summary>The window predicate that keeps current and scheduled memberships.</summary>
        private const string NotEnded = "EndsAt is null or EndsAt > now()";

        /// <summary>The rigs a test composed, disposed with it.</summary>
        private readonly List<QueryRig> _rigs = [];

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            foreach (QueryRig rig in _rigs)
            {
                await rig.DisposeAsync();
            }
        }

        [Fact]
        public async Task The_role_page_s_one_details_read_carries_the_members_under_the_caller_s_filter_and_the_drifted_permissions()
        {
            QueryRig rig = Rig();
            rig.Grant((CoreResources.Role, AccessActions.Read, null), (CoreResources.User, AccessActions.Read, UsersFilter));
            RowPage members = QueryRig.Page(11, 12);
            Script(rig, members, RoleRow(Stored(1, CoreResources.User, AccessActions.Read), Stored(2, "fx.Gone", AccessActions.Read), Stored(3, CoreResources.User, "Fly")));

            await using AsyncServiceScope scope = rig.Scope();
            EntitiesResult<Role> result = await Roles(scope).GetByIdAsync(RoleId, new DetailsRequest { Include = [RoleService<Role>.MembersExtra, RoleService<Role>.ProblemsExtra] }, Token);

            // --- One round trip; the members' one query joins the visible roles on its role ---
            Assert.Equal(1, rig.Script.RoundTrips);
            ScriptedBatch batch = Assert.Single(rig.Script.Batches);
            string idsSource = batch.Of(ScriptedKind.Query).Single().Query<Role>().Restrictions[0].TableSource;
            QuerySpec spec = Members(batch).Spec;
            Assert.Equal("Id, RoleId, UserId, User.Name, User.Email, User.ImageId, User.IsActive, User.State, StartsAt, EndsAt, Notes", spec.Select);
            Assert.Equal(("RoleId", idsSource), (Assert.Single(spec.Restrictions).Path, spec.Restrictions[0].TableSource));
            Assert.Equal("User.Name, UserId, Id", spec.OrderBy);
            Assert.Equal(RoleService<Role>.MaxMembers, spec.Take);

            // --- Unended memberships only, each user under the caller's read rebased through the membership's user ---
            string filter = QueryRig.Render(spec.Filter);
            Assert.StartsWith($"and({NotEnded} | via User(", filter, StringComparison.Ordinal);
            Assert.Contains(UsersFilter, filter, StringComparison.Ordinal);

            // --- The extras in request order: the members' rows as read, the drifted permissions in order ---
            Assert.Equal([RoleService<Role>.MembersExtra, RoleService<Role>.ProblemsExtra], result.Extras!.Keys);
            Assert.Equal(JsonSerializer.Serialize(members.Rows, TellmaJson.Options), result.Extras[RoleService<Role>.MembersExtra].GetRawText());
            JsonElement problems = result.Extras[RoleService<Role>.ProblemsExtra];
            Assert.Equal(
                [(2, nameof(AccessProblemCode.UnknownResource)), (3, nameof(AccessProblemCode.UnknownAction))],
                problems.EnumerateArray().Select(static problem => (problem.GetProperty("permissionId").GetInt32(), problem.GetProperty("code").GetString())));
            Assert.All(problems.EnumerateArray(), static problem => Assert.Equal(0, problem.GetProperty("diagnostics").GetArrayLength()));
        }

        [Fact]
        public async Task An_unrestricted_read_of_the_users_lists_every_unended_membership_and_a_wider_shape_carries_the_name_twins()
        {
            QueryRig rig = Rig();
            rig.Shape = MultilingualShape.All;
            rig.Grant((CoreResources.Role, AccessActions.Read, null), (CoreResources.User, AccessActions.Read, null));
            Script(rig, QueryRig.Page(), RoleRow());

            await using AsyncServiceScope scope = rig.Scope();
            EntitiesResult<Role> result = await Roles(scope).GetByIdAsync(RoleId, new DetailsRequest { Include = [RoleService<Role>.MembersExtra] }, Token);

            QuerySpec spec = Members(Assert.Single(rig.Script.Batches)).Spec;
            Assert.Equal(NotEnded, QueryRig.Render(spec.Filter));
            Assert.Equal("Id, RoleId, UserId, User.Name, User.Name2, User.Name3, User.Email, User.ImageId, User.IsActive, User.State, StartsAt, EndsAt, Notes", spec.Select);
            Assert.Equal([RoleService<Role>.MembersExtra], result.Extras!.Keys);
        }

        [Fact]
        public async Task A_caller_who_may_read_no_user_gets_no_members_extra_and_no_statement_for_it()
        {
            // The own-row criterion lets every member read their own user, so this composition drops it.
            QueryRig rig = Rig(static services => services.RemoveAll<IAccessCriteriaProvider>());
            rig.Grant((CoreResources.Role, AccessActions.Read, null));
            Script(rig, QueryRig.Page(11), RoleRow(Stored(1, CoreResources.User, "Fly")));

            await using AsyncServiceScope scope = rig.Scope();
            EntitiesResult<Role> result = await Roles(scope).GetByIdAsync(RoleId, new DetailsRequest { Include = [RoleService<Role>.MembersExtra, RoleService<Role>.ProblemsExtra] }, Token);

            ScriptedBatch batch = Assert.Single(rig.Script.Batches);
            Assert.DoesNotContain(batch.Of(ScriptedKind.Rows), static statement => statement.Spec.Root.Name == "core.RoleMembership");
            Assert.Equal([RoleService<Role>.ProblemsExtra], result.Extras!.Keys);
            Assert.Equal(1, result.Extras[RoleService<Role>.ProblemsExtra].GetArrayLength());
        }

        [Fact]
        public async Task A_users_filter_the_batch_refuses_as_too_complex_is_an_error_logged_with_the_roles_behind_it_and_nothing_runs()
        {
            QueryRig rig = Rig(static services => services.AddFakeLogging());
            rig.Grant((CoreResources.Role, AccessActions.Read, null), (CoreResources.User, AccessActions.Read, UsersFilter));
            Script(rig, QueryRig.Page(11), RoleRow());

            // --- The batch refuses the members' query as a filter past its compile ceilings ---
            ArgumentException ceiling = new("The filter exceeds the compile ceilings.");
            rig.Script.Refuses = statement => statement.Kind == ScriptedKind.Rows && statement.Spec.Root.Name == "core.RoleMembership" ? ceiling : null;

            await using AsyncServiceScope scope = rig.Scope();
            InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Roles(scope).GetByIdAsync(RoleId, new DetailsRequest { Include = [RoleService<Role>.MembersExtra] }, Token));

            // --- The ceiling's error is kept, logged once at Error with the roles behind the filter, and no batch ran ---
            Assert.Same(ceiling, refused.InnerException);
            FakeLogRecord logged = Assert.Single(scope.ServiceProvider.GetFakeLogCollector().GetSnapshot(), static record => record.Id.Id == AccessEvents.FilterTooComplex);
            Assert.Equal(LogLevel.Error, logged.Level);
            Assert.Same(ceiling, logged.Exception);
            Assert.Equal(
                ("1", "42", CoreResources.User, AccessActions.Read, "10"),
                (logged.GetStructuredStateValue("TenantId"), logged.GetStructuredStateValue("UserId"), logged.GetStructuredStateValue("Resource"), logged.GetStructuredStateValue("Action"), logged.GetStructuredStateValue("RoleIds")));
            Assert.Equal(0, rig.Script.RoundTrips);
            Assert.False(Assert.Single(rig.Script.Batches).IsExecuted);
        }

        [Fact]
        public async Task A_read_that_includes_neither_extra_adds_neither()
        {
            QueryRig rig = Rig();
            rig.Grant((CoreResources.Role, AccessActions.Read, null), (CoreResources.User, AccessActions.Read, null));
            Script(rig, QueryRig.Page(11), RoleRow());

            await using AsyncServiceScope scope = rig.Scope();
            EntitiesResult<Role> result = await Roles(scope).GetByIdAsync(RoleId, new DetailsRequest(), Token);

            Assert.DoesNotContain(Assert.Single(rig.Script.Batches).Of(ScriptedKind.Rows), static statement => statement.Spec.Root.Name == "core.RoleMembership");
            Assert.Null(result.Extras);
        }

        /// <summary>A rig over the default composition, disposed with the test.</summary>
        /// <param name="configure">Adjusts the services last; nothing when null.</param>
        /// <returns>The rig.</returns>
        private QueryRig Rig(Action<IServiceCollection>? configure = null)
        {
            QueryRig rig = new(static _ => { }, configure);
            _rigs.Add(rig);
            return rig;
        }

        /// <summary>Answers the details read: the role visible, its row, and the members' query.</summary>
        /// <param name="rig">The rig.</param>
        /// <param name="members">The page the members' query reads.</param>
        /// <param name="role">The role's row.</param>
        private static void Script(QueryRig rig, RowPage members, Role role)
        {
            _ = rig.Script
                .OnRows(statement => statement.Spec.Root.Name == "core.RoleMembership" ? members : QueryRig.Page(RoleId))
                .OnQuery(_ => new[] { role });
        }

        /// <summary>The role read, with its permissions.</summary>
        /// <param name="permissions">The permissions, in order.</param>
        /// <returns>The role.</returns>
        private static Role RoleRow(params Permission[] permissions)
        {
            return new Role { Id = RoleId, Name = "Clerks", Permissions = permissions };
        }

        /// <summary>A stored permission without a filter.</summary>
        /// <param name="id">The id.</param>
        /// <param name="resource">The stored resource.</param>
        /// <param name="action">The stored action.</param>
        /// <returns>The permission.</returns>
        private static Permission Stored(int id, string resource, string action)
        {
            return new Permission { Id = id, RoleId = RoleId, Resource = resource, Action = action };
        }

        /// <summary>The members' query of a batch.</summary>
        /// <param name="batch">The batch.</param>
        /// <returns>The statement.</returns>
        private static ScriptedStatement Members(ScriptedBatch batch)
        {
            return Assert.Single(batch.Of(ScriptedKind.Rows), static statement => statement.Spec.Root.Name == "core.RoleMembership");
        }

        /// <summary>A scope's roles service.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static RoleService<Role> Roles(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<RoleService<Role>>();
        }
    }
}
