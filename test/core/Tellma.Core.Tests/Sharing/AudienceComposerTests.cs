// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Sharing;
using Tellma.Core.Sharing;

namespace Tellma.Core.Tests.Sharing
{
    /// <summary>
    ///     The audience's merge over facts a test states: one grant per owner, share, stored permission
    ///     and row seen through, each with its members and the members who cannot read each data
    ///     securable; the members with every reason and their highest access, cut at the cap; and
    ///     nothing listed one by one to a caller who cannot read the roles.
    /// </summary>
    public sealed class AudienceComposerTests
    {
        /// <summary>The instant every grant was made.</summary>
        private static readonly DateTimeOffset SharedAt = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

        /// <summary>Orders users by id, standing in for the display names.</summary>
        private static readonly IComparer<int> ById = Comparer<int>.Default;

        [Fact]
        public void A_user_grant_and_a_role_grant_answer_their_members_and_who_cannot_read_the_data()
        {
            // Report 42, owned by 1, is shared with Omar (9) to view and with Sales (4) to edit; of
            // the nine members of Sales, 9 is not one, and 11 and 12 cannot read the root.
            int[] sales = [10, 11, 12, 13, 14, 15, 16, 17, 18];
            AudienceSubject row = new(null, 1, true, [UserShare(1, 9, ShareAccess.View), RoleShare(2, 4, ShareAccess.Edit)], []);
            DataReaders root = Readers(1, 10, 13, 14, 15, 16, 17, 18);

            AudienceComposition audience = Compose(Facts(20, [row], Roles((4, sales)), root));

            (AudienceReasonKind Kind, int? User, int? Role, AudienceAccess Access, int Members, int Lacking)[] grants =
            [
                (AudienceReasonKind.Owner, 1, null, AudienceAccess.Owner, 1, 0),
                (AudienceReasonKind.UserShare, 9, null, AudienceAccess.View, 1, 1),
                (AudienceReasonKind.RoleShare, null, 4, AudienceAccess.Edit, 9, 2),
            ];
            Assert.Equal(
                grants,
                audience.Grants.Select(static grant => (grant.Kind, grant.UserId, grant.RoleId, grant.Access, grant.Members, grant.CannotRead[0])));
            Assert.Equal((11, false, 3), (audience.Total, audience.Capped, audience.CannotRead[0]));
            AudienceMember omar = Assert.Single(audience.Members!, static member => member.UserId == 9);
            Assert.Equal((AudienceAccess.View, 1), (omar.Access, omar.Reasons.Count));
            Assert.Equal([0], omar.CannotRead);
            Assert.Equal(AudienceAccess.Owner, Assert.Single(audience.Members!, static member => member.UserId == 1).Access);
        }

        [Fact]
        public void A_member_reached_twice_has_both_reasons_and_the_higher_access()
        {
            AudienceSubject row = new(null, null, false, [UserShare(1, 5, ShareAccess.View)], [new AudiencePermission(70, 3, false, AudienceAccess.Edit, true)]);

            AudienceComposition audience = Compose(Facts(10, [row], Roles((3, [5]))));

            AudienceMember member = Assert.Single(audience.Members!);
            Assert.Equal(AudienceAccess.Edit, member.Access);
            Assert.Equal([AudienceReasonKind.UserShare, AudienceReasonKind.Permission], member.Reasons.Select(static reason => reason.Kind));
            AudienceReason permission = member.Reasons[1];
            Assert.Equal((3, 70, true), (permission.RoleId, permission.PermissionId, permission.Filtered));
            Assert.Equal(1, audience.Total);
        }

        [Fact]
        public void A_deactivated_grantee_and_an_inactive_role_stay_in_the_grants_with_no_members()
        {
            AudienceShare gone = UserShare(1, 9, ShareAccess.View) with { UserIsAudience = false };
            AudienceShare inactive = RoleShare(2, 4, ShareAccess.View) with { RoleIsActive = false };
            AudienceSubject row = new(null, 9, false, [gone, inactive], []);

            AudienceComposition audience = Compose(Facts(10, [row], Roles((4, [5, 6]))));

            Assert.Equal([0, 0, 0], audience.Grants.Select(static grant => grant.Members));
            Assert.Equal(0, audience.Total);
            Assert.Empty(audience.Members!);
        }

        [Fact]
        public void The_everyone_grant_counts_every_audience_user_and_lists_the_first_by_name_up_to_the_cap()
        {
            AudienceSubject row = new(null, null, false, [EveryoneShare(1)], []);
            DataReaders root = new(false, 600, new HashSet<int> { 2 });

            AudienceComposition audience = Compose(Facts(1000, [row], Roles(), root, listed: [3, 2, 1]), cap: 2);

            Assert.Equal((1000, true, 400), (audience.Total, audience.Capped, audience.CannotRead[0]));
            Assert.Equal([1, 2], audience.Members!.Select(static member => member.UserId));
            Assert.Equal([[0], []], audience.Members!.Select(static member => member.CannotRead));
            Assert.Equal((1000, 400), (audience.Grants[0].Members, audience.Grants[0].CannotRead[0]));
        }

        [Fact]
        public void A_public_role_counts_every_audience_user_through_its_grant_and_its_permission()
        {
            AudienceSubject row = new(null, null, false, [RoleShare(1, 4, ShareAccess.View) with { RoleIsPublic = true }], [new AudiencePermission(80, 4, true, AudienceAccess.View, false)]);

            AudienceComposition audience = Compose(Facts(7, [row], Roles(), listed: [1, 2]));

            Assert.Equal([7, 7], audience.Grants.Select(static grant => grant.Members));
            Assert.Equal((7, true), (audience.Total, audience.Capped));
        }

        [Fact]
        public void A_criterion_that_admits_every_user_leaves_no_one_lacking_the_data()
        {
            AudienceSubject row = new(null, null, false, [EveryoneShare(1)], []);

            AudienceComposition audience = Compose(Facts(50, [row], Roles(), new DataReaders(true, 50, new HashSet<int>()), listed: [1]));

            Assert.Equal(0, audience.CannotRead[0]);
            Assert.Equal(0, audience.Grants[0].CannotRead[0]);
        }

        [Fact]
        public void A_caller_who_cannot_read_the_roles_sees_the_grants_and_counts_but_no_member()
        {
            AudienceSubject row = new(null, 1, true, [RoleShare(1, 4, ShareAccess.View)], []);

            AudienceComposition audience = Compose(Facts(10, [row], Roles((4, [2, 3]))), visible: false);

            Assert.Null(audience.Members);
            Assert.False(audience.Capped);
            Assert.Equal((3, 2), (audience.Total, audience.Grants.Count));
            Assert.Equal([1], audience.Users.Order());
        }

        [Fact]
        public void A_row_seen_through_adds_one_grant_whose_members_see_this_row_through_it()
        {
            AudienceSubject row = new(null, 1, true, [], []);
            AudienceSubject dashboard = new(77, 2, true, [UserShare(5, 3, ShareAccess.Edit)], []);

            AudienceComposition audience = Compose(Facts(10, [row, dashboard], Roles()));

            AudienceGrant through = audience.Grants[^1];
            Assert.Equal((AudienceReasonKind.Through, 77, AudienceAccess.View, 2), (through.Kind, through.ThroughId, through.Access, through.Members));
            AudienceMember seen = Assert.Single(audience.Members!, static member => member.UserId == 3);
            Assert.Equal((AudienceAccess.View, AudienceReasonKind.Through, 77), (seen.Access, Assert.Single(seen.Reasons).Kind, seen.Reasons[0].ThroughId));
            Assert.Equal(3, audience.Total);
        }

        [Fact]
        public void The_members_are_cut_at_the_cap_while_the_total_stays_exact()
        {
            AudienceSubject row = new(null, null, false, [RoleShare(1, 4, ShareAccess.View)], []);

            AudienceComposition audience = Compose(Facts(10, [row], Roles((4, [5, 4, 3, 2, 1]))), cap: 3);

            Assert.Equal((5, true), (audience.Total, audience.Capped));
            Assert.Equal([1, 2, 3], audience.Members!.Select(static member => member.UserId));
        }

        /// <summary>Composes facts with the members visible and a cap of a hundred unless the test says otherwise.</summary>
        /// <param name="facts">The facts.</param>
        /// <param name="visible">Whether the caller sees the members.</param>
        /// <param name="cap">The most members listed.</param>
        /// <returns>The composition.</returns>
        private static AudienceComposition Compose(AudienceFacts facts, bool visible = true, int cap = 100)
        {
            return AudienceComposer.Compose(facts, visible, cap, ById);
        }

        /// <summary>The facts of an audience.</summary>
        /// <param name="audience">The audience size.</param>
        /// <param name="subjects">The rows.</param>
        /// <param name="roles">The members of each role.</param>
        /// <param name="readers">Who reads each data securable, if any.</param>
        /// <param name="listed">The first users by name, when a grant reaches everyone.</param>
        /// <returns>The facts.</returns>
        private static AudienceFacts Facts(int audience, IReadOnlyList<AudienceSubject> subjects, IReadOnlyDictionary<int, IReadOnlyList<int>> roles, DataReaders? readers = null, IReadOnlyList<int>? listed = null)
        {
            return new AudienceFacts(audience, subjects, roles, listed ?? [], readers is null ? [] : [readers]);
        }

        /// <summary>The members of some roles.</summary>
        /// <param name="roles">Each role with its members.</param>
        /// <returns>The map.</returns>
        private static Dictionary<int, IReadOnlyList<int>> Roles(params (int Role, int[] Members)[] roles)
        {
            return roles.ToDictionary(static role => role.Role, static role => (IReadOnlyList<int>)role.Members);
        }

        /// <summary>A data securable whose readers are some named users, and only they.</summary>
        /// <param name="users">The readers.</param>
        /// <returns>The readers.</returns>
        private static DataReaders Readers(params int[] users)
        {
            return new DataReaders(false, users.Length, users.ToHashSet());
        }

        /// <summary>A grant to an audience user.</summary>
        /// <param name="id">The grant.</param>
        /// <param name="user">The user.</param>
        /// <param name="access">The access.</param>
        /// <returns>The grant.</returns>
        private static AudienceShare UserShare(int id, int user, ShareAccess access)
        {
            return new AudienceShare(id, user, null, access, 1, SharedAt, true, false, false);
        }

        /// <summary>A grant to an active role that is not public.</summary>
        /// <param name="id">The grant.</param>
        /// <param name="role">The role.</param>
        /// <param name="access">The access.</param>
        /// <returns>The grant.</returns>
        private static AudienceShare RoleShare(int id, int role, ShareAccess access)
        {
            return new AudienceShare(id, null, role, access, 1, SharedAt, false, true, false);
        }

        /// <summary>The Everyone grant.</summary>
        /// <param name="id">The grant.</param>
        /// <returns>The grant.</returns>
        private static AudienceShare EveryoneShare(int id)
        {
            return new AudienceShare(id, null, null, ShareAccess.View, 1, SharedAt, false, false, false);
        }
    }
}
