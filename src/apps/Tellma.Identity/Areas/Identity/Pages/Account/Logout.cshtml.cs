// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;
using Tellma.Identity.Data;
using Tellma.Identity.Services.Audit;
using Tellma.Identity.Services.AuthenticationPolicy;
using Tellma.Identity.Services.BackchannelLogout;

namespace Tellma.Identity.Areas.Identity.Pages.Account
{
    /// <summary>
    ///     Direct sign-out at the authority (global by design): terminates the SSO session,
    ///     revokes the session's grants, and fans back-channel logout out to every distribution
    ///     that holds tokens under it. RP-initiated logout goes through
    ///     <c>/connect/endsession</c>, which performs the same termination.
    /// </summary>
    /// <param name="signInManager">The Identity sign-in manager.</param>
    /// <param name="backchannelLogout">The logout-token fan-out service.</param>
    /// <param name="auditLogger">Audit emission.</param>
    [AllowAnonymous]
    public sealed class LogoutModel(
        SignInManager<TellmaIdentityUser> signInManager,
        IBackchannelLogoutService backchannelLogout,
        IAuditLogger auditLogger) : PageModel
    {
        /// <summary>Terminates the SSO session and notifies its distributions.</summary>
        /// <returns>A redirect to the signed-out page.</returns>
        public async Task<IActionResult> OnPostAsync()
        {
            ClaimsPrincipal principal = User;
            string? sid = principal.FindFirst(TellmaClaims.Sid)?.Value;
            string? subject = signInManager.UserManager.GetUserId(principal);

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

            return RedirectToPage("LoggedOut");
        }
    }
}
