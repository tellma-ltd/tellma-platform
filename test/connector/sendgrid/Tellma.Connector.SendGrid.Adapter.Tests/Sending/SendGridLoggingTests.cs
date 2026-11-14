// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Headers;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Testing.Support.Http;
using Tellma.Testing.Support.Logging;

namespace Tellma.Connector.SendGrid.Adapter.Tests.Sending
{
    /// <summary>
    ///     The API key never reaches a log: not the rendered message, and not the structured state a
    ///     sink serialises, even with every category enabled at Trace.
    /// </summary>
    /// <remarks>
    ///     The sender is built through the adapter's own registration — the named client and all —
    ///     with only the wire replaced, so what is asserted is what a deployment composes.
    /// </remarks>
    public sealed class SendGridLoggingTests
    {
        /// <summary>The API key, distinctive enough that no other text contains it by accident.</summary>
        private const string ApiKey = "SG.never-logged-5c1f0e7a9b3d4c2e";

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task The_api_key_reaches_no_log_entry_at_trace(bool sandbox)
        {
            StateCapturingLoggerProvider logs = new();
            ScriptedHttpMessageHandler wire = Accepting();
            await using ServiceProvider services = Compose(logs, wire);

            EmailTransportRegistration registration = services.GetRequiredService<EmailTransportRegistration>();
            IEmailSender sender = sandbox ? registration.Sandbox!(services) : registration.Live(services);
            EmailSendResult result = Assert.Single(await sender.SendAsync([Message()], TestContext.Current.CancellationToken));

            // The key did travel, as the bearer credential, through the client the adapter composed.
            Assert.Equal(EmailSendOutcome.Sent, result.Outcome);
            Assert.Equal($"Bearer {ApiKey}", Assert.Single(wire.Exchanges).Header("Authorization"));

            Assert.Empty(logs.Mentioning(ApiKey));
        }

        [Fact]
        public async Task A_client_with_the_factorys_default_loggers_would_carry_the_key_in_its_state()
        {
            // The hazard the adapter's client is composed against, shown on a client composed the
            // ordinary way: the rendered messages redact the header, the state does not. This also
            // proves the capture sees what a structured sink would, so the empty result above is a
            // finding rather than a blind spot.
            StateCapturingLoggerProvider logs = new();
            ScriptedHttpMessageHandler wire = Accepting();
            ServiceCollection collection = new();
            _ = collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
            _ = collection.AddHttpClient("ordinary").ConfigurePrimaryHttpMessageHandler(() => wire);
            await using ServiceProvider services = collection.BuildServiceProvider();

            using HttpClient client = services.GetRequiredService<IHttpClientFactory>().CreateClient("ordinary");
            using HttpRequestMessage request = new(HttpMethod.Post, new Uri("https://api.sendgrid.com/v3/mail/send"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
            using HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.NotEmpty(logs.Mentioning(ApiKey));
            Assert.DoesNotContain(logs.Entries, static entry => entry.Message?.Contains(ApiKey, StringComparison.Ordinal) == true);
        }

        /// <summary>
        ///     The adapter's composition with every category logged at Trace and its named client's
        ///     wire replaced.
        /// </summary>
        /// <param name="logs">Where the logs go.</param>
        /// <param name="wire">What answers the client's requests.</param>
        /// <returns>The services.</returns>
        private static ServiceProvider Compose(StateCapturingLoggerProvider logs, ScriptedHttpMessageHandler wire)
        {
            ServiceCollection services = new();
            _ = services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
            _ = services.AddSingleton(new DeploymentIdentity("etpharma", "Production"));
            _ = services.AddSendGridEmail(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Email:SendGrid:ApiKey"] = ApiKey,
                    ["Email:SendGrid:From:Address"] = "no-reply@tellma.com",
                })
                .Build());

            // Only the primary handler changes: the factory's own handlers stay as composed.
            _ = services.AddHttpClient(SendGridEmailSender.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => wire);
            return services.BuildServiceProvider();
        }

        /// <summary>A wire that accepts every message.</summary>
        /// <returns>The handler.</returns>
        private static ScriptedHttpMessageHandler Accepting()
        {
            return new ScriptedHttpMessageHandler(static (_, _) =>
                ScriptedHttpMessageHandler.Respond(HttpStatusCode.Accepted, messageIdHeader: "msg-1"));
        }

        /// <summary>A valid message.</summary>
        /// <returns>The message.</returns>
        private static EmailMessage Message()
        {
            return new EmailMessage
            {
                To = [new EmailAddress("someone@example.com")],
                Subject = "Hello",
                TextBody = "Hello.",
                Audience = EmailAudience.Internal,
            };
        }
    }
}
