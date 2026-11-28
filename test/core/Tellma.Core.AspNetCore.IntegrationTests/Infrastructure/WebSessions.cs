// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Security.Claims;
using Tellma.Core.AspNetCore.Authentication;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Sessions;
using Tellma.Distro.Acme;
using Tellma.Distro.Acme.TestSupport;

namespace Tellma.Core.AspNetCore.IntegrationTests.Infrastructure
{
    /// <summary>
    ///     Signs a member in without a browser round trip: the ticket the sign-in's callback would
    ///     store goes into the host's session store, and the cookie that references it is protected
    ///     by the host's own cookie scheme, so every request after it runs the real session
    ///     validation, refresh check and principal binding.
    /// </summary>
    internal static class WebSessions
    {
        /// <summary>The claim the cookie scheme names a stored session's key by.</summary>
        private const string SessionKeyClaim = "Microsoft.AspNetCore.Authentication.Cookies-SessionId";

        /// <summary>
        ///     Stores a session for a member and answers the <c>Cookie</c> header value that carries it.
        /// </summary>
        /// <param name="host">The host whose session store and cookie scheme the session belongs to.</param>
        /// <param name="member">The member signing in.</param>
        /// <param name="assurance">
        ///     The authentication context class the sign-in reached, such as the step-up bar's; null for
        ///     a sign-in that states none.
        /// </param>
        /// <param name="authenticatedAgo">How long ago the member authenticated; now by default.</param>
        /// <returns>The cookie header value.</returns>
        public static async Task<string> SignInAsync(AcmeHost host, Member member, string? assurance = null, TimeSpan? authenticatedAgo = null)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(member);

            // --- The principal, as the OpenID Connect callback keeps it ---
            DateTimeOffset now = host.Services.GetRequiredService<TimeProvider>().GetUtcNow();
            DateTimeOffset authenticatedAt = now - (authenticatedAgo ?? TimeSpan.Zero);
            List<Claim> claims =
            [
                new(TellmaClaimTypes.Subject, member.Subject),
                new(TellmaClaimTypes.AuthorizedParty, AcmeComposition.Slug),
                new(TellmaClaimTypes.Name, member.Name),
                new(TellmaClaimTypes.Email, member.Email),
                new("locale", "en"),
                new(TellmaClaimTypes.SessionId, Guid.NewGuid().ToString("N")),
                new(TellmaClaimTypes.AuthenticationTime, authenticatedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
            ];
            if (assurance is not null)
            {
                claims.Add(new Claim(TellmaClaimTypes.AuthenticationContextClass, assurance));
            }

            // --- The properties, as the sign-in sets them: persistent, stamped, with tokens that need no refresh for an hour ---
            TellmaSessionOptions options = host.Services.GetRequiredService<IOptions<TellmaSessionOptions>>().Value;
            AuthenticationProperties properties = new() { IsPersistent = true, IssuedUtc = now };
            SessionProperties.SetCreatedAt(properties, now);
            properties.ExpiresUtc = SessionProperties.ExpiryAt(now, now, options.IdleLifetime, options.AbsoluteLifetime);
            properties.StoreTokens(
            [
                new AuthenticationToken { Name = "access_token", Value = "suite-access-token" },
                new AuthenticationToken { Name = "token_type", Value = "Bearer" },
                new AuthenticationToken { Name = SessionProperties.ExpiresAtToken, Value = (now + TimeSpan.FromHours(1)).ToString("o", CultureInfo.InvariantCulture) },
                new AuthenticationToken { Name = "refresh_token", Value = "suite-refresh-token" },
            ]);

            // --- The row in the catalog's session table, then the cookie that references it ---
            ClaimsPrincipal principal = new(new ClaimsIdentity(claims, TellmaAuthentication.SessionScheme, TellmaClaimTypes.Subject, null));
            string key = await host.Services.GetRequiredService<ICatalogSessionStore>()
                .StoreAsync(new AuthenticationTicket(principal, properties, TellmaAuthentication.SessionScheme), new DefaultHttpContext(), TestContext.Current.CancellationToken);
            CookieAuthenticationOptions cookie = host.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(TellmaAuthentication.SessionScheme);
            ClaimsPrincipal reference = new(new ClaimsIdentity([new Claim(SessionKeyClaim, key)], cookie.ClaimsIssuer));
            string value = cookie.TicketDataFormat.Protect(new AuthenticationTicket(reference, new AuthenticationProperties(), TellmaAuthentication.SessionScheme));
            return $"{cookie.Cookie.Name}={value}";
        }

        /// <summary>
        ///     Stores a session for a member that meets the host's step-up bar, as a sign-in stepped up
        ///     to it moments ago.
        /// </summary>
        /// <param name="host">The host.</param>
        /// <param name="member">The member.</param>
        /// <returns>The cookie header value.</returns>
        public static Task<string> SteppedUpAsync(AcmeHost host, Member member)
        {
            ArgumentNullException.ThrowIfNull(host);
            string bar = host.Services.GetRequiredService<IOptions<TellmaSessionOptions>>().Value.StepUp.Acr;
            return SignInAsync(host, member, bar);
        }
    }
}
