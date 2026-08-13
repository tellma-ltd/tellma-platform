// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Tellma.Connector.AcsEmail.Adapter;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Hosting;

namespace Tellma.Connector.AcsEmail.IntegrationTests
{
    /// <summary>
    ///     Real sends against a dedicated ACS test resource, because ACS has no validate-only mode.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Two messages to one Tellma-owned mailbox — volume that sits comfortably inside even a
    ///         fresh subscription's default quota. Event Grid delivery is deliberately not asserted:
    ///         that is synthetic monitoring, and the receiver's correctness rests on the
    ///         recorded-payload suite instead.
    ///     </para>
    ///     <para>
    ///         Authentication is a <c>TokenCredential</c> — a developer's own Azure sign-in locally,
    ///         a federated CI identity in the nightly run — so no mail secret exists to leak.
    ///     </para>
    /// </remarks>
    [Trait("Category", "Integration")]
    [Trait("Live", "true")]
    public class AcsEmailLiveSendTests
    {
        private const string EndpointVariable = "TELLMA_ACS_TEST_ENDPOINT";
        private const string SenderVariable = "TELLMA_ACS_TEST_SENDER";
        private const string RecipientVariable = "TELLMA_ACS_TEST_RECIPIENT";

        private const string SkipReason =
            "Set TELLMA_ACS_TEST_ENDPOINT, TELLMA_ACS_TEST_SENDER, and TELLMA_ACS_TEST_RECIPIENT, and sign in to Azure, to run the live ACS suite.";

        /// <summary>Whether the environment carries everything this suite needs.</summary>
        public static bool HasCredentials =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EndpointVariable))
            && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SenderVariable))
            && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RecipientVariable));

        [Fact(Skip = SkipReason, SkipUnless = nameof(HasCredentials), SkipType = typeof(AcsEmailLiveSendTests))]
        public async Task Accepts_a_minimal_message()
        {
            await using ServiceProvider provider = Compose();
            IEmailSender sender = LiveSender(provider);

            EmailSendResult result = Assert.Single(
                await sender.SendAsync([Message("Tellma live check")], TestContext.Current.CancellationToken));

            // A 202 is the contract's "accepted by the transport"; the operation is never polled.
            Assert.Equal(EmailSendOutcome.Sent, result.Outcome);
            Assert.NotNull(result.ProviderMessageId);
        }

        [Fact(Skip = SkipReason, SkipUnless = nameof(HasCredentials), SkipType = typeof(AcsEmailLiveSendTests))]
        public async Task Accepts_a_full_feature_message()
        {
            await using ServiceProvider provider = Compose();
            IEmailSender sender = LiveSender(provider);

            EmailMessage message = Message("Tellma live check — فاتورة") with
            {
                HtmlBody = "<p>Live check <img src=\"cid:logo\" /></p>",
                // A correlation makes the adapter stamp its Message-ID, so a first pilot can watch
                // the echo come back on the delivery report.
                Correlation = new EmailCorrelation("outbox", "live-check", 1),
                Attachments =
                [
                    new EmailAttachment("logo.png", "image/png", Encoding.UTF8.GetBytes("not-a-real-png"), "logo"),
                    new EmailAttachment("invoice.pdf", "application/pdf", Encoding.UTF8.GetBytes("not-a-real-pdf")),
                ],
            };

            EmailSendResult result = Assert.Single(
                await sender.SendAsync([message], TestContext.Current.CancellationToken));

            Assert.Equal(EmailSendOutcome.Sent, result.Outcome);
        }

        private static ServiceProvider Compose()
        {
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Email:AcsEmail:Endpoint"] = Environment.GetEnvironmentVariable(EndpointVariable),
                    ["Email:AcsEmail:From:Address"] = Environment.GetEnvironmentVariable(SenderVariable),
                    ["Email:AcsEmail:From:DisplayName"] = "Tellma",
                    ["Email:AcsEmail:MaxConcurrency"] = "2",
                })
                .Build();

            ServiceCollection services = new();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddLogging();
            services.AddSingleton(new DeploymentIdentity("tellma", "Development"));
            services.AddAcsEmail(configuration);

            return services.BuildServiceProvider();
        }

        private static IEmailSender LiveSender(IServiceProvider provider)
        {
            EmailTransportRegistration registration =
                provider.GetServices<EmailTransportRegistration>()
                    .Single(static r => r.Name == AcsEmailServiceCollectionExtensions.TransportName);

            return registration.Live(provider);
        }

        private static EmailMessage Message(string subject)
        {
            return new EmailMessage
            {
                To = [new EmailAddress(Environment.GetEnvironmentVariable(RecipientVariable)!)],
                Subject = subject,
                TextBody = "Sent by the Tellma nightly live suite. If you are reading this, the pipe works.",
                Audience = EmailAudience.Internal,
            };
        }
    }
}
