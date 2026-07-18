// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>
    ///     The short-lived login-flow cookie that binds an email one-time code to the browser
    ///     session that requested it: a code phished into a different browser fails verification.
    /// </summary>
    public static class LoginFlowCookie
    {
        /// <summary>The cookie name.</summary>
        public const string Name = "tellma.identity.flow";

        /// <summary>How long a flow stays valid.</summary>
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

        /// <summary>Returns the current flow id, issuing a new one when none exists.</summary>
        /// <param name="context">The request context.</param>
        /// <returns>The flow id.</returns>
        public static string GetOrCreate(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (context.Request.Cookies.TryGetValue(Name, out string? existing)
                && !string.IsNullOrWhiteSpace(existing)
                && existing.Length <= 64)
            {
                return existing;
            }

            string id = Guid.NewGuid().ToString("N");
            context.Response.Cookies.Append(Name, id, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = context.Request.IsHttps,
                MaxAge = Lifetime,
                IsEssential = true,
            });

            return id;
        }

        /// <summary>Reads the current flow id without issuing one.</summary>
        /// <param name="context">The request context.</param>
        /// <returns>The flow id, or null when the browser carries none.</returns>
        public static string? Get(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            return context.Request.Cookies.TryGetValue(Name, out string? value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;
        }
    }
}
