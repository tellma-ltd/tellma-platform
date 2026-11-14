// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Tellma.Core.AspNetCore.Authentication;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Problems;

namespace Tellma.Core.AspNetCore.Sessions
{
    /// <summary>
    ///     The session cookie's events: a persistent cookie whose expiry is the row's, the absolute
    ///     lifetime, the refresh-anchored liveness, no renewal by background requests, the profile cookie
    ///     cleared with the session, and answers that never redirect.
    /// </summary>
    internal sealed class TellmaSessionEvents : CookieAuthenticationEvents
    {
        private readonly SessionRefresher _refresher;
        private readonly TellmaSessionOptions _options;
        private readonly TimeProvider _time;
        private readonly bool _secureCookies;

        /// <summary>Creates the events.</summary>
        /// <param name="refresher">Refreshes sessions whose access token is about to expire.</param>
        /// <param name="options">The session options.</param>
        /// <param name="time">The clock.</param>
        /// <param name="cookies">Whether the host's cookies are restricted to HTTPS.</param>
        public TellmaSessionEvents(SessionRefresher refresher, IOptions<TellmaSessionOptions> options, TimeProvider time, SessionCookieSecurity cookies)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(cookies);
            _refresher = refresher;
            _options = options.Value;
            _time = time;
            _secureCookies = cookies.Secure;

            // --- An API answers 401 and 403; it never redirects to a sign-in page ---
            OnRedirectToLogin = static context => ProblemWriter.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized, "unauthenticated");
            OnRedirectToAccessDenied = static context => ProblemWriter.WriteAsync(context.HttpContext, StatusCodes.Status403Forbidden, "forbidden");
            OnRedirectToLogout = static context =>
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            };
            OnRedirectToReturnUrl = static context =>
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            };
        }

        /// <inheritdoc />
        public override Task SigningIn(CookieSigningInContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // --- A new session: persistent, its absolute lifetime running from now ---
            DateTimeOffset now = _time.GetUtcNow();
            AuthenticationProperties properties = context.Properties;
            properties.IsPersistent = true;
            SessionProperties.SetCreatedAt(properties, now);
            properties.IssuedUtc = now;
            properties.ExpiresUtc = SessionProperties.ExpiryAt(now, now, _options.IdleLifetime, _options.AbsoluteLifetime);
            return base.SigningIn(context);
        }

        /// <inheritdoc />
        public override Task CheckSlidingExpiration(CookieSlidingExpirationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // --- Background activity keeps neither cookie alive; anything else renews at half the window ---
            if (BackgroundRequests.IsBackground(context.HttpContext))
            {
                context.ShouldRenew = false;
            }
            else if (context.ShouldRenew)
            {
                PrepareRenewal(context.Properties);
            }

            return base.CheckSlidingExpiration(context);
        }

        /// <inheritdoc />
        public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            HttpContext http = context.HttpContext;
            AuthenticationProperties properties = context.Properties;
            DateTimeOffset now = _time.GetUtcNow();

            // --- Only a ticket the store holds, under its absolute lifetime ---
            string? key = CatalogSessionStore.CurrentKey(http);
            string? handle = SessionProperties.GetHandle(properties);
            string? subject = context.Principal?.FindFirst(TellmaClaimTypes.Subject)?.Value;
            DateTimeOffset? created = SessionProperties.GetCreatedAt(properties);
            DateTimeOffset? expiry = SessionProperties.GetAccessTokenExpiry(properties);
            if (key is null || handle is null || subject is null || created is null || expiry is null
                || now >= created.Value + _options.AbsoluteLifetime)
            {
                await RejectAsync(context).ConfigureAwait(false);
                return;
            }

            // --- An access token with time to live needs nothing ---
            if (expiry.Value - now > _options.RefreshSkew)
            {
                return;
            }

            // --- The refresh, shared with every request of the session on this instance ---
            RefreshOutcome outcome = await _refresher.RefreshAsync(
                new RefreshRequest(key, handle, subject, SessionProperties.GetTokensVersion(properties) ?? 0, expiry.Value)).ConfigureAwait(false);
            switch (outcome.Kind)
            {
                case RefreshOutcomeKind.Refreshed:
                case RefreshOutcomeKind.Adopted:
                    Apply(context, outcome.Session!);
                    break;

                case RefreshOutcomeKind.Rejected:
                    await RejectAsync(context).ConfigureAwait(false);
                    break;

                case RefreshOutcomeKind.Deferred:
                default:
                    // The session serves on its current tokens until the authority answers again.
                    break;
            }
        }

        /// <inheritdoc />
        public override Task SigningOut(CookieSigningOutContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            ProfileCookie.Delete(context.Response, _secureCookies);
            return base.SigningOut(context);
        }

        /// <summary>
        ///     Moves a ticket's issue and expiry so the handler's renewal lands on the row's next expiry:
        ///     the idle window from now, capped by the absolute lifetime.
        /// </summary>
        /// <param name="properties">The ticket's properties.</param>
        private void PrepareRenewal(AuthenticationProperties properties)
        {
            // The handler renews to now plus the ticket's own span, so the span is set to end at the cap.
            DateTimeOffset now = _time.GetUtcNow();
            DateTimeOffset created = SessionProperties.GetCreatedAt(properties) ?? properties.IssuedUtc ?? now;
            properties.IssuedUtc = now;
            properties.ExpiresUtc = SessionProperties.ExpiryAt(now, created, _options.IdleLifetime, _options.AbsoluteLifetime);
        }

        /// <summary>Takes a refreshed or adopted session as the request's, renewing it unless the request is background.</summary>
        /// <param name="context">The event's context.</param>
        /// <param name="session">The session.</param>
        private void Apply(CookieValidatePrincipalContext context, StoredSession session)
        {
            // The handler renews from the ticket it read, so its properties take the stored ticket's
            // items: the new tokens and the tokens version among them. Every request that joined one
            // refresh holds the same stored session, so each takes its own copy of the principal, which
            // a claims transformation may then change without reaching the other requests.
            context.ReplacePrincipal(new ClaimsPrincipal(session.Ticket.Principal.Identities.Select(static identity => identity.Clone())));
            AuthenticationProperties properties = context.Properties;
            properties.Items.Clear();
            foreach (KeyValuePair<string, string?> item in session.Ticket.Properties.Items)
            {
                properties.Items[item.Key] = item.Value;
            }

            // A background request refreshes too, so its tokens stay valid; it only renews no cookie.
            if (!BackgroundRequests.IsBackground(context.HttpContext))
            {
                PrepareRenewal(properties);
                context.ShouldRenew = true;
            }
        }

        /// <summary>Refuses the request's session and signs it out: the row deleted, both cookies cleared.</summary>
        /// <param name="context">The event's context.</param>
        /// <returns>A task that completes when the session is signed out.</returns>
        private static async Task RejectAsync(CookieValidatePrincipalContext context)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(TellmaAuthentication.SessionScheme).ConfigureAwait(false);
        }
    }
}
