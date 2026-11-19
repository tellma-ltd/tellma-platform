// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Tenancy;
using Tellma.Core.Users;
using static Tellma.Core.Tests.Access.AccessRulesHarness;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     The administrator bootstrap without a database: the membership it grants an existing user,
    ///     the scope and the environment it refuses, the name the provisioning step derives, and the
    ///     subject it holds for the save.
    /// </summary>
    public class TenantBootstrapperTests
    {
        /// <summary>The administrator role.</summary>
        private const int Administrators = WellKnownIds.AdministratorRoleId;

        // --- The membership an existing user gains ---

        [Fact]
        public void A_user_with_a_started_permanent_administrator_membership_gains_nothing()
        {
            Assert.Null(TenantBootstrapper<User>.Grant([Membership(9, Administrators, null, null)], Now));
            Assert.Null(TenantBootstrapper<User>.Grant([Membership(9, 3, null, null), Membership(10, Administrators, Now.AddDays(-1), null)], Now));
        }

        [Fact]
        public void A_user_without_one_gains_a_permanent_membership_beside_their_other_roles()
        {
            IReadOnlyList<RoleMembership>? granted = TenantBootstrapper<User>.Grant([Membership(9, 3, null, Now.AddDays(5))], Now);

            Assert.Equal([(9, 3, null, Now.AddDays(5)), (0, Administrators, null, null)], Windows(granted));
        }

        [Fact]
        public void A_future_or_time_bounded_administrator_membership_is_replaced_by_a_permanent_one_from_now()
        {
            IReadOnlyList<RoleMembership>? granted = TenantBootstrapper<User>.Grant(
                [Membership(9, Administrators, Now.AddDays(1), null), Membership(10, Administrators, Now.AddDays(-5), Now.AddDays(-4)), Membership(11, Administrators, Now.AddDays(-2), Now.AddDays(3))],
                Now);

            // The ended row stays as the record of its window; the new row starts where the last ended one ended.
            Assert.Equal([(10, Administrators, Now.AddDays(-5), Now.AddDays(-4)), (0, Administrators, Now.AddDays(-4), null)], Windows(granted));
        }

        [Fact]
        public void The_new_membership_starts_after_the_last_ended_one_so_no_two_windows_overlap()
        {
            IReadOnlyList<RoleMembership>? granted = TenantBootstrapper<User>.Grant(
                [Membership(9, Administrators, null, Now.AddDays(-10)), Membership(10, Administrators, Now.AddDays(-3), Now.AddDays(-1))],
                Now);

            Assert.Equal(
                [(9, Administrators, null, Now.AddDays(-10)), (10, Administrators, Now.AddDays(-3), Now.AddDays(-1)), (0, Administrators, Now.AddDays(-1), null)],
                Windows(granted));
            Assert.False(UserAccessRules<User>.Overlap(granted![1], granted[2]));
        }

        // --- The scope and the environment ---

        [Fact]
        public async Task A_user_s_scope_is_refused()
        {
            TenantBootstrapper<User> bootstrapper = Bootstrapper(Context(50), isDevelopment: true);

            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => bootstrapper.BootstrapAdministratorAsync(new TenantBootstrapRequest("ann@example.com", "ann", "en", null), TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task A_subject_outside_a_development_environment_is_refused()
        {
            TenantBootstrapper<User> bootstrapper = Bootstrapper(Context(null) with { Kind = PrincipalKind.System }, isDevelopment: false);

            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => bootstrapper.BootstrapAdministratorAsync(new TenantBootstrapRequest("ann@example.com", "ann", "en", "sub-1"), TestContext.Current.CancellationToken));
        }

        [Theory]
        [InlineData("", "ann")]
        [InlineData("ann@example.com", " ")]
        public async Task A_blank_email_or_name_is_refused(string email, string name)
        {
            TenantBootstrapper<User> bootstrapper = Bootstrapper(Context(null) with { Kind = PrincipalKind.System }, isDevelopment: true);

            _ = await Assert.ThrowsAsync<ArgumentException>(() => bootstrapper.BootstrapAdministratorAsync(new TenantBootstrapRequest(email, name, "en", null), TestContext.Current.CancellationToken));
        }

        // --- The provisioning step's name, and the held subject ---

        [Theory]
        [InlineData("ann@example.com", "ann")]
        [InlineData("  ann.lee@a@example.com ", "ann.lee@a")]
        [InlineData("ann", "ann")]
        [InlineData("@example.com", "@example.com")]
        public void The_step_names_the_administrator_by_the_address_s_local_part(string email, string name)
        {
            Assert.Equal(name, BootstrapAdministratorStep.LocalPart(email));
        }

        [Fact]
        public void One_subject_is_held_at_a_time_and_released_with_its_handle()
        {
            BootstrapSubject subject = new();

            using (subject.Hold("ann@example.com", "sub-1"))
            {
                Assert.Equal(("ann@example.com", "sub-1"), (subject.Email, subject.Subject));
                _ = Assert.Throws<InvalidOperationException>(() => subject.Hold("ben@example.com", "sub-2"));
            }

            Assert.Equal((null, null), (subject.Email, subject.Subject));
        }

        // --- Fixtures ---

        /// <summary>A membership.</summary>
        /// <param name="id">The id; 0 for a new one.</param>
        /// <param name="roleId">The role.</param>
        /// <param name="startsAt">The start.</param>
        /// <param name="endsAt">The end.</param>
        /// <returns>The membership.</returns>
        private static RoleMembership Membership(int id, int roleId, DateTimeOffset? startsAt, DateTimeOffset? endsAt)
        {
            return new RoleMembership { Id = id, RoleId = roleId, StartsAt = startsAt, EndsAt = endsAt };
        }

        /// <summary>The memberships as their ids, roles and windows.</summary>
        /// <param name="memberships">The memberships.</param>
        /// <returns>The windows, in order.</returns>
        private static IReadOnlyList<(int Id, int RoleId, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt)> Windows(IReadOnlyList<RoleMembership>? memberships)
        {
            Assert.NotNull(memberships);
            return [.. memberships.Select(static membership => (membership.Id, membership.RoleId, membership.StartsAt, membership.EndsAt))];
        }

        /// <summary>A bootstrapper whose refusals come before any read or save.</summary>
        /// <param name="context">The scope's context.</param>
        /// <param name="isDevelopment">Whether the environment is a development one.</param>
        /// <returns>The bootstrapper.</returns>
        private static TenantBootstrapper<User> Bootstrapper(RequestContext context, bool isDevelopment)
        {
            return new TenantBootstrapper<User>(
                new UserService<User>(new RefusingPipelines()),
                new SchemaDatabase(Schema),
                new FixedContextAccessor(context),
                new TenancyEnvironment("fixture", isDevelopment),
                new BootstrapSubject(),
                TimeProvider.System);
        }
    }
}
