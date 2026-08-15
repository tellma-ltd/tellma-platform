// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Connector.SendGrid
{
    /// <summary>What the client needs to talk to SendGrid.</summary>
    /// <param name="ApiKey">The API key presented as a bearer token.</param>
    /// <param name="Timeout">The per-request timeout. Owned by the client rather than the
    ///     <c>HttpClient</c> so a timeout can be told apart from the caller abandoning the batch.</param>
    /// <param name="BaseAddress">The API root; overridable for a proxy or a test double.</param>
    public sealed record SendGridClientOptions(
        string ApiKey,
        TimeSpan Timeout,
        Uri? BaseAddress = null)
    {
        /// <summary>The public API root.</summary>
        public static Uri DefaultBaseAddress { get; } = new Uri("https://api.sendgrid.com", UriKind.Absolute);

        /// <summary>The API root this client will use.</summary>
        public Uri EffectiveBaseAddress => BaseAddress ?? DefaultBaseAddress;
    }
}
