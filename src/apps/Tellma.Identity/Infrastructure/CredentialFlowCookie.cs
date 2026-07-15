// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>
    ///     A short-lived, Data-Protection-encrypted cookie that carries the identity of the user
    ///     an unauthenticated credential ceremony (invitation accept, recovery, dev bootstrap) is
    ///     scoped to. It is established when a single-use link/pass is consumed — proof of
    ///     ownership — and lets the passkey and password pages act for that user without a session.
    /// </summary>
    public static class CredentialFlowCookie
    {
        /// <summary>The cookie name.</summary>
        public const string Name = "tellma.identity.credflow";

        /// <summary>The Data Protection purpose isolating this cookie's payload.</summary>
        private const string Purpose = "Tellma.Identity.CredentialFlow.v1";

        /// <summary>How long a credential flow stays valid.</summary>
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(20);

        /// <summary>Issues the flow cookie for a user after an ownership proof is verified.</summary>
        /// <param name="context">The request context.</param>
        /// <param name="userId">The user the ceremony may act for.</param>
        public static void Issue(HttpContext context, string userId)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentException.ThrowIfNullOrWhiteSpace(userId);

            IDataProtector protector = GetProtector(context);
            string payload = protector.Protect($"{userId}|{DateTimeOffset.UtcNow.Add(Lifetime).ToUnixTimeSeconds()}");

            context.Response.Cookies.Append(Name, payload, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = context.Request.IsHttps,
                MaxAge = Lifetime,
                IsEssential = true,
            });
        }

        /// <summary>Reads the user id the current credential flow is scoped to.</summary>
        /// <param name="context">The request context.</param>
        /// <returns>The user id, or null when absent, tampered, or expired.</returns>
        public static string? GetUserId(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (!context.Request.Cookies.TryGetValue(Name, out string? payload) || string.IsNullOrEmpty(payload))
            {
                return null;
            }

            try
            {
                string[] parts = GetProtector(context).Unprotect(payload).Split('|', 2);
                return parts.Length != 2
                    || !long.TryParse(parts[1], out long expiresUnix)
                    || DateTimeOffset.FromUnixTimeSeconds(expiresUnix) < DateTimeOffset.UtcNow
                    ? null
                    : parts[0];
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                return null;
            }
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
