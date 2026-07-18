// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Tellma.Identity.Data;
using Tellma.Identity.Infrastructure;

namespace Tellma.Identity.Controllers
{
    /// <summary>
    ///     Serves the WebAuthn options JSON the browser needs to start a passkey ceremony. The
    ///     resulting credential is always posted back through a normal page form (hidden field),
    ///     so these endpoints only produce options — they never complete the ceremony. The
    ///     challenge state is carried by the framework in a short-lived cookie between the options
    ///     call and the attestation/assertion.
    /// </summary>
    /// <param name="signInManager">The Identity sign-in manager (passkey options APIs).</param>
    /// <param name="userManager">The Identity user manager.</param>
    public sealed class PasskeyCeremonyController(
        SignInManager<TellmaIdentityUser> signInManager,
        UserManager<TellmaIdentityUser> userManager) : Controller
    {
        /// <summary>
        ///     Produces creation (attestation) options for enrolling a new passkey. The subject is
        ///     the authenticated user, or — during invitation/recovery — the user identified by the
        ///     flow context established when the single-use link was consumed.
        /// </summary>
        /// <returns>The WebAuthn creation options JSON.</returns>
        [HttpPost("Identity/api/passkey/creation-options")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreationOptions()
        {
            TellmaIdentityUser? user = await ResolveCeremonyUserAsync();
            if (user is null)
            {
                return Forbid();
            }

            PasskeyUserEntity entity = new()
            {
                Id = user.Id,
                Name = user.Email ?? user.Id,
                DisplayName = user.DisplayName ?? user.Email ?? user.Id,
            };

            string options = await signInManager.MakePasskeyCreationOptionsAsync(entity);
            return Content(options, "application/json");
        }

        /// <summary>
        ///     Produces request (assertion) options for signing in with a passkey. The ceremony is
        ///     always discoverable-credential (the authenticator picks the account): scoping by a
        ///     caller-supplied email on this anonymous endpoint would leak account existence and a
        ///     user's credential ids, and §8.1's resident-key + conditional-UI model makes email
        ///     scoping only a UX filter with no security value.
        /// </summary>
        /// <returns>The WebAuthn request options JSON.</returns>
        [AllowAnonymous]
        [HttpPost("Identity/api/passkey/assertion-options")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssertionOptions()
        {
            string options = await signInManager.MakePasskeyRequestOptionsAsync(user: null);
            return Content(options, "application/json");
        }

        /// <summary>Resolves the user a passkey-creation ceremony is for.</summary>
        private async Task<TellmaIdentityUser?> ResolveCeremonyUserAsync()
        {
            // Authenticated users (Account &amp; Security) enroll for themselves.
            if (User.Identity?.IsAuthenticated == true)
            {
                return await userManager.GetUserAsync(User);
            }

            // Unauthenticated enrollment (invitation, recovery, dev bootstrap) is scoped to the
            // user id the consumed single-use link established in the flow cookie.
            string? userId = CredentialFlowCookie.GetUserId(HttpContext);
            return userId is null ? null : await userManager.FindByIdAsync(userId);
        }
    }
}
