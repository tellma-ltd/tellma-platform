// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using Tellma.Core.Abstractions.Webhooks;
using Tellma.Core.Testing.Email;

namespace Tellma.Connector.SendGrid.Adapter.Tests.Webhook
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
    public class SendGridDeliveryEventDeclarationTests
    {
        [Fact]
        public void Registers_a_delivery_event_receiver_and_is_declared_as_a_transport_that_emits_them()
        {
            ServiceCollection services = new();
            services.AddSendGridEmail(Configuration());

            Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWebhookReceiver));

            // The silent-webhook alert evaluates only the declared transports, so a connector that
            // has a callback but is missing from the list has its silence go unwatched — precisely
            // the failure that alert exists to catch.
            Assert.Contains(
                SendGridEmailServiceCollectionExtensions.TransportName,
                EmailDeliveryEventTransports.Names);
        }

        private static IConfigurationSection Configuration()
        {
            // A real key: the composition parses the verification keys eagerly, so a placeholder
            // would fail registration rather than exercise it.
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Email:SendGrid:ApiKey"] = "SG.test-key",
                    ["Email:SendGrid:From:Address"] = "no-reply@tellma.com",
                    ["Email:SendGrid:Webhook:VerificationKeys:0"] =
                        Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),
                })
                .Build()
                .GetSection(SendGridEmailOptions.SectionName);
        }
    }
}
