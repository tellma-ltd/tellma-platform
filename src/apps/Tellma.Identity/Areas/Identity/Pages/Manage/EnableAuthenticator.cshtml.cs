// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using System.Globalization;
using System.Text;
using Tellma.Identity.Data;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Services.Audit;

namespace Tellma.Identity.Areas.Identity.Pages.Manage
{
    /// <summary>
    ///     Enrolls an authenticator-app TOTP second factor: the server renders the
    ///     <c>otpauth://</c> URI as a QR code (no client-side QR library, CSP-clean), verifies the
    ///     first code, enables two-factor, and shows recovery codes once.
    /// </summary>
    /// <param name="userManager">The Identity user manager.</param>
    /// <param name="auditLogger">Audit emission.</param>
    /// <param name="localizer">UI strings.</param>
    [Authorize]
    public sealed class EnableAuthenticatorModel(
        UserManager<TellmaIdentityUser> userManager,
        IAuditLogger auditLogger,
        IStringLocalizer<SharedResources> localizer) : PageModel
    {
        /// <summary>The submitted verification code.</summary>
        [BindProperty]
        public string? Code { get; set; }

        /// <summary>The shared secret, formatted for manual entry.</summary>
        public string SharedKey { get; private set; } = string.Empty;

        /// <summary>The QR code encoding the <c>otpauth://</c> URI, as an SVG data URI.</summary>
        public string QrCodeDataUri { get; private set; } = string.Empty;

        /// <summary>The one-time recovery codes shown after enabling.</summary>
        public IReadOnlyList<string>? RecoveryCodes { get; private set; }

        /// <summary>Prepares the enrollment (key + QR).</summary>
        /// <returns>The page.</returns>
        public async Task<IActionResult> OnGetAsync()
        {
            await PrepareAsync();
            return Page();
        }

        /// <summary>Verifies the first code, enables two-factor, and issues recovery codes.</summary>
        /// <returns>The page (with recovery codes on success, an error otherwise).</returns>
        public async Task<IActionResult> OnPostAsync()
        {
            TellmaIdentityUser user = (await userManager.GetUserAsync(User))!;

            string code = (Code ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal);
            bool valid = await userManager.VerifyTwoFactorTokenAsync(
                user, userManager.Options.Tokens.AuthenticatorTokenProvider, code);
            if (!valid)
            {
                await PrepareAsync();
                ModelState.AddModelError(string.Empty, localizer["InvalidCode"].Value);
                return Page();
            }

            await userManager.SetTwoFactorEnabledAsync(user, true);
            RecoveryCodes = [.. await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10) ?? []];

            await auditLogger.LogAsync(new AuditEventEntry
            {
                Action = AuditActions.TotpEnabled,
                Subject = user.Id,
                Outcome = "success",
            });

            return Page();
        }

        /// <summary>Loads (generating if needed) the authenticator key and builds the QR code.</summary>
        private async Task PrepareAsync()
        {
            TellmaIdentityUser user = (await userManager.GetUserAsync(User))!;

            string? key = await userManager.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrEmpty(key))
            {
                await userManager.ResetAuthenticatorKeyAsync(user);
                key = await userManager.GetAuthenticatorKeyAsync(user);
            }

            SharedKey = FormatKey(key!);
            string email = await userManager.GetEmailAsync(user) ?? user.Id;
            string uri = string.Create(
                CultureInfo.InvariantCulture,
                $"otpauth://totp/Tellma:{Uri.EscapeDataString(email)}?secret={key}&issuer=Tellma&digits=6");
            QrCodeDataUri = QrCodeSvg.ToSvgDataUri(uri);
        }

        /// <summary>Groups the shared key into readable four-character chunks.</summary>
        private static string FormatKey(string key)
        {
            StringBuilder builder = new();
            for (int i = 0; i < key.Length; i += 4)
            {
                if (i > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(key.AsSpan(i, Math.Min(4, key.Length - i)));
            }

            return builder.ToString().ToUpperInvariant();
        }
    }
}
