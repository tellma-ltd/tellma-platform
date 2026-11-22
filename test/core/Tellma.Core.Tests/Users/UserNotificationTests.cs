// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Collections.Concurrent;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Jobs;
using Tellma.Core.Tests.Access;
using Tellma.Core.Users;
using Tellma.Testing.Support.Identity;

namespace Tellma.Core.Tests.Users
{
    /// <summary>
    ///     The users' stack's notification surfaces in memory: the caller's test message refused for
    ///     the inbox and an unknown key, not configured for a channel with no sender, sent through the
    ///     channel's sender after a due prologue, and counted by channel and outcome; the invitation's
    ///     notice raised once per added user with both names and no target, in requests of at most the
    ///     notifier's cap; and an administrator's save of another user's notification settings refused
    ///     for a service account and for rows the rules refuse, each at its argument.
    /// </summary>
    public sealed class UserNotificationTests : IDisposable
    {
        /// <summary>The kits a test composed, disposed with it.</summary>
        private readonly List<UsersKit> _kits = [];

        /// <inheritdoc />
        public void Dispose()
        {
            foreach (UsersKit kit in _kits)
            {
                kit.Dispose();
            }
        }

        // --- The caller's test message ---

        [Fact]
        public async Task The_inbox_cannot_be_tested()
        {
            UsersKit kit = Kit(senders: [new ScriptedChannel("email", TestNotificationResult.Sent)]);

            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => kit.Service.RunSendTestNotificationAsync(new TestNotificationRequest("inbox")));

            ValidationError error = Assert.Single(refused.Errors);
            Assert.Equal(("Channel", AccessCodes.ChannelNotTestable), (error.Path.ToString(), error.Code));
            Assert.Empty(kit.TestNotifications.GetMeasurementSnapshot());
        }

        [Fact]
        public async Task A_key_no_channel_has_is_refused_as_unknown()
        {
            UsersKit kit = Kit();

            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => kit.Service.RunSendTestNotificationAsync(new TestNotificationRequest("fax")));

            ValidationError error = Assert.Single(refused.Errors);
            Assert.Equal(("Channel", NotificationValidationCodes.UnknownChannel), (error.Path.ToString(), error.Code));
            Assert.Equal("fax", error.Arguments["channel"]);
        }

        [Fact]
        public async Task A_request_without_a_body_is_a_bad_request()
        {
            UsersKit kit = Kit();

            _ = await Assert.ThrowsAsync<BadRequestException>(() => kit.Service.RunSendTestNotificationAsync(null!));
        }

        [Fact]
        public async Task A_registered_channel_without_a_sender_is_not_configured()
        {
            UsersKit kit = Kit();

            Assert.Equal(TestNotificationResult.NotConfigured, await kit.Service.RunSendTestNotificationAsync(new TestNotificationRequest("email")));
            Assert.Equal([("email", "NotConfigured")], Counted(kit.TestNotifications));
            Assert.Empty(kit.Runner.Runs);
        }

        [Theory]
        [InlineData(TestNotificationResult.Sent, "Sent")]
        [InlineData(TestNotificationResult.NoContactAddress, "NoContactAddress")]
        [InlineData(TestNotificationResult.NotConfigured, "NotConfigured")]
        public async Task A_channel_answers_through_its_sender_with_no_round_trip_and_is_counted(TestNotificationResult answer, string outcome)
        {
            ScriptedChannel email = new("email", answer);
            UsersKit kit = Kit(senders: [new ScriptedChannel("sms", TestNotificationResult.Sent), email]);

            Assert.Equal(answer, await kit.Service.RunSendTestNotificationAsync(new TestNotificationRequest("email")));

            Assert.Equal(1, email.Tests);
            Assert.Empty(kit.Runner.Runs);
            Assert.Equal([("email", outcome)], Counted(kit.TestNotifications));
        }

        [Fact]
        public async Task A_due_prologue_runs_alone_before_the_send()
        {
            PrologueRunner runner = new();
            ScriptedChannel email = new("email", TestNotificationResult.Sent);
            UsersKit kit = Kit(runner, [email]);
            kit.Connection.PrologueDue = true;
            email.OnSend = () => Assert.Equal([BatchPurpose.Read], runner.Runs);

            Assert.Equal(TestNotificationResult.Sent, await kit.Service.RunSendTestNotificationAsync(new TestNotificationRequest("email")));

            Assert.Equal([BatchPurpose.Read], runner.Runs);
            Assert.Equal(1, email.Tests);
        }

        [Fact]
        public async Task An_unavailable_channel_is_counted_and_raised()
        {
            ScriptedChannel email = new("email", TestNotificationResult.Sent) { Failure = new DependencyUnavailableException("email", null, null) };
            UsersKit kit = Kit(senders: [email]);

            _ = await Assert.ThrowsAsync<DependencyUnavailableException>(() => kit.Service.RunSendTestNotificationAsync(new TestNotificationRequest("email")));

            Assert.Equal([("email", "Unavailable")], Counted(kit.TestNotifications));
        }

        // --- The invitation's notice ---

        [Fact]
        public async Task Every_user_the_invite_added_is_noticed_once_with_both_names_and_no_target()
        {
            UsersKit kit = Kit();
            User ada = UsersKit.Individual(3, "Ada");
            User bob = UsersKit.Individual(4, "Bob");
            User cy = UsersKit.Individual(5, "Cy");
            User di = UsersKit.Individual(6, "Di");
            User ed = UsersKit.Individual(7, "Ed");
            ed.Subject = "sub-other";
            kit.Identity.OnInvite = static (invitations, _) => Task.FromResult<IReadOnlyList<IdentityInvitationResult>>(
                [.. invitations.Select(static invitation => invitation.Email switch
                {
                    "user3@example.com" => new IdentityInvitationResult(invitation.Email, ScriptedIdentityClient.SubjectOf(invitation.Email), InviteStatus.Invited, null),
                    "user4@example.com" => new IdentityInvitationResult(invitation.Email, ScriptedIdentityClient.SubjectOf(invitation.Email), InviteStatus.Reinvited, null),
                    "user5@example.com" => new IdentityInvitationResult(invitation.Email, ScriptedIdentityClient.SubjectOf(invitation.Email), InviteStatus.Active, null),
                    "user7@example.com" => new IdentityInvitationResult(invitation.Email, ScriptedIdentityClient.SubjectOf(invitation.Email), InviteStatus.Active, null),
                    _ => new IdentityInvitationResult(invitation.Email, null, null, "account_disabled"),
                }),
                ]);
            (IDataBatch batch, RecordingBatchProxy recorder) = RecordingBatchProxy.Create();
            ActionContext<User, int> context = UsersKit.Action(batch, sandbox: false, [3, 4, 5, 6, 7], ada, bob, cy, di, ed);

            _ = await RunInviteAsync(kit, context, recorder);

            // Di was refused and Ed holds another account than the one answered.
            NotificationRequest notice = Assert.Single(UsersKit.Notices(context));
            Assert.Equal(("core.user.added", "core.user.added", UsersKit.CallerId), (notice.Type, notice.DedupKey, notice.ActorUserId));
            Assert.Equal([3, 4, 5], notice.RecipientIds);
            Assert.Equal((null, null), (notice.TargetResource, notice.TargetId));
            Assert.Equal((DuplicatePolicy.Suppress, false), (notice.OnDuplicate, notice.InboxOnly));

            // Both names, every slot, as the stored arguments' JSON carries them.
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(notice.Arguments, notice.Arguments!.GetType(), TellmaJson.Options));
            Assert.Equal(["Acme", "أكمي", null], arguments.RootElement.GetProperty("tenantName").EnumerateArray().Select(static slot => slot.GetString()));
            Assert.Equal(["Caller", "المتصل", null], arguments.RootElement.GetProperty("actorName").EnumerateArray().Select(static slot => slot.GetString()));
        }

        [Fact]
        public async Task The_notice_is_raised_in_requests_of_at_most_the_notifier_cap()
        {
            UsersKit kit = Kit();
            kit.NotificationsOptions.MaxRecipientsPerRequest = 2;
            (IDataBatch batch, RecordingBatchProxy recorder) = RecordingBatchProxy.Create();
            ActionContext<User, int> context = UsersKit.Action(batch, sandbox: false, [3, 4, 5], UsersKit.Individual(3, "Ada"), UsersKit.Individual(4, "Bob"), UsersKit.Individual(5, "Cy"));

            _ = await RunInviteAsync(kit, context, recorder);

            Assert.Equal([[3, 4], [5]], UsersKit.Notices(context).Select(static notice => notice.RecipientIds));
        }

        [Fact]
        public async Task An_invite_whose_every_user_was_refused_raises_no_notice()
        {
            UsersKit kit = Kit();
            kit.Identity.OnInvite = static (invitations, _) => Task.FromResult<IReadOnlyList<IdentityInvitationResult>>(
                [new(invitations[0].Email, null, null, "account_disabled")]);
            (IDataBatch batch, RecordingBatchProxy recorder) = RecordingBatchProxy.Create();
            ActionContext<User, int> context = UsersKit.Action(batch, sandbox: false, [3], UsersKit.Individual(3, "Ada"));

            _ = await RunInviteAsync(kit, context, recorder);

            Assert.Empty(UsersKit.Notices(context));
        }

        // --- Another user's notification settings ---

        [Fact]
        public async Task A_save_of_notification_settings_refuses_a_service_account_and_each_refused_row_at_its_argument()
        {
            UsersKit kit = Kit();
            ActionContext<User, int> context = AccessRulesHarness.Action(new RulesLoader(), UsersKit.CallerId, [3], UsersKit.ServiceAccount(3, null));
            context = With(context, new NotificationPreferencesArguments(
                [
                    new NotificationPreference { Type = "core.user.added", Channel = "inbox", Enabled = false },
                    new NotificationPreference { Type = "acme.unknown", Channel = "email", Enabled = true },
                    new NotificationPreference { Type = "core.user.added", Channel = "fax", Enabled = true },
                    new NotificationPreference { Type = JobNotices.FailedDescriptor.Key, Channel = "inbox", Enabled = false },
                    new NotificationPreference { Type = "core.user.added", Channel = "inbox", Enabled = true },
                ]));

            await kit.Hooks.ValidateActionAsync(UserService<User>.NotificationPreferencesSaveAction, context);

            Assert.Equal(
                [
                    ("Arguments.Preferences[1].Type", NotificationValidationCodes.UnknownType),
                    ("Arguments.Preferences[2].Channel", NotificationValidationCodes.UnknownChannel),
                    ("Arguments.Preferences[3].Enabled", NotificationValidationCodes.CannotMute),
                    ("Arguments.Preferences[4].Channel", NotificationValidationCodes.DuplicatePreference),
                    ("Ids[0]", AccessCodes.NotIndividual),
                ],
                AccessRulesHarness.Errors(context.Errors));
        }

        [Fact]
        public async Task A_save_of_an_individual_settings_with_valid_rows_passes()
        {
            UsersKit kit = Kit();
            ActionContext<User, int> context = AccessRulesHarness.Action(new RulesLoader(), UsersKit.CallerId, [3], UsersKit.Individual(3, "Ada"));
            context = With(context, new NotificationPreferencesArguments(
                [new NotificationPreference { Type = "core.user.added", Channel = "email", Enabled = true }]));

            await kit.Hooks.ValidateActionAsync(UserService<User>.NotificationPreferencesSaveAction, context);

            Assert.False(context.Errors.HasErrors);
        }

        /// <summary>A kit the test disposes.</summary>
        /// <param name="runner">The guarded runner; null for the kit's own.</param>
        /// <param name="senders">The channel senders the host registers.</param>
        /// <returns>The kit.</returns>
        private UsersKit Kit(IGuardedBatchRunner? runner = null, IReadOnlyList<INotificationChannel>? senders = null)
        {
            UsersKit kit = new(runner, senders: senders);
            _kits.Add(kit);
            return kit;
        }

        /// <summary>A context with the arguments set, as the binding sets them.</summary>
        /// <param name="context">The context.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>The context.</returns>
        private static ActionContext<User, int> With(ActionContext<User, int> context, object arguments)
        {
            return new ActionContext<User, int>
            {
                Ids = context.Ids,
                Context = context.Context,
                Loader = context.Loader,
                Errors = context.Errors,
                Batch = context.Batch,
                Host = context.Host,
                Entities = context.Entities,
                Arguments = arguments,
            };
        }

        /// <summary>Runs an invite to its commit: the write-back answers no mismatch, then the persist completes.</summary>
        /// <param name="kit">The kit.</param>
        /// <param name="context">The action's context.</param>
        /// <param name="recorder">The batch's recorder.</param>
        /// <returns>The results.</returns>
        private static async Task<IReadOnlyList<InviteResult>> RunInviteAsync(UsersKit kit, ActionContext<User, int> context, RecordingBatchProxy recorder)
        {
            Task<IReadOnlyList<InviteResult>> running = kit.Service.RunInviteAsync(context);
            if (recorder.Statements.Count > 0)
            {
                recorder.Complete();
            }

            Assert.True(context.CompletePersisted());
            return await running;
        }

        /// <summary>The test messages counted, each as its channel and outcome, in order.</summary>
        /// <param name="collector">The counter's collector.</param>
        /// <returns>The pairs.</returns>
        private static IReadOnlyList<(string Channel, string Outcome)> Counted(MetricCollector<long> collector)
        {
            return [.. collector.GetMeasurementSnapshot().Select(static measurement =>
            {
                Assert.Equal(1, measurement.Value);
                return ((string)measurement.Tags[UsersTelemetryNames.ChannelTag]!, (string)measurement.Tags[UsersTelemetryNames.OutcomeTag]!);
            })];
        }

        /// <summary>A channel sender whose test send answers as scripted.</summary>
        /// <param name="key">The channel's key.</param>
        /// <param name="answer">What a test send answers.</param>
        private sealed class ScriptedChannel(string key, TestNotificationResult answer) : INotificationChannel
        {
            /// <summary>How many test sends it answered.</summary>
            public int Tests { get; private set; }

            /// <summary>Thrown by a test send when set.</summary>
            public Exception? Failure { get; init; }

            /// <summary>Runs as a test send starts, when set.</summary>
            public Action? OnSend { get; set; }

            /// <inheritdoc />
            public string Key => key;

            /// <inheritdoc />
            public Task DeliverAsync(IOpenWriteHost host, NotificationRequest request, IReadOnlyList<NotificationRecipient> recipients, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException("The users' stack delivers nothing.");
            }

            /// <inheritdoc />
            public Task<TestNotificationResult> SendTestAsync(CancellationToken cancellationToken = default)
            {
                OnSend?.Invoke();
                Tests++;
                return Failure is { } failure ? Task.FromException<TestNotificationResult>(failure) : Task.FromResult(answer);
            }
        }

        /// <summary>A guarded runner that records each run's purpose and refuses any statement.</summary>
        private sealed class PrologueRunner : IGuardedBatchRunner
        {
            /// <summary>Every run's purpose, in call order.</summary>
            public ConcurrentQueue<BatchPurpose> Runs { get; } = new();

            /// <inheritdoc />
            public async Task<TResult> RunAsync<TResult>(
                BatchPurpose purpose,
                Func<ConnectedUser, IDataBatch, ValueTask> compose,
                Func<ConnectedUser, BatchOutcome, TResult> read,
                CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(compose);
                ArgumentNullException.ThrowIfNull(read);
                Runs.Enqueue(purpose);
                await compose(null!, Refusing.Of<IDataBatch>()).ConfigureAwait(false);
                return read(null!, null!);
            }
        }
    }
}
