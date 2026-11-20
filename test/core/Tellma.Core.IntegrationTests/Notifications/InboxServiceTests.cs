// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Composition;
using Tellma.Core.Notifications;
using Tellma.Testing.Entities;
using static Tellma.Core.IntegrationTests.Notifications.InboxDatabase;

namespace Tellma.Core.IntegrationTests.Notifications
{
    /// <summary>
    ///     The inbox, the preferences and the notification stack's self-scope against a fixture
    ///     database, each call made as a connected user through the action invoker: the summary's
    ///     capped counts, newest rows, actors and running jobs; the verbs' counts, versions and events,
    ///     another user's rows untouched; the tenant states the verbs serve; the preference save's
    ///     codes, its stored deviations and its preferences tag. Every batch's writes are audited
    ///     against change tracking. The tests run once with read-committed snapshot on and once with it off.
    /// </summary>
    /// <param name="fixture">The class's database, reset before each test.</param>
    public abstract class InboxServiceTests(InboxDatabase fixture) : IAsyncLifetime
    {
        /// <summary>The inbox's route.</summary>
        private const string Inbox = "inbox";

        /// <summary>The preferences' route.</summary>
        private const string Preferences = "notification-preferences";

        /// <summary>The class's database.</summary>
        protected InboxDatabase Fixture => fixture;

        /// <summary>The cancellation token of the running test.</summary>
        protected static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            await fixture.ResetAsync();
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }

        [Fact]
        public async Task The_database_reads_committed_rows_as_the_class_requires()
        {
            Assert.Equal(
                fixture.ReadCommittedSnapshot,
                await SqlServerFixture.ScalarAsync<bool>(fixture.ConnectionString, "SELECT [is_read_committed_snapshot_on] FROM sys.databases WHERE [name] = DB_NAME();"));
        }

        [Fact]
        public async Task The_summary_caps_its_counts_and_takes_the_most_severe_of_the_capped_newest_unseen_rows()
        {
            // --- 105 unseen rows: the oldest an error past the cap, one warning inside it ---
            _ = await fixture.InsertAsync(AdaId, secondsAgo: 2000, severity: nameof(NotificationSeverity.Error));
            for (int index = 1; index < 105; index++)
            {
                _ = await fixture.InsertAsync(AdaId, secondsAgo: 2000 - index, severity: index == 50 ? nameof(NotificationSeverity.Warning) : nameof(NotificationSeverity.Info));
            }

            _ = await fixture.InsertAsync(BenId, secondsAgo: 1, severity: nameof(NotificationSeverity.Error));

            InboxSummary summary = await SummaryAsync(AdaId);

            Assert.Equal(100, summary.Unseen);
            Assert.Equal(100, summary.Unread);
            Assert.Equal(NotificationSeverity.Warning, summary.MaxUnseenSeverity);
            Assert.Equal(10, summary.Latest.Count);
            Assert.All(summary.Latest, static row => Assert.Equal(AdaId, row.UserId));
            Assert.Null(summary.SeenAt);
            Assert.Equal(0, summary.Version);
        }

        [Fact]
        public async Task The_newest_rows_come_newest_first_with_the_higher_id_first_on_a_tie_and_related_holds_their_actors_only()
        {
            // --- Rows older than the newest ten name Ben; the newest name Cy and Dee ---
            for (int index = 0; index < 5; index++)
            {
                _ = await fixture.InsertAsync(AdaId, secondsAgo: 500 + index, actor: BenId);
            }

            List<int> newest = [];
            for (int index = 0; index < 8; index++)
            {
                newest.Add(await fixture.InsertAsync(AdaId, secondsAgo: 100 - index, actor: index % 2 == 0 ? CyId : null));
            }

            // Two rows at one instant: the higher id comes first.
            int tiedFirst = await fixture.InsertAsync(AdaId, secondsAgo: 10, actor: DeeId);
            int tiedSecond = await fixture.InsertAsync(AdaId, secondsAgo: 10, actor: DeeId);
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Notifications] SET [CreatedAt] = (SELECT [CreatedAt] FROM [core].[Notifications] WHERE [Id] = {tiedFirst}) WHERE [Id] = {tiedSecond};"));

            InboxSummary summary = await SummaryAsync(AdaId);

            int[] expected = [tiedSecond, tiedFirst, .. Enumerable.Reverse(newest)];
            Assert.Equal(expected, summary.Latest.Select(static row => row.Id));

            // --- The actors of the newest rows, and nobody else ---
            RelatedEntitySet users = Assert.Single(summary.Related.Sets).Value;
            Assert.Equal([CyId, DeeId], users.Entities.Cast<User>().Select(static user => user.Id).Order());
            Assert.Contains(nameof(User.Name), users.Projection);
        }

        [Fact]
        public async Task Running_lists_the_callers_own_requested_jobs_that_have_not_ended_with_their_labels()
        {
            int pending = await JobAsync(AdaId, JobStatus.Pending, label: "Core_Job_ExportReport");
            int running = await JobAsync(AdaId, JobStatus.Running);
            int held = await JobAsync(AdaId, JobStatus.Held);
            int confirming = await JobAsync(AdaId, JobStatus.PendingConfirmation);
            _ = await JobAsync(AdaId, JobStatus.Succeeded);
            _ = await JobAsync(AdaId, JobStatus.Failed);
            _ = await JobAsync(AdaId, JobStatus.Pending, scheduled: true);
            _ = await JobAsync(BenId, JobStatus.Pending);

            InboxSummary summary = await SummaryAsync(AdaId);

            Assert.Equal([confirming, held, running, pending], summary.Running.Select(static job => job.Id));
            InboxRunningJob labelled = summary.Running[^1];
            Assert.Equal(JobStatus.Pending, labelled.Status);
            Assert.Equal("Core_Job_ExportReport", labelled.LabelKey);
            Assert.Equal(/*lang=json,strict*/ """{"title":"Q3"}""", labelled.LabelArgumentsJson);
            Assert.Equal(FixtureJobKeys.Probe, labelled.HandlerKey);
        }

        [Fact]
        public async Task The_verbs_count_the_callers_rows_move_the_version_once_each_and_ignore_other_users_ids()
        {
            int first = await fixture.InsertAsync(AdaId, secondsAgo: 30);
            _ = await fixture.InsertAsync(AdaId, secondsAgo: 20);
            int third = await fixture.InsertAsync(AdaId, secondsAgo: 10);
            int bens = await fixture.InsertAsync(BenId, secondsAgo: 10);

            // --- Read: Ben's id and a missing one are ignored; a repeated id counts once ---
            InboxVerbResult read = await VerbAsync("read", new IdsRequest([first, first, bens, 999_999, 5_000_000_000]));
            Assert.Equal(new InboxVerbResult(1, 1), read);

            // --- Read again: nothing new, the version still moves ---
            Assert.Equal(new InboxVerbResult(0, 2), await VerbAsync("read", new IdsRequest([first])));

            // --- Read-all, clear, clear-read, seen ---
            Assert.Equal(new InboxVerbResult(2, 3), await VerbAsync("read-all"));
            Assert.Equal(new InboxVerbResult(1, 4), await VerbAsync("clear", new IdsRequest([third, bens])));
            Assert.Equal(new InboxVerbResult(2, 5), await VerbAsync("clear-read"));
            Assert.Equal(new InboxVerbResult(1, 6), await VerbAsync("seen"));
            Assert.Empty(await fixture.IdsAsync(AdaId));

            // --- Ben's row is untouched, and each verb told the caller once its batch committed ---
            Assert.Equal([bens], await fixture.IdsAsync(BenId, read: false));
            IReadOnlyList<ClientEvent> events = [.. fixture.Events.Published.Where(static published => published.Name == Notifier.InboxChangedEvent)];
            Assert.Equal(6, events.Count);
            Assert.All(events, static published => Assert.Equal([AdaId], published.UserIds));

            // --- The summary reads the version the verbs left, and the seen stamp ---
            InboxSummary summary = await SummaryAsync(AdaId);
            Assert.Equal(6, summary.Version);
            Assert.NotNull(summary.SeenAt);
            Assert.Equal(0, summary.Unseen);
            Assert.Null(summary.MaxUnseenSeverity);
        }

        [Fact]
        public async Task A_verb_naming_more_ids_than_the_ceiling_is_refused_before_anything_is_written()
        {
            int id = await fixture.InsertAsync(AdaId, secondsAgo: 10);

            LimitExceededException refused = await Assert.ThrowsAsync<LimitExceededException>(
                () => VerbAsync("read", new IdsRequest([id, .. Enumerable.Range(1, StackLimits.Default.MaxIds).Select(static value => (long)value + 1_000_000)])));

            Assert.Equal(LimitNames.MaxIds, refused.Limit);
            Assert.Equal([id], await fixture.IdsAsync(AdaId, read: false));
            Assert.Equal(0, (await SummaryAsync(AdaId)).Version);
        }

        [Fact]
        public async Task A_read_only_tenant_serves_the_verbs_and_a_frozen_one_refuses_them()
        {
            _ = await fixture.InsertAsync(AdaId, secondsAgo: 10);

            Assert.Equal(new InboxVerbResult(1, 1), await fixture.InvokeAsync<InboxVerbResult>(AdaId, Inbox, "read-all", state: TenantState.ReadOnly));
            Assert.Equal(1, (await fixture.InvokeAsync<InboxSummary>(AdaId, Inbox, "summary", state: TenantState.ReadOnly)).Version);

            TenantUnavailableException refused = await Assert.ThrowsAsync<TenantUnavailableException>(
                () => fixture.InvokeAsync<InboxVerbResult>(AdaId, Inbox, "clear-read", state: TenantState.Frozen));
            Assert.Equal(TenantProblemCodes.Frozen, refused.Code);
            Assert.Single(await fixture.IdsAsync(AdaId));
        }

        [Fact]
        public async Task A_save_stores_the_deviations_alone_moves_the_callers_preferences_tag_and_get_answers_them()
        {
            Guid adaBefore = await PreferencesTagAsync(AdaId);
            Guid benBefore = await PreferencesTagAsync(BenId);

            IReadOnlyList<NotificationPreference> saved = await fixture.InvokeAsync<IReadOnlyList<NotificationPreference>>(
                AdaId,
                Preferences,
                "save",
                new List<NotificationPreference>
                {
                    Row(FixtureNotifications.Shipped, FixtureNotifications.SmsChannel, false),
                    Row(FixtureNotifications.Shipped, CoreFeature.EmailChannel, true),
                    Row(FixtureNotifications.Shipped, CoreFeature.InboxChannel, true),
                    Row(FixtureNotifications.Held, CoreFeature.EmailChannel, false),
                });

            // --- The rows equal to their channel's default are dropped ---
            (string, string, bool)[] stored = [(FixtureNotifications.Shipped, CoreFeature.EmailChannel, true), (FixtureNotifications.Shipped, FixtureNotifications.SmsChannel, false)];
            Assert.Equal(stored, saved.Select(static row => (row.Type, row.Channel, row.Enabled)));
            Assert.NotEqual(adaBefore, await PreferencesTagAsync(AdaId));
            Assert.Equal(benBefore, await PreferencesTagAsync(BenId));

            // --- A row of a removed type is invisible to get, and the next save deletes it ---
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                string.Create(CultureInfo.InvariantCulture, $"INSERT INTO [core].[NotificationPreferences] ([UserId], [Type], [Channel], [Enabled]) VALUES ({AdaId}, N'fixture.removed', 'sms', 0);"));
            IReadOnlyList<NotificationPreference> got = await fixture.InvokeAsync<IReadOnlyList<NotificationPreference>>(AdaId, Preferences, "get");
            Assert.Equal(stored, got.Select(static row => (row.Type, row.Channel, row.Enabled)));

            // --- An empty save reverts everything to the defaults ---
            Assert.Empty(await fixture.InvokeAsync<IReadOnlyList<NotificationPreference>>(AdaId, Preferences, "save", new List<NotificationPreference>()));
            Assert.Equal(0, await SqlServerFixture.ScalarAsync<int>(fixture.ConnectionString, string.Create(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM [core].[NotificationPreferences] WHERE [UserId] = {AdaId};")));
        }

        [Fact]
        public async Task A_save_is_refused_with_the_codes_at_their_rows_and_writes_nothing()
        {
            Guid before = await PreferencesTagAsync(AdaId);

            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => fixture.InvokeAsync<IReadOnlyList<NotificationPreference>>(
                AdaId,
                Preferences,
                "save",
                new List<NotificationPreference>
                {
                    Row("fixture.unknown", CoreFeature.EmailChannel, true),
                    Row(FixtureNotifications.Shipped, "fax", true),
                    Row(FixtureNotifications.Shipped, CoreFeature.EmailChannel, true),
                    Row(FixtureNotifications.Shipped, CoreFeature.EmailChannel, false),
                    Row(FixtureNotifications.Held, CoreFeature.InboxChannel, false),
                }));

            Assert.Equal(
                [
                    ("[0].Type", NotificationValidationCodes.UnknownType),
                    ("[1].Channel", NotificationValidationCodes.UnknownChannel),
                    ("[3].Channel", NotificationValidationCodes.DuplicatePreference),
                    ("[4].Enabled", NotificationValidationCodes.CannotMute),
                ],
                refused.Errors.Select(static error => (error.Path.ToString(), error.Code)));
            Assert.Equal(before, await PreferencesTagAsync(AdaId));
            Assert.Equal(0, await SqlServerFixture.ScalarAsync<int>(fixture.ConnectionString, "SELECT COUNT(*) FROM [core].[NotificationPreferences];"));
        }

        [Fact]
        public async Task The_notification_stack_reads_the_callers_own_rows_and_answers_not_found_for_another_users()
        {
            int adas = await fixture.InsertAsync(AdaId, secondsAgo: 10);
            int bens = await fixture.InsertAsync(BenId, secondsAgo: 10);

            await using AsyncServiceScope scope = await fixture.UserScopeAsync(AdaId);
            EntityService<Notification, int> service = scope.ServiceProvider.GetRequiredService<EntityService<Notification, int>>();

            Notification own = Assert.Single((await service.GetByIdAsync(adas, DetailsRequest.None, Token)).Entities);
            Assert.Equal(adas, own.Id);
            NotFoundException refused = await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(bens, DetailsRequest.None, Token));
            Assert.Equal(CoreResources.Notification, refused.Resource);
        }

        /// <summary>The caller's summary.</summary>
        /// <param name="userId">The caller.</param>
        /// <returns>The summary.</returns>
        protected Task<InboxSummary> SummaryAsync(int userId)
        {
            return fixture.InvokeAsync<InboxSummary>(userId, Inbox, "summary");
        }

        /// <summary>A verb as Ada.</summary>
        /// <param name="action">The verb's action name.</param>
        /// <param name="body">The body; null for none.</param>
        /// <returns>The verb's result.</returns>
        private Task<InboxVerbResult> VerbAsync(string action, object? body = null)
        {
            return fixture.InvokeAsync<InboxVerbResult>(AdaId, Inbox, action, body);
        }

        /// <summary>A user's preferences tag.</summary>
        /// <param name="userId">The user.</param>
        /// <returns>The tag.</returns>
        private Task<Guid> PreferencesTagAsync(int userId)
        {
            return SqlServerFixture.ScalarAsync<Guid>(
                fixture.ConnectionString,
                string.Create(CultureInfo.InvariantCulture, $"SELECT [PreferencesTag] FROM [core].[UserStamps] WHERE [UserId] = {userId};"));
        }

        /// <summary>Inserts a job a user requested, created in id order.</summary>
        /// <param name="requester">The user who requested it.</param>
        /// <param name="status">Its status.</param>
        /// <param name="label">Its label's key; null for none.</param>
        /// <param name="scheduled">Whether it is a schedule's occurrence.</param>
        /// <returns>The job's id.</returns>
        private Task<int> JobAsync(int requester, JobStatus status, string? label = null, bool scheduled = false)
        {
            string lease = status == JobStatus.Running ? "NEWID(), DATEADD(minute, 5, SYSUTCDATETIME())" : "NULL, NULL";
            string pending = status == JobStatus.PendingConfirmation ? "N'[]'" : "NULL";
            string labelKey = label is null ? "NULL" : $"N'{label}'";
            string labelArguments = label is null ? "NULL" : "N'{\"title\":\"Q3\"}'";
            string schedule = scheduled ? "2, SYSUTCDATETIME()" : "NULL, NULL";
            string completed = status is JobStatus.Succeeded or JobStatus.Failed ? "SYSUTCDATETIME()" : "NULL";
            return SqlServerFixture.ScalarAsync<int>(
                fixture.ConnectionString,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"""
                    DECLARE @id int = NEXT VALUE FOR [core].[sq_Jobs];
                    INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [Attempts], [LeaseToken], [LeaseExpiresAt], [PendingConfirmationsJson], [LabelKey], [LabelArgumentsJson], [ScheduleId], [ScheduledFor], [CompletedAt], [RunAsUserId], [RequestedById], [CreatedAt])
                    VALUES (@id, N'{FixtureJobKeys.Probe}', '{status}', SYSUTCDATETIME(), 0, {lease}, {pending}, {labelKey}, {labelArguments}, {schedule}, {completed}, {requester}, {requester}, DATEADD(millisecond, @id, SYSUTCDATETIME()));
                    SELECT @id;
                    """));
        }

        /// <summary>A preference row as sent.</summary>
        /// <param name="type">The type.</param>
        /// <param name="channel">The channel.</param>
        /// <param name="enabled">Whether it is on.</param>
        /// <returns>The row.</returns>
        private static NotificationPreference Row(string type, string channel, bool enabled)
        {
            return new NotificationPreference { Type = type, Channel = channel, Enabled = enabled };
        }
    }

    /// <summary>The inbox with read-committed snapshot on, as provisioning creates a tenant database.</summary>
    /// <param name="fixture">The class's database.</param>
    [Trait("Category", "Integration")]
    public sealed class InboxSnapshotTests(InboxDatabase fixture) : InboxServiceTests(fixture), IClassFixture<InboxDatabase>
    {
        [Fact]
        public async Task A_summary_reads_the_committed_rows_while_a_verbs_transaction_holds_them()
        {
            _ = await Fixture.InsertAsync(AdaId, secondsAgo: 20);
            _ = await Fixture.InsertAsync(AdaId, secondsAgo: 10);

            // --- Another session marks the rows read and stamps the version, and has not committed ---
            await using SqlConnection connection = new(Fixture.ConnectionString);
            await connection.OpenAsync(Token);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(Token);
            await using (SqlCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = string.Create(
                    CultureInfo.InvariantCulture,
                    $"UPDATE [core].[Notifications] SET [ReadAt] = SYSUTCDATETIME() WHERE [UserId] = {AdaId}; UPDATE [core].[UserStamps] SET [InboxVersion] = [InboxVersion] + 1 WHERE [UserId] = {AdaId};");
                _ = await command.ExecuteNonQueryAsync(Token);
            }

            // --- The summary is served from the row versions, without waiting on the writer ---
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            InboxSummary summary = await SummaryAsync(AdaId).WaitAsync(deadline.Token);
            Assert.Equal((2, 0L), (summary.Unread, summary.Version));

            await transaction.RollbackAsync(Token);
        }
    }

    /// <summary>The inbox with read-committed snapshot off, where readers take shared locks.</summary>
    /// <param name="fixture">The class's database.</param>
    [Trait("Category", "Integration")]
    public sealed class InboxLockingTests(LockingInboxDatabase fixture) : InboxServiceTests(fixture), IClassFixture<LockingInboxDatabase>;
}
