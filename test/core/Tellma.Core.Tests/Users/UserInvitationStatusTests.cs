// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.Users;

namespace Tellma.Core.Tests.Users
{
    /// <summary>
    ///     How far a user's invitation got: no delivery before an invitation or after joining, a
    ///     delivery that was never expected for an account that already held a credential, and else
    ///     the identity server's answer for the user's account, when it gave one.
    /// </summary>
    public sealed class UserInvitationStatusTests
    {
        /// <summary>The identity server's answer for the invited account.</summary>
        private static readonly IdentityDeliveryStatus Sent = new("sub-ada", IdentityDeliveryState.Sent, true, UsersKit.Now, null, null);

        /// <summary>The answers, by subject.</summary>
        private static readonly Dictionary<string, IdentityDeliveryStatus> Deliveries = new(StringComparer.Ordinal) { ["sub-ada"] = Sent };

        [Fact]
        public void A_new_user_has_no_delivery_and_echoes_the_last_error()
        {
            User user = new() { Id = 3, State = UserState.New, LastInviteError = "account_disabled" };

            Assert.Equal(new InvitationStatus(3, UserState.New, null, "account_disabled", null), UserService<User>.Status(user, Deliveries));
        }

        [Fact]
        public void An_invited_user_takes_the_identity_server_answer_for_the_account()
        {
            User user = new() { Id = 3, State = UserState.Invited, Subject = "sub-ada", InviteStatus = InviteStatus.Invited };
            User unanswered = new() { Id = 4, State = UserState.Invited, Subject = "sub-bob", InviteStatus = InviteStatus.Reinvited };

            Assert.Equal(new InvitationStatus(3, UserState.Invited, InviteStatus.Invited, null, Sent), UserService<User>.Status(user, Deliveries));
            Assert.Null(UserService<User>.Status(unanswered, Deliveries).Delivery);
        }

        [Fact]
        public void An_account_that_already_held_a_credential_expected_no_email()
        {
            User user = new() { Id = 3, State = UserState.Invited, Subject = "sub-ada", InviteStatus = InviteStatus.Active };

            Assert.Equal(new IdentityDeliveryStatus("sub-ada", IdentityDeliveryState.NotFound, false, null, null, null), UserService<User>.Status(user, Deliveries).Delivery);
        }

        [Fact]
        public void A_joined_user_has_no_delivery()
        {
            User user = new() { Id = 3, State = UserState.Joined, Subject = "sub-ada", InviteStatus = InviteStatus.Invited };

            Assert.Null(UserService<User>.Status(user, Deliveries).Delivery);
        }
    }
}
