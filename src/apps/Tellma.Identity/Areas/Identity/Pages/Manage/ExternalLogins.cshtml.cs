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
        /// <summary>One provider as the page presents it, linked or not.</summary>
        /// <param name="Provider">The provider's scheme name, and the row's title.</param>
        /// <param name="Account">
        ///     Which account is linked, when the link recorded one. Null both for an unlinked
        ///     provider and for a link made before the address was captured, or by a provider that
        ///     returned none — the row then says nothing rather than repeating the provider name.
        /// </param>
        /// <param name="Key">The provider's stable subject for this user; null when not linked.</param>
        public sealed record ProviderRow(string Provider, string? Account, string? Key)
        {
            /// <summary>Whether this provider is currently linked to the account.</summary>
            public bool IsLinked => Key is not null;
        }

        /// <summary>Every provider this page shows, in a stable order, linked or not.</summary>
        public IReadOnlyList<ProviderRow> Providers { get; private set; } = [];

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
                // A refusal, not a failure: nothing went wrong, the account simply cannot give up
                // its last way in. The design draws it amber for that reason, and the warning
                // treatment still takes focus so the unchanged list is not the only answer.
                StatusMessage = PageStatus.Warning(localizer["CannotRemoveOnlySignInMethod"].Value);
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

        /// <summary>Builds the provider rows and permits the redirects their buttons start.</summary>
        private async Task LoadAsync()
        {
            TellmaIdentityUser user = (await userManager.GetUserAsync(User))!;
            IList<UserLoginInfo> logins = await userManager.GetLoginsAsync(user);

            List<string> configured = [];
            if (engineOptions.Value.ExternalProviders.Google.IsConfigured)
            {
                configured.Add("Google");
            }

            if (engineOptions.Value.ExternalProviders.Microsoft.IsConfigured)
            {
                configured.Add("Microsoft");
            }

            // Configured providers first, in a fixed order, then anything the user has linked that
            // this deployment no longer configures. That tail matters: dropping a provider from
            // configuration must not strand a link on the account with no way to remove it.
            List<string> shown =
            [
                .. configured,
                .. logins.Select(static login => login.LoginProvider)
                    .Where(provider => !configured.Contains(provider, StringComparer.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase),
            ];

            Providers = [.. shown.Select(provider => ToRow(provider, logins))];

            // The unlinked rows are the ones that start a redirect off this origin, and form-action
            // is enforced on this page's policy across every hop of it. Derived from the rows rather
            // than recomputed: two computations of one set is how a button appears that the policy
            // will not let the browser follow.
            ExternalProviderFormAction.Allow(
                HttpContext, Providers.Where(static row => !row.IsLinked).Select(static row => row.Provider));
        }

        /// <summary>Pairs a provider with the account's link to it, when there is one.</summary>
        private static ProviderRow ToRow(string provider, IList<UserLoginInfo> logins)
        {
            UserLoginInfo? login = logins.FirstOrDefault(
                candidate => string.Equals(candidate.LoginProvider, provider, StringComparison.OrdinalIgnoreCase));
            if (login is null)
            {
                return new ProviderRow(provider, Account: null, Key: null);
            }

            // The stored name is the linked address for anything linked since that was captured,
            // and the provider's own name for everything older. The latter is what the row's title
            // already says, so it carries no information and is dropped.
            string? account = string.Equals(login.ProviderDisplayName, provider, StringComparison.OrdinalIgnoreCase)
                ? null
                : login.ProviderDisplayName;

            return new ProviderRow(provider, account, login.ProviderKey);
        }
    }
}
