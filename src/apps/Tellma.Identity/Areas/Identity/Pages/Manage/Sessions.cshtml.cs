// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Tellma.Identity.Data;
using Tellma.Identity.Services.Audit;
using Tellma.Identity.Services.AuthenticationPolicy;
using Tellma.Identity.Services.BackchannelLogout;
using Tellma.Identity.Services.Sessions;

namespace Tellma.Identity.Areas.Identity.Pages.Manage
{
    /// <summary>
    ///     The active-sessions page. "Sign out everywhere" bumps the security stamp (stopping
    ///     token renewal within one access-token lifetime), terminates every session in the
    ///     registry, and fans back-channel logout out to each distribution.
    /// </summary>
    /// <param name="userManager">The Identity user manager.</param>
    /// <param name="signInManager">The Identity sign-in manager.</param>
    /// <param name="sessionRegistry">The sid registry.</param>
    /// <param name="backchannelLogout">The logout-token fan-out service.</param>
    /// <param name="auditLogger">Audit emission.</param>
    [Authorize]
    public sealed class SessionsModel(
        UserManager<TellmaIdentityUser> userManager,
        SignInManager<TellmaIdentityUser> signInManager,
        ISessionRegistry sessionRegistry,
        IBackchannelLogoutService backchannelLogout,
        IAuditLogger auditLogger) : PageModel
    {
        /// <summary>One active session shown in the list.</summary>
        /// <param name="UserAgent">The device's user-agent summary.</param>
        /// <param name="LastSeenUtc">When the session was last observed.</param>
        /// <param name="IsCurrent">Whether this is the browser's current session.</param>
        public sealed record SessionView(string? UserAgent, DateTimeOffset LastSeenUtc, bool IsCurrent);

        /// <summary>The user's active sessions, most recent first.</summary>
        public IReadOnlyList<SessionView> Sessions { get; private set; } = [];

        /// <summary>Loads the active sessions.</summary>
        /// <returns>The page.</returns>
        public async Task<IActionResult> OnGetAsync()
        {
            await LoadAsync();
            return Page();
        }

        /// <summary>Signs the user out of every session everywhere.</summary>
        /// <returns>A redirect to the signed-out page.</returns>
        public async Task<IActionResult> OnPostSignOutEverywhereAsync()
        {
            TellmaIdentityUser user = (await userManager.GetUserAsync(User))!;

            // Stop renewal first (security stamp), then terminate + notify distributions.
            await userManager.UpdateSecurityStampAsync(user);
            await backchannelLogout.TerminateAllSessionsAsync(user.Id, HttpContext.RequestAborted);
            await signInManager.SignOutAsync();

            await auditLogger.LogAsync(new AuditEventEntry
            {
                Action = AuditActions.SignOutEverywhere,
                Subject = user.Id,
                Outcome = "success",
            });

            return RedirectToPage("/Account/LoggedOut", new { area = TellmaIdentityConstants.AreaName });
        }

        /// <summary>Loads the current user's active sessions.</summary>
        private async Task LoadAsync()
        {
            TellmaIdentityUser user = (await userManager.GetUserAsync(User))!;
            string? currentSid = User.FindFirst(TellmaClaims.Sid)?.Value;

            IReadOnlyList<Data.Entities.IdentitySession> sessions =
                await sessionRegistry.GetActiveSessionsAsync(user.Id, HttpContext.RequestAborted);
            Sessions = [.. sessions.Select(session =>
                new SessionView(session.UserAgent, session.LastSeenUtc, string.Equals(session.Sid, currentSid, StringComparison.Ordinal)))];
        }
    }
}
