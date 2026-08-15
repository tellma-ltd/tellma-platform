// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Core.Email
{
    /// <summary>
    ///     Resolves the active transport once for the lifetime of the host, so every scope's router
    ///     sees the same answer and the selection rules run exactly once.
    /// </summary>
    /// <remarks>
    ///     Reading <c>IOptions&lt;EmailOptions&gt;.Value</c> here is what triggers
    ///     <see cref="EmailOptionsValidator" />, so constructing this type is never possible against
    ///     an invalid composition — which is why the throw below is a guard rather than a code path
    ///     a deployment can reach.
    /// </remarks>
    internal sealed class EmailTransportSelector
    {
        /// <summary>Resolves the configured provider against the registered transports.</summary>
        /// <param name="options">The bound pipeline options.</param>
        /// <param name="environment">The host environment.</param>
        /// <param name="registrations">Every transport the composition declared.</param>
        /// <exception cref="InvalidOperationException">The composition is invalid.</exception>
        public EmailTransportSelector(
            IOptions<EmailOptions> options,
            IHostEnvironment environment,
            IEnumerable<EmailTransportRegistration> registrations)
        {
            ArgumentNullException.ThrowIfNull(options);

            EmailTransportResolutionResult resolution =
                EmailTransportResolution.Resolve(options.Value, environment, [.. registrations]);

            Active = resolution.Active
                ?? throw new InvalidOperationException(
                    "The email composition is invalid: " + string.Join(" ", resolution.Failures));
        }

        /// <summary>The transport that sends mail.</summary>
        public EmailTransportRegistration Active { get; }
    }
}
