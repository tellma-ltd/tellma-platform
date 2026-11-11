// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.Net;

namespace Tellma.Core.AspNetCore.Hosting
{
    /// <summary>
    ///     Which reverse proxies the host trusts for <c>X-Forwarded-For</c> and
    ///     <c>X-Forwarded-Proto</c>, bound from <c>Tellma:ForwardedHeaders</c>.
    /// </summary>
    public sealed class ForwardedHeadersHostOptions : IValidatableObject
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:ForwardedHeaders";

        /// <summary>Whether the forwarded headers are honoured at all.</summary>
        public bool Enabled { get; set; }

        /// <summary>The trusted proxies' networks, in CIDR notation.</summary>
        public IList<string> KnownNetworks { get; } = [];

        /// <summary>The trusted proxies' addresses.</summary>
        public IList<string> KnownProxies { get; } = [];

        /// <inheritdoc />
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // The framework treats an empty proxy set as trusting every caller, which would make the
            // client address spoofable by anyone who sends the header.
            if (Enabled && KnownNetworks.Count == 0 && KnownProxies.Count == 0)
            {
                yield return new ValidationResult(
                    $"{SectionPath}:{nameof(Enabled)} is true with neither {nameof(KnownNetworks)} nor {nameof(KnownProxies)}, which would trust the forwarded headers of every caller; list the deployment's proxies.",
                    [nameof(Enabled)]);
            }

            foreach (string network in KnownNetworks)
            {
                if (!IPNetwork.TryParse(network, out _))
                {
                    yield return new ValidationResult($"{SectionPath}:{nameof(KnownNetworks)} holds '{network}', which is not a CIDR network.", [nameof(KnownNetworks)]);
                }
            }

            foreach (string proxy in KnownProxies)
            {
                if (!IPAddress.TryParse(proxy, out _))
                {
                    yield return new ValidationResult($"{SectionPath}:{nameof(KnownProxies)} holds '{proxy}', which is not an IP address.", [nameof(KnownProxies)]);
                }
            }
        }
    }
}
