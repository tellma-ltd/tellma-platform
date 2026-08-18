// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Tellma.Identity.Data;
using Tellma.Identity.Data.Entities;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.Invitations;
using Tellma.Identity.Services.Provisioning;
using Tellma.Identity.TestSupport;

namespace Tellma.Identity.IntegrationTests.Api
{
    /// <summary>
    ///     The recovery sweep that finishes invitations whose mail never reached a transport.
    ///     <para>
    ///         This is the only path by which a lost invitation is ever sent. Every other identity
    ///         email is recovered by the person waiting for it asking again; an invitation's
    ///         recipient does not know one exists, so if the sweep does not deliver it, nothing
    ///         does and nobody finds out.
    ///     </para>
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class InvitationDispatchSweepTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task An_invitation_whose_mail_was_lost_is_delivered_by_the_sweep()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idsweep");
            string email = "swept@example.com";
            await InviteAsync(factory, email);

            string original = await factory.Emails.WaitForLinkAsync(email);
            await LoseTheMailAsync(factory, email);
            factory.Emails.Clear();

            int sent = await SweepAsync(factory);

            Assert.Equal(1, sent);
            string resent = await factory.Emails.WaitForLinkAsync(email);

            // A fresh secret, because the original was never stored and so cannot be reproduced.
            // The recipient gets a working link; the one that was never delivered is now dead.
            Assert.NotEqual(original, resent);

            SingleUseCode row = await SingleRowAsync(factory, email);
            Assert.Equal(EmailDispatchState.Sent, row.DispatchState);
            Assert.NotNull(row.SentUtc);
        }

        [Fact]
        public async Task The_sweep_leaves_alone_what_it_must_not_resend()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idsweepskip");

            // Already sent by the normal path, and still inside the grace window besides.
            await InviteAsync(factory, "sent@example.com");

            // Lost, but consumed since — the recipient has the invitation after all.
            await InviteAsync(factory, "consumed@example.com");
            await LoseTheMailAsync(factory, "consumed@example.com");
            await ConsumeAsync(factory, "consumed@example.com");

            // Lost, but only moments ago: inside the grace window, so the normal path may still
            // be mid-send and the sweep must not race it.
            await InviteAsync(factory, "fresh@example.com");
            await LoseTheMailAsync(factory, "fresh@example.com", age: TimeSpan.Zero);

            factory.Emails.Clear();

            Assert.Equal(0, await SweepAsync(factory));
            Assert.Empty(factory.Emails.Captured);
        }

        [Fact]
        public async Task Two_instances_sweeping_at_once_send_each_invitation_exactly_once()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idsweeprace");

            string[] addresses = [.. Enumerable.Range(0, 12).Select(static i => $"racer{i}@example.com")];
            foreach (string address in addresses)
            {
                await InviteAsync(factory, address);
                await LoseTheMailAsync(factory, address);
            }

            factory.Emails.Clear();

            // Widen the window each instance spends holding a claim, so the two sweeps genuinely
            // overlap rather than finishing one after the other by accident.
            factory.Emails.OnSending((_, _) =>
            {
                Thread.Sleep(20);
                return new Core.Abstractions.Email.EmailSendResult(Core.Abstractions.Email.EmailSendOutcome.Sent);
            });

            // Quartz runs on its default in-memory store with no clustering here, so in a
            // multi-instance deployment every instance fires this trigger on its own schedule.
            // Nothing but the database-level claim stops them mailing the same invitation twice.
            int[] sent = await Task.WhenAll(SweepAsync(factory), SweepAsync(factory));

            Assert.Equal(addresses.Length, sent.Sum());
            foreach (string address in addresses)
            {
                Assert.Single(
                    factory.Emails.Captured,
                    captured => captured.Message.To.Any(
                        to => string.Equals(to.Address, address, StringComparison.OrdinalIgnoreCase)));
            }
        }

        [Fact]
        public async Task A_claim_left_behind_by_a_crashed_instance_is_reclaimed_when_it_expires()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idsweeplease");
            string email = "stranded@example.com";
            await InviteAsync(factory, email);
            await LoseTheMailAsync(factory, email);
            factory.Emails.Clear();

            // An instance claimed this row and died before sending. A flag would strand it here
            // forever; a lease lets it come back on its own.
            await MutateRowAsync(
                factory, email, static row => row.DispatchClaimedUntil = DateTimeOffset.UtcNow.AddMinutes(10));

            Assert.Equal(0, await SweepAsync(factory));

            await MutateRowAsync(
                factory, email, static row => row.DispatchClaimedUntil = DateTimeOffset.UtcNow.AddMinutes(-1));

            Assert.Equal(1, await SweepAsync(factory));
            await factory.Emails.WaitForLinkAsync(email);
        }

        /// <summary>Runs one sweep in its own scope, the way a scheduled execution would.</summary>
        private static async Task<int> SweepAsync(StandaloneFactory factory)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            return await scope.ServiceProvider
                .GetRequiredService<InvitationDispatchJob>()
                .SweepAsync(TestContext.Current.CancellationToken);
        }

        /// <summary>
        ///     Puts a row back the way a crash would leave it: the user and the token committed,
        ///     the mail never sent. Backdated past the grace window, because the sweep deliberately
        ///     ignores anything recent enough that the normal path might still be working on it.
        /// </summary>
        private static async Task LoseTheMailAsync(
            StandaloneFactory factory, string email, TimeSpan? age = null)
        {
            DateTimeOffset created = DateTimeOffset.UtcNow - (age ?? TimeSpan.FromMinutes(30));
            await MutateRowAsync(factory, email, row =>
            {
                row.DispatchState = EmailDispatchState.Pending;
                row.SentUtc = null;
                row.DispatchClaimedUntil = null;
                row.DispatchAttempts = 0;
                row.CreatedUtc = created;
            });
        }

        /// <summary>Marks the invitation as redeemed.</summary>
        private static Task ConsumeAsync(StandaloneFactory factory, string email)
        {
            return MutateRowAsync(factory, email, static row => row.ConsumedUtc = DateTimeOffset.UtcNow);
        }

        /// <summary>Applies a change to the invitation row belonging to one address.</summary>
        private static async Task MutateRowAsync(
            StandaloneFactory factory, string email, Action<SingleUseCode> mutate)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            TellmaIdentityDbContext store = scope.ServiceProvider.GetRequiredService<TellmaIdentityDbContext>();

            string userId = await store.Set<TellmaIdentityUser>()
                .Where(u => u.Email == email)
                .Select(static u => u.Id)
                .SingleAsync(TestContext.Current.CancellationToken);

            SingleUseCode row = await store.Set<SingleUseCode>()
                .SingleAsync(
                    c => c.UserId == userId && c.Purpose == SingleUseCodePurpose.Invitation,
                    TestContext.Current.CancellationToken);

            mutate(row);
            await store.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        /// <summary>The single invitation row belonging to one address.</summary>
        private static async Task<SingleUseCode> SingleRowAsync(StandaloneFactory factory, string email)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            TellmaIdentityDbContext store = scope.ServiceProvider.GetRequiredService<TellmaIdentityDbContext>();

            return await store.Set<SingleUseCode>()
                .Where(c => c.Purpose == SingleUseCodePurpose.Invitation)
                .Join(
                    store.Set<TellmaIdentityUser>().Where(u => u.Email == email),
                    static code => code.UserId,
                    static user => user.Id,
                    static (code, _) => code)
                .SingleAsync(TestContext.Current.CancellationToken);
        }

        /// <summary>Invites one address through the real API.</summary>
        private static async Task InviteAsync(StandaloneFactory factory, string email)
        {
            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", await TokenAsync(factory));

            using HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri("/api/identity/invitations", UriKind.Relative),
                new { users = new object[] { new { email, displayName = email, locale = "en" } } },
                TestContext.Current.CancellationToken);

            Assert.True(
                response.IsSuccessStatusCode,
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

            await factory.Emails.WaitForBodyAsync(email);

            // The captured message says the send happened; it does not say the outcome has been
            // written back yet. Waiting for the row settles that, so a test that then rewinds the
            // row cannot have its rewind overwritten by the worker finishing behind it.
            await WaitForDispatchRecordedAsync(factory, email);
        }

        /// <summary>Waits until the send outcome has reached the row.</summary>
        private static async Task WaitForDispatchRecordedAsync(StandaloneFactory factory, string email)
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                if ((await SingleRowAsync(factory, email)).DispatchState != EmailDispatchState.Pending)
                {
                    return;
                }

                await Task.Delay(50, TestContext.Current.CancellationToken);
            }

            Assert.Fail($"The dispatch outcome for {email} was never recorded.");
        }

        /// <summary>Obtains a management-scope token for the invitation API.</summary>
        private static async Task<string> TokenAsync(StandaloneFactory factory)
        {
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = distribution.ServiceClientId,
                    ["client_secret"] = distribution.ServiceClientSecret,
                    ["scope"] = "tellma_identity",
                }),
                TestContext.Current.CancellationToken);

            using var token = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            return token.RootElement.GetProperty("access_token").GetString()!;
        }
    }
}
