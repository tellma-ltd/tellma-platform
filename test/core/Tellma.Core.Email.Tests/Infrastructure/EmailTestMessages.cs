// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Email;

namespace Tellma.Core.Email.Tests.Infrastructure
{
    /// <summary>Message builders the routing suites share.</summary>
    public static class EmailTestMessages
    {
        /// <summary>Builds a valid message with the given audience.</summary>
        /// <param name="audience">Whose inbox it targets.</param>
        /// <param name="subject">The subject.</param>
        /// <param name="correlation">The correlation, if any.</param>
        /// <returns>The message.</returns>
        public static EmailMessage Message(
            EmailAudience audience, string subject = "Subject", EmailCorrelation? correlation = null)
        {
            return new EmailMessage
            {
                To = [new EmailAddress("recipient@example.com")],
                Subject = subject,
                TextBody = "Body",
                Audience = audience,
                Correlation = correlation,
            };
        }

        /// <summary>Registers a transport backed by the supplied fake senders.</summary>
        /// <param name="services">The service collection.</param>
        /// <param name="name">The transport name.</param>
        /// <param name="live">The live sender.</param>
        /// <param name="sandbox">The sandbox sender, or null for a transport without one.</param>
        public static void AddFakeTransport(
            this Microsoft.Extensions.DependencyInjection.IServiceCollection services,
            string name,
            FakeEmailSender live,
            FakeEmailSender? sandbox = null)
        {
            Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(
                services,
                new EmailTransportRegistration(
                    name,
                    _ => live,
                    sandbox is null ? null : _ => sandbox));
        }
    }
}
