// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Tellma.Identity.Data;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>
    ///     Resolves the account a passkey-enrollment ceremony acts for.
    ///     <para>
    ///         A WebAuthn enrollment spans two requests: one mints the creation options, the
    ///         browser runs the ceremony against them, and a second stores what comes back. Both
    ///         requests have to name the same account, and neither can check the other — the
    ///         options carry the user entity the authenticator scopes and excludes against, while
    ///         the handler decides whose credential list to add to. They resolve it here, once, so
    ///         a change to the rule cannot land on one side and not the other.
    ///     </para>
    /// </summary>
    public static class CredentialCeremony
    {
        /// <summary>
        ///     Resolves the ceremony's subject.
        ///     <para>
        ///         The flow cookie outranks an ambient session, and the order matters. The cookie
        ///         names the account a single-use token was redeemed for moments ago — a statement
        ///         about <em>this</em> ceremony. A session only says who last signed in on this
        ///         browser. Taking the session first is how an invitation opened on a machine
        ///         already signed in as someone else enrolls the credential onto that someone
        ///         else's account, having consumed the invited user's one-time link to get there.
        ///     </para>
        /// </summary>
        /// <param name="context">The request, carrying the flow cookie and any ambient session.</param>
        /// <param name="userManager">The Identity user manager.</param>
        /// <returns>The user, or null when neither a flow nor a session identifies one.</returns>
        public static async Task<TellmaIdentityUser?> ResolveUserAsync(
            HttpContext context, UserManager<TellmaIdentityUser> userManager)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(userManager);

            return CredentialFlowCookie.GetUserId(context) is { } flowUserId
                ? await userManager.FindByIdAsync(flowUserId)
                : context.User.Identity?.IsAuthenticated == true
                    ? await userManager.GetUserAsync(context.User)
                    : null;
        }
    }
}
