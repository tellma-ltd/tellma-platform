// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using Tellma.Identity.Options;

namespace Tellma.Identity.Services.Invitations
{
    /// <summary>
    ///     Builds the absolute invitation link. Shared by the invite API and the recovery sweep so
    ///     a resent invitation cannot address a different place from the original.
    /// </summary>
    /// <param name="options">Engine options, the source of the issuer and path base.</param>
    public sealed class InvitationLinkBuilder(IOptions<TellmaIdentityOptions> options)
    {
        /// <summary>Builds the link for a clear invitation token.</summary>
        /// <param name="token">The clear token.</param>
        /// <returns>The absolute URL the recipient opens.</returns>
        public string Build(string token)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(token);

            // The token travels in the URL; the return url never does — it is held server-side so
            // the emailed link cannot be edited into pointing somewhere else.
            string prefix = options.Value.PathBase;
            return new Uri(
                options.Value.Issuer!,
                $"{prefix}/Identity/Account/Invitation?code={Uri.EscapeDataString(token)}").AbsoluteUri;
        }
    }
}
