// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using Tellma.Core.AspNetCore.Authentication;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Observability;

namespace Tellma.Core.AspNetCore.Sessions
{
    /// <summary>How a refresh ended.</summary>
    internal enum RefreshOutcomeKind
    {
        /// <summary>This refresh redeemed the refresh token and wrote the new ticket.</summary>
        Refreshed,

        /// <summary>Another refresh had already written a newer ticket, which this one took.</summary>
        Adopted,

        /// <summary>The session ended: the authority refused it, it is gone, or the authority stayed unreachable too long.</summary>
        Rejected,

        /// <summary>The authority could not be reached; the session stays as it is for now.</summary>
        Deferred,
    }

    /// <summary>How a refresh ended, and the session it left.</summary>
    /// <param name="Kind">How it ended.</param>
    /// <param name="Session">The refreshed or adopted session.</param>
    internal sealed record RefreshOutcome(RefreshOutcomeKind Kind, StoredSession? Session);

    /// <summary>What a refresh needs of the session that asks for it.</summary>
    /// <param name="Key">The session's key.</param>
    /// <param name="Handle">The session's handle.</param>
    /// <param name="Subject">The session's subject.</param>
    /// <param name="TokensVersion">The tokens version the request's ticket was read at.</param>
    /// <param name="AccessTokenExpiry">When the request's access token expires, which the outage allowance runs from.</param>
    internal sealed record RefreshRequest(string Key, string Handle, string Subject, int TokensVersion, DateTimeOffset AccessTokenExpiry);

    /// <summary>
    ///     The refresh-anchored liveness of a session: when its access token is about to expire, the
    ///     session's row is re-read past the cache, a newer ticket is adopted, and otherwise the refresh
    ///     token is redeemed and the new ticket written with the compare-and-set. Concurrent requests on
    ///     one session on this instance share one refresh; a compare-and-set lost to another instance
    ///     adopts that instance's ticket.
    /// </summary>
    /// <remarks>
    ///     A refusal by the authority ends the session — its row is deleted and the listeners told — so a
    ///     sign-out everywhere, a disabled user or a tightened policy takes effect within one access token's
    ///     lifetime even when no back-channel logout arrived. An unreachable authority keeps the session up
    ///     to the outage allowance past the access token's expiry, then ends it the same way. A refresh put
    ///     off is not attempted again for <see cref="DeferralBackoff" />, so during an outage the session's
    ///     requests are served on its current tokens instead of each waiting on the authority.
    /// </remarks>
    /// <param name="store">The session store.</param>
    /// <param name="redeemer">Redeems refresh tokens.</param>
    /// <param name="options">The session options.</param>
    /// <param name="time">The clock.</param>
    /// <param name="metrics">The host's instruments.</param>
    /// <param name="logger">The logger.</param>
    internal sealed class SessionRefresher(
        CatalogSessionStore store,
        ITokenRedeemer redeemer,
        IOptions<TellmaSessionOptions> options,
        TimeProvider time,
        HostMetrics metrics,
        ILogger<SessionRefresher> logger)
    {
        /// <summary>The claims a refreshed identity token replaces on the session's principal.</summary>
        internal static readonly string[] RefreshedClaims =
        [
            TellmaClaimTypes.AuthenticationContextClass,
            TellmaClaimTypes.AcrAuthenticationTime,
            TellmaClaimTypes.Name,
            TellmaClaimTypes.Locale,
        ];

        /// <summary>How long a refresh put off is not attempted again for the same session.</summary>
        internal static readonly TimeSpan DeferralBackoff = TimeSpan.FromSeconds(20);

        /// <summary>How many remembered deferrals prompt a pass that forgets the lapsed ones.</summary>
        private const int DeferralPruneThreshold = 10_000;

        /// <summary>The refreshes in flight on this instance, by session key.</summary>
        private readonly ConcurrentDictionary<string, Lazy<Task<RefreshOutcome>>> _flights = new(StringComparer.Ordinal);

        /// <summary>The sessions whose refresh was put off on this instance, by key, with when it may be tried again.</summary>
        private readonly ConcurrentDictionary<string, DateTimeOffset> _deferrals = new(StringComparer.Ordinal);

        /// <summary>Refreshes a session, joining the refresh already in flight for it on this instance.</summary>
        /// <param name="request">The session.</param>
        /// <returns>How the refresh ended.</returns>
        public async Task<RefreshOutcome> RefreshAsync(RefreshRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            // --- A refresh put off moments ago is not tried again yet, unless the allowance is spent ---
            if (_deferrals.TryGetValue(request.Key, out DateTimeOffset retryAt))
            {
                DateTimeOffset now = time.GetUtcNow();
                if (now < retryAt && now <= request.AccessTokenExpiry + options.Value.MaxAuthorityOutage)
                {
                    return new RefreshOutcome(RefreshOutcomeKind.Deferred, null);
                }

                _ = _deferrals.TryRemove(new KeyValuePair<string, DateTimeOffset>(request.Key, retryAt));
            }

            // The flight is not the asking request's to cancel: other requests may be waiting on it.
            Lazy<Task<RefreshOutcome>> created = new(() => RunGuardedAsync(request), LazyThreadSafetyMode.ExecutionAndPublication);
            Lazy<Task<RefreshOutcome>> flight = _flights.GetOrAdd(request.Key, created);
            try
            {
                return await flight.Value.ConfigureAwait(false);
            }
            finally
            {
                if (ReferenceEquals(flight, created))
                {
                    _ = _flights.TryRemove(new KeyValuePair<string, Lazy<Task<RefreshOutcome>>>(request.Key, created));
                }
            }
        }

        /// <summary>
        ///     One refresh, where a failure of the catalog counts as an unreachable authority would: the
        ///     session stays within the outage allowance, and past it the request is refused.
        /// </summary>
        /// <param name="request">The session.</param>
        /// <returns>How it ended.</returns>
        private async Task<RefreshOutcome> RunGuardedAsync(RefreshRequest request)
        {
            try
            {
                return await RunAsync(request).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                if (time.GetUtcNow() > request.AccessTokenExpiry + options.Value.MaxAuthorityOutage)
                {
                    return Reject(request, "the session could not be refreshed within the outage allowance");
                }

                HostLog.SessionRefreshDeferred(logger, request.Handle, exception);
                return Deferred(request);
            }
        }

        /// <summary>One refresh: re-read, adopt or redeem, then write with the compare-and-set.</summary>
        /// <param name="request">The session.</param>
        /// <returns>How it ended.</returns>
        private async Task<RefreshOutcome> RunAsync(RefreshRequest request)
        {
            // --- The row as it is now: another request or instance may have refreshed it already ---
            StoredSession? current = await store.ReadFreshAsync(request.Key, CancellationToken.None).ConfigureAwait(false);
            if (current is null)
            {
                return Reject(request, "the session no longer exists");
            }

            if (current.TokensVersion != request.TokensVersion)
            {
                return Adopt(current);
            }

            if (current.Ticket.Properties.GetTokenValue(OpenIdConnectParameterNames.RefreshToken) is not { Length: > 0 } refreshToken)
            {
                return await EndAsync(request, "the session holds no refresh token").ConfigureAwait(false);
            }

            // --- The redemption ---
            Redemption redemption = await redeemer.RedeemAsync(refreshToken, CancellationToken.None).ConfigureAwait(false);
            switch (redemption.Status)
            {
                case RedemptionStatus.Unreachable:
                    return await DeferAsync(request, redemption).ConfigureAwait(false);

                case RedemptionStatus.Refused:
                    return await EndAsync(request, redemption.Reason ?? "refused").ConfigureAwait(false);

                case RedemptionStatus.Redeemed:
                default:
                    break;
            }

            RedeemedTokens tokens = redemption.Tokens!;
            if (tokens.IdTokenClaims is { } refreshed
                && !string.Equals(refreshed.FindFirst(TellmaClaimTypes.Subject)?.Value, request.Subject, StringComparison.Ordinal))
            {
                return await EndAsync(request, "the refreshed identity token names another subject").ConfigureAwait(false);
            }

            // --- The new ticket, written only if no other writer moved the row meanwhile ---
            AuthenticationTicket ticket = Refreshed(current.Ticket, tokens, time.GetUtcNow());
            int? version = await store.ReplaceAsync(request.Key, ticket, current.ExpiresAt, current.TokensVersion, CancellationToken.None).ConfigureAwait(false);
            if (version is int written)
            {
                metrics.RecordSessionRefresh(HostMetrics.RefreshedOutcome);
                return new RefreshOutcome(RefreshOutcomeKind.Refreshed, new StoredSession(ticket, current.Subject, written, current.ExpiresAt));
            }

            // A lost compare-and-set: the winner's ticket is the session's now.
            StoredSession? winner = await store.ReadFreshAsync(request.Key, CancellationToken.None).ConfigureAwait(false);
            return winner is null ? Reject(request, "the session no longer exists") : Adopt(winner);
        }

        /// <summary>The session's ticket with the redeemed tokens and the refreshed identity token's claims.</summary>
        /// <param name="ticket">The stored ticket.</param>
        /// <param name="tokens">The redeemed tokens.</param>
        /// <param name="now">The current instant.</param>
        /// <returns>The new ticket.</returns>
        internal static AuthenticationTicket Refreshed(AuthenticationTicket ticket, RedeemedTokens tokens, DateTimeOffset now)
        {
            // --- The principal: the refreshed assurance, evidence time, name and language replace the old ---
            ClaimsIdentity identity = ((ClaimsIdentity?)ticket.Principal.Identity ?? new ClaimsIdentity(TellmaAuthentication.SessionScheme)).Clone();
            if (tokens.IdTokenClaims is { } claims)
            {
                foreach (string type in RefreshedClaims)
                {
                    foreach (Claim stale in identity.FindAll(type).ToList())
                    {
                        _ = identity.TryRemoveClaim(stale);
                    }

                    foreach (Claim fresh in claims.FindAll(type))
                    {
                        identity.AddClaim(new Claim(type, fresh.Value, fresh.ValueType, fresh.Issuer));
                    }
                }
            }

            // --- The tokens, as the sign-in saved them ---
            AuthenticationProperties properties = ticket.Properties.Clone();
            properties.StoreTokens(
            [
                new AuthenticationToken { Name = OpenIdConnectParameterNames.AccessToken, Value = tokens.AccessToken },
                new AuthenticationToken { Name = OpenIdConnectParameterNames.RefreshToken, Value = tokens.RefreshToken ?? ticket.Properties.GetTokenValue(OpenIdConnectParameterNames.RefreshToken) ?? string.Empty },
                new AuthenticationToken { Name = OpenIdConnectParameterNames.IdToken, Value = tokens.IdToken ?? ticket.Properties.GetTokenValue(OpenIdConnectParameterNames.IdToken) ?? string.Empty },
                new AuthenticationToken { Name = OpenIdConnectParameterNames.TokenType, Value = tokens.TokenType ?? ticket.Properties.GetTokenValue(OpenIdConnectParameterNames.TokenType) ?? "Bearer" },
                new AuthenticationToken { Name = SessionProperties.ExpiresAtToken, Value = (now + tokens.ExpiresIn).ToString("o", CultureInfo.InvariantCulture) },
            ]);

            return new AuthenticationTicket(new ClaimsPrincipal(identity), properties, ticket.AuthenticationScheme);
        }

        /// <summary>Takes the ticket another refresh wrote.</summary>
        /// <param name="current">The row as it is now.</param>
        /// <returns>The outcome.</returns>
        private RefreshOutcome Adopt(StoredSession current)
        {
            metrics.RecordSessionRefresh(HostMetrics.AdoptedOutcome);
            return new RefreshOutcome(RefreshOutcomeKind.Adopted, current);
        }

        /// <summary>Puts a refresh off while the authority is unreachable, within the outage allowance.</summary>
        /// <param name="request">The session.</param>
        /// <param name="redemption">The failed redemption.</param>
        /// <returns>The outcome: deferred, or rejected once the allowance is spent.</returns>
        private async Task<RefreshOutcome> DeferAsync(RefreshRequest request, Redemption redemption)
        {
            if (time.GetUtcNow() > request.AccessTokenExpiry + options.Value.MaxAuthorityOutage)
            {
                return await EndAsync(request, "the identity server stayed unreachable beyond the outage allowance").ConfigureAwait(false);
            }

            HostLog.SessionRefreshDeferred(logger, request.Handle, redemption.Failure);
            return Deferred(request);
        }

        /// <summary>Counts a deferral and remembers it, so the session's next requests do not try again at once.</summary>
        /// <param name="request">The session.</param>
        /// <returns>The deferral.</returns>
        private RefreshOutcome Deferred(RefreshRequest request)
        {
            DateTimeOffset now = time.GetUtcNow();
            _deferrals[request.Key] = now + DeferralBackoff;
            if (_deferrals.Count > DeferralPruneThreshold)
            {
                // Sessions that never came back would otherwise be remembered for the instance's life.
                foreach (KeyValuePair<string, DateTimeOffset> lapsed in _deferrals.Where(entry => entry.Value <= now).ToList())
                {
                    _ = _deferrals.TryRemove(lapsed);
                }
            }

            metrics.RecordSessionRefresh(HostMetrics.DeferredOutcome);
            return new RefreshOutcome(RefreshOutcomeKind.Deferred, null);
        }

        /// <summary>Ends the session: its row deleted, the listeners told, the rejection logged and counted.</summary>
        /// <param name="request">The session.</param>
        /// <param name="reason">Why.</param>
        /// <returns>The rejection.</returns>
        private async Task<RefreshOutcome> EndAsync(RefreshRequest request, string reason)
        {
            await store.EndAsync(new EndedSession(request.Key, request.Handle, request.Subject), CancellationToken.None).ConfigureAwait(false);
            return Reject(request, reason);
        }

        /// <summary>Logs and counts a rejection.</summary>
        /// <param name="request">The session.</param>
        /// <param name="reason">Why.</param>
        /// <returns>The rejection.</returns>
        private RefreshOutcome Reject(RefreshRequest request, string reason)
        {
            HostLog.SessionRefreshRejected(logger, request.Handle, reason);
            metrics.RecordSessionRefresh(HostMetrics.RejectedOutcome);
            return new RefreshOutcome(RefreshOutcomeKind.Rejected, null);
        }
    }
}
