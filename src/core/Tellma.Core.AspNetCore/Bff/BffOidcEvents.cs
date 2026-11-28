// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Globalization;
using System.Security.Claims;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Authentication;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Observability;
using Tellma.Core.AspNetCore.Sessions;
using Tellma.Core.Tenancy;

namespace Tellma.Core.AspNetCore.Bff
{
    /// <summary>
    ///     The sign-in scheme's events: the callback addresses from the public origin, the sign-in's
    ///     constraints forwarded to the identity server, the principal checked and trimmed to the claims
    ///     the platform keeps, the post-sign-in landing, the end-session address captured for
    ///     <c>POST /bff/logout</c> instead of followed, and the identity server's return from it sent
    ///     to the signed-out page.
    /// </summary>
    /// <param name="directory">The membership hints, for the landing.</param>
    /// <param name="configuration">The application's configuration, which holds the public origin.</param>
    /// <param name="logger">The logger.</param>
    internal sealed class BffOidcEvents(
        ITenantMembershipDirectory directory,
        IConfiguration configuration,
        ILogger<BffOidcEvents> logger) : OpenIdConnectEvents
    {
        /// <summary>The authentication property carrying the acr values a sign-in requests.</summary>
        public const string AcrValuesParameter = OpenIdConnectParameterNames.AcrValues;

        /// <summary>The authentication property carrying the sign-in page's language.</summary>
        public const string UiLocalesParameter = OpenIdConnectParameterNames.UiLocales;

        /// <summary>The authentication property and request parameter carrying the allowed sign-in methods.</summary>
        public const string AllowedMethodsParameter = "tellma_allowed_methods";

        /// <summary>The claims a session principal keeps of the identity token.</summary>
        internal static readonly HashSet<string> KeptClaims = new(StringComparer.Ordinal)
        {
            TellmaClaimTypes.Subject,
            TellmaClaimTypes.SessionId,
            TellmaClaimTypes.Name,
            TellmaClaimTypes.Email,
            TellmaClaimTypes.Locale,
            TellmaClaimTypes.AuthenticationContextClass,
            TellmaClaimTypes.AuthenticationMethods,
            TellmaClaimTypes.AuthenticationTime,
            TellmaClaimTypes.AcrAuthenticationTime,
        };

        /// <summary>The request item holding the end-session address a sign-out captured.</summary>
        private static readonly object EndSessionItem = new();

        /// <summary>The end-session address the request's global sign-out captured.</summary>
        /// <param name="context">The request.</param>
        /// <returns>The address, or null when none was captured.</returns>
        public static string? CapturedEndSession(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return context.Items.TryGetValue(EndSessionItem, out object? url) ? url as string : null;
        }

        /// <inheritdoc />
        public override Task RedirectToIdentityProvider(RedirectContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            OpenIdConnectMessage message = context.ProtocolMessage;

            // --- The callback, from the public origin rather than whatever host the request came through ---
            if (PublicUri(context.Options.CallbackPath) is string callback)
            {
                message.RedirectUri = callback;
            }

            // --- The sign-in's constraints, which the pushed request carries to the identity server ---
            foreach (string name in (string[])[AcrValuesParameter, UiLocalesParameter, AllowedMethodsParameter])
            {
                if (context.Properties.GetParameter<string>(name) is { Length: > 0 } value)
                {
                    message.SetParameter(name, value);
                }
            }

            return base.RedirectToIdentityProvider(context);
        }

        /// <inheritdoc />
        public override Task TokenValidated(TokenValidatedContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // --- A session names its user and the identity server's session, or it is refused ---
            ClaimsPrincipal? principal = context.Principal;
            string? subject = principal?.FindFirst(TellmaClaimTypes.Subject)?.Value;
            string? sid = principal?.FindFirst(TellmaClaimTypes.SessionId)?.Value;
            if (principal is null || string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(sid))
            {
                context.Fail("The identity token names no subject or no session.");
                return Task.CompletedTask;
            }

            try
            {
                TenantMembershipDirectory.ValidateSubject(subject, nameof(subject));
            }
            catch (ArgumentException)
            {
                context.Fail("The identity token's subject is not printable ASCII of at most 255 characters.");
                return Task.CompletedTask;
            }

            // --- The principal keeps the claims the platform reads, and nothing else of the token ---
            var source = (ClaimsIdentity)principal.Identity!;
            ClaimsIdentity kept = new(
                source.Claims.Where(static claim => KeptClaims.Contains(claim.Type)),
                source.AuthenticationType,
                TellmaClaimTypes.Subject,
                roleType: null);
            context.Principal = new ClaimsPrincipal(kept);
            return base.TokenValidated(context);
        }

        /// <inheritdoc />
        public override async Task TicketReceived(TicketReceivedContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // --- The sign-in replaces whatever session the browser carried ---
            CatalogSessionStore.MarkSignIn(context.HttpContext);

            // --- The landing: a sign-in that returns to the root goes to the user's one live tenant ---
            if (context.ReturnUri is null or "/" && context.Principal?.FindFirst(TellmaClaimTypes.Subject)?.Value is string subject)
            {
                try
                {
                    IReadOnlyList<TenantMembership> memberships = await directory.ListAsync(subject, context.HttpContext.RequestAborted).ConfigureAwait(false);
                    if (TenantLanding.SingleLiveTenant(memberships) is int tenantId)
                    {
                        context.ReturnUri = "/" + tenantId.ToString(CultureInfo.InvariantCulture);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !context.HttpContext.RequestAborted.IsCancellationRequested)
                {
                    // The application's launch redirect decides from the root instead.
                    HostLog.BffLandingFailed(logger, exception);
                }
            }

            await base.TicketReceived(context).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override Task RedirectToIdentityProviderForSignOut(RedirectContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            OpenIdConnectMessage message = context.ProtocolMessage;
            if (PublicUri(context.Options.SignedOutCallbackPath) is string callback)
            {
                message.PostLogoutRedirectUri = callback;
            }

            // The caller is a fetch, which cannot follow the identity server's redirect: the address is
            // handed back to it instead, and the handler writes nothing.
            if (!string.IsNullOrEmpty(message.IssuerAddress))
            {
                context.HttpContext.Items[EndSessionItem] = message.CreateLogoutRequestUrl();
            }

            context.HandleResponse();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override Task SignedOutCallbackRedirect(RemoteSignOutContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // The captured end-session address carries no state (the handler adds it only after the
            // event above returns), so the handler alone would answer the return with an empty 200.
            // Every return lands on the signed-out page instead, whatever the query carries, which
            // also leaves the query no way to name another destination.
            context.Response.Redirect(TellmaAuthenticationComposition.SignedOutPath);
            context.HandleResponse();
            return Task.CompletedTask;
        }

        /// <summary>An absolute URI on the public origin.</summary>
        /// <param name="path">The path.</param>
        /// <returns>The URI, or null when no public origin is configured.</returns>
        private string? PublicUri(PathString path)
        {
            return configuration[HostConfigurationKeys.PublicOrigin] is { Length: > 0 } origin ? origin + path.Value : null;
        }
    }
}
