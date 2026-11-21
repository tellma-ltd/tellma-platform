// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text.Json;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Calendars;
using Tellma.Core.Localization;
using Tellma.Core.Notifications;
using Tellma.Core.Outbox;
using Tellma.Core.Tests.Access;

namespace Tellma.Core.Tests.Outbox
{
    /// <summary>
    ///     The email channel's delivery with no database, over a fake outbox and renderer: one request
    ///     per addressed recipient in their order, to the contact address over the sign-in address,
    ///     a recipient with neither skipped; one rendering per localization, in each recipient's
    ///     language, calendar and zone, with the tenant's name in their content language; the link to
    ///     the target in both bodies, HTML-encoded in the HTML one; a recipient whose address the
    ///     outbox cannot store skipped with a warning while the others are enqueued, and a name with
    ///     a control character kept on one line; and nothing enqueued when no recipient has an
    ///     address the outbox can store.
    /// </summary>
    public sealed class EmailNotificationDeliveryTests : IDisposable
    {
        /// <summary>The tenant every scope here belongs to.</summary>
        private const int TenantId = 7;

        /// <summary>A zone three hours ahead of UTC, the same on every host.</summary>
        private static readonly TimeZoneInfo PlusThree = TimeZoneInfo.CreateCustomTimeZone("Test+03", TimeSpan.FromHours(3), "Test+03", "Test+03");

        /// <summary>The tenant's content languages: English first, Arabic second.</summary>
        private static readonly LanguageInfo[] ContentLanguages = [new("en", "English", "E", false), new("ar", "العربية", "ع", true)];

        /// <summary>A reader in English, the Gregorian calendar and UTC, reading the first content slot.</summary>
        private static readonly LocalizationContext English = new(NegotiatedCultures.Get("en", "GB"), new GregorianCalendarSystem(), TimeZoneInfo.Utc, 1, ContentLanguages, "en", "en-GB");

        /// <summary>A reader in Arabic, the Umm al-Qura calendar and UTC+3, reading the second content slot.</summary>
        private static readonly LocalizationContext Arabic = new(NegotiatedCultures.Get("ar", "SA"), new UmAlQuraCalendarSystem(), PlusThree, 2, ContentLanguages, "ar", "ar-SA");

        /// <summary>The notification every test delivers.</summary>
        private static readonly NotificationRequest Shipped = new(
            "fixture.shipment.shipped", [1, 2, 3, 4], JsonSerializer.SerializeToElement(new { number = "SO-7" }), "fixture.Shipment", 42, null, null);

        /// <summary>The metrics the localizer factory's meter comes from, and the logs the channel writes.</summary>
        private readonly ServiceProvider _services = new ServiceCollection().AddMetrics().AddLogging().AddFakeLogging().BuildServiceProvider();

        /// <summary>The outbox the channel enqueues through.</summary>
        private readonly RecordingOutbox _outbox = new();

        /// <summary>The renderer the channel renders the text with.</summary>
        private readonly RecordingRenderer _renderer = new();

        /// <summary>The host the delivery job's frame stands for.</summary>
        private readonly JobHost _host = new();

        /// <inheritdoc />
        public void Dispose()
        {
            _services.Dispose();
        }

        [Fact]
        public async Task Each_addressed_recipient_gets_one_internal_email_in_their_order_and_one_without_an_address_is_skipped()
        {
            await Channel().DeliverAsync(
                _host,
                Shipped,
                [
                    Recipient(1, "Ada", "ada@example.com", contactEmail: "ada.contact@example.com", English),
                    Recipient(2, "Ben", email: null, contactEmail: null, English),
                    Recipient(3, " ", "cy@example.com", contactEmail: " ", Arabic),
                ],
                TestContext.Current.CancellationToken);

            // --- One call, with the job's host, of one request per addressed recipient ---
            (IWriteHost host, IReadOnlyList<EmailEnqueueRequest> requests) = Assert.Single(_outbox.Calls);
            Assert.Same(_host, host);
            Assert.Equal([1, 3], requests.Select(static request => request.RecipientUserId));
            Assert.Equal(
                [new EmailAddress("ada.contact@example.com", "Ada"), new EmailAddress("cy@example.com")],
                requests.Select(static request => Assert.Single(request.Message.To)));
            Assert.All(requests, static request =>
            {
                Assert.Equal((EmailAudience.Internal, "fixture.Shipment", 42, EmailEnqueueRequest.NotificationSource), (request.Message.Audience, request.RegardingEntity, request.RegardingId, request.Source));
                Assert.Empty(request.Message.Attachments);
                Assert.Null(request.BlobAttachments);
                Assert.Null(request.SendAfter);
            });
        }

        [Fact]
        public async Task Recipients_sharing_a_localization_share_one_rendering_in_their_language_calendar_and_zone()
        {
            await Channel().DeliverAsync(
                _host,
                Shipped,
                [
                    Recipient(1, "Ada", "ada@example.com", null, English),
                    Recipient(2, "Ben", "ben@example.com", null, Arabic),
                    Recipient(3, "Cy", "cy@example.com", null, English with { ContentLanguages = [.. ContentLanguages] }),
                ],
                TestContext.Current.CancellationToken);

            // --- Two renderings, each given the stored arguments and the target ---
            Assert.Equal(
                [("en-GB", CalendarCodes.Gregorian, "UTC"), ("ar-SA", CalendarCodes.UmAlQura, "Test+03")],
                _renderer.Calls.Select(static call => (call.Localization.CultureTag, call.Localization.Calendar.Code, call.Localization.TimeZone.Id)));
            Assert.All(_renderer.Calls, static call => Assert.Equal(("fixture.shipment.shipped", /*lang=json,strict*/ """{"number":"SO-7"}""", "fixture.Shipment", 42), (call.Type, call.ArgumentsJson, call.TargetResource, call.TargetId)));

            // --- Each email in its reader's language, the tenant named in their content language ---
            IReadOnlyList<EmailEnqueueRequest> requests = Assert.Single(_outbox.Calls).Requests;
            Assert.Equal(
                ["Acme: Shipped (en-GB, gc, UTC)", "أكمي: Shipped (ar-SA, uq, Test+03)", "Acme: Shipped (en-GB, gc, UTC)"],
                requests.Select(static request => request.Message.Subject));
            Assert.StartsWith("Shipped (en-GB, gc, UTC)\nSO-7\n\nOpen: https://app.test/7/fixture.Shipment/42\n\n", requests[0].Message.TextBody, StringComparison.Ordinal);
            Assert.Contains("فتح: https://app.test/7/fixture.Shipment/42", requests[1].Message.TextBody, StringComparison.Ordinal);
            Assert.Contains("أكمي", requests[1].Message.TextBody, StringComparison.Ordinal);
            Assert.Equal(requests[0].Message.HtmlBody, requests[2].Message.HtmlBody);
        }

        [Fact]
        public async Task The_html_body_encodes_the_text_and_keeps_its_lines_as_line_breaks()
        {
            _renderer.Text = "Tom & <Jerry>\nShipped";

            await Channel().DeliverAsync(_host, Shipped, [Recipient(1, "Ada", "ada@example.com", null, English)], TestContext.Current.CancellationToken);

            EmailMessage message = Assert.Single(Assert.Single(_outbox.Calls).Requests).Message;
            Assert.Equal("Acme: Tom & <Jerry>", message.Subject);
            Assert.Contains("<p>Tom &amp; &lt;Jerry&gt;<br>Shipped</p>", message.HtmlBody, StringComparison.Ordinal);
            Assert.Contains("href=\"https://app.test/7/fixture.Shipment/42\"", message.HtmlBody, StringComparison.Ordinal);
            Assert.StartsWith("Tom & <Jerry>\nShipped\n\n", message.TextBody, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Nothing_is_enqueued_when_no_recipient_has_an_address()
        {
            await Channel().DeliverAsync(_host, Shipped, [Recipient(2, "Ben", null, null, English)], TestContext.Current.CancellationToken);

            Assert.Empty(_outbox.Calls);
            Assert.Empty(_renderer.Calls);
        }

        [Fact]
        public async Task A_recipient_whose_address_the_outbox_refuses_is_skipped_with_a_warning_and_the_others_are_enqueued()
        {
            await Channel().DeliverAsync(
                _host,
                Shipped,
                [
                    Recipient(1, "Ada", "ada@example.com", contactEmail: "Ada <ada@acme.example>", English),
                    Recipient(2, "Ben\tJones\r\nBcc: x@example.net", "ben@example.com", contactEmail: null, English),
                    Recipient(3, "Cy", " cy@example.com ", contactEmail: null, Arabic),
                ],
                TestContext.Current.CancellationToken);

            // --- Ben, his name on one line, and Cy, trimmed, in one call the outbox accepts ---
            IReadOnlyList<EmailEnqueueRequest> requests = Assert.Single(_outbox.Calls).Requests;
            Assert.Equal([2, 3], requests.Select(static request => request.RecipientUserId));
            Assert.Equal(
                [new EmailAddress("ben@example.com", "Ben Jones  Bcc: x@example.net"), new EmailAddress("cy@example.com", "Cy")],
                requests.Select(static request => Assert.Single(request.Message.To)));
            Assert.All(requests, static request => Assert.True(EmailOutbox.IsWellFormed(request)));

            // --- One warning, naming Ada by id and never by address ---
            FakeLogRecord skipped = Assert.Single(Logged());
            Assert.Equal(LogLevel.Warning, skipped.Level);
            Assert.Contains(new KeyValuePair<string, string?>("RecipientUserId", "1"), skipped.StructuredState!);
            Assert.DoesNotContain("acme.example", skipped.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Nothing_is_enqueued_when_every_address_is_one_the_outbox_refuses()
        {
            await Channel().DeliverAsync(
                _host,
                Shipped,
                [Recipient(1, "Ada", "ada@example.com", contactEmail: "Ada <ada@acme.example>", English), Recipient(2, "Ben", "ben @example.com", null, English)],
                TestContext.Current.CancellationToken);

            Assert.Empty(_outbox.Calls);
            Assert.Equal(2, Logged().Count);
        }

        /// <summary>The channel's skipped-recipient warnings, in order.</summary>
        /// <returns>The records.</returns>
        private List<FakeLogRecord> Logged()
        {
            return [.. _services.GetFakeLogCollector().GetSnapshot().Where(static record => record.Id.Name == "EmailNotificationRecipientSkipped")];
        }

        /// <summary>A recipient.</summary>
        /// <param name="userId">The user.</param>
        /// <param name="name">Their name.</param>
        /// <param name="email">Their sign-in address.</param>
        /// <param name="contactEmail">Their contact address.</param>
        /// <param name="localization">Their localization.</param>
        /// <returns>The recipient.</returns>
        private static NotificationRecipient Recipient(int userId, string name, string? email, string? contactEmail, LocalizationContext localization)
        {
            return new NotificationRecipient(userId, name, email, contactEmail, null, localization);
        }

        /// <summary>The channel over the fakes, in a scope of tenant 7 whose settings name it in English and Arabic.</summary>
        /// <returns>The channel.</returns>
        private EmailNotificationChannel Channel()
        {
            DateTimeOffset now = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
            RequestContext context = RequestContext.Tenantless(new GregorianCalendarSystem(), now) with
            {
                Tenant = new TenantDescriptor(TenantId, "Acme Catalog", null, null, ["en", "ar"], TenantCategory.Live, TenantState.Active, null),
                Kind = PrincipalKind.System,
                Subject = "system",
                TenantSettings = new TenantSettings
                {
                    TenantId = TenantId,
                    Tag = new VersionTag(Guid.Empty),
                    Names = ["Acme", "أكمي", null],
                    Languages = ContentLanguages,
                    Shape = MultilingualShape.Primary,
                    Calendars = [],
                    TimeZone = TimeZoneInfo.Utc,
                    SqlServerTimeZoneName = "UTC",
                    ModifiedAt = now,
                    Entries = new Dictionary<string, JsonElement>(),
                },
            };
            IcuStringLocalizerFactory localizers = new(
                new LanguageCatalog(new LanguageDeclaration(["en-GB", "ar-SA"], [])), NullLoggerFactory.Instance, _services.GetRequiredService<IMeterFactory>());
            return new EmailNotificationChannel(
                _outbox, _renderer, localizers, new Links(), new FixedContextAccessor(context), new NoSettingsCache(), _services.GetRequiredService<ILoggerFactory>());
        }

        /// <summary>An outbox that records each call.</summary>
        private sealed class RecordingOutbox : IEmailOutbox
        {
            /// <summary>The calls, in order.</summary>
            public List<(IWriteHost Host, IReadOnlyList<EmailEnqueueRequest> Requests)> Calls { get; } = [];

            /// <inheritdoc />
            public Task<EnlistedSave<OutgoingEmail>> EnqueueAsync(IWriteHost host, IReadOnlyList<EmailEnqueueRequest> requests, CancellationToken cancellationToken = default)
            {
                Calls.Add((host, requests));
                return Task.FromResult<EnlistedSave<OutgoingEmail>>(null!);
            }
        }

        /// <summary>A renderer that records each call and names the localization it rendered in.</summary>
        private sealed class RecordingRenderer : INotificationRenderer
        {
            /// <summary>The calls, in order.</summary>
            public List<(string Type, string? ArgumentsJson, string? TargetResource, long? TargetId, LocalizationContext Localization)> Calls { get; } = [];

            /// <summary>The text to render; null for the title naming the localization, then the number.</summary>
            public string? Text { get; set; }

            /// <inheritdoc />
            public string Render(string type, string? argumentsJson, string? targetResource, long? targetId, LocalizationContext localization)
            {
                Calls.Add((type, argumentsJson, targetResource, targetId, localization));
                return Text ?? string.Create(
                    CultureInfo.InvariantCulture,
                    $"Shipped ({localization.CultureTag}, {localization.Calendar.Code}, {localization.TimeZone.Id})\nSO-7");
            }
        }

        /// <summary>Links of the form <c>https://app.test/{tenant}/{resource}/{id}</c>.</summary>
        private sealed class Links : ITellmaLinks
        {
            /// <inheritdoc />
            public string Tenant(int tenantId)
            {
                return string.Create(CultureInfo.InvariantCulture, $"https://app.test/{tenantId}");
            }

            /// <inheritdoc />
            public string Target(int tenantId, string? resource, long? id)
            {
                return string.Create(CultureInfo.InvariantCulture, $"https://app.test/{tenantId}/{resource}/{id}");
            }
        }

        /// <summary>A settings cache the channel must not need: the scope's context holds the settings.</summary>
        private sealed class NoSettingsCache : ITenantSettingsCache
        {
            /// <inheritdoc />
            public Task<CacheResult<TenantSettings>> GetAsync()
            {
                throw new InvalidOperationException("The context holds the settings.");
            }

            /// <inheritdoc />
            public Task<(byte[] Json, string WireTag)> GetForClientAsync()
            {
                throw new InvalidOperationException("The context holds the settings.");
            }

            /// <inheritdoc />
            public TenantSettings? Peek()
            {
                throw new InvalidOperationException("The context holds the settings.");
            }
        }

        /// <summary>The delivery job's frame, which the fake outbox only passes along.</summary>
        private sealed class JobHost : IOpenWriteHost
        {
            /// <inheritdoc />
            public WriteHostKind Kind => WriteHostKind.Job;

            /// <inheritdoc />
            public string Operation => "job:core.notification-delivery";

            /// <inheritdoc />
            public IConfirmations Confirmations => throw new NotSupportedException();

            /// <inheritdoc />
            public Task<EnlistmentOutcome> PersistAsync(CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }
        }
    }
}
