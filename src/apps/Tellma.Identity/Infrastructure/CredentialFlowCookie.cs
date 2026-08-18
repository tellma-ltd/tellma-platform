// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>Why a credential-flow context was established, which bounds what it may authorize.</summary>
    public enum CredentialFlowPurpose
    {
        /// <summary>Invitation accept: the single-use link proves mailbox control.</summary>
        Invitation = 0,

        /// <summary>Admin-assisted recovery or break-glass bootstrap, whose only exit is a passkey.</summary>
        Recovery = 1,
    }

    /// <summary>A resolved credential-flow context.</summary>
    /// <param name="UserId">The user the ceremony may act for.</param>
    /// <param name="Purpose">Why the context exists.</param>
    /// <param name="ReturnUrl">Where to send the user once the ceremony completes; null for the
    ///     authority's own account page. Already validated when it was sealed in.</param>
    public sealed record CredentialFlowContext(
        string UserId, CredentialFlowPurpose Purpose, string? ReturnUrl = null);

    /// <summary>
    ///     A short-lived, Data-Protection-encrypted cookie that carries the identity of the user
    ///     an unauthenticated credential ceremony (invitation accept, recovery, dev bootstrap) is
    ///     scoped to, plus why it exists. It is established when a single-use link/pass is consumed
    ///     — proof of ownership — and lets the passkey page act for that user without a session. The
    ///     purpose narrows what else the context permits: only an invitation may link an external
    ///     login, while recovery and bootstrap have a passkey-only exit.
    /// </summary>
    public static class CredentialFlowCookie
    {
        /// <summary>The cookie name.</summary>
        public const string Name = "tellma.identity.credflow";

        /// <summary>The Data Protection purpose isolating this cookie's payload.</summary>
        private const string Purpose = "Tellma.Identity.CredentialFlow.v2";

        /// <summary>How long a credential flow stays valid.</summary>
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(20);

        /// <summary>Issues the flow cookie for a user after an ownership proof is verified.</summary>
        /// <param name="context">The request context.</param>
        /// <param name="userId">The user the ceremony may act for.</param>
        /// <param name="purpose">Why the context exists (bounds what it authorizes).</param>
        /// <param name="returnUrl">Where the completed ceremony sends the user, already validated.
        ///     Carried here rather than through the page's query string precisely because it may be
        ///     an absolute address: sealed in the cookie it is a value this server put there and
        ///     checked, where a query parameter would be whatever the browser was handed.</param>
        public static void Issue(
            HttpContext context, string userId, CredentialFlowPurpose purpose, string? returnUrl = null)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentException.ThrowIfNullOrWhiteSpace(userId);

            IDataProtector protector = GetProtector(context);
            string payload = protector.Protect(
                $"{userId}|{DateTimeOffset.UtcNow.Add(Lifetime).ToUnixTimeSeconds()}|{(int)purpose}|{returnUrl}");

            context.Response.Cookies.Append(Name, payload, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = context.Request.IsHttps,
                MaxAge = Lifetime,
                IsEssential = true,
            });
        }

        /// <summary>Reads the current credential-flow context (user and purpose).</summary>
        /// <param name="context">The request context.</param>
        /// <returns>The context, or null when absent, tampered, or expired.</returns>
        public static CredentialFlowContext? Read(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (!context.Request.Cookies.TryGetValue(Name, out string? payload) || string.IsNullOrEmpty(payload))
            {
                return null;
            }

            try
            {
                // Four fields now, three before the return url was added. Both are accepted so a
                // ceremony already under way when this deploys is not stranded — its invitation
                // link is single-use and has already been spent, so there is no retry to offer.
                string[] parts = GetProtector(context).Unprotect(payload).Split('|', 4);
                return parts.Length < 3
                    || !long.TryParse(parts[1], out long expiresUnix)
                    || DateTimeOffset.FromUnixTimeSeconds(expiresUnix) < DateTimeOffset.UtcNow
                    || !int.TryParse(parts[2], out int purpose)
                    ? null
                    : new CredentialFlowContext(
                        parts[0],
                        (CredentialFlowPurpose)purpose,
                        parts.Length == 4 && parts[3].Length > 0 ? parts[3] : null);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                return null;
            }
        }

        /// <summary>Reads the user id the current credential flow is scoped to, ignoring purpose.</summary>
        /// <param name="context">The request context.</param>
        /// <returns>The user id, or null when absent, tampered, or expired.</returns>
        public static string? GetUserId(HttpContext context)
        {
            return Read(context)?.UserId;
        }

        /// <summary>Clears the flow cookie once the ceremony completes.</summary>
        /// <param name="context">The request context.</param>
        public static void Clear(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            context.Response.Cookies.Delete(Name);
        }

        /// <summary>Resolves the request-scoped data protector for this cookie.</summary>
        private static IDataProtector GetProtector(HttpContext context)
        {
            IDataProtectionProvider provider = context.RequestServices.GetRequiredService<IDataProtectionProvider>();
            return provider.CreateProtector(Purpose);
        }
    }
}
