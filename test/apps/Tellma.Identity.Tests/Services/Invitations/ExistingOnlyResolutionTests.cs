// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Identity.Data;
using Tellma.Identity.Services.Invitations;

namespace Tellma.Identity.Tests.Services.Invitations
{
    /// <summary>
    ///     The outcome table of an <c>existingOnly</c> invite item: whom the email belongs to
    ///     decides the answer, and no row of it ever leads to an invitation.
    /// </summary>
    public sealed class ExistingOnlyResolutionTests
    {
        [Theory]
        [InlineData(UserLifecycleState.Active, true, ExistingOnlyOutcome.Active)]
        [InlineData(UserLifecycleState.Orphaned, true, ExistingOnlyOutcome.Active)]
        [InlineData(UserLifecycleState.Active, false, ExistingOnlyOutcome.NoCredential)]
        [InlineData(UserLifecycleState.Orphaned, false, ExistingOnlyOutcome.NoCredential)]
        [InlineData(UserLifecycleState.Disabled, true, ExistingOnlyOutcome.Refused)]
        [InlineData(UserLifecycleState.Disabled, false, ExistingOnlyOutcome.Refused)]
        [InlineData(UserLifecycleState.Purged, true, ExistingOnlyOutcome.Refused)]
        [InlineData(UserLifecycleState.Purged, false, ExistingOnlyOutcome.Refused)]
        public void An_existing_user_answers_by_state_and_credential(
            UserLifecycleState state, bool hasCredential, ExistingOnlyOutcome expected)
        {
            Assert.Equal(expected, ExistingOnlyResolution.Resolve(state, hasCredential));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void An_address_with_no_user_is_unknown(bool hasCredential)
        {
            Assert.Equal(ExistingOnlyOutcome.UnknownEmail, ExistingOnlyResolution.Resolve(null, hasCredential));
        }

        [Fact]
        public void An_unknown_lifecycle_state_is_never_answered_active()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ExistingOnlyResolution.Resolve((UserLifecycleState)99, hasCredential: true));
        }

        [Fact]
        public void The_error_codes_are_the_stable_wire_values()
        {
            // Callers branch on these exact strings, so they are pinned here.
            Assert.Equal("unknown_email", InvitationErrors.UnknownEmail);
            Assert.Equal("existing_user_has_no_credential", InvitationErrors.ExistingUserHasNoCredential);
        }
    }
}
