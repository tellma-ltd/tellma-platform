// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;

namespace Tellma.Identity.IntegrationTests.Infrastructure
{
    /// <summary>
    ///     Stands in for a distribution's back-channel logout endpoint: captures every
    ///     <c>logout_token</c> the authority POSTs so tests can validate its signature and claims,
    ///     then returns 200 so the fan-out records success.
    /// </summary>
    public sealed class RecordingBackchannelHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<string> _logoutTokens = new();

        /// <summary>The captured logout tokens, oldest first.</summary>
        public IReadOnlyCollection<string> LogoutTokens => _logoutTokens;

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                string body = await request.Content.ReadAsStringAsync(cancellationToken);
                foreach (string pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = pair.Split('=', 2);
                    if (parts.Length == 2 && string.Equals(parts[0], "logout_token", StringComparison.Ordinal))
                    {
                        _logoutTokens.Enqueue(Uri.UnescapeDataString(parts[1]));
                    }
                }
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }
    }
}
