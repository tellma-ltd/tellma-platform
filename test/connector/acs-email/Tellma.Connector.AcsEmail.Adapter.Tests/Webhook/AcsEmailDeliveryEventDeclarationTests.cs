// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Webhooks;
using Tellma.Core.Testing.Email;

namespace Tellma.Connector.AcsEmail.Adapter.Tests.Webhook
{
    /// <summary>
    ///     This connector's half of the shared delivery-event declaration.
    /// </summary>
    /// <remarks>
    ///     "Registers a delivery-event receiver" and "is named in the shared list of transports that
    ///     emit delivery events" are one fact stated in two assemblies that cannot see each other —
    ///     the core suite deliberately does not reference any connector. So the halves are checked
    ///     where each is visible: the core suite checks the list against the alert query, and this
    ///     checks the list against what the composition actually registers.
    /// </remarks>
    public class AcsEmailDeliveryEventDeclarationTests
    {
        [Fact]
        public void Registers_a_delivery_event_receiver_and_is_declared_as_a_transport_that_emits_them()
        {
            ServiceCollection services = new();
            services.AddAcsEmail(Configuration());

            Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWebhookReceiver));

            // The silent-webhook alert evaluates only the declared transports, so a connector that
            // has a callback but is missing from the list has its silence go unwatched — precisely
            // the failure that alert exists to catch.
            Assert.Contains(
                AcsEmailServiceCollectionExtensions.TransportName,
                EmailDeliveryEventTransports.Names);
        }

        private static IConfigurationSection Configuration()
        {
            // The receiver is registered only when a subscription token is configured, so the token
            // is what makes this composition the one a deployment with the webhook enabled builds.
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Email:AcsEmail:Endpoint"] = "https://tellma-test.communication.azure.com",
                    ["Email:AcsEmail:From:Address"] = "no-reply@tellma.com",
                    ["Email:AcsEmail:Webhook:Tokens:0"] = "a-subscription-token",
                })
                .Build()
                .GetSection(AcsEmailOptions.SectionName);
        }
    }
}
