// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Email;
using Tellma.Identity.Data;
using Tellma.Identity.Data.Entities;
using Tellma.Identity.IntegrationTests.Infrastructure;

namespace Tellma.Identity.IntegrationTests.Api
{
    /// <summary>
    ///     What the engine records when a provider reports on its mail.
    ///     <para>
    ///         Providers deliver events at least once and out of order, so the two rules that
    ///         matter are invisible in the happy path: a redelivered event must change nothing, and
    ///         a late "delayed" must not overwrite the bounce that already arrived. Both would fail
    ///         silently in production — the row would simply read wrong.
    ///     </para>
    ///     <para>
    ///         Against a real database rather than a fake one, because the rules live in a single
    ///         conditional update whose whole point is that it cannot interleave — including a
    ///         comparison between enum-valued columns that has to survive translation to SQL.
    ///     </para>
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class DeliveryEventTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task A_delivery_report_is_recorded_against_the_row_it_names()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "iddeliver");
            string codeId = await SeedAsync(factory);

            await HandleAsync(factory, Event(codeId, EmailDeliveryEventType.Delivered, "evt-1"));

            SingleUseCode row = await ReadAsync(factory, codeId);
            Assert.Equal(EmailDeliveryStatus.Delivered, row.DeliveryStatus);
            Assert.NotNull(row.DeliveryUpdatedUtc);
            Assert.Equal("evt-1", row.LastProviderEventId);
        }

        [Fact]
        public async Task A_redelivered_event_changes_nothing()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "iddedupe");
            string codeId = await SeedAsync(factory);

            await HandleAsync(factory, Event(codeId, EmailDeliveryEventType.Bounced, "evt-1", reason: "first"));
            DateTimeOffset? first = (await ReadAsync(factory, codeId)).DeliveryUpdatedUtc;

            // The same event id again, which is exactly what "at least once" delivery produces.
            await HandleAsync(factory, Event(codeId, EmailDeliveryEventType.Bounced, "evt-1", reason: "second"));

            SingleUseCode row = await ReadAsync(factory, codeId);
            Assert.Equal(first, row.DeliveryUpdatedUtc);
            Assert.Equal("first", row.DeliveryReason);
        }

        [Fact]
        public async Task A_late_transient_report_does_not_overwrite_a_terminal_one()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idstale");
            string codeId = await SeedAsync(factory);

            await HandleAsync(factory, Event(codeId, EmailDeliveryEventType.Bounced, "evt-1"));

            // Out of order: the provider deferred the message before it bounced, and the two
            // reports reach us the wrong way round. The bounce is the truth.
            await HandleAsync(factory, Event(codeId, EmailDeliveryEventType.Deferred, "evt-2"));

            Assert.Equal(EmailDeliveryStatus.Bounced, (await ReadAsync(factory, codeId)).DeliveryStatus);
        }

        [Fact]
        public async Task A_spam_report_supersedes_a_delivery()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idspam");
            string codeId = await SeedAsync(factory);

            await HandleAsync(factory, Event(codeId, EmailDeliveryEventType.Delivered, "evt-1"));

            // Delivered and then reported as spam is a real progression, and the complaint is the
            // more actionable of the two — it must win.
            await HandleAsync(factory, Event(codeId, EmailDeliveryEventType.SpamReported, "evt-2"));

            Assert.Equal(EmailDeliveryStatus.SpamReported, (await ReadAsync(factory, codeId)).DeliveryStatus);
        }

        [Theory]
        [InlineData(EmailDeliveryEventType.Opened)]
        [InlineData(EmailDeliveryEventType.Clicked)]
        public async Task An_engagement_report_is_discarded(EmailDeliveryEventType type)
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(
                fixture, "idengage" + type.ToString().ToLowerInvariant());
            string codeId = await SeedAsync(factory);

            // Both connectors can translate these, so enabling tracking at a provider would start
            // delivering them here. Identity keeps no engagement data: nothing on the row moves.
            await HandleAsync(factory, Event(codeId, type, "evt-1"));

            SingleUseCode row = await ReadAsync(factory, codeId);
            Assert.Null(row.DeliveryStatus);
            Assert.Null(row.DeliveryUpdatedUtc);
            Assert.Null(row.LastProviderEventId);
        }

        /// <summary>Runs the engine's handler over one event, as the webhook path would.</summary>
        private static async Task HandleAsync(StandaloneFactory factory, EmailDeliveryEvent @event)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            IEmailDeliveryEventHandler handler =
                scope.ServiceProvider.GetRequiredService<IEmailDeliveryEventHandler>();

            Assert.Equal("identity", handler.OwnerKey);
            await handler.HandleAsync([@event], TestContext.Current.CancellationToken);
        }

        /// <summary>Builds an event correlated to a row.</summary>
        private static EmailDeliveryEvent Event(
            string codeId, EmailDeliveryEventType type, string providerEventId, string? reason = null)
        {
            return new EmailDeliveryEvent(
                new EmailCorrelation("identity", codeId),
                Recipient: "recipient@example.com",
                type,
                RawType: type.ToString().ToLowerInvariant(),
                reason,
                Timestamp: DateTimeOffset.UtcNow,
                providerEventId);
        }

        /// <summary>Stores one sent invitation awaiting delivery events, and returns its row id.</summary>
        private static async Task<string> SeedAsync(StandaloneFactory factory)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            TellmaIdentityDbContext store = scope.ServiceProvider.GetRequiredService<TellmaIdentityDbContext>();

            TellmaIdentityUser user = new()
            {
                Id = Guid.NewGuid().ToString("N"),
                UserName = "recipient@example.com",
                Email = "recipient@example.com",
                LifecycleState = UserLifecycleState.Active,
                CreatedUtc = DateTimeOffset.UtcNow,
            };
            store.Set<TellmaIdentityUser>().Add(user);

            string codeId = Guid.NewGuid().ToString("N");
            store.Set<SingleUseCode>().Add(new SingleUseCode
            {
                Id = codeId,
                UserId = user.Id,
                Purpose = SingleUseCodePurpose.Invitation,
                SecretHash = "hash",
                CreatedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7),
                DispatchState = EmailDispatchState.Sent,
                SentUtc = DateTimeOffset.UtcNow,
                ExpectsDeliveryEvents = true,
            });

            await store.SaveChangesAsync(TestContext.Current.CancellationToken);
            return codeId;
        }

        /// <summary>Reads a row back, untracked so each assertion sees the stored values.</summary>
        private static async Task<SingleUseCode> ReadAsync(StandaloneFactory factory, string codeId)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            TellmaIdentityDbContext store = scope.ServiceProvider.GetRequiredService<TellmaIdentityDbContext>();

            return await store.Set<SingleUseCode>()
                .AsNoTracking()
                .SingleAsync(c => c.Id == codeId, TestContext.Current.CancellationToken);
        }
    }
}
