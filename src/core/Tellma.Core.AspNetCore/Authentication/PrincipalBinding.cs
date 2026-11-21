// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using System.Globalization;
using System.Security.Claims;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.AspNetCore.Authentication
{
    /// <summary>The principal part of a request context: who is calling, as the authenticated principal says.</summary>
    /// <param name="Kind">The principal's kind.</param>
    /// <param name="Subject">The user's subject, or the service account's client id.</param>
    /// <param name="ClientId">The client the credential was issued to.</param>
    /// <param name="SessionId">The identity server's session.</param>
    /// <param name="SessionHandle">The cookie session's handle; null on a bearer token.</param>
    /// <param name="Assurance">The authentication context the session reached.</param>
    internal sealed record PrincipalPart(
        PrincipalKind Kind,
        string? Subject,
        string? ClientId,
        string? SessionId,
        string? SessionHandle,
        AuthenticationAssurance? Assurance)
    {
        /// <summary>No authenticated caller.</summary>
        public static PrincipalPart Anonymous { get; } = new(PrincipalKind.Anonymous, null, null, null, null, null);
    }

    /// <summary>
    ///     Derives the principal part from what the endpoint's policy authenticated. A cookie session
    ///     is a user. A bearer token is what its <c>tellma_kind</c> claim says, or else a user when it
    ///     carries <c>auth_time</c> (a person signed in through a tool) and a service account when it
    ///     does not. Anything the derivation cannot place is anonymous, which every tenant refuses.
    /// </summary>
    internal static class PrincipalBinding
    {
        /// <summary>Derives the principal part of a request.</summary>
        /// <param name="context">The request, past authorization.</param>
        /// <returns>The principal part.</returns>
        public static async Task<PrincipalPart> BindAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // The authorization middleware records which schemes the endpoint's policy authenticated.
            AuthenticateResult? result = context.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult;
            if (result is not { Succeeded: true, Ticket: { } ticket } || context.User.Identity?.IsAuthenticated != true)
            {
                return PrincipalPart.Anonymous;
            }

            ClaimsPrincipal user = context.User;
            switch (ticket.AuthenticationScheme)
            {
                case TellmaAuthentication.SessionScheme:
                    // The handle rides on the cookie ticket's own properties, which the authorization's
                    // merged ticket does not carry; the handler answers again from its per-request cache.
                    AuthenticateResult session = await context.AuthenticateAsync(TellmaAuthentication.SessionScheme).ConfigureAwait(false);
                    string? handle = session.Properties?.Items.TryGetValue(TellmaAuthentication.SessionHandleProperty, out string? stamped) == true ? stamped : null;
                    return FromUser(user, PrincipalKind.User, handle);

                case TellmaAuthentication.BearerScheme:
                    return FromBearer(user);

                default:
                    // A policy over several schemes, or one the platform does not define: not a caller
                    // the platform can name.
                    return PrincipalPart.Anonymous;
            }
        }

        /// <summary>Derives the principal part of a bearer token.</summary>
        /// <param name="user">The token's principal.</param>
        /// <returns>The principal part.</returns>
        internal static PrincipalPart FromBearer(ClaimsPrincipal user)
        {
            // The claim wins; the sign-in time stands in for a token without it. An unknown kind is a
            // token the platform cannot place.
            return user.FindFirst(TellmaClaimTypes.Kind)?.Value switch
            {
                TellmaClaimTypes.IndividualKind => FromUser(user, PrincipalKind.User, handle: null),
                TellmaClaimTypes.ServiceKind => FromService(user),
                null when user.FindFirst(TellmaClaimTypes.AuthenticationTime) is not null => FromUser(user, PrincipalKind.User, handle: null),
                null => FromService(user),
                _ => PrincipalPart.Anonymous,
            };
        }

        /// <summary>The principal part of an individual: the subject is the user's.</summary>
        /// <param name="user">The principal.</param>
        /// <param name="kind">The kind.</param>
        /// <param name="handle">The session handle, if any.</param>
        /// <returns>The principal part, or anonymous when the principal names no subject.</returns>
        private static PrincipalPart FromUser(ClaimsPrincipal user, PrincipalKind kind, string? handle)
        {
            string? subject = NonEmpty(user.FindFirst(TellmaClaimTypes.Subject)?.Value);
            return subject is null
                ? PrincipalPart.Anonymous
                : new PrincipalPart(
                    kind,
                    subject,
                    ClientOf(user),
                    NonEmpty(user.FindFirst(TellmaClaimTypes.SessionId)?.Value),
                    handle,
                    new AuthenticationAssurance(
                        NonEmpty(user.FindFirst(TellmaClaimTypes.AuthenticationContextClass)?.Value),
                        EpochTime(user, TellmaClaimTypes.AuthenticationTime),
                        EpochTime(user, TellmaClaimTypes.AcrAuthenticationTime)));
        }

        /// <summary>The principal part of a service account: the subject is its client id.</summary>
        /// <param name="user">The principal.</param>
        /// <returns>The principal part, or anonymous when the token names no client.</returns>
        private static PrincipalPart FromService(ClaimsPrincipal user)
        {
            string? client = ClientOf(user);
            return client is null
                ? PrincipalPart.Anonymous
                : new PrincipalPart(PrincipalKind.ServiceAccount, client, client, null, null, null);
        }

        /// <summary>The client a credential was issued to.</summary>
        /// <param name="user">The principal.</param>
        /// <returns>The client id, or null.</returns>
        private static string? ClientOf(ClaimsPrincipal user)
        {
            return NonEmpty(user.FindFirst(TellmaClaimTypes.AuthorizedParty)?.Value)
                ?? NonEmpty(user.FindFirst(TellmaClaimTypes.ClientId)?.Value);
        }

        /// <summary>A claim holding seconds since the epoch, as an instant.</summary>
        /// <param name="user">The principal.</param>
        /// <param name="type">The claim type.</param>
        /// <returns>The instant, or null when the claim is absent or malformed.</returns>
        private static DateTimeOffset? EpochTime(ClaimsPrincipal user, string type)
        {
            return user.FindFirst(type)?.Value is string value
                && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds)
                && seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds()
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
        }

        /// <summary>A value, or null when it is empty.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The value, or null.</returns>
        private static string? NonEmpty(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
