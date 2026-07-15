// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using OpenIddict.Abstractions;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tellma.Identity.Services.AuthenticationPolicy
{
    /// <summary>
    ///     Decides which tokens each claim is serialized into. Claims without a destination are
    ///     dropped by OpenIddict — private engine state (the allow-list snapshot, the security
    ///     stamp) rides the server-side principal only and never reaches a token.
    /// </summary>
    public static class TellmaClaimDestinations
    {
        /// <summary>The Identity security-stamp claim type (the framework default).</summary>
        public const string SecurityStampClaimType = "AspNet.Identity.SecurityStamp";

        /// <summary>Resolves the destinations of one claim.</summary>
        /// <param name="claim">The claim; its subject identity carries the granted scopes.</param>
        /// <returns>The token destinations.</returns>
        public static IEnumerable<string> Resolve(Claim claim)
        {
            ArgumentNullException.ThrowIfNull(claim);

            // Server-side state: never serialized into any token.
            if (claim.Type is TellmaClaims.AllowedMethods or SecurityStampClaimType)
            {
                yield break;
            }

            switch (claim.Type)
            {
                // Profile claims ride the access token; the id token gets them under `profile`.
                case Claims.Name or Claims.PreferredUsername or Claims.Locale:
                    yield return Destinations.AccessToken;
                    if (claim.Subject!.HasScope(Scopes.Profile))
                    {
                        yield return Destinations.IdentityToken;
                    }

                    yield break;

                case Claims.Email or Claims.EmailVerified:
                    yield return Destinations.AccessToken;
                    if (claim.Subject!.HasScope(Scopes.Email))
                    {
                        yield return Destinations.IdentityToken;
                    }

                    yield break;

                // Assurance and session-binding claims ride both tokens: the id token informs
                // the client at login; the access token is what resource servers re-check.
                case Claims.AuthenticationContextReference
                    or Claims.AuthenticationMethodReference
                    or Claims.AuthenticationTime
                    or TellmaClaims.Sid
                    or TellmaClaims.Methods:
                    yield return Destinations.AccessToken;
                    yield return Destinations.IdentityToken;
                    yield break;

                default:
                    yield return Destinations.AccessToken;
                    yield break;
            }
        }
    }
}
