// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Tellma.Identity.Data;
using Tellma.Identity.Infrastructure;
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
    /// <remarks>
    ///     Because the logout is global, the server ends a session at once only when the request
    ///     proves it comes from a relying party of that session: an <c>id_token_hint</c> whose
    ///     <c>sid</c> is the signed-in session's. Any other request — no hint, or a hint from
    ///     another session — asks the user first (OpenID Connect RP-Initiated Logout 1.0 §6), and
    ///     signs out only on the user's own anti-forgery-protected confirmation, so no page can end
    ///     a user's sessions by navigating them here.
    /// </remarks>
    /// <param name="signInManager">The Identity sign-in manager.</param>
    /// <param name="applicationManager">Finds the client a post-logout redirect URI is registered to.</param>
    /// <param name="backchannelLogout">The logout-token fan-out service.</param>
    /// <param name="auditLogger">Audit emission.</param>
    public sealed class EndSessionController(
        SignInManager<TellmaIdentityUser> signInManager,
        IOpenIddictApplicationManager applicationManager,
        IBackchannelLogoutService backchannelLogout,
        IAuditLogger auditLogger) : Controller
    {
        /// <summary>
        ///     Handles the end-session request: completes it at once when its hint names the
        ///     signed-in session (or no session is signed in), and otherwise asks the user to
        ///     confirm.
        /// </summary>
        /// <returns>The sign-out result, the confirmation page, or the refusal.</returns>
        [HttpGet("connect/endsession")]
        [HttpPost("connect/endsession")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> EndSession()
        {
            // A request OpenIddict refused while validating it — an unregistered post-logout
            // redirect URI, an unknown client, a mismatched id_token_hint — still reaches this
            // action through error pass-through. It never signs anyone out, never terminates a
            // session and writes no audit row: it only renders the refusal.
            if (ProtocolRefusal.FindAtEndSession(HttpContext) is { } refusal)
            {
                return refusal;
            }

            AuthenticateResult cookie = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
            string? sid = cookie.Succeeded ? cookie.Principal.FindFirst(TellmaClaims.Sid)?.Value : null;

            // No session to end, or a hint from this very session: the relying party that holds the
            // session's id token asked, so the logout proceeds without a question.
            if (sid is null || await HintNamesSessionAsync(sid))
            {
                return await CompleteAsync(cookie);
            }

            // Anything else asks the user. The confirmation posts back here, and its answer is
            // the redirect to the request's post-logout URI, which the form-action policy must
            // name or the browser would discard it; only a URI registered to a client is named.
            OpenIddictRequest request = HttpContext.GetOpenIddictServerRequest()
                ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");
            if (request.PostLogoutRedirectUri is { Length: > 0 } postLogoutRedirectUri)
            {
                await using IAsyncEnumerator<object> registered = applicationManager
                    .FindByPostLogoutRedirectUriAsync(postLogoutRedirectUri, HttpContext.RequestAborted)
                    .GetAsyncEnumerator(HttpContext.RequestAborted);
                if (await registered.MoveNextAsync())
                {
                    CspFormAction.Allow(HttpContext, [postLogoutRedirectUri]);
                }
            }

            return View("Confirm");
        }

        /// <summary>Completes the end-session request the user confirmed on the confirmation page.</summary>
        /// <returns>The sign-out result, or the refusal.</returns>
        [HttpPost("connect/endsession")]
        [FormValueRequired("submit.SignOut")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Confirm()
        {
            // The confirmation posts the whole request back, which OpenIddict validates again: a
            // request refused this time ends nothing, whatever the form says.
            return ProtocolRefusal.FindAtEndSession(HttpContext) is { } refusal
                ? refusal
                : await CompleteAsync(await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme));
        }

        /// <summary>
        ///     Whether the request's <c>id_token_hint</c> — which OpenIddict has already validated
        ///     — was issued for the signed-in session.
        /// </summary>
        private async Task<bool> HintNamesSessionAsync(string sid)
        {
            AuthenticateResult hint = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            return hint.Succeeded
                && string.Equals(hint.Principal.FindFirst(TellmaClaims.Sid)?.Value, sid, StringComparison.Ordinal);
        }

        /// <summary>Terminates the SSO session, notifies its clients, and answers the protocol.</summary>
        private async Task<IActionResult> CompleteAsync(AuthenticateResult cookie)
        {
            // The SSO session identity is the source of truth for what to terminate.
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
            // and redirects there; absent one, it redirects to the logged-out page. That page's
            // address is generated by routing, so it carries the engine's mount point and the
            // host's path base: a literal path would leave an in-proc engine for the host's routes.
            string loggedOut = Url.Page("/Account/LoggedOut", new { area = TellmaIdentityConstants.AreaName })
                ?? throw new InvalidOperationException("The logged-out page cannot be addressed.");
            return SignOut(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties { RedirectUri = loggedOut });
        }
    }
}
