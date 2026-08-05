// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Tellma.Identity.Data;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tellma.Identity.Controllers
{
    /// <summary>
    ///     The userinfo endpoint (pass-through): OpenIddict has already validated the access
    ///     token; this controller returns the user's claims from the store, gated on the granted
    ///     scopes exactly like the token claims — a token without <c>openid</c> is refused
    ///     outright, and one without <c>profile</c>/<c>email</c> receives no more here than in
    ///     its tokens.
    /// </summary>
    /// <param name="userManager">The Identity user manager.</param>
    [Authorize(AuthenticationSchemes = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)]
    public sealed class UserinfoController(UserManager<TellmaIdentityUser> userManager) : Controller
    {
        /// <summary>Returns the authenticated user's claims.</summary>
        /// <returns>The claims object, or an invalid-token challenge.</returns>
        [HttpGet("connect/userinfo")]
        [HttpPost("connect/userinfo")]
        [IgnoreAntiforgeryToken]
        [Produces("application/json")]
        public async Task<IActionResult> Userinfo()
        {
            // The endpoint answers only for a token issued with `openid` (OpenID Connect Core
            // 5.3): without it the client was never granted the identity contract, whatever other
            // scopes its token carries, and even `sub` is more than it asked for.
            if (!User.HasScope(Scopes.OpenId))
            {
                return Challenge(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InsufficientScope,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                            "The 'openid' scope is required to access the userinfo endpoint.",
                    }));
            }

            // A machine token's subject is a client id, and a user may have been removed or
            // suspended since issuance: both are an invalid-token challenge, prompting the client
            // to drop its token, never an empty success.
            TellmaIdentityUser? user = User.GetClaim(Claims.Subject) is { Length: > 0 } subject
                ? await userManager.FindByIdAsync(subject)
                : null;
            if (user is null || user.LifecycleState != UserLifecycleState.Active)
            {
                return Challenge(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidToken,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                            "The access token is not associated with a usable user account.",
                    }));
            }

            Dictionary<string, object> claims = new(StringComparer.Ordinal)
            {
                [Claims.Subject] = user.Id,
            };

            if (User.HasScope(Scopes.Email))
            {
                claims[Claims.Email] = user.Email!;
                claims[Claims.EmailVerified] = user.EmailConfirmed;
            }

            if (User.HasScope(Scopes.Profile))
            {
                claims[Claims.PreferredUsername] = user.Email!;
                if (user.DisplayName is not null)
                {
                    claims[Claims.Name] = user.DisplayName;
                }

                if (user.Gender is { } gender)
                {
                    claims[Claims.Gender] = gender.ToString().ToLowerInvariant();
                }

                if (user.Locale is not null)
                {
                    claims[Claims.Locale] = user.Locale;
                }
            }

            return Ok(claims);
        }
    }
}
