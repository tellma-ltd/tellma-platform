// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Users;

namespace Tellma.Core.Tests.Users
{
    /// <summary>
    ///     Whether a provisioning run invites its first administrator: never without an address or
    ///     when the run names a subject, only as far as a log when the host holds no service-client
    ///     secret, and otherwise through the users' invite action.
    /// </summary>
    public sealed class TenantAdministratorInviterTests
    {
        [Theory]
        [InlineData(null, null, "secret")]
        [InlineData(" ", null, "secret")]
        [InlineData("owner@example.com", "sub-owner", "secret")]
        [InlineData("owner@example.com", "sub-owner", null)]
        public void A_run_naming_no_address_or_a_subject_invites_nobody(string? email, string? subject, string? secret)
        {
            Assert.Equal(InvitationPlan.None, TenantAdministratorInviter<User>.Plan(email, subject, secret));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public void A_host_without_a_service_client_secret_only_logs_that_it_could_not_invite(string? secret)
        {
            Assert.Equal(InvitationPlan.NoSecret, TenantAdministratorInviter<User>.Plan("owner@example.com", null, secret));
        }

        [Fact]
        public void A_host_holding_the_secret_invites_the_address_the_run_names()
        {
            Assert.Equal(InvitationPlan.Invite, TenantAdministratorInviter<User>.Plan("owner@example.com", null, "secret"));
        }
    }
}
