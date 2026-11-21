// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Composition;
using Tellma.Core.IntegrationTests.Jobs;
using Tellma.Core.Outbox;
using Tellma.Testing.Entities;
using static Tellma.Core.IntegrationTests.Outbox.OutboxDatabase;

namespace Tellma.Core.IntegrationTests.Outbox
{
    /// <summary>
    ///     The outbox against a fixture database: an enqueue in a job's frame committing its rows,
    ///     their attachments' blobs and their sending jobs with the job's completion, a committed blob
    ///     attached as a copy, a frame that throws before persisting and a completion that fails after
    ///     it each leaving only the staged blobs, a refused blob reference reported at its path, and the email notification channel
    ///     enqueuing one email per addressed recipient with the delivery job's completion, skipping a
    ///     recipient whose address cannot be stored, or none when the job fails. Every batch's writes
    ///     are audited against change tracking.
    /// </summary>
    /// <param name="fixture">The class's database, reset before each test.</param>
    [Trait("Category", "Integration")]
    public sealed class OutboxTests(OutboxDatabase fixture) : IAsyncLifetime, IClassFixture<OutboxDatabase>
    {
        /// <summary>The notification delivery handler's key.</summary>
        private const string Delivery = "core.notification-delivery";

        /// <summary>The first bytes of a PNG file, which the attachments carry.</summary>
        private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

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
        public async Task An_enqueue_in_a_jobs_frame_commits_the_row_its_attachments_and_its_sending_job_with_the_jobs_completion()
        {
            var sendAfter = new DateTimeOffset(DateTime.UtcNow.AddHours(1).Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, TimeSpan.Zero);
            fixture.Script.Probe = async (batch, context) =>
            {
                // --- A file the frame stages itself, then the email with one file of its own and that one ---
                BlobDescriptor staged = await context.Services.GetRequiredService<IBlobService>().StageAsync(
                    new BlobStageRequest(OutgoingEmailAttachment.BlobKind, new MemoryStream(Png(40), writable: false), 40, BlobContentTypes.Png, "staged.png"),
                    Token);
                EmailMessage message = Message() with
                {
                    Cc = [new EmailAddress("cc@example.com")],
                    HtmlBody = "<p>The report is attached.</p>",
                    Attachments = [new EmailAttachment("own.png", BlobContentTypes.Png, Png(30))],
                };
                IEmailOutbox outbox = context.Services.GetRequiredService<IEmailOutbox>();
                _ = await outbox.EnqueueAsync(
                    context.Host,
                    [new EmailEnqueueRequest(message, [new EmailBlobAttachment(OutgoingEmailAttachment.BlobKind, staged.Id, FileName: "renamed.png")], AdaId, FixtureNotifications.ShipmentResource, 42, sendAfter)],
                    Token);
                _ = await context.Host.PersistAsync(Token);
            };
            _ = await fixture.InsertJobAsync(JobRunKeys.Probe, WellKnownIds.SystemUserId);

            PolledJob polled = Assert.Single(await JobPollDriver.PollOnceAsync(fixture.Tenant, [JobRunKeys.Probe], Token, Token));
            Assert.Equal((PolledJobEnd.Completed, JobStatus.Succeeded), (polled.End, polled.Status));

            // --- The pending row, its envelope as given and its addresses as stored lists ---
            IReadOnlyDictionary<string, object?> email = Assert.Single(await fixture.RowsAsync("SELECT * FROM [core].[OutgoingEmails];"));
            Assert.Equal(
                (nameof(OutgoingEmailState.Pending), nameof(EmailAudience.Internal), "The monthly report", (object?)AdaId, FixtureNotifications.ShipmentResource, (object?)42L, (object?)0),
                (email["State"], email["Audience"], email["Subject"], email["RecipientUserId"], email["RegardingEntity"], email["RegardingId"], email["AttemptCount"]));
            Assert.Equal([new EmailAddress(SignInEmail(AdaId), "Ada")], EmailAddressList.Parse((string)email["To"]!));
            Assert.Equal([new EmailAddress("cc@example.com")], EmailAddressList.Parse((string)email["Cc"]!));
            Assert.Null(email["Bcc"]);
            Assert.Equal((object?)false, email["ExpectsDeliveryEvents"]);

            // --- Its sending job: run by the platform, requested by no one, due at the send-after instant ---
            IReadOnlyDictionary<string, object?> job = Assert.Single(await fixture.RowsAsync(
                string.Create(CultureInfo.InvariantCulture, $"SELECT * FROM [core].[Jobs] WHERE [Id] = {email["JobId"]};")));
            Assert.Equal(
                (OutgoingEmailService.SendHandlerKey, nameof(JobStatus.Pending), (object?)WellKnownIds.SystemUserId, null),
                (job["HandlerKey"], job["Status"], job["RunAsUserId"], job["RequestedById"]));
            Assert.Equal(sendAfter, Assert.IsType<DateTimeOffset>(job["DueAt"]));

            // --- Both attachments, each a committed email attachment with no thumbnail ---
            IReadOnlyList<IReadOnlyDictionary<string, object?>> attachments = await fixture.RowsAsync(string.Create(
                CultureInfo.InvariantCulture,
                $"""
                SELECT a.[FileName], b.[Kind], b.[State], b.[Variants], b.[Size]
                FROM [core].[OutgoingEmailAttachments] AS a JOIN [core].[Blobs] AS b ON b.[Id] = a.[FileId]
                WHERE a.[OutgoingEmailId] = {email["Id"]} ORDER BY a.[Id];
                """));
            Assert.Equal(
                [("own.png", 30L), ("renamed.png", 40L)],
                attachments.Select(static row => ((string)row["FileName"]!, Convert.ToInt64(row["Size"], CultureInfo.InvariantCulture))));
            Assert.All(attachments, static row => Assert.Equal(
                (OutgoingEmailAttachment.BlobKind, nameof(BlobState.Committed), string.Empty),
                ((string)row["Kind"]!, (string)row["State"]!, (string?)row["Variants"] ?? string.Empty)));
        }

        [Fact]
        public async Task A_committed_blob_is_attached_as_a_copy_the_email_owns_and_the_original_stays()
        {
            // --- A first email commits its own attachment ---
            fixture.Script.Probe = async (batch, context) =>
            {
                _ = await context.Services.GetRequiredService<IEmailOutbox>().EnqueueAsync(
                    context.Host, [new EmailEnqueueRequest(Message() with { Attachments = [new EmailAttachment("first.png", BlobContentTypes.Png, Png(25))] })], Token);
                _ = await context.Host.PersistAsync(Token);
            };
            _ = await fixture.InsertJobAsync(JobRunKeys.Probe, WellKnownIds.SystemUserId);
            _ = Assert.Single(await JobPollDriver.PollOnceAsync(fixture.Tenant, [JobRunKeys.Probe], Token, Token));
            int original = Convert.ToInt32(Assert.Single(await fixture.RowsAsync("SELECT [FileId] FROM [core].[OutgoingEmailAttachments];"))["FileId"], CultureInfo.InvariantCulture);

            // --- A second email attaches that committed blob by reference ---
            fixture.Script.Probe = async (batch, context) =>
            {
                _ = await context.Services.GetRequiredService<IEmailOutbox>().EnqueueAsync(
                    context.Host, [new EmailEnqueueRequest(Message(), [new EmailBlobAttachment(OutgoingEmailAttachment.BlobKind, original)])], Token);
                _ = await context.Host.PersistAsync(Token);
            };
            _ = await fixture.InsertJobAsync(JobRunKeys.Probe, WellKnownIds.SystemUserId);
            PolledJob polled = Assert.Single(await JobPollDriver.PollOnceAsync(fixture.Tenant, [JobRunKeys.Probe], Token, Token));
            Assert.Equal((PolledJobEnd.Completed, JobStatus.Succeeded), (polled.End, polled.Status));

            // --- Two committed blobs of the same bytes: the original and the second email's copy ---
            IReadOnlyList<IReadOnlyDictionary<string, object?>> blobs = await fixture.RowsAsync(
                "SELECT a.[FileId], a.[FileName], b.[State], b.[StorageKey] FROM [core].[OutgoingEmailAttachments] AS a JOIN [core].[Blobs] AS b ON b.[Id] = a.[FileId] ORDER BY a.[Id];");
            Assert.Equal(2, blobs.Count);
            Assert.Equal(original, Convert.ToInt32(blobs[0]["FileId"], CultureInfo.InvariantCulture));
            Assert.NotEqual(original, Convert.ToInt32(blobs[1]["FileId"], CultureInfo.InvariantCulture));
            Assert.Equal(["first.png", "first.png"], blobs.Select(static row => (string)row["FileName"]!));
            Assert.All(blobs, static row => Assert.Equal(nameof(BlobState.Committed), row["State"]));
            Assert.All(blobs, row => Assert.Equal(Png(25), fixture.Blobs.Find(FixtureTenant.TenantId, (string)row["StorageKey"]!)?.Bytes));
        }

        [Fact]
        public async Task A_frame_that_throws_after_the_enqueue_leaves_only_the_staged_blobs()
        {
            fixture.Script.Probe = async (batch, context) =>
            {
                _ = await context.Services.GetRequiredService<IEmailOutbox>().EnqueueAsync(
                    context.Host, [new EmailEnqueueRequest(Message() with { Attachments = [new EmailAttachment("lost.png", BlobContentTypes.Png, Png(20))] })], Token);
                throw new InvalidOperationException("The handler failed before its frame persisted.");
            };
            _ = await fixture.InsertJobAsync(JobRunKeys.Probe, WellKnownIds.SystemUserId);

            PolledJob polled = Assert.Single(await JobPollDriver.PollOnceAsync(fixture.Tenant, [JobRunKeys.Probe], Token, Token));
            Assert.NotEqual(JobStatus.Succeeded, polled.Status);

            // --- No row and no sending job; the staged blob waits for its deadline ---
            Assert.Empty(await fixture.RowsAsync("SELECT [Id] FROM [core].[OutgoingEmails];"));
            Assert.Empty(await fixture.RowsAsync($"SELECT [Id] FROM [core].[Jobs] WHERE [HandlerKey] = N'{OutgoingEmailService.SendHandlerKey}';"));
            IReadOnlyDictionary<string, object?> blob = Assert.Single(await fixture.RowsAsync("SELECT [Kind], [State] FROM [core].[Blobs];"));
            Assert.Equal((OutgoingEmailAttachment.BlobKind, nameof(BlobState.Staged)), ((string)blob["Kind"]!, (string)blob["State"]!));
        }

        [Fact]
        public async Task A_completion_that_fails_after_the_enqueue_persisted_rolls_back_the_row_its_job_and_the_blob_confirm()
        {
            // --- The enqueue's groups sit in the completion batch; a later statement then breaks it ---
            fixture.Script.Probe = async (batch, context) =>
            {
                _ = await context.Services.GetRequiredService<IEmailOutbox>().EnqueueAsync(
                    context.Host, [new EmailEnqueueRequest(Message() with { Attachments = [new EmailAttachment("persisted.png", BlobContentTypes.Png, Png(20))] })], Token);
                _ = await context.Host.PersistAsync(Token);
                _ = batch.Batch.Sql($"DECLARE @zero int = 0, @broken int; SET @broken = 1 / @zero;", SqlOptions.None);
            };
            int id = await fixture.InsertJobAsync(JobRunKeys.Probe, WellKnownIds.SystemUserId);

            // --- The outcomes alone are recorded: a retry of the unexpected failure ---
            PolledJob polled = Assert.Single(await JobPollDriver.PollOnceAsync(fixture.Tenant, [JobRunKeys.Probe], Token, Token));
            Assert.Equal((id, PolledJobEnd.Completed, JobStatus.Pending, JobErrorCodes.Internal), (polled.JobId, polled.End, polled.Status, polled.ErrorCode));

            // --- No row and no sending job; the blob was never confirmed ---
            Assert.Empty(await fixture.RowsAsync("SELECT [Id] FROM [core].[OutgoingEmails];"));
            Assert.Empty(await fixture.RowsAsync("SELECT [Id] FROM [core].[OutgoingEmailAttachments];"));
            Assert.Empty(await fixture.RowsAsync($"SELECT [Id] FROM [core].[Jobs] WHERE [HandlerKey] = N'{OutgoingEmailService.SendHandlerKey}';"));
            IReadOnlyDictionary<string, object?> blob = Assert.Single(await fixture.RowsAsync("SELECT [Kind], [State] FROM [core].[Blobs];"));
            Assert.Equal((OutgoingEmailAttachment.BlobKind, nameof(BlobState.Staged)), ((string)blob["Kind"]!, (string)blob["State"]!));
        }

        [Fact]
        public async Task A_blob_reference_that_does_not_resolve_is_refused_at_its_path_before_anything_is_staged()
        {
            ValidationException? refused = null;
            fixture.Script.Probe = async (batch, context) =>
            {
                refused = await Assert.ThrowsAsync<ValidationException>(() => context.Services.GetRequiredService<IEmailOutbox>().EnqueueAsync(
                    context.Host,
                    [
                        new EmailEnqueueRequest(Message()),
                        new EmailEnqueueRequest(
                            Message() with { Attachments = [new EmailAttachment("kept.png", BlobContentTypes.Png, Png(20))] },
                            [new EmailBlobAttachment(OutgoingEmailAttachment.BlobKind, 987654)]),
                    ],
                    Token));
            };
            _ = await fixture.InsertJobAsync(JobRunKeys.Probe, WellKnownIds.SystemUserId);

            PolledJob polled = Assert.Single(await JobPollDriver.PollOnceAsync(fixture.Tenant, [JobRunKeys.Probe], Token, Token));
            Assert.Equal(JobStatus.Succeeded, polled.Status);

            ValidationError error = Assert.Single(Assert.IsType<ValidationException>(refused).Errors);
            Assert.Equal(("[1].BlobAttachments[0]", ValidationCodes.BlobNotAttachable), (error.Path.ToString(), error.Code));
            Assert.Empty(await fixture.RowsAsync("SELECT [Id] FROM [core].[Blobs];"));
            Assert.Empty(await fixture.RowsAsync("SELECT [Id] FROM [core].[OutgoingEmails];"));
        }

        [Fact]
        public async Task The_email_channel_enqueues_one_email_per_addressed_recipient_with_the_delivery_jobs_completion()
        {
            // --- Ada and Cy opt into email; Cy is written to at the contact address ---
            await PreferEmailAsync(AdaId, CyId);
            await NotifyAsync(new NotificationRequest(FixtureNotifications.Shipped, [CyId, AdaId], new { Number = "SO-7" }, FixtureNotifications.ShipmentResource, 42, null, null));

            PolledJob polled = Assert.Single(await JobPollDriver.PollOnceAsync(fixture.Tenant, [Delivery], Token, Token));
            Assert.Equal((PolledJobEnd.Completed, JobStatus.Succeeded), (polled.End, polled.Status));

            // --- One internal email each, in the recipients' order, about the notification's target ---
            IReadOnlyList<IReadOnlyDictionary<string, object?>> emails = await fixture.RowsAsync("SELECT * FROM [core].[OutgoingEmails] ORDER BY [Id];");
            Assert.Equal([(object?)CyId, AdaId], emails.Select(static row => row["RecipientUserId"]));
            Assert.Equal([new EmailAddress(CyContactEmail, "Cy")], EmailAddressList.Parse((string)emails[0]["To"]!));
            Assert.Equal([new EmailAddress(SignInEmail(AdaId), "Ada")], EmailAddressList.Parse((string)emails[1]["To"]!));

            string link = fixture.Tenant.Services.GetRequiredService<ITellmaLinks>().Target(FixtureTenant.TenantId, FixtureNotifications.ShipmentResource, 42);
            Assert.All(emails, row =>
            {
                Assert.Equal(
                    (nameof(OutgoingEmailState.Pending), nameof(EmailAudience.Internal), FixtureNotifications.ShipmentResource, (object?)42L),
                    (row["State"], row["Audience"], row["RegardingEntity"], row["RegardingId"]));
                Assert.StartsWith("Fixture: ", (string)row["Subject"]!, StringComparison.Ordinal);
                Assert.Contains(link, (string)row["TextBody"]!, StringComparison.Ordinal);
                Assert.Contains($"href=\"{link}\"", (string)row["HtmlBody"]!, StringComparison.Ordinal);
                Assert.NotNull(row["JobId"]);
            });

            // --- Each row's sending job committed with the delivery job's completion ---
            Assert.Equal(
                2,
                Assert.Single(await fixture.RowsAsync($"SELECT COUNT(*) AS [Count] FROM [core].[Jobs] WHERE [HandlerKey] = N'{OutgoingEmailService.SendHandlerKey}' AND [Status] = 'Pending';"))["Count"]);
        }

        [Fact]
        public async Task A_recipient_whose_address_the_outbox_refuses_is_skipped_and_the_others_emails_commit()
        {
            // --- Dee's contact address is a mailbox; Eve's name holds a tab ---
            await PreferEmailAsync(AdaId, DeeId, EveId);
            await NotifyAsync(new NotificationRequest(FixtureNotifications.Shipped, [AdaId, DeeId, EveId], null, FixtureNotifications.ShipmentResource, 42, null, null));

            PolledJob polled = Assert.Single(await JobPollDriver.PollOnceAsync(fixture.Tenant, [Delivery], Token, Token));
            Assert.Equal((PolledJobEnd.Completed, JobStatus.Succeeded), (polled.End, polled.Status));

            // --- Ada's email and Eve's, her name on one line; none to Dee ---
            IReadOnlyList<IReadOnlyDictionary<string, object?>> emails = await fixture.RowsAsync("SELECT [RecipientUserId], [To] FROM [core].[OutgoingEmails] ORDER BY [Id];");
            Assert.Equal([(object?)AdaId, EveId], emails.Select(static row => row["RecipientUserId"]));
            Assert.Equal([new EmailAddress(SignInEmail(EveId), "Eve Stone")], EmailAddressList.Parse((string)emails[1]["To"]!));
        }

        [Fact]
        public async Task A_delivery_job_that_fails_after_the_email_channel_enlisted_commits_no_email()
        {
            // --- Email delivers first, then sms throws: the job's frame never persists ---
            fixture.Sms.Fails = true;
            await PreferEmailAsync(AdaId);
            await NotifyAsync(new NotificationRequest(FixtureNotifications.Shipped, [AdaId], null, FixtureNotifications.ShipmentResource, 42, null, null));

            PolledJob polled = Assert.Single(await JobPollDriver.PollOnceAsync(fixture.Tenant, [Delivery], Token, Token));
            Assert.NotEqual(JobStatus.Succeeded, polled.Status);

            Assert.Empty(await fixture.RowsAsync("SELECT [Id] FROM [core].[OutgoingEmails];"));
            Assert.Empty(await fixture.RowsAsync($"SELECT [Id] FROM [core].[Jobs] WHERE [HandlerKey] = N'{OutgoingEmailService.SendHandlerKey}';"));
        }

        /// <summary>A plain internal message to Ada.</summary>
        /// <returns>The message.</returns>
        private static EmailMessage Message()
        {
            return new EmailMessage
            {
                To = [new EmailAddress(SignInEmail(AdaId), "Ada")],
                Subject = "The monthly report",
                TextBody = "The report is attached.",
                Audience = EmailAudience.Internal,
            };
        }

        /// <summary>Bytes that open with a PNG signature.</summary>
        /// <param name="length">How many bytes.</param>
        /// <returns>The bytes.</returns>
        private static byte[] Png(int length)
        {
            byte[] bytes = new byte[length];
            PngSignature.CopyTo(bytes, 0);
            return bytes;
        }

        /// <summary>Raises a request in a persist batch of its own, in a system scope.</summary>
        /// <param name="request">The request.</param>
        /// <returns>A task that completes once the batch committed.</returns>
        private async Task NotifyAsync(NotificationRequest request)
        {
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            await scope.ServiceProvider.GetRequiredService<INotifier>().NotifyAsync([request], Token);
        }

        /// <summary>Turns email on for the shipped type, for each user.</summary>
        /// <param name="userIds">The users.</param>
        /// <returns>A task that completes when the preferences are stored.</returns>
        private Task PreferEmailAsync(params int[] userIds)
        {
            return SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                [.. userIds.Select(static userId => string.Create(
                    CultureInfo.InvariantCulture,
                    $"INSERT INTO [core].[NotificationPreferences] ([UserId], [Type], [Channel], [Enabled]) VALUES ({userId}, N'{FixtureNotifications.Shipped}', '{CoreFeature.EmailChannel}', 1);"))]);
        }
    }
}
