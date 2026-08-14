// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Tellma.Identity.Data;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Options;
using Tellma.Identity.Services.Audit;

namespace Tellma.Identity.Areas.Identity.Pages.Manage
{
    /// <summary>
    ///     The external logins linked to an account, and the controls to link and unlink one.
    ///     <para>
    ///         Linking runs through the ordinary external-login flow rather than a second copy of
    ///         it: that flow already treats an authenticated session as proof of ownership, which
    ///         is exactly what a signed-in user adding a provider has. This page only starts it
    ///         and shows the result.
    ///     </para>
    /// </summary>
    /// <param name="userManager">The Identity user manager.</param>
    /// <param name="engineOptions">The engine options (which providers are configured).</param>
    /// <param name="auditLogger">Audit emission.</param>
    /// <param name="localizer">UI strings.</param>
    [Authorize]
    public sealed class ExternalLoginsModel(
        UserManager<TellmaIdentityUser> userManager,
        IOptions<TellmaIdentityOptions> engineOptions,
        IAuditLogger auditLogger,
        IStringLocalizer<SharedResources> localizer) : PageModel
    {
        /// <summary>One provider already linked to the account.</summary>
        /// <param name="Provider">The provider's scheme name.</param>
        /// <param name="DisplayName">The provider's human-readable name.</param>
        /// <param name="Key">The provider's stable subject for this user.</param>
        public sealed record LinkedLogin(string Provider, string DisplayName, string Key);

        /// <summary>The providers already linked.</summary>
        public IReadOnlyList<LinkedLogin> Linked { get; private set; } = [];

        /// <summary>The configured providers not yet linked, offered to add.</summary>
        public IReadOnlyList<string> Available { get; private set; } = [];

        /// <summary>An informational banner, when any.</summary>
        public PageStatus? StatusMessage { get; private set; }

        /// <summary>Loads the linked and available providers.</summary>
        /// <returns>The page.</returns>
        public async Task<IActionResult> OnGetAsync()
        {
            await LoadAsync();
            return Page();
        }

        /// <summary>Unlinks a provider, refusing when it is the account's only way back in.</summary>
        /// <param name="provider">The provider scheme to unlink.</param>
        /// <param name="key">That provider's subject for this user.</param>
        /// <returns>The refreshed page.</returns>
        public async Task<IActionResult> OnPostRemoveAsync(string provider, string key)
        {
            TellmaIdentityUser user = (await userManager.GetUserAsync(User))!;

            // The same guard the passkey list applies, for the same reason: removing the last
            // thing that can sign you in locks you out of your own account, and the recovery path
            // out of that needs an administrator.
            bool hasOtherFactor = (await userManager.GetLoginsAsync(user)).Count > 1
                || (await userManager.GetPasskeysAsync(user)).Count > 0
                || await userManager.HasPasswordAsync(user);
            if (!hasOtherFactor)
            {
                StatusMessage = PageStatus.Error(localizer["CannotRemoveOnlySignInMethod"].Value);
                await LoadAsync();
                return Page();
            }

            IdentityResult removed = await userManager.RemoveLoginAsync(user, provider, key);
            if (!removed.Succeeded)
            {
                StatusMessage = PageStatus.Error(localizer["ExternalLoginRemoveFailed"].Value);
                await LoadAsync();
                return Page();
            }

            await auditLogger.LogAsync(new AuditEventEntry
            {
                Action = AuditActions.ExternalLoginRemoved,
                Subject = user.Id,
                Outcome = "success",
            });

            StatusMessage = PageStatus.Success(localizer["ExternalLoginRemoved"].Value);
            await LoadAsync();
            return Page();
        }

        /// <summary>Reads the account's links and works out what is left to offer.</summary>
        private async Task LoadAsync()
        {
            TellmaIdentityUser user = (await userManager.GetUserAsync(User))!;

            IList<UserLoginInfo> logins = await userManager.GetLoginsAsync(user);
            Linked = [.. logins.Select(static login => new LinkedLogin(
                login.LoginProvider, login.ProviderDisplayName ?? login.LoginProvider, login.ProviderKey))];

            // Only providers this deployment actually configured, minus the ones already linked —
            // offering to add a provider twice, or one with no client id, is offering nothing.
            List<string> configured = [];
            if (engineOptions.Value.ExternalProviders.Google.IsConfigured)
            {
                configured.Add("Google");
            }

            if (engineOptions.Value.ExternalProviders.Microsoft.IsConfigured)
            {
                configured.Add("Microsoft");
            }

            Available = [.. configured.Where(provider => !logins.Any(
                login => string.Equals(login.LoginProvider, provider, StringComparison.OrdinalIgnoreCase)))];

            // Each offered provider is a form that redirects off this origin to start the link, and
            // form-action is enforced on this page's policy across every hop of that redirect.
            ExternalProviderFormAction.Allow(HttpContext, Available);
        }
    }
}
