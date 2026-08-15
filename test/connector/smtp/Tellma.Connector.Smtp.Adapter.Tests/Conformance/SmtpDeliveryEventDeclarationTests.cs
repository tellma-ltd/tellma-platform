// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Webhooks;
using Tellma.Core.Testing.Email;

namespace Tellma.Connector.Smtp.Adapter.Tests.Conformance
{
    /// <summary>
    ///     The other direction of the shared delivery-event declaration: a transport with no callback
    ///     must stay off the list.
    /// </summary>
    public class SmtpDeliveryEventDeclarationTests
    {
        [Fact]
        public void Registers_no_delivery_event_receiver_and_is_not_declared_as_a_transport_that_emits_them()
        {
            ServiceCollection services = new();
            services.AddSmtpEmail(Configuration());

            // SMTP's only feedback is the synchronous accept, which means the smarthost took
            // responsibility rather than that anything was delivered; bounces come back out of band
            // as mail to the return path, which nothing in this adapter reads.
            Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IWebhookReceiver));

            // So the silent-webhook alert must not watch it: an alert asking "why has no delivery
            // event arrived" would fire forever against a transport that is behaving correctly.
            Assert.DoesNotContain(
                SmtpEmailServiceCollectionExtensions.TransportName,
                EmailDeliveryEventTransports.Names);
        }

        private static IConfigurationSection Configuration()
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Email:Smtp:Host"] = "localhost",
                    ["Email:Smtp:From:Address"] = "no-reply@tellma.com",
                })
                .Build()
                .GetSection(SmtpEmailOptions.SectionName);
        }
    }
}
