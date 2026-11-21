// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Data.Batches;
using Tellma.Core.Tests.Data.Batches;
using Tellma.Core.Users;
using Tellma.Testing.Support.Identity;

namespace Tellma.Core.Tests.Users
{
    /// <summary>
    ///     The invite action: one invitation per target in request order, each carrying the locale,
    ///     the name in it, the tenant's link and the sandbox's lookup flag; one write-back of what the
    ///     identity server answered, read once the action committed; membership hints for the users
    ///     who gained an account; and a prefix the identity server answered recorded and raised with
    ///     the ids never attempted.
    /// </summary>
    public sealed class UserInviteTests : IDisposable
    {
        /// <summary>The service over a scripted identity server.</summary>
        private readonly UsersKit _kit = new();

        /// <inheritdoc />
        public void Dispose()
        {
            _kit.Dispose();
        }

        [Fact]
        public void An_invitation_carries_the_user_language_or_else_the_tenant_first_and_the_name_in_it()
        {
            User ada = UsersKit.Individual(3, "Ada");
            ada.Name2 = "آدا";
            ada.Gender = Gender.Female;
            User bob = UsersKit.Individual(4, "Bob");
            bob.PreferredLanguage = "ar";

            IdentityInvitation first = UserService<User>.Invitation(ada, UsersKit.Settings(), UsersKit.TenantLink, existingOnly: false);
            ada.PreferredLanguage = "ar";
            IdentityInvitation second = UserService<User>.Invitation(ada, UsersKit.Settings(), UsersKit.TenantLink, existingOnly: true);
            IdentityInvitation third = UserService<User>.Invitation(bob, UsersKit.Settings(), UsersKit.TenantLink, existingOnly: false);

            Assert.Equal(new IdentityInvitation("user3@example.com", "Ada", "en", Gender.Female, UsersKit.TenantLink, false), first);
            Assert.Equal(new IdentityInvitation("user3@example.com", "آدا", "ar", Gender.Female, UsersKit.TenantLink, true), second);

            // A name missing in the locale's language falls back to the first.
            Assert.Equal("Bob", third.DisplayName);
        }

        [Fact]
        public void A_success_writes_the_account_and_the_status_and_a_refusal_writes_only_its_error()
        {
            UserInvitationOutcome success = UserService<User>.Outcome(3, new IdentityInvitationResult("a@example.com", "sub-a", InviteStatus.Reinvited, null), existingOnly: false);
            UserInvitationOutcome refused = UserService<User>.Outcome(4, new IdentityInvitationResult("b@example.com", "sub-b", null, "account_disabled"), existingOnly: false);
            UserInvitationOutcome silent = UserService<User>.Outcome(5, new IdentityInvitationResult("c@example.com", null, null, null), existingOnly: false);
            UserInvitationOutcome long_ = UserService<User>.Outcome(6, new IdentityInvitationResult("d@example.com", null, null, new string('x', 5000)), existingOnly: false);

            Assert.Equal(3, success.UserId);
            Assert.Equal("sub-a", success.Subject);
            Assert.Equal("Reinvited", success.InviteStatus);
            Assert.Null(success.LastInviteError);

            // A refusal writes no account, even one the identity server named.
            Assert.Equal(4, refused.UserId);
            Assert.Null(refused.Subject);
            Assert.Null(refused.InviteStatus);
            Assert.Equal("account_disabled", refused.LastInviteError);
            Assert.Equal(UserService<User>.NoAnswerError, silent.LastInviteError);
            Assert.Equal(UserService<User>.MaxInviteErrorLength, long_.LastInviteError!.Length);
        }

        [Theory]
        [InlineData(UserService<User>.UnknownEmailError)]
        [InlineData(UserService<User>.NoCredentialError)]
        public void A_sandbox_lookup_that_finds_no_usable_account_records_the_sandbox_code(string error)
        {
            IdentityInvitationResult answer = new("a@example.com", null, null, error);

            Assert.Equal(AccessCodes.SandboxRequiresExistingIdentity, UserService<User>.Outcome(3, answer, existingOnly: true).LastInviteError);
            Assert.Equal(error, UserService<User>.Outcome(3, answer, existingOnly: false).LastInviteError);
        }

        [Fact]
        public void A_result_is_a_mismatch_the_statement_found_else_the_answer()
        {
            IdentityInvitationResult answer = new("a@example.com", "sub-a", InviteStatus.Invited, null);
            UserInvitationOutcome outcome = UserService<User>.Outcome(3, answer, existingOnly: false);
            UserInvitationOutcome refused = UserService<User>.Outcome(4, new IdentityInvitationResult("b@example.com", null, null, "nope"), existingOnly: false);

            Assert.Equal(new InviteResult(3, InviteStatus.Invited, null), UserService<User>.Result(outcome, answer, new HashSet<int>()));
            Assert.Equal(new InviteResult(3, null, AccessCodes.SubjectMismatch), UserService<User>.Result(outcome, answer, new HashSet<int> { 3 }));
            Assert.Equal(new InviteResult(4, null, "nope"), UserService<User>.Result(refused, answer, new HashSet<int>()));
        }

        [Fact]
        public async Task The_write_back_is_one_statement_that_writes_only_the_users_table()
        {
            using BatchHarness harness = new();
            DataBatch batch = harness.Batch(BatchPurpose.Persist);

            _ = UserService<User>.ComposeWriteBack(batch, [new UserInvitationOutcome { UserId = 3, Subject = "sub-a", InviteStatus = "Invited" }]);
            BatchAssembly assembly = await BatchHarness.SealAsync(batch);

            Assert.Contains("UPDATE u SET", assembly.Text, StringComparison.Ordinal);
            Assert.Contains("u.[Subject] <> o.[Subject]", assembly.Text, StringComparison.Ordinal);
        }

        [Fact]
        public async Task An_invite_answers_in_request_order_writes_back_once_and_hints_the_users_who_gained_an_account()
        {
            User ada = UsersKit.Individual(3, "Ada");
            User bob = UsersKit.Individual(4, "Bob");
            bob.Subject = "sub-user4@example.com";
            bob.State = UserState.Invited;
            User cy = UsersKit.Individual(5, "Cy");
            User di = UsersKit.Individual(6, "Di");
            _kit.Identity.OnInvite = static (invitations, _) => Task.FromResult<IReadOnlyList<IdentityInvitationResult>>(
            [
                .. invitations.Select(static invitation => invitation.Email switch
                {
                    "user3@example.com" => new IdentityInvitationResult(invitation.Email, ScriptedIdentityClient.SubjectOf(invitation.Email), InviteStatus.Invited, null),
                    "user4@example.com" => new IdentityInvitationResult(invitation.Email, ScriptedIdentityClient.SubjectOf(invitation.Email), InviteStatus.Reinvited, null),
                    "user5@example.com" => new IdentityInvitationResult(invitation.Email, "sub-someone-else", InviteStatus.Active, null),
                    _ => new IdentityInvitationResult(invitation.Email, null, null, "account_disabled"),
                }),
            ]);
            (IDataBatch batch, RecordingBatchProxy recorder) = RecordingBatchProxy.Create();

            // The ids are asked out of the loaded order, one of them twice.
            ActionContext<User, int> context = UsersKit.Action(batch, sandbox: false, [5, 3, 4, 3, 6], ada, bob, cy, di);
            IReadOnlyList<InviteResult> results = await RunAsync(context, recorder, mismatched: [5]);

            Assert.Equal(
                [
                    new InviteResult(5, null, AccessCodes.SubjectMismatch),
                    new InviteResult(3, InviteStatus.Invited, null),
                    new InviteResult(4, InviteStatus.Reinvited, null),
                    new InviteResult(6, null, "account_disabled"),
                ],
                results);

            // One identity call, one statement over the answered rows in request order.
            Assert.Equal(["user5@example.com", "user3@example.com", "user4@example.com", "user6@example.com"], Assert.Single(_kit.Identity.Invites).Select(static invitation => invitation.Email));
            UserInvitationOutcome[] rows = [.. (IEnumerable<UserInvitationOutcome>)recorder.Tables[0]];
            Assert.Equal([5, 3, 4, 6], rows.Select(static row => row.UserId));
            Assert.Equal([5, 3, 4, 6], ((IEnumerable<IdList>)recorder.Tables[1]).Select(static row => row.Id));

            // Only Ada gained an account: Bob held his already, Cy's belongs to another, Di was refused.
            Assert.Equal([(UsersKit.TenantId, "sub-user3@example.com", true)], _kit.Memberships.Hints());

            // Every answered user is counted by outcome, nothing as never attempted.
            Assert.Equal(
                [("Active", 0L), ("Error", 1L), ("Invited", 1L), ("NotAttempted", 0L), ("Reinvited", 1L), ("SubjectMismatch", 1L)],
                Counts(_kit.Invites));
        }

        [Fact]
        public async Task An_invite_on_a_sandbox_only_looks_users_up_and_names_the_sandbox_refusal()
        {
            User ada = UsersKit.Individual(3, "Ada");
            _kit.Identity.OnInvite = (invitations, _) => Task.FromResult<IReadOnlyList<IdentityInvitationResult>>(
                [new(invitations[0].Email, null, null, UserService<User>.UnknownEmailError)]);
            (IDataBatch batch, RecordingBatchProxy recorder) = RecordingBatchProxy.Create();

            IReadOnlyList<InviteResult> results = await RunAsync(UsersKit.Action(batch, sandbox: true, [3], ada), recorder);

            Assert.True(Assert.Single(Assert.Single(_kit.Identity.Invites)).ExistingOnly);
            Assert.Equal([new InviteResult(3, null, AccessCodes.SandboxRequiresExistingIdentity)], results);
            Assert.Empty(_kit.Memberships.Recorded);
        }

        [Fact]
        public async Task A_prefix_is_recorded_and_raised_with_the_ids_never_attempted()
        {
            User ada = UsersKit.Individual(3, "Ada");
            User bob = UsersKit.Individual(4, "Bob");
            User cy = UsersKit.Individual(5, "Cy");
            _kit.Identity.OnInvite = (invitations, _) => Task.FromResult<IReadOnlyList<IdentityInvitationResult>>(
                [new(invitations[0].Email, ScriptedIdentityClient.SubjectOf(invitations[0].Email), InviteStatus.Invited, null)]);
            (IDataBatch batch, RecordingBatchProxy recorder) = RecordingBatchProxy.Create();

            PartialFailureException failure = await Assert.ThrowsAsync<PartialFailureException>(
                () => RunAsync(UsersKit.Action(batch, sandbox: false, [3, 4, 5], ada, bob, cy), recorder));

            Assert.Equal(UserService<User>.PartialFailureCode, failure.Code);
            Assert.Equal([new InviteResult(3, InviteStatus.Invited, null)], (IEnumerable<InviteResult>)failure.Results);
            Assert.Equal(["4", "5"], failure.Failed);

            // The answered prefix was written back and hinted before the raise.
            Assert.Equal([3], ((IEnumerable<UserInvitationOutcome>)recorder.Tables[0]).Select(static row => row.UserId));
            Assert.Single(_kit.Memberships.Recorded);
            Assert.Contains(("NotAttempted", 2L), Counts(_kit.Invites));
        }

        [Fact]
        public async Task An_identity_server_that_answers_nothing_writes_nothing_and_raises_every_id()
        {
            User ada = UsersKit.Individual(3, "Ada");
            _kit.Identity.OnInvite = static (_, _) => Task.FromResult<IReadOnlyList<IdentityInvitationResult>>([]);
            (IDataBatch batch, RecordingBatchProxy recorder) = RecordingBatchProxy.Create();

            PartialFailureException failure = await Assert.ThrowsAsync<PartialFailureException>(
                () => RunAsync(UsersKit.Action(batch, sandbox: false, [3], ada), recorder));

            Assert.Equal(["3"], failure.Failed);
            Assert.Empty(recorder.Statements);
        }

        [Fact]
        public async Task An_identity_server_that_answers_more_than_it_was_sent_fails_with_nothing_written()
        {
            User ada = UsersKit.Individual(3, "Ada");
            _kit.Identity.OnInvite = static (invitations, _) => Task.FromResult<IReadOnlyList<IdentityInvitationResult>>(
                [new(invitations[0].Email, "sub-a", InviteStatus.Invited, null), new("x@example.com", "sub-x", InviteStatus.Invited, null)]);
            (IDataBatch batch, RecordingBatchProxy recorder) = RecordingBatchProxy.Create();

            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => _kit.Service.RunInviteAsync(UsersKit.Action(batch, sandbox: false, [3], ada)));

            Assert.Empty(recorder.Statements);
        }

        [Fact]
        public async Task A_target_the_validation_refused_reaches_no_identity_server()
        {
            User ada = UsersKit.Individual(3, "Ada");
            (IDataBatch batch, RecordingBatchProxy recorder) = RecordingBatchProxy.Create();
            ActionContext<User, int> context = UsersKit.Action(batch, sandbox: false, [3], ada);
            context.Errors.Add(ValidationPath.Root, AccessCodes.Inactive);
            InvalidOperationException ended = new("The action's validation failed.");

            Task<IReadOnlyList<InviteResult>> running = _kit.Service.RunInviteAsync(context);
            Assert.False(running.IsCompleted);
            Assert.True(context.FaultPersisted(ended));

            Assert.Same(ended, await Assert.ThrowsAsync<InvalidOperationException>(() => running));
            Assert.Empty(_kit.Identity.Invites);
            Assert.Empty(recorder.Statements);
        }

        [Fact]
        public async Task A_persist_that_never_commits_hints_nothing()
        {
            User ada = UsersKit.Individual(3, "Ada");
            (IDataBatch batch, RecordingBatchProxy recorder) = RecordingBatchProxy.Create();
            ActionContext<User, int> context = UsersKit.Action(batch, sandbox: false, [3], ada);
            TimeoutException ended = new("The persist failed.");

            Task<IReadOnlyList<InviteResult>> running = _kit.Service.RunInviteAsync(context);
            Assert.Single(recorder.Statements);
            Assert.True(context.FaultPersisted(ended));

            Assert.Same(ended, await Assert.ThrowsAsync<TimeoutException>(() => running));
            Assert.Empty(_kit.Memberships.Recorded);
            Assert.Empty(_kit.Invites.GetMeasurementSnapshot());
        }

        [Fact]
        public async Task A_membership_hint_that_fails_is_logged_and_the_invite_still_answers()
        {
            User ada = UsersKit.Individual(3, "Ada");
            _kit.Memberships.Failure = new HttpRequestException("The directory is down.");
            (IDataBatch batch, RecordingBatchProxy recorder) = RecordingBatchProxy.Create();

            IReadOnlyList<InviteResult> results = await RunAsync(UsersKit.Action(batch, sandbox: false, [3], ada), recorder);

            Assert.Equal([new InviteResult(3, InviteStatus.Invited, null)], results);
            Assert.Single(_kit.Logs.Entries, static entry => entry.EventId.Name == "UserMembershipHintFailed");
        }

        /// <summary>Runs an invite to its commit: the write-back answers the mismatches, then the persist completes.</summary>
        /// <param name="context">The action's context.</param>
        /// <param name="recorder">The batch's recorder.</param>
        /// <param name="mismatched">The ids the write-back reports mismatched.</param>
        /// <returns>The results.</returns>
        private async Task<IReadOnlyList<InviteResult>> RunAsync(ActionContext<User, int> context, RecordingBatchProxy recorder, int[]? mismatched = null)
        {
            // Every collaborator answers at once, so the method has composed and waits for the persist.
            Task<IReadOnlyList<InviteResult>> running = _kit.Service.RunInviteAsync(context);
            Assert.False(running.IsCompleted);
            if (recorder.Statements.Count > 0)
            {
                recorder.Complete(mismatched ?? []);
            }

            Assert.True(context.CompletePersisted());
            return await running;
        }

        /// <summary>The invites counted, summed by outcome, in outcome order.</summary>
        /// <param name="collector">The counter's collector.</param>
        /// <returns>The sums.</returns>
        private static IReadOnlyList<(string Outcome, long Count)> Counts(MetricCollector<long> collector)
        {
            string[] outcomes = ["Active", "Error", "Invited", "NotAttempted", "Reinvited", "SubjectMismatch"];
            IReadOnlyList<CollectedMeasurement<long>> measurements = collector.GetMeasurementSnapshot();
            return [.. outcomes.Select(outcome => (outcome, measurements
                .Where(measurement => Equals(measurement.Tags[UsersTelemetryNames.OutcomeTag], outcome))
                .Sum(static measurement => measurement.Value)))];
        }
    }
}
