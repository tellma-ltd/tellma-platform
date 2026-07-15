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
        /// <summary>Invitation accept: the single-use link proves mailbox control (§8.4).</summary>
        Invitation = 0,

        /// <summary>Admin-assisted recovery or break-glass bootstrap: a passkey-only exit (§10.3–§10.4).</summary>
        Recovery = 1,
    }

    /// <summary>A resolved credential-flow context.</summary>
    /// <param name="UserId">The user the ceremony may act for.</param>
    /// <param name="Purpose">Why the context exists.</param>
    public sealed record CredentialFlowContext(string UserId, CredentialFlowPurpose Purpose);

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
        public static void Issue(HttpContext context, string userId, CredentialFlowPurpose purpose)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentException.ThrowIfNullOrWhiteSpace(userId);

            IDataProtector protector = GetProtector(context);
            string payload = protector.Protect(
                $"{userId}|{DateTimeOffset.UtcNow.Add(Lifetime).ToUnixTimeSeconds()}|{(int)purpose}");

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
                string[] parts = GetProtector(context).Unprotect(payload).Split('|', 3);
                return parts.Length != 3
                    || !long.TryParse(parts[1], out long expiresUnix)
                    || DateTimeOffset.FromUnixTimeSeconds(expiresUnix) < DateTimeOffset.UtcNow
                    || !int.TryParse(parts[2], out int purpose)
                    ? null
                    : new CredentialFlowContext(parts[0], (CredentialFlowPurpose)purpose);
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
