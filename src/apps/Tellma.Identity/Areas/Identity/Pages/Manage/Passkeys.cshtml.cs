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

namespace Tellma.Identity.Areas.Identity.Pages.Manage
{
    /// <summary>
    ///     The self-service passkey manager: list enrolled credentials with their synced vs.
    ///     device-bound status, add a new one, and remove one (never the last sign-in method).
    /// </summary>
    /// <param name="userManager">The Identity user manager.</param>
    /// <param name="auditLogger">Audit emission.</param>
    [Authorize]
    public sealed class PasskeysModel(
        UserManager<TellmaIdentityUser> userManager,
        IAuditLogger auditLogger) : PageModel
    {
        /// <summary>A passkey shown in the list.</summary>
        /// <param name="CredentialId">Base64url credential id (round-tripped for removal).</param>
        /// <param name="Label">A short display label for the credential.</param>
        /// <param name="IsDeviceBound">Whether the credential is device-bound (non-synced).</param>
        public sealed record PasskeyView(string CredentialId, string Label, bool IsDeviceBound);

        /// <summary>The user's enrolled passkeys.</summary>
        public IReadOnlyList<PasskeyView> Passkeys { get; private set; } = [];

        /// <summary>An informational banner, when any.</summary>
        public string? StatusMessage { get; private set; }

        /// <summary>Loads the user's passkeys.</summary>
        /// <returns>The page.</returns>
        public async Task<IActionResult> OnGetAsync()
        {
            await LoadAsync();
            return Page();
        }

        /// <summary>Removes a passkey, refusing to remove the user's only sign-in credential.</summary>
        /// <param name="credentialId">The base64url credential id to remove.</param>
        /// <returns>The refreshed page.</returns>
        public async Task<IActionResult> OnPostRemoveAsync(string credentialId)
        {
            TellmaIdentityUser user = (await userManager.GetUserAsync(User))!;

            IList<UserPasskeyInfo> passkeys = await userManager.GetPasskeysAsync(user);
            bool hasOtherFactor = passkeys.Count > 1
                || await userManager.GetLoginsAsync(user) is { Count: > 0 }
                || await userManager.HasPasswordAsync(user);
            if (!hasOtherFactor)
            {
                StatusMessage = "You cannot remove your only sign-in method.";
                await LoadAsync();
                return Page();
            }

            byte[] id = Base64UrlDecode(credentialId);
            await userManager.RemovePasskeyAsync(user, id);
            await auditLogger.LogAsync(new AuditEventEntry
            {
                Action = AuditActions.PasskeyRemoved,
                Subject = user.Id,
                Outcome = "success",
            });

            await LoadAsync();
            return Page();
        }

        /// <summary>Loads the current user's passkeys into <see cref="Passkeys" />.</summary>
        private async Task LoadAsync()
        {
            TellmaIdentityUser user = (await userManager.GetUserAsync(User))!;
            IList<UserPasskeyInfo> passkeys = await userManager.GetPasskeysAsync(user);

            Passkeys = [.. passkeys.Select(static passkey => new PasskeyView(
                Base64UrlEncode(passkey.CredentialId),
                passkey.Name ?? Base64UrlEncode(passkey.CredentialId)[..8],
                IsDeviceBound: !passkey.IsBackedUp))];
        }

        /// <summary>Encodes a credential id as base64url.</summary>
        private static string Base64UrlEncode(byte[] value)
        {
            return Convert.ToBase64String(value).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        /// <summary>Decodes a base64url credential id.</summary>
        private static byte[] Base64UrlDecode(string value)
        {
            string padded = value.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
            return Convert.FromBase64String(padded);
        }
    }
}
