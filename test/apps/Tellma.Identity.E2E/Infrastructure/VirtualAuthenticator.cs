// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Playwright;

namespace Tellma.Identity.E2E.Infrastructure
{
    /// <summary>
    ///     A CDP virtual authenticator: a synthetic platform authenticator with resident-key and
    ///     user-verification support, so passkey ceremonies run without real hardware. Enrolled
    ///     credentials persist for the authenticator's lifetime, letting a test register then sign
    ///     in with the same passkey.
    /// </summary>
    public sealed class VirtualAuthenticator : IAsyncDisposable
    {
        private readonly ICDPSession _session;

        private VirtualAuthenticator(ICDPSession session)
        {
            _session = session;
        }

        /// <summary>Attaches a virtual authenticator to a page.</summary>
        /// <param name="context">The browser context the page belongs to.</param>
        /// <param name="page">The page to attach to.</param>
        /// <returns>The attached authenticator.</returns>
        public static async Task<VirtualAuthenticator> AttachAsync(IBrowserContext context, IPage page)
        {
            ICDPSession session = await context.NewCDPSessionAsync(page);
            await session.SendAsync("WebAuthn.enable");
            await session.SendAsync("WebAuthn.addVirtualAuthenticator", new Dictionary<string, object>
            {
                ["options"] = new Dictionary<string, object>
                {
                    ["protocol"] = "ctap2",
                    ["transport"] = "internal",
                    ["hasResidentKey"] = true,
                    ["hasUserVerification"] = true,
                    ["isUserVerified"] = true,
                    ["automaticPresenceSimulation"] = true,
                },
            });

            return new VirtualAuthenticator(session);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _session.DetachAsync();
        }
    }
}
