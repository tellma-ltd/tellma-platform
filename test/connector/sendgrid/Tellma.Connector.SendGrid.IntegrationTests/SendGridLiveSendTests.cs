// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Tellma.Connector.SendGrid.Adapter;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Hosting;

namespace Tellma.Connector.SendGrid.IntegrationTests
{
    /// <summary>
    ///     The one thing no offline test can vouch for: that the payloads, credentials, and quotas
    ///     satisfy the real API.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every request goes out on the transport's sandbox channel, which is SendGrid's own
    ///         validate-only mode: the whole payload is validated, nothing is delivered, no credits
    ///         are consumed, and no events are emitted. That is also why the webhook cannot be
    ///         exercised here — its correctness rests on the signature vectors and recorded payloads
    ///         in the offline suites.
    ///     </para>
    ///     <para>
    ///         The sender is composed through the adapter's own registration rather than constructed
    ///         directly, so this suite also proves the composition a deployment uses.
    ///     </para>
    /// </remarks>
    [Trait("Category", "Integration")]
    [Trait("Live", "true")]
    public class SendGridLiveSendTests
    {
        private const string ApiKeyVariable = "TELLMA_SENDGRID_TEST_APIKEY";
        private const string SenderVariable = "TELLMA_SENDGRID_TEST_SENDER";

        private const string SkipReason =
            "Set TELLMA_SENDGRID_TEST_APIKEY (a Mail Send-only key) and TELLMA_SENDGRID_TEST_SENDER to run the live SendGrid suite.";

        /// <summary>Whether the environment carries the credentials this suite needs.</summary>
        public static bool HasCredentials =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ApiKeyVariable))
            && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SenderVariable));

        [Fact(Skip = SkipReason, SkipUnless = nameof(HasCredentials), SkipType = typeof(SendGridLiveSendTests))]
        public async Task Accepts_a_minimal_message()
        {
            await using ServiceProvider provider = Compose();
            IEmailSender sender = SandboxSender(provider);

            EmailSendResult result = Assert.Single(
                await sender.SendAsync([Message("Tellma live check")], TestContext.Current.CancellationToken));

            Assert.Equal(EmailSendOutcome.Sent, result.Outcome);
            Assert.NotNull(result.ProviderMessageId);
        }

        [Fact(Skip = SkipReason, SkipUnless = nameof(HasCredentials), SkipType = typeof(SendGridLiveSendTests))]
        public async Task Accepts_a_full_feature_message()
        {
            await using ServiceProvider provider = Compose();
            IEmailSender sender = SandboxSender(provider);

            EmailMessage message = Message("Tellma live check — فاتورة") with
            {
                HtmlBody = "<p>Live check <img src=\"cid:logo\" /></p>",
                ReplyTo = new EmailAddress(SenderAddress(), "Tellma"),
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

        [Fact(Skip = SkipReason, SkipUnless = nameof(HasCredentials), SkipType = typeof(SendGridLiveSendTests))]
        public async Task Accepts_a_small_batch()
        {
            await using ServiceProvider provider = Compose();
            IEmailSender sender = SandboxSender(provider);

            IReadOnlyList<EmailSendResult> results = await sender.SendAsync(
                [Message("Tellma live check 1"), Message("Tellma live check 2"), Message("Tellma live check 3")],
                TestContext.Current.CancellationToken);

            Assert.Equal(3, results.Count);
            Assert.All(results, static r => Assert.Equal(EmailSendOutcome.Sent, r.Outcome));
        }

        private static string SenderAddress()
        {
            return Environment.GetEnvironmentVariable(SenderVariable)!;
        }

        private static ServiceProvider Compose()
        {
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Email:SendGrid:ApiKey"] = Environment.GetEnvironmentVariable(ApiKeyVariable),
                    ["Email:SendGrid:From:Address"] = SenderAddress(),
                    ["Email:SendGrid:From:DisplayName"] = "Tellma",
                    ["Email:SendGrid:MaxConcurrency"] = "2",
                })
                .Build();

            ServiceCollection services = new();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddLogging();
            services.AddSingleton(new DeploymentIdentity("tellma", "Development"));
            services.AddSendGridEmail(configuration);

            return services.BuildServiceProvider();
        }

        private static IEmailSender SandboxSender(IServiceProvider provider)
        {
            EmailTransportRegistration registration =
                provider.GetServices<EmailTransportRegistration>()
                    .Single(static r => r.Name == SendGridEmailServiceCollectionExtensions.TransportName);

            return registration.Sandbox!(provider);
        }

        private static EmailMessage Message(string subject)
        {
            return new EmailMessage
            {
                To = [new EmailAddress("live-check@example.com")],
                Subject = subject,
                TextBody = "Sent by the Tellma nightly live suite in sandbox mode; never delivered.",
                Audience = EmailAudience.Internal,
            };
        }
    }
}
