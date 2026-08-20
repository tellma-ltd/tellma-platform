// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Server.AspNetCore;
using Tellma.Identity.Data;
using Tellma.Identity.Services.Audit;
using Tellma.Identity.Services.AuthenticationPolicy;
using Tellma.Identity.Services.BackchannelLogout;

namespace Tellma.Identity.Controllers
{
    /// <summary>
    ///     The RP-initiated (end-session) logout endpoint. Logout at the authority is global by
    ///     design: it ends the SSO session, revokes the grants backing the session's refresh
    ///     tokens, and fans a signed <c>logout_token</c> out to every distribution that holds a
    ///     session under the same <c>sid</c>. A distribution-local logout never reaches the
    ///     authority — the distribution simply drops its own cookie.
    /// </summary>
    /// <param name="signInManager">The Identity sign-in manager.</param>
    /// <param name="backchannelLogout">The logout-token fan-out service.</param>
    /// <param name="auditLogger">Audit emission.</param>
    public sealed class EndSessionController(
        SignInManager<TellmaIdentityUser> signInManager,
        IBackchannelLogoutService backchannelLogout,
        IAuditLogger auditLogger) : Controller
    {
        /// <summary>Completes the end-session request, terminating the session and notifying clients.</summary>
        /// <returns>The sign-out result redirecting to the validated post-logout URI.</returns>
        [HttpGet("connect/endsession")]
        [HttpPost("connect/endsession")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> EndSession()
        {
            // The end-session request principal (from id_token_hint) is available, but the SSO
            // session identity is the source of truth for what to terminate.
            AuthenticateResult cookie = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
            string? sid = cookie.Succeeded ? cookie.Principal.FindFirst(TellmaClaims.Sid)?.Value : null;
            string? subject = cookie.Succeeded ? signInManager.UserManager.GetUserId(cookie.Principal) : null;

            await signInManager.SignOutAsync();

            if (sid is not null && subject is not null)
            {
                await backchannelLogout.TerminateSessionAsync(sid, subject, HttpContext.RequestAborted);
                await auditLogger.LogAsync(new AuditEventEntry
                {
                    Action = AuditActions.SessionTerminated,
                    Subject = subject,
                    Sid = sid,
                    Outcome = "success",
                });
            }

            // OpenIddict validates the post_logout_redirect_uri against the client's registration
            // and redirects there; absent one, it renders the logged-out page.
            return SignOut(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties { RedirectUri = "/Identity/Account/LoggedOut" });
        }
    }
}
