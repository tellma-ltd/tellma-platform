// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Access;
using static Tellma.Core.Tests.Access.AccessRulesHarness;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     The users' access rules: the system user, the kind and the email, the memberships'
    ///     windows and cap, the caller's own administrator membership, the escalation test of a
    ///     granted role, and the deletes and actions they validate.
    /// </summary>
    public sealed class UserAccessRulesTests : IDisposable
    {
        /// <summary>The caller of every test.</summary>
        private const int CallerId = 50;

        /// <summary>Another user.</summary>
        private const int OtherId = 51;

        /// <summary>The administrator role.</summary>
        private const int Administrators = WellKnownIds.AdministratorRoleId;

        /// <summary>The wildcard action.</summary>
        private const string Any = AccessActions.Wildcard;

        /// <summary>The instruments, logs and clock.</summary>
        private readonly AccessRulesHarness _harness = new();

        /// <summary>The loader, whose roles hold what each test gives them.</summary>
        private readonly RulesLoader _loader = new();

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
        }

        // --- The system user, the kind and the email ---

        [Fact]
        public async Task The_system_user_is_refused_on_save_delete_and_every_action_and_nothing_more_is_said_of_it()
        {
            User system = new() { Id = WellKnownIds.SystemUserId, Kind = UserKind.System, Name = "System", State = UserState.Joined };

            Assert.Equal([("[0]", AccessCodes.SystemUserImmutable)], await SaveAsync(Admin(), (system, system)));
            Assert.Equal([("Ids[0]", AccessCodes.SystemUserImmutable)], await DeleteAsync(Admin(), [1], system));
            Assert.Equal([("Ids[0]", AccessCodes.SystemUserImmutable)], await ActionAsync(Admin(), "activate", [1], system));
            Assert.Equal([("Ids[0]", AccessCodes.SystemUserImmutable)], await ActionAsync(Admin(), "rename", [1], system));
        }

        [Fact]
        public async Task The_kind_and_the_email_agree()
        {
            User system = new() { Id = 60, Kind = UserKind.System, Name = "Second system" };
            User nobody = new() { Id = 61, Kind = UserKind.Individual, Name = "No email" };
            User service = new() { Id = 62, Kind = UserKind.Service, Name = "Service", Email = "svc@example.com" };
            User person = new() { Id = 63, Kind = UserKind.Individual, Name = "Ann", Email = "ann@example.com" };

            Assert.Equal(
                [("[0].Kind", AccessCodes.KindNotAllowed), ("[1].Email", ValidationCodes.Required), ("[2].Email", AccessCodes.EmailNotAllowed)],
                await SaveAsync(Admin(), (system, null), (nobody, null), (service, null), (person, null)));
        }

        [Fact]
        public async Task An_invited_user_s_email_is_locked_and_a_new_user_s_is_not()
        {
            User invited = Individual(60, "ann@example.com", UserState.Invited);
            User fresh = Individual(61, "ben@example.com", UserState.New);

            Assert.Equal(
                [("[0].Email", AccessCodes.EmailLockedAfterInvite)],
                await SaveAsync(Admin(), (Individual(60, "anne@example.com", UserState.Invited), invited), (Individual(61, "benny@example.com", UserState.New), fresh)));
        }

        // --- The memberships' windows ---

        [Fact]
        public async Task A_window_that_ends_before_it_starts_is_reported_at_its_end_and_overlaps_nothing()
        {
            User user = WithMemberships(Individual(60), Membership(0, 3, Now.AddDays(2), Now.AddDays(1)), Membership(0, 3, null, null));

            Assert.Equal([("[0].RoleMemberships[0].EndsAt", AccessCodes.MembershipEndsBeforeStart)], await SaveAsync(Admin(), (user, null)));
        }

        [Fact]
        public async Task A_new_or_changed_window_that_already_ended_is_refused_and_an_ended_window_saved_as_it_was_passes()
        {
            RoleMembership ended = Membership(9, 3, Now.AddDays(-10), Now.AddDays(-1));
            User stored = WithMemberships(Individual(60), ended);
            User saved = WithMemberships(Individual(60), Membership(9, 3, Now.AddDays(-10), Now.AddDays(-1)), Membership(0, 4, Now.AddDays(-3), Now.AddDays(-2)));
            User moved = WithMemberships(Individual(60), Membership(9, 3, Now.AddDays(-10), Now.AddDays(-2)));

            Assert.Equal([("[0].RoleMemberships[1].EndsAt", AccessCodes.MembershipEndsInPast)], await SaveAsync(Admin(), (saved, stored)));
            Assert.Equal([("[0].RoleMemberships[0].EndsAt", AccessCodes.MembershipEndsInPast)], await SaveAsync(Admin(), (moved, stored)));
        }

        [Fact]
        public async Task Two_windows_of_one_role_never_overlap_a_permanent_one_overlaps_every_other_and_adjacent_ones_do_not()
        {
            User adjacent = WithMemberships(Individual(60), Membership(0, 3, Now.AddDays(1), Now.AddDays(2)), Membership(0, 3, Now.AddDays(2), Now.AddDays(3)));
            User overlapping = WithMemberships(Individual(61), Membership(0, 3, Now.AddDays(1), Now.AddDays(3)), Membership(0, 3, Now.AddDays(2), Now.AddDays(4)));
            User permanent = WithMemberships(Individual(62), Membership(0, 3, Now.AddDays(5), Now.AddDays(6)), Membership(0, 3, null, null));
            User roles = WithMemberships(Individual(63), Membership(0, 3, null, null), Membership(0, 4, null, null));

            Assert.Equal(
                [("[1].RoleMemberships[1].StartsAt", AccessCodes.MembershipOverlap), ("[2].RoleMemberships[1].StartsAt", AccessCodes.MembershipOverlap)],
                await SaveAsync(Admin(), (adjacent, null), (overlapping, null), (permanent, null), (roles, null)));
        }

        [Fact]
        public async Task More_memberships_than_the_cap_that_have_not_ended_are_refused_and_ended_ones_do_not_count()
        {
            _harness.Options.MaxRolesPerUser = 2;
            RoleMembership ended = Membership(9, 5, Now.AddDays(-10), Now.AddDays(-1));
            User stored = WithMemberships(Individual(60), ended);
            User within = WithMemberships(Individual(60), Membership(9, 5, Now.AddDays(-10), Now.AddDays(-1)), Membership(0, 3, null, null), Membership(0, 4, null, null));
            User beyond = WithMemberships(Individual(61), Membership(0, 3, null, null), Membership(0, 4, null, null), Membership(0, 6, Now.AddDays(1), null));

            Assert.Equal([("[1].RoleMemberships", AccessCodes.TooManyRoles)], await SaveAsync(Admin(), (within, stored), (beyond, null)));
        }

        // --- The caller's own administrator membership ---

        [Fact]
        public async Task The_caller_cannot_delete_their_own_administrator_membership()
        {
            User stored = WithMemberships(Individual(CallerId), Membership(9, Administrators, null, null));

            Assert.Equal([("[0].RoleMemberships", AccessCodes.CannotRemoveOwnAdministratorMembership)], await SaveAsync(Admin(), (WithMemberships(Individual(CallerId)), stored)));
            Assert.Equal([AccessTelemetryNames.GuardOwnAdministratorMembership], _harness.GuardTags);
        }

        [Fact]
        public async Task The_caller_cannot_end_defer_or_move_their_own_administrator_membership()
        {
            User stored = WithMemberships(Individual(CallerId), Membership(9, Administrators, null, null));
            User ending = WithMemberships(Individual(CallerId), Membership(9, Administrators, null, Now.AddDays(30)));
            User deferred = WithMemberships(Individual(CallerId), Membership(9, Administrators, Now.AddDays(1), null));
            User moved = WithMemberships(Individual(CallerId), Membership(9, 3, null, null));

            Assert.Equal([("[0].RoleMemberships[0].EndsAt", AccessCodes.CannotRemoveOwnAdministratorMembership)], await SaveAsync(Admin(), (ending, stored)));
            Assert.Equal([("[0].RoleMemberships[0].StartsAt", AccessCodes.CannotRemoveOwnAdministratorMembership)], await SaveAsync(Admin(), (deferred, stored)));
            Assert.Equal([("[0].RoleMemberships[0].RoleId", AccessCodes.CannotRemoveOwnAdministratorMembership)], await SaveAsync(Admin(), (moved, stored)));
        }

        [Fact]
        public async Task The_caller_cannot_move_the_end_of_their_own_ending_administrator_membership_but_may_clear_it()
        {
            User stored = WithMemberships(Individual(CallerId), Membership(9, Administrators, null, Now.AddDays(30)));
            User moved = WithMemberships(Individual(CallerId), Membership(9, Administrators, null, Now.AddDays(10)));
            User cleared = WithMemberships(Individual(CallerId), Membership(9, Administrators, null, null));
            User started = WithMemberships(Individual(CallerId), Membership(9, Administrators, Now.AddDays(-1), Now.AddDays(30)));

            Assert.Equal([("[0].RoleMemberships[0].EndsAt", AccessCodes.CannotRemoveOwnAdministratorMembership)], await SaveAsync(Admin(), (moved, stored)));
            Assert.Empty(await SaveAsync(Admin(), (cleared, stored)));
            Assert.Empty(await SaveAsync(Admin(), (started, stored)));
        }

        [Fact]
        public async Task Another_user_s_administrator_membership_and_the_caller_s_ended_one_may_be_removed()
        {
            User other = WithMemberships(Individual(OtherId), Membership(9, Administrators, null, null));
            User ended = WithMemberships(Individual(CallerId), Membership(10, Administrators, Now.AddDays(-10), Now.AddDays(-1)));

            Assert.Empty(await SaveAsync(Admin(), (WithMemberships(Individual(OtherId)), other), (WithMemberships(Individual(CallerId)), ended)));
            Assert.Empty(_harness.GuardTags);
        }

        // --- Escalation through a granted role ---

        [Fact]
        public async Task A_membership_of_a_role_holding_a_pair_the_caller_lacks_is_refused_counted_and_logged()
        {
            _loader.Permissions[3] = [Pair(Widget, AccessActions.Save)];
            _loader.Permissions[4] = [Pair(Widget, AccessActions.Read)];
            UserAccess caller = Caller(CallerId, (Widget, AccessActions.Read));
            User user = WithMemberships(Individual(OtherId), Membership(0, 4, null, null), Membership(0, 3, null, null));

            Assert.Equal([("[0].RoleMemberships[1].RoleId", AccessCodes.MembershipEscalation)], await SaveAsync(caller, (user, null)));
            Assert.Equal([AccessTelemetryNames.GuardEscalation], _harness.GuardTags);
            Assert.Single(_harness.Logs(AccessEvents.EscalationRejected));
            Assert.Equal([[3, 4]], _loader.PermissionLoads);
        }

        [Fact]
        public async Task Reading_through_a_caller_s_other_action_on_the_resource_passes()
        {
            _loader.Permissions[4] = [Pair(Widget, AccessActions.Read)];
            UserAccess caller = Caller(CallerId, (Widget, AccessActions.Save));

            Assert.Empty(await SaveAsync(caller, (WithMemberships(Individual(OtherId), Membership(0, 4, null, null)), null)));
        }

        [Fact]
        public async Task An_unchanged_membership_is_not_tested_and_a_widened_one_is()
        {
            _loader.Permissions[3] = [Pair(Widget, AccessActions.Save)];
            UserAccess caller = Caller(CallerId, (Widget, AccessActions.Read));
            User stored = WithMemberships(Individual(OtherId), Membership(9, 3, Now.AddDays(-1), Now.AddDays(10)));
            User unchanged = WithMemberships(Individual(OtherId), Membership(9, 3, Now.AddDays(-1), Now.AddDays(10)));
            User narrowed = WithMemberships(Individual(OtherId), Membership(9, 3, Now.AddDays(-1), Now.AddDays(5)));
            User widened = WithMemberships(Individual(OtherId), Membership(9, 3, Now.AddDays(-1), Now.AddDays(20)));

            Assert.Empty(await SaveAsync(caller, (unchanged, stored)));
            Assert.Empty(await SaveAsync(caller, (narrowed, stored)));
            Assert.Equal([("[0].RoleMemberships[0].RoleId", AccessCodes.MembershipEscalation)], await SaveAsync(caller, (widened, stored)));
        }

        [Theory]
        [InlineData(Widget, Any, Widget, AccessActions.Save, true)]
        [InlineData(Widget, Any, Widget, Any, true)]
        [InlineData(Widget, Any, "*", AccessActions.Save, false)]
        [InlineData(Widget, Any, Kind, AccessActions.Read, false)]
        [InlineData("*", AccessActions.Save, Widget, AccessActions.Save, true)]
        [InlineData("*", AccessActions.Save, "*", AccessActions.Save, true)]
        [InlineData("*", AccessActions.Save, Widget, Any, false)]
        [InlineData("*", AccessActions.Save, Kind, AccessActions.Read, false)]
        [InlineData("*", Any, "*", Any, true)]
        [InlineData("*", Any, Kind, AccessActions.Read, true)]
        [InlineData(Widget, AccessActions.Save, "fx.Gadget", AccessActions.Read, true)]
        [InlineData(Widget, AccessActions.Read, "fx.Gadget", AccessActions.Save, false)]
        public async Task A_role_is_granted_only_when_the_caller_holds_each_of_its_permissions_unrestricted(string heldResource, string heldAction, string resource, string action, bool granted)
        {
            _loader.Permissions[3] = [Pair(resource, action)];
            User user = WithMemberships(Individual(OtherId), Membership(0, 3, null, null));

            IReadOnlyList<(string Path, string Code)> errors = await SaveAsync(Caller(CallerId, (heldResource, heldAction)), (user, null));

            (string Path, string Code)[] expected = granted ? [] : [("[0].RoleMemberships[0].RoleId", AccessCodes.MembershipEscalation)];
            Assert.Equal(expected, errors);
        }

        [Fact]
        public async Task A_granted_role_whose_permissions_the_load_did_not_answer_fails_the_save_rather_than_passing_it()
        {
            _loader.Permissions[3] = [Pair(Widget, AccessActions.Save)];
            _loader.Omitted.Add(3);
            User user = WithMemberships(Individual(OtherId), Membership(0, 3, null, null));

            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(Caller(CallerId, (Widget, AccessActions.Read)), (user, null)));
        }

        [Fact]
        public async Task A_caller_holding_the_unrestricted_wildcard_pair_grants_any_role_and_loads_nothing()
        {
            _loader.Permissions[3] = [Pair("*", AccessActions.Wildcard)];

            Assert.Empty(await SaveAsync(Admin(), (WithMemberships(Individual(OtherId), Membership(0, 3, null, null)), null)));
            Assert.Empty(_loader.PermissionLoads);
        }

        // --- Deletes and actions ---

        [Fact]
        public async Task The_caller_cannot_delete_themselves_and_only_a_new_user_is_deleted()
        {
            User caller = Individual(CallerId, state: UserState.New);
            User invited = Individual(60, state: UserState.Invited);
            User fresh = Individual(61, state: UserState.New);

            // The ids name the targets in another order than they loaded in.
            Assert.Equal(
                [("Ids[2]", AccessCodes.CannotDeleteSelf), ("Ids[0]", AccessCodes.OnlyNewUsersDeletable)],
                await DeleteAsync(Admin(), [60, 61, CallerId], caller, invited, fresh));
            Assert.Equal([AccessTelemetryNames.GuardSelfDeletion], _harness.GuardTags);
        }

        [Fact]
        public async Task The_caller_cannot_deactivate_themselves_and_may_deactivate_another_or_activate_themselves()
        {
            Assert.Equal([("Ids[0]", AccessCodes.CannotDeactivateSelf)], await ActionAsync(Admin(), "deactivate", [CallerId], Individual(CallerId)));
            Assert.Empty(await ActionAsync(Admin(), "deactivate", [OtherId], Individual(OtherId)));
            Assert.Empty(await ActionAsync(Admin(), "activate", [CallerId], Individual(CallerId)));
            Assert.Equal([AccessTelemetryNames.GuardSelfDeactivation], _harness.GuardTags);
        }

        // --- Fixtures ---

        /// <summary>The caller holding the unrestricted wildcard pair.</summary>
        /// <returns>The caller's set.</returns>
        private static UserAccess Admin()
        {
            return Caller(CallerId, ("*", AccessActions.Wildcard));
        }

        /// <summary>An individual.</summary>
        /// <param name="id">The id.</param>
        /// <param name="email">The email; one made from the id when null.</param>
        /// <param name="state">The state.</param>
        /// <returns>The user.</returns>
        private static User Individual(int id, string? email = null, UserState state = UserState.Joined)
        {
            return new User { Id = id, Kind = UserKind.Individual, Name = "User", Email = email ?? $"user{id}@example.com", State = state };
        }

        /// <summary>A user with memberships.</summary>
        /// <param name="user">The user.</param>
        /// <param name="memberships">The memberships.</param>
        /// <returns>The same user.</returns>
        private static User WithMemberships(User user, params RoleMembership[] memberships)
        {
            user.RoleMemberships = memberships;
            return user;
        }

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

        /// <summary>A permission of a role, as the escalation test loads it.</summary>
        /// <param name="resource">The resource.</param>
        /// <param name="action">The action.</param>
        /// <returns>The permission.</returns>
        private static Permission Pair(string resource, string action)
        {
            return new Permission { Resource = resource, Action = action };
        }

        /// <summary>The rules under a caller's set.</summary>
        /// <param name="caller">The caller's set.</param>
        /// <returns>The rules.</returns>
        private UserAccessRules<User> Rules(UserAccess caller)
        {
            return new UserAccessRules<User>(new FixedEvaluator(caller), Registry, _harness.AccessOptions(), _harness.Time, _harness.Metrics, _harness.LoggerFactory);
        }

        /// <summary>Validates a save.</summary>
        /// <param name="caller">The caller's set.</param>
        /// <param name="rows">The payload with the before images.</param>
        /// <returns>The errors.</returns>
        private async Task<IReadOnlyList<(string Path, string Code)>> SaveAsync(UserAccess caller, params (User Row, User? Before)[] rows)
        {
            SaveContext<User> context = Save(_loader, CallerId, rows);
            await Rules(caller).ValidateAsync(context);
            return Errors(context.Errors);
        }

        /// <summary>Validates a delete.</summary>
        /// <param name="caller">The caller's set.</param>
        /// <param name="ids">The ids asked.</param>
        /// <param name="targets">The targets.</param>
        /// <returns>The errors.</returns>
        private async Task<IReadOnlyList<(string Path, string Code)>> DeleteAsync(UserAccess caller, IReadOnlyList<int> ids, params User[] targets)
        {
            DeleteContext<User, int> context = Delete(_loader, CallerId, ids, targets);
            await Rules(caller).ValidateDeleteAsync(context);
            return Errors(context.Errors);
        }

        /// <summary>Validates an action.</summary>
        /// <param name="caller">The caller's set.</param>
        /// <param name="action">The action.</param>
        /// <param name="ids">The ids asked.</param>
        /// <param name="targets">The targets.</param>
        /// <returns>The errors.</returns>
        private async Task<IReadOnlyList<(string Path, string Code)>> ActionAsync(UserAccess caller, string action, IReadOnlyList<int> ids, params User[] targets)
        {
            ActionContext<User, int> context = Action(_loader, CallerId, ids, targets);
            await Rules(caller).ValidateActionAsync(action, context);
            return Errors(context.Errors);
        }
    }
}
