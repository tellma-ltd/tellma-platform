// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Composition;
using Tellma.Core.Jobs;
using Tellma.Testing.Entities;
using static Tellma.Core.IntegrationTests.Notifications.InboxDatabase;

namespace Tellma.Core.IntegrationTests.Notifications
{
    /// <summary>
    ///     The delivery and retention handlers under the worker against a fixture database: a raised
    ///     request's delivery job resolving each channel's recipients from their preferences, with each
    ///     recipient's own localization, on the job's write host; a failing channel's job failing with
    ///     its notice in the administrator's inbox only; a failure notice naming no actor whichever scope
    ///     completes the job; and retention deleting each band a page at a
    ///     time. Every batch's writes are audited against change tracking.
    /// </summary>
    /// <param name="fixture">The class's database, reset before each test.</param>
    [Trait("Category", "Integration")]
    public sealed class NotificationJobsTests(InboxDatabase fixture) : IAsyncLifetime, IClassFixture<InboxDatabase>
    {
        /// <summary>The delivery handler's key.</summary>
        private const string Delivery = "core.notification-delivery";

        /// <summary>The retention handler's key.</summary>
        private const string Retention = "core.notification-retention";

        /// <summary>The cancellation token of the running test.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

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
        public async Task A_delivery_job_resolves_each_channels_recipients_and_delivers_on_the_jobs_write_host()
        {
            // --- Ada opts into email, Ben mutes sms; Cy takes the defaults; the inactive user receives nothing ---
            await PreferAsync((AdaId, CoreFeature.EmailChannel, true), (BenId, FixtureNotifications.SmsChannel, false));
            await NotifyAsync(new NotificationRequest(
                FixtureNotifications.Shipped, [CyId, BenId, AdaId, InactiveUserId], new { Number = "SO-7" }, FixtureNotifications.ShipmentResource, 42, DeeId, null));

            PolledJob polled = Assert.Single(await JobPollDriver.PollOnceAsync(fixture.Tenant, [Delivery], Token, Token));

            Assert.Equal((PolledJobEnd.Completed, JobStatus.Succeeded), (polled.End, polled.Status));

            // --- Email, then sms, in registration order; each list in the request's order ---
            IReadOnlyList<Delivery> delivered = fixture.Deliveries.Delivered;
            Assert.Equal(
                [(CoreFeature.EmailChannel, new[] { AdaId }), (FixtureNotifications.SmsChannel, new[] { CyId, AdaId })],
                [.. delivered.Select(static delivery => (delivery.Channel, delivery.Recipients.Select(static recipient => recipient.UserId).ToArray()))],
                new DeliveryComparer());
            Assert.All(delivered, static delivery => Assert.Equal(WriteHostKind.Job, delivery.Host));

            // --- The request rebuilt from the slice ---
            NotificationRequest request = delivered[0].Request;
            Assert.Equal(FixtureNotifications.Shipped, request.Type);
            Assert.Equal([CyId, BenId, AdaId, InactiveUserId], request.RecipientIds);
            Assert.Equal(NotificationSeverity.Info, request.Severity);
            Assert.Equal("SO-7", Assert.IsType<JsonElement>(request.Arguments).GetProperty("number").GetString());
            Assert.Equal((FixtureNotifications.ShipmentResource, 42L, DeeId), (request.TargetResource, request.TargetId, request.ActorUserId));
            Assert.Null(request.DedupKey);

            // --- Each recipient's contact columns and their own localization ---
            NotificationRecipient cy = delivered[1].Recipients[0];
            Assert.Equal(("Cy", "+1555103"), (cy.Name, cy.ContactMobile));
            Assert.Equal(TimeSpan.FromHours(3), cy.Localization.TimeZone.BaseUtcOffset);
            Assert.Equal(TimeSpan.Zero, delivered[1].Recipients[1].Localization.TimeZone.BaseUtcOffset);
        }

        [Fact]
        public async Task A_channel_that_throws_on_the_last_attempt_fails_the_job_and_its_notice_reaches_the_inbox_only()
        {
            fixture.Deliveries.SmsFails = true;
            await PreferAsync((AdaId, CoreFeature.EmailChannel, true));
            await NotifyAsync(new NotificationRequest(FixtureNotifications.Shipped, [AdaId], null, FixtureNotifications.ShipmentResource, 42, null, null));
            int jobId = await SqlServerFixture.ScalarAsync<int>(fixture.ConnectionString, $"SELECT [Id] FROM [core].[Jobs] WHERE [HandlerKey] = N'{Delivery}';");
            await SqlServerFixture.ExecuteAsync(fixture.ConnectionString, string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Jobs] SET [Attempts] = 11 WHERE [Id] = {jobId};"));

            PolledJob polled = Assert.Single(await JobPollDriver.PollOnceAsync(fixture.Tenant, [Delivery], Token, Token));

            Assert.Equal((jobId, JobStatus.Failed), (polled.JobId, polled.Status));

            // --- The administrator's notice is in their inbox, and no delivery job carries it to sms ---
            Assert.Equal(
                1,
                await SqlServerFixture.ScalarAsync<int>(
                    fixture.ConnectionString,
                    string.Create(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM [core].[Notifications] WHERE [UserId] = {AdministratorId} AND [Type] = N'{JobNotices.FailedDescriptor.Key}';")));
            Assert.Equal(
                [jobId],
                await IdsAsync($"SELECT [Id] FROM [core].[Jobs] WHERE [HandlerKey] = N'{Delivery}' ORDER BY [Id];"));
        }

        [Theory]
        [InlineData(11)]
        [InlineData(12)]
        public async Task A_failed_job_run_as_a_user_raises_its_notice_with_no_actor_whether_its_handler_or_the_system_completes_it(int attempts)
        {
            // --- A delivery job run as and requested by Ada: on its last attempt the handler fails in
            // Ada's scope; past its attempts it fails without the handler, in the system scope ---
            fixture.Deliveries.SmsFails = true;
            await PreferAsync((AdaId, CoreFeature.EmailChannel, true));
            await NotifyAsync(new NotificationRequest(FixtureNotifications.Shipped, [AdaId], null, FixtureNotifications.ShipmentResource, 42, null, null));
            int jobId = await SqlServerFixture.ScalarAsync<int>(fixture.ConnectionString, $"SELECT [Id] FROM [core].[Jobs] WHERE [HandlerKey] = N'{Delivery}';");
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Jobs] SET [Attempts] = {attempts}, [RunAsUserId] = {AdaId}, [RequestedById] = {AdaId} WHERE [Id] = {jobId};"));

            PolledJob polled = Assert.Single(await JobPollDriver.PollOnceAsync(fixture.Tenant, [Delivery], Token, Token));

            // --- The notice reaches Ada, caused by the system rather than by Ada herself ---
            Assert.Equal((jobId, JobStatus.Failed), (polled.JobId, polled.Status));
            Assert.Equal(
                0,
                await SqlServerFixture.ScalarAsync<int>(
                    fixture.ConnectionString,
                    string.Create(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM [core].[Notifications] WHERE [UserId] = {AdaId} AND [Type] = N'{JobNotices.FailedDescriptor.Key}' AND [ActorUserId] IS NOT NULL;")));
            Assert.Equal(
                1,
                await SqlServerFixture.ScalarAsync<int>(
                    fixture.ConnectionString,
                    string.Create(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM [core].[Notifications] WHERE [UserId] = {AdaId} AND [Type] = N'{JobNotices.FailedDescriptor.Key}';")));
        }

        [Fact]
        public async Task Retention_deletes_each_band_past_its_cut_off_a_page_at_a_time()
        {
            // --- Seven read rows past 90 days, four unread past 365, and rows inside both cut-offs ---
            for (int index = 0; index < 7; index++)
            {
                _ = await fixture.InsertAsync(AdaId, secondsAgo: Days(200), readSecondsAgo: Days(91) + index);
            }

            for (int index = 0; index < 4; index++)
            {
                _ = await fixture.InsertAsync(BenId, secondsAgo: Days(366) + index);
            }

            int recentRead = await fixture.InsertAsync(AdaId, secondsAgo: Days(200), readSecondsAgo: Days(89));
            int recentUnread = await fixture.InsertAsync(BenId, secondsAgo: Days(364));
            int jobId = await RetentionJobAsync();
            using MetricCollector<long> retained = new(fixture.Tenant.Services.GetRequiredService<IMeterFactory>(), NotificationsTelemetryNames.MeterName, NotificationsTelemetryNames.Retained);

            PolledJob polled = Assert.Single(await JobPollDriver.PollOnceAsync(fixture.Tenant, [Retention], Token, Token));

            Assert.Equal((jobId, JobStatus.Succeeded), (polled.JobId, polled.Status));
            Assert.Equal([recentRead, recentUnread], await IdsAsync("SELECT [Id] FROM [core].[Notifications] ORDER BY [Id];"));

            // --- A page of three at a time: the read band's 3, 3, 1, then the unread band's 3, 1 ---
            Assert.Equal(
                [("read", 3L), ("read", 3L), ("read", 1L), ("unread", 3L), ("unread", 1L)],
                retained.GetMeasurementSnapshot().Select(static measurement => ((string)measurement.Tags[NotificationsTelemetryNames.BandTag]!, measurement.Value)));

            // --- Each page reports both bands' running totals; the last names them all ---
            Assert.Equal(
                [
                    "Deleted 3 read notifications and 0 unread notifications.",
                    "Deleted 6 read notifications and 0 unread notifications.",
                    "Deleted 7 read notifications and 0 unread notifications.",
                    "Deleted 7 read notifications and 3 unread notifications.",
                    "Deleted 7 read notifications and 4 unread notifications.",
                ],
                fixture.Log.GetSnapshot()
                    .Where(static record => record.Id.Name == "JobProgress")
                    .Select(static record => record.StructuredState!.Single(static pair => pair.Key == "Message").Value));
        }

        [Fact]
        public async Task Retention_with_nothing_to_delete_succeeds_after_one_page_per_band()
        {
            _ = await fixture.InsertAsync(AdaId, secondsAgo: 10);
            int jobId = await RetentionJobAsync();

            PolledJob polled = Assert.Single(await JobPollDriver.PollOnceAsync(fixture.Tenant, [Retention], Token, Token));

            Assert.Equal((jobId, JobStatus.Succeeded), (polled.JobId, polled.Status));
            Assert.Single(await fixture.IdsAsync(AdaId));
        }

        /// <summary>Seconds in some days.</summary>
        /// <param name="days">The days.</param>
        /// <returns>The seconds.</returns>
        private static int Days(int days)
        {
            return days * 24 * 60 * 60;
        }

        /// <summary>Raises a request in a persist batch of its own, in a system scope.</summary>
        /// <param name="request">The request.</param>
        /// <returns>A task that completes once the batch committed.</returns>
        private async Task NotifyAsync(NotificationRequest request)
        {
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            await scope.ServiceProvider.GetRequiredService<INotifier>().NotifyAsync([request], Token);
        }

        /// <summary>Stores preference rows directly.</summary>
        /// <param name="preferences">The rows: user, channel and whether it is on, all for the shipped type.</param>
        /// <returns>A task that completes when the rows are stored.</returns>
        private Task PreferAsync(params (int UserId, string Channel, bool Enabled)[] preferences)
        {
            return SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                [.. preferences.Select(static preference => string.Create(
                    CultureInfo.InvariantCulture,
                    $"INSERT INTO [core].[NotificationPreferences] ([UserId], [Type], [Channel], [Enabled]) VALUES ({preference.UserId}, N'{FixtureNotifications.Shipped}', '{preference.Channel}', {(preference.Enabled ? 1 : 0)});"))]);
        }

        /// <summary>Inserts a due retention job, as the built-in schedule would, run by the system user.</summary>
        /// <returns>The job's id.</returns>
        private Task<int> RetentionJobAsync()
        {
            return SqlServerFixture.ScalarAsync<int>(
                fixture.ConnectionString,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"""
                    DECLARE @id int = NEXT VALUE FOR [core].[sq_Jobs];
                    INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [Attempts], [RunAsUserId], [CreatedAt])
                    VALUES (@id, N'{Retention}', 'Pending', DATEADD(second, -5, SYSUTCDATETIME()), 0, {WellKnownIds.SystemUserId}, SYSUTCDATETIME());
                    SELECT @id;
                    """));
        }

        /// <summary>The ids a query returns.</summary>
        /// <param name="query">The query, whose first column is an int.</param>
        /// <returns>The ids, in the query's order.</returns>
        private async Task<IReadOnlyList<int>> IdsAsync(string query)
        {
            List<int> ids = [];
            await using SqlConnection connection = new(fixture.ConnectionString);
            await connection.OpenAsync(Token);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = query;
            await using SqlDataReader reader = await command.ExecuteReaderAsync(Token);
            while (await reader.ReadAsync(Token))
            {
                ids.Add(reader.GetInt32(0));
            }

            return ids;
        }

        /// <summary>Compares a channel and its recipients by value.</summary>
        private sealed class DeliveryComparer : IEqualityComparer<(string Channel, int[] Recipients)>
        {
            /// <inheritdoc />
            public bool Equals((string Channel, int[] Recipients) x, (string Channel, int[] Recipients) y)
            {
                return x.Channel == y.Channel && x.Recipients.SequenceEqual(y.Recipients);
            }

            /// <inheritdoc />
            public int GetHashCode((string Channel, int[] Recipients) obj)
            {
                return StringComparer.Ordinal.GetHashCode(obj.Channel);
            }
        }
    }
}
