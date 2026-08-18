// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace Tellma.Identity.IntegrationTests.Infrastructure
{
    /// <summary>
    ///     Stands in for the round trip to Google or Microsoft, which a test cannot make.
    ///     <para>
    ///         The external-login callback reads what the provider asserted out of the Identity
    ///         external cookie — the scheme's handler writes it there and is finished. This writes
    ///         the same cookie directly, so everything the callback then does with it is the real
    ///         code path. Only the assertion is fabricated, which is precisely the part that has to
    ///         be: no test can hold a Google account.
    ///     </para>
    /// </summary>
    public sealed class ExternalProviderStub : IStartupFilter
    {
        /// <summary>The path that mints an assertion. Test-only, and never mounted in production.</summary>
        public const string Path = "/test/external-assertion";

        /// <summary>The property the sign-in manager reads the provider's name back out of.</summary>
        private const string LoginProviderKey = "LoginProvider";

        /// <summary>Builds the URL that asserts one provider identity.</summary>
        /// <param name="provider">The provider scheme, as the callback will see it.</param>
        /// <param name="key">The provider's stable subject for this identity.</param>
        /// <param name="email">The address the provider asserts, when it asserts one.</param>
        /// <param name="emailVerified">Whether the provider vouches for that address.</param>
        /// <returns>A relative URL to GET before the callback.</returns>
        public static string AssertionUrl(string provider, string key, string? email = null, bool emailVerified = true)
        {
            string url = $"{Path}?provider={Uri.EscapeDataString(provider)}&key={Uri.EscapeDataString(key)}";
            if (email is not null)
            {
                url += $"&email={Uri.EscapeDataString(email)}&verified={(emailVerified ? "true" : "false")}";
            }

            return url;
        }

        /// <inheritdoc />
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            ArgumentNullException.ThrowIfNull(next);

            return app =>
            {
                app.Use(static async (context, following) =>
                {
                    if (!context.Request.Path.Equals(Path, StringComparison.Ordinal))
                    {
                        await following(context);
                        return;
                    }

                    await WriteAssertionAsync(context);
                });

                next(app);
            };
        }

        /// <summary>Writes the external cookie the way a provider's handler would have.</summary>
        private static async Task WriteAssertionAsync(HttpContext context)
        {
            string provider = context.Request.Query["provider"].ToString();
            string key = context.Request.Query["key"].ToString();
            string? email = context.Request.Query["email"].ToString() is { Length: > 0 } value ? value : null;

            // The subject is the only claim the sign-in manager requires; the address and its
            // verification are what the linking rules read.
            List<Claim> claims = [new Claim(ClaimTypes.NameIdentifier, key)];
            if (email is not null)
            {
                claims.Add(new Claim(ClaimTypes.Email, email));
            }

            if (context.Request.Query["verified"] == "true")
            {
                // Rendered the way the framework renders a mapped JSON boolean, so a case-sensitive
                // comparison would fail here exactly as it fails against a real provider.
                claims.Add(new Claim("email_verified", "True"));
            }

            AuthenticationProperties properties = new();
            properties.Items[LoginProviderKey] = provider;

            await context.SignInAsync(
                IdentityConstants.ExternalScheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims, provider)),
                properties);

            context.Response.StatusCode = StatusCodes.Status204NoContent;
        }
    }
}
