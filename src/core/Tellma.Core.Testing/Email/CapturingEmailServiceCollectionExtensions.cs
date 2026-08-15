// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Core.Testing.Email
{
    /// <summary>How a capturing sender takes the place of the real one.</summary>
    public enum CapturingEmailMode
    {
        /// <summary>
        ///     Replace the registered <see cref="IEmailSender" /> outright. The simplest thing that
        ///     works when a test only cares about what a feature composed, and not about the routing
        ///     policy that would carry it.
        /// </summary>
        ReplaceSender,

        /// <summary>
        ///     Register as an ordinary transport named <c>capture</c>, selected by setting
        ///     <c>Email:Provider</c> to it. The router, the sandbox policy, and the marking all stay
        ///     in the loop, so an integration test exercises exactly what production runs.
        /// </summary>
        Transport,
    }

    /// <summary>Wires a <see cref="CapturingEmailSender" /> into a test composition.</summary>
    public static class CapturingEmailServiceCollectionExtensions
    {
        /// <summary>The transport name to set <c>Email:Provider</c> to in transport mode.</summary>
        public const string TransportName = "capture";

        /// <summary>Registers a capturing sender and returns it for assertions.</summary>
        /// <param name="services">The service collection.</param>
        /// <param name="mode">Whether to replace the sender outright or to register as a transport.</param>
        /// <param name="timeProvider">The clock capture timestamps come from.</param>
        /// <returns>The sender the test asserts against; also resolvable from the container.</returns>
        public static CapturingEmailSender AddCapturingEmail(
            this IServiceCollection services,
            CapturingEmailMode mode = CapturingEmailMode.ReplaceSender,
            TimeProvider? timeProvider = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            CapturingEmailSender sender = new(timeProvider);
            services.AddSingleton(sender);

            if (mode == CapturingEmailMode.ReplaceSender)
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(sender);
                return sender;
            }

            // The same instance backs both channels, so a sandbox tenant's external mail is captured
            // too — and the router's Sent-to-Sandboxed rewrite is visible in what SendAsync returned
            // to the caller, while CapturedEmail.Result holds what this sender itself reported.
            services.AddSingleton(new EmailTransportRegistration(
                TransportName, _ => sender, _ => sender));

            return sender;
        }
    }
}
