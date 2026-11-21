// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;

namespace Tellma.Core.IntegrationTests.Access
{
    /// <summary>
    ///     The access endpoints against a real tenant database, through the action invoker as their
    ///     routes call them: the caller's own check with no round trip, another user's behind the
    ///     roles' read grant in two, and the holders in one, each listing only users the caller may
    ///     read. The class has its own databases, so it alone seeds their users and roles.
    /// </summary>
    /// <param name="fixture">The tenant databases and their composition.</param>
    [Trait("Category", "Integration")]
    public sealed class AccessServiceTests(ConnectDatabase fixture) : IClassFixture<ConnectDatabase>
    {
        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        // --- The check ---

        [Fact]
        public async Task The_caller_s_own_check_answers_in_order_with_no_round_trip()
        {
            int jobs = await fixture.SeedRoleAsync((CoreResources.Job, AccessActions.Read, null), (CoreResources.Schedule, AccessActions.Read, "Id > 0"));
            (int caller, string subject) = await fixture.SeedCallerAsync("Self checker");
            await fixture.SeedMembershipAsync(caller, jobs);
            await WarmAsync(subject);

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int start = access.RoundTrips;
            IReadOnlyList<AccessDecision> decisions = await CheckAsync(scope, null, (CoreResources.Job, AccessActions.Read), (CoreResources.Schedule, AccessActions.Read));
            IReadOnlyList<AccessDecision> byId = await CheckAsync(scope, caller, (CoreResources.Schedule, AccessActions.Read));

            Assert.Equal(0, access.RoundTrips - start);
            Assert.Equal([AccessOutcome.Unrestricted, AccessOutcome.Filtered], decisions.Select(static decision => decision.Outcome));
            Assert.Equal(AccessOutcome.Filtered, Assert.Single(byId).Outcome);
        }

        [Fact]
        public async Task Another_user_s_check_answers_their_decisions_in_two_round_trips_and_an_unknown_pair_as_denied()
        {
            (int target, _) = await fixture.SeedCallerAsync("Checked");
            int jobs = await fixture.SeedRoleAsync((CoreResources.Job, AccessActions.Read, null));
            await fixture.SeedMembershipAsync(target, jobs);
            (_, string subject) = await RoleReaderAsync("Other checker", usersFilter: null);
            await WarmAsync(subject);

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int start = access.RoundTrips;
            IReadOnlyList<AccessDecision> decisions = await CheckAsync(
                scope, target, (CoreResources.Job, AccessActions.Read), (CoreResources.Role, AccessActions.Read), ("core.Gone", AccessActions.Read), (CoreResources.Job, "Fly"));

            Assert.Equal(2, access.RoundTrips - start);
            Assert.Equal([AccessOutcome.Unrestricted, AccessOutcome.Denied, AccessOutcome.Denied, AccessOutcome.Denied], decisions.Select(static decision => decision.Outcome));
            Assert.Equal(AccessProblemCode.UnknownResource, Assert.Single(decisions[2].Problems).Code);
            Assert.Equal(AccessProblemCode.UnknownAction, Assert.Single(decisions[3].Problems).Code);
        }

        [Fact]
        public async Task Another_user_s_check_without_the_roles_read_grant_or_of_a_user_the_caller_may_not_read_is_refused()
        {
            (int vis, _) = await fixture.SeedCallerAsync("Vis to check");
            (int hid, _) = await fixture.SeedCallerAsync("Hid from check");
            (_, string reader) = await RoleReaderAsync("Filtered checker", usersFilter: "Name = 'Vis to check'");
            (_, string stranger) = await fixture.SeedCallerAsync("Checker without roles");

            // --- No roles grant: 403, whoever is named ---
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(stranger))
            {
                ForbiddenException refused = await Assert.ThrowsAsync<ForbiddenException>(() => CheckAsync(scope, vis, (CoreResources.Job, AccessActions.Read)));
                Assert.Equal((CoreResources.Role, AccessActions.Read), (refused.Resource, refused.Action));
            }

            // --- The roles grant, but a user the caller's read of the users hides: not found, as a missing one is ---
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(reader))
            {
                Assert.Equal(AccessOutcome.Denied, Assert.Single(await CheckAsync(scope, vis, (CoreResources.Role, AccessActions.Read))).Outcome);
                NotFoundException hidden = await Assert.ThrowsAsync<NotFoundException>(() => CheckAsync(scope, hid, (CoreResources.Job, AccessActions.Read)));
                NotFoundException missing = await Assert.ThrowsAsync<NotFoundException>(() => CheckAsync(scope, 987654, (CoreResources.Job, AccessActions.Read)));
                Assert.Equal((CoreResources.User, hid.ToString(CultureInfo.InvariantCulture)), (hidden.Resource, Assert.Single(hidden.Ids)));
                Assert.Equal(CoreResources.User, missing.Resource);
            }
        }

        // --- The holders ---

        [Fact]
        public async Task The_holders_count_a_hidden_holder_without_listing_them_in_one_round_trip()
        {
            (int vis, _) = await fixture.SeedCallerAsync("Vis holder");
            (int hid, _) = await fixture.SeedCallerAsync("Hid holder");
            int deleters = await fixture.SeedRoleAsync((CoreResources.OutgoingEmail, AccessActions.Delete, null));
            await fixture.SeedMembershipAsync(vis, deleters);
            await fixture.SeedMembershipAsync(hid, deleters);
            (_, string subject) = await RoleReaderAsync("Holders reader", usersFilter: "Name = 'Vis holder'");
            await WarmAsync(subject);

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int start = access.RoundTrips;
            AccessHolders holders = await HoldersAsync(scope, CoreResources.OutgoingEmail, AccessActions.Delete);

            Assert.Equal(1, access.RoundTrips - start);
            Assert.Equal([vis], holders.Holders.Select(static holder => holder.UserId));
            Assert.Equal(2, holders.Total);
            Assert.Equal(AccessOutcome.Denied, holders.Everyone);
        }

        [Fact]
        public async Task A_public_role_answers_everyone_and_an_ended_membership_lists_no_holder()
        {
            (int ended, _) = await fixture.SeedCallerAsync("Ended holder");
            int schedulers = await fixture.SeedRoleAsync((CoreResources.Schedule, AccessActions.Save, null));
            await fixture.SeedMembershipAsync(ended, schedulers, startsAt: "DATEADD(day, -2, SYSDATETIMEOFFSET())", endsAt: "DATEADD(day, -1, SYSDATETIMEOFFSET())");
            int everyone = await fixture.SeedRoleAsync((CoreResources.OutgoingEmail, AccessActions.Read, null));
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Roles] SET [IsPublic] = 1 WHERE [Id] = {everyone};"));
            (_, string subject) = await RoleReaderAsync("Public reader", usersFilter: null);

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            AccessHolders open = await HoldersAsync(scope, CoreResources.OutgoingEmail, AccessActions.Read);
            AccessHolders saving = await HoldersAsync(scope, CoreResources.Schedule, AccessActions.Save);

            Assert.Equal(AccessOutcome.Unrestricted, open.Everyone);
            Assert.Contains(open.Grants, grant => grant.RoleId == everyone && grant.IsPublic);
            Assert.DoesNotContain(saving.Holders, holder => holder.UserId == ended);
            Assert.Equal(0, saving.Total);
        }

        [Fact]
        public async Task The_holders_without_the_roles_read_grant_are_refused_and_an_unknown_pair_answers_its_problem()
        {
            (_, string stranger) = await fixture.SeedCallerAsync("Holders without roles");
            (_, string reader) = await RoleReaderAsync("Unknown pair reader", usersFilter: null);

            await using (AsyncServiceScope scope = await fixture.ScopeAsync(stranger))
            {
                ForbiddenException refused = await Assert.ThrowsAsync<ForbiddenException>(() => HoldersAsync(scope, CoreResources.Job, AccessActions.Read));
                Assert.Equal((CoreResources.Role, AccessActions.Read), (refused.Resource, refused.Action));
            }

            await using (AsyncServiceScope scope = await fixture.ScopeAsync(reader))
            {
                AccessHolders unknown = await HoldersAsync(scope, "core.Gone", AccessActions.Read);
                Assert.Equal(AccessProblemCode.UnknownResource, Assert.Single(unknown.Problems).Code);
                Assert.Empty(unknown.Holders);
            }
        }

        // --- Helpers ---

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

        /// <summary>Connects a caller once in a scope of their own, so the scopes after it find their connection cached.</summary>
        /// <param name="subject">The caller.</param>
        /// <returns>A task that completes when the caller is connected.</returns>
        private async Task WarmAsync(string subject)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            _ = await CheckAsync(scope, null, (CoreResources.Role, AccessActions.Read));
        }

        /// <summary>Calls the check through the action invoker, as its route does.</summary>
        /// <param name="scope">The caller's scope.</param>
        /// <param name="userId">The user checked; null for the caller.</param>
        /// <param name="pairs">The securables.</param>
        /// <returns>The decisions.</returns>
        private static async Task<IReadOnlyList<AccessDecision>> CheckAsync(AsyncServiceScope scope, int? userId, params (string Resource, string Action)[] pairs)
        {
            AccessCheckRequest request = new(userId, [.. pairs.Select(static pair => new SecurableRef(pair.Resource, pair.Action))]);
            return Assert.IsAssignableFrom<IReadOnlyList<AccessDecision>>(await InvokeAsync(scope, "check", request));
        }

        /// <summary>Calls the holders through the action invoker, as its route does.</summary>
        /// <param name="scope">The caller's scope.</param>
        /// <param name="resource">The securable's resource.</param>
        /// <param name="action">The securable's action.</param>
        /// <returns>The holders.</returns>
        private static async Task<AccessHolders> HoldersAsync(AsyncServiceScope scope, string resource, string action)
        {
            return Assert.IsType<AccessHolders>(await InvokeAsync(scope, "holders", new AccessHoldersRequest(resource, action)));
        }

        /// <summary>Calls one of the access endpoints through the action invoker.</summary>
        /// <param name="scope">The caller's scope.</param>
        /// <param name="name">The action's name.</param>
        /// <param name="body">The body.</param>
        /// <returns>The answer.</returns>
        private static Task<object?> InvokeAsync(AsyncServiceScope scope, string name, object body)
        {
            ApiActionDescriptor action = scope.ServiceProvider.GetRequiredService<IStackRegistry>().Services
                .Single(static service => service.Route == "access").Actions
                .Single(candidate => candidate.Name == name);
            return scope.ServiceProvider.GetRequiredService<IApiActionInvoker>().InvokeAsync(action, body, Token);
        }
    }
}
