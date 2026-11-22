// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Calendars;
using Tellma.Core.Localization;
using Tellma.Core.Notifications;
using Tellma.Core.Testing.Email;
using Tellma.Core.Tests.Access;

namespace Tellma.Core.Tests.Outbox
{
    /// <summary>
    ///     The email channel's test send with no database, over a capturing sender: one internal
    ///     message to the connected user's contact address, else their sign-in address, in their
    ///     language and calendar and at the tenant's current time; no contact address and no email
    ///     sender each answered without a send; and a transport that fails, refuses or throws reported
    ///     as an unavailable email dependency, its warning never naming the address.
    /// </summary>
    public sealed class EmailTestNotificationTests : IDisposable
    {
        /// <summary>The tenant every scope here belongs to.</summary>
        private const int TenantId = 7;

        /// <summary>The instant every test sends at: 09:00 UTC, 12:00 on the tenant's clock.</summary>
        private static readonly DateTimeOffset Now = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

        /// <summary>The tenant's zone, three hours ahead of UTC, the same on every host.</summary>
        private static readonly TimeZoneInfo PlusThree = TimeZoneInfo.CreateCustomTimeZone("Test+03", TimeSpan.FromHours(3), "Test+03", "Test+03");

        /// <summary>The metrics and the logs, and the sender a test registers.</summary>
        private readonly ServiceCollection _registrations = new();

        /// <summary>The provider over <see cref="_registrations" />, built by the first channel.</summary>
        private ServiceProvider? _services;

        /// <inheritdoc />
        public void Dispose()
        {
            _services?.Dispose();
        }

        [Fact]
        public async Task The_message_goes_to_the_contact_address_once_internal_with_the_tenant_name_and_its_current_time()
        {
            CapturingEmailSender sender = _registrations.AddCapturingEmail();

            TestNotificationResult result = await Channel(Profile("ada@example.com", "ada.contact@example.com")).SendTestAsync(TestContext.Current.CancellationToken);

            Assert.Equal(TestNotificationResult.Sent, result);
            EmailMessage message = Assert.Single(sender.Captured).Message;
            Assert.Equal(new EmailAddress("ada.contact@example.com", "Ada"), Assert.Single(message.To));
            Assert.Equal(EmailAudience.Internal, message.Audience);
            Assert.Equal("Test message from Acme", message.Subject);
            Assert.StartsWith("This is a test message from Acme, sent on 20 November 2026 at 12:00.", message.TextBody, StringComparison.Ordinal);
            Assert.Null(message.HtmlBody);
            Assert.Empty(message.Attachments);
        }

        [Fact]
        public async Task The_sign_in_address_serves_when_there_is_no_contact_address()
        {
            CapturingEmailSender sender = _registrations.AddCapturingEmail();

            _ = await Channel(Profile("ada@example.com", " ")).SendTestAsync(TestContext.Current.CancellationToken);

            Assert.Equal("ada@example.com", Assert.Single(Assert.Single(sender.Captured).Message.To).Address);
        }

        [Fact]
        public async Task The_message_renders_in_the_user_language_calendar_and_name_slot_at_the_tenant_time()
        {
            CapturingEmailSender sender = _registrations.AddCapturingEmail();
            UserProfile profile = Profile("ada@example.com", null) with { Name2 = "آدا", PreferredLanguage = "ar", PreferredCalendar = CalendarCodes.UmAlQura, PreferredTimeZone = "Asia/Tokyo" };

            _ = await Channel(profile).SendTestAsync(TestContext.Current.CancellationToken);

            // Arabic text, the tenant's and the user's second names, a Hijri date, and the time on
            // the tenant's clock rather than the user's own zone.
            EmailMessage message = Assert.Single(sender.Captured).Message;
            Assert.Equal("رسالة تجريبية من أكمي", message.Subject);
            Assert.Equal("آدا", Assert.Single(message.To).DisplayName);
            Assert.Contains("1448", message.TextBody, StringComparison.Ordinal);
            Assert.DoesNotContain("2026", message.TextBody, StringComparison.Ordinal);
            Assert.Contains("12:00", message.TextBody, StringComparison.Ordinal);
            Assert.DoesNotContain("{", message.TextBody, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_sandboxed_message_counts_as_sent()
        {
            CapturingEmailSender sender = _registrations.AddCapturingEmail();
            sender.OnSending(static (_, _) => new EmailSendResult(EmailSendOutcome.Sandboxed));

            Assert.Equal(TestNotificationResult.Sent, await Channel(Profile("ada@example.com", null)).SendTestAsync(TestContext.Current.CancellationToken));
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData(" ", "")]
        [InlineData(null, "ada @example.com")]
        public async Task A_user_with_no_usable_address_is_answered_without_a_send(string? email, string? contactEmail)
        {
            CapturingEmailSender sender = _registrations.AddCapturingEmail();

            Assert.Equal(TestNotificationResult.NoContactAddress, await Channel(Profile(email, contactEmail)).SendTestAsync(TestContext.Current.CancellationToken));
            Assert.Empty(sender.Captured);
        }

        [Fact]
        public async Task A_host_with_no_email_sender_is_not_configured()
        {
            Assert.Equal(TestNotificationResult.NotConfigured, await Channel(Profile("ada@example.com", null)).SendTestAsync(TestContext.Current.CancellationToken));
        }

        [Theory]
        [InlineData(EmailSendOutcome.TransientFailure)]
        [InlineData(EmailSendOutcome.Rejected)]
        public async Task A_transport_that_fails_or_refuses_is_an_unavailable_email_dependency_a_refusal_marked_and_logged_with_no_address_of_its_own(EmailSendOutcome outcome)
        {
            CapturingEmailSender sender = _registrations.AddCapturingEmail();
            sender.OnSending((_, _) => new EmailSendResult(outcome, Error: "550 mailbox unavailable"));

            DependencyUnavailableException refused = await Assert.ThrowsAsync<DependencyUnavailableException>(
                () => Channel(Profile("ada@example.com", null)).SendTestAsync(TestContext.Current.CancellationToken));

            Assert.Equal("email", refused.Dependency);
            if (outcome == EmailSendOutcome.Rejected)
            {
                _ = Assert.IsType<TestMessageRejectedException>(refused.InnerException);
            }
            else
            {
                Assert.Null(refused.InnerException);
            }

            // The channel adds no address of its own to the warning; the transport's detail is there.
            FakeLogRecord warning = Assert.Single(Logged("TestEmailNotSent"));
            Assert.Equal(LogLevel.Warning, warning.Level);
            Assert.Contains("550 mailbox unavailable", warning.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("ada@example.com", warning.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_sender_that_throws_is_an_unavailable_email_dependency_carrying_the_failure()
        {
            InvalidOperationException failure = new("The connection was refused.");
            _ = _registrations.AddSingleton<IEmailSender>(new ThrowingSender(failure));

            DependencyUnavailableException refused = await Assert.ThrowsAsync<DependencyUnavailableException>(
                () => Channel(Profile("ada@example.com", null)).SendTestAsync(TestContext.Current.CancellationToken));

            Assert.Equal("email", refused.Dependency);
            Assert.Same(failure, refused.InnerException);
            _ = Assert.Single(Logged("TestEmailFailed"));
        }

        [Fact]
        public async Task A_cancelled_send_propagates_as_cancellation()
        {
            _ = _registrations.AddSingleton<IEmailSender>(new ThrowingSender(new OperationCanceledException()));

            _ = await Assert.ThrowsAsync<OperationCanceledException>(
                () => Channel(Profile("ada@example.com", null)).SendTestAsync(TestContext.Current.CancellationToken));
        }

        /// <summary>A connected user's profile named Ada, with the addresses given.</summary>
        /// <param name="email">The sign-in address.</param>
        /// <param name="contactEmail">The contact address.</param>
        /// <returns>The profile.</returns>
        private static UserProfile Profile(string? email, string? contactEmail)
        {
            return new UserProfile("Ada", null, null, email, contactEmail, null, null, null, null, null, null, null, null, null, Now);
        }

        /// <summary>The channel, in a scope of tenant 7 connected as a user of the profile, over the registered sender.</summary>
        /// <param name="profile">The connected user's profile.</param>
        /// <returns>The channel.</returns>
        private EmailNotificationChannel Channel(UserProfile profile)
        {
            _ = _registrations.AddMetrics().AddLogging().AddFakeLogging();
            _services = _registrations.BuildServiceProvider();
            IMeterFactory meters = _services.GetRequiredService<IMeterFactory>();

            // --- The tenant: English and Arabic, Gregorian and Umm al-Qura, three hours ahead of UTC ---
            LanguageInfo[] languages = [new("en", "English", "E", false), new("ar", "العربية", "ع", true)];
            RequestContext context = RequestContext.Tenantless(new GregorianCalendarSystem(), Now) with
            {
                Tenant = new TenantDescriptor(TenantId, "Acme Catalog", null, null, ["en", "ar"], TenantCategory.Live, TenantState.Active, null),
                Kind = PrincipalKind.User,
                Subject = "sub-ada",
                UserId = 3,
                TenantSettings = new TenantSettings
                {
                    TenantId = TenantId,
                    Tag = new VersionTag(Guid.Empty),
                    Names = ["Acme", "أكمي", null],
                    Languages = languages,
                    Shape = MultilingualShape.Primary,
                    Calendars = [new GregorianCalendarSystem(), new UmAlQuraCalendarSystem()],
                    TimeZone = PlusThree,
                    SqlServerTimeZoneName = "Arab Standard Time",
                    ModifiedAt = Now,
                    Entries = new Dictionary<string, JsonElement>(),
                },
            };

            // --- The localization the user's preferences negotiate, over the deployment's offer ---
            LanguageCatalog catalog = new(new LanguageDeclaration(["en-GB", "ar-SA"], []));
            CalendarRegistry calendars = new([new GregorianCalendarSystem(), new UmAlQuraCalendarSystem()]);
            ValueSetRegistry valueSets = new(catalog, calendars, []);
            IcuStringLocalizerFactory localizers = new(catalog, _services.GetRequiredService<ILoggerFactory>(), meters);
            return new EmailNotificationChannel(
                Refusing.Of<IEmailOutbox>(),
                Refusing.Of<INotificationRenderer>(),
                localizers,
                Refusing.Of<ITellmaLinks>(),
                new FixedContextAccessor(context),
                Refusing.Of<ITenantSettingsCache>(),
                ConnectedAs.Of(new ConnectedUser(3, UserKind.Individual, UserState.Joined, profile, Guid.NewGuid(), UserAccess.System, new Dictionary<string, Guid>(StringComparer.Ordinal), null)),
                new LocalizationNegotiator(catalog, calendars, valueSets, meters),
                _services,
                _services.GetRequiredService<ILoggerFactory>());
        }

        /// <summary>The channel's records of one event, in order.</summary>
        /// <param name="eventName">The event's name.</param>
        /// <returns>The records.</returns>
        private List<FakeLogRecord> Logged(string eventName)
        {
            return [.. _services!.GetFakeLogCollector().GetSnapshot().Where(record => record.Id.Name == eventName)];
        }

        /// <summary>A sender that throws before any message goes out.</summary>
        /// <param name="failure">What it throws.</param>
        private sealed class ThrowingSender(Exception failure) : IEmailSender
        {
            /// <inheritdoc />
            public Task<IReadOnlyList<EmailSendResult>> SendAsync(IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken)
            {
                return Task.FromException<IReadOnlyList<EmailSendResult>>(failure);
            }
        }

        /// <summary>A scope connection already connected as one user; nothing else of a connection is reached.</summary>
        public class ConnectedAs : DispatchProxy
        {
            /// <summary>The connected user.</summary>
            private ConnectedUser? _user;

            /// <summary>A connection connected as a user.</summary>
            /// <param name="user">The user.</param>
            /// <returns>The connection.</returns>
            internal static IScopeConnection Of(ConnectedUser user)
            {
                IScopeConnection connection = Create<IScopeConnection, ConnectedAs>();
                ((ConnectedAs)(object)connection)._user = user;
                return connection;
            }

            /// <inheritdoc />
            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                return targetMethod?.Name == nameof(IScopeConnection.CurrentAsync)
                    ? Task.FromResult(_user)
                    : throw new NotSupportedException($"The test reaches {targetMethod?.Name}, which the connection does not answer.");
            }
        }
    }
}
