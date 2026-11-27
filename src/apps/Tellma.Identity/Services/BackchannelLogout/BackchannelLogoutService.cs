// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using System.Collections.Immutable;
using System.Text.Json;
using Tellma.Identity.Data;
using Tellma.Identity.Data.Entities;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Services.Audit;
using Tellma.Identity.Services.Provisioning;
using Tellma.Identity.Services.Sessions;

namespace Tellma.Identity.Services.BackchannelLogout
{
    /// <summary>The engine's back-channel logout orchestrator.</summary>
    /// <param name="sessionRegistry">The sid registry.</param>
    /// <param name="applicationManager">The OpenIddict application manager.</param>
    /// <param name="authorizationManager">The OpenIddict authorization manager.</param>
    /// <param name="tokenFactory">Logout-token minting.</param>
    /// <param name="httpClientFactory">The resilient delivery client.</param>
    /// <param name="auditLogger">Audit emission.</param>
    /// <param name="metrics">Identity metrics.</param>
    /// <param name="dbContext">The request's store context, the one the OpenIddict managers read through.</param>
    /// <param name="logger">Revocation and delivery diagnostics.</param>
    public sealed class BackchannelLogoutService(
        ISessionRegistry sessionRegistry,
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictAuthorizationManager authorizationManager,
        LogoutTokenFactory tokenFactory,
        IHttpClientFactory httpClientFactory,
        IAuditLogger auditLogger,
        IdentityMetrics metrics,
        TellmaIdentityDbContext dbContext,
        ILogger<BackchannelLogoutService> logger) : IBackchannelLogoutService
    {
        /// <summary>The named <see cref="HttpClient" /> deliveries go through.</summary>
        public const string HttpClientName = "tellma-identity-backchannel-logout";

        /// <summary>
        ///     How many times a logout writes the revocation of one authorization before it gives
        ///     up. A write is refused when another writer changed the authorization since it was
        ///     read, such as a grant of another tab extending it; each refusal therefore means a
        ///     newer version was saved, and the next attempt revokes that one.
        /// </summary>
        internal const int MaxRevokeAttempts = 3;

        /// <inheritdoc />
        public async Task TerminateSessionAsync(string sid, string subject, CancellationToken cancellationToken)
        {
            IReadOnlyList<IdentitySessionClient> clients = await sessionRegistry.TerminateAsync(sid, cancellationToken);
            await NotifyAsync(subject, clients, cancellationToken);
        }

        /// <inheritdoc />
        public async Task TerminateAllSessionsAsync(string subject, CancellationToken cancellationToken)
        {
            IReadOnlyList<IdentitySessionClient> clients = await sessionRegistry.TerminateAllAsync(subject, cancellationToken);
            await NotifyAsync(subject, clients, cancellationToken);
        }

        /// <summary>Revokes each registration's grant and delivers each logout token.</summary>
        private async Task NotifyAsync(
            string subject, IReadOnlyList<IdentitySessionClient> clients, CancellationToken cancellationToken)
        {
            // Revoke the backing authorizations first: renewal stops even if a delivery fails.
            // Every authorization the client's grants used under the session is revoked, not only
            // the latest: grants asking for different scopes, or two first grants made at once,
            // each back their own refresh tokens.
            foreach (IdentitySessionClient registration in clients)
            {
                foreach (string authorizationId in UsedAuthorizationIds(registration))
                {
                    await RevokeAsync(subject, registration, authorizationId, cancellationToken);
                }
            }

            // Deliveries run sequentially: each one reads client metadata and writes notification
            // state and audit rows through the request-scoped DbContext, which is not thread-safe
            // — a parallel fan-out would trip EF's concurrency detector and mark reachable
            // distributions as failed. The per-user client count is small.
            foreach (IdentitySessionClient registration in clients)
            {
                await DeliverAsync(subject, registration, cancellationToken);
            }
        }

        /// <summary>
        ///     Revokes one authorization a registration records, and audits the outcome. A revoke
        ///     the store refuses, because another writer changed the authorization after it was
        ///     read (a grant of another tab extending it, say), is retried over the authorization
        ///     as it now stands, up to <see cref="MaxRevokeAttempts" /> attempts. A revoke that still
        ///     fails is logged and audited as a failure: the authorization stays valid, so the
        ///     refresh tokens it backs keep renewing until it is revoked another way. An
        ///     authorization no longer in the store has nothing left to revoke.
        /// </summary>
        /// <param name="subject">The user whose session ended.</param>
        /// <param name="registration">The client's registration under the session.</param>
        /// <param name="authorizationId">The authorization to revoke.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>A task that completes when the outcome is audited.</returns>
        private async Task RevokeAsync(
            string subject, IdentitySessionClient registration, string authorizationId, CancellationToken cancellationToken)
        {
            for (int attempt = 1; ; attempt++)
            {
                object? authorization = await authorizationManager.FindByIdAsync(authorizationId, cancellationToken);
                if (authorization is null)
                {
                    return;
                }

                if (await authorizationManager.TryRevokeAsync(authorization, cancellationToken))
                {
                    await AuditRevokeAsync(subject, registration, authorizationId, "success", attempt, cancellationToken);
                    return;
                }

                // The manager leaves the refused copy tracked, holding the revoked status and a
                // concurrency token the database never saw (or still modified, when the save failed
                // for another reason). The store hands a tracked copy back to the next read as it
                // is, so a retry over it would find it revoked and report success without writing;
                // and change detection at the next save on this context, the audit row's, would
                // write the stale copy again. Detaching it makes the next read load the
                // authorization as it now stands.
                dbContext.Entry(authorization).State = EntityState.Detached;

                if (attempt == MaxRevokeAttempts)
                {
                    BackchannelLogoutLog.GaveUpRevoke(logger, authorizationId, registration.ClientId, registration.Sid, attempt);
                    await AuditRevokeAsync(subject, registration, authorizationId, "failure", attempt, cancellationToken);
                    return;
                }

                BackchannelLogoutLog.LostRevoke(logger, authorizationId, registration.Sid, attempt, MaxRevokeAttempts);
            }
        }

        /// <summary>Audits the outcome of revoking one authorization at a logout.</summary>
        /// <param name="subject">The user whose session ended.</param>
        /// <param name="registration">The client's registration under the session.</param>
        /// <param name="authorizationId">The authorization the logout revoked, or failed to.</param>
        /// <param name="outcome"><c>success</c> or <c>failure</c>.</param>
        /// <param name="attempts">The attempts the revoke took.</param>
        /// <param name="cancellationToken">Aborts the write.</param>
        /// <returns>A task that completes when the event is persisted (or the failure logged).</returns>
        private Task AuditRevokeAsync(
            string subject,
            IdentitySessionClient registration,
            string authorizationId,
            string outcome,
            int attempts,
            CancellationToken cancellationToken)
        {
            return auditLogger.LogAsync(
                new AuditEventEntry
                {
                    Action = AuditActions.TokenRevoked,
                    Subject = subject,
                    ClientId = registration.ClientId,
                    Sid = registration.Sid,
                    Outcome = outcome,
                    DetailsJson = JsonSerializer.Serialize(new { authorizationId, attempts }),
                },
                cancellationToken);
        }

        /// <summary>
        ///     Every authorization a registration records, each once: the rows, and the latest one
        ///     in the registration's own column. The column alone holds the authorization of a
        ///     registration that the previous engine release made, which wrote no rows.
        /// </summary>
        /// <param name="registration">The client's registration under a session.</param>
        /// <returns>The distinct authorization ids.</returns>
        private static IEnumerable<string> UsedAuthorizationIds(IdentitySessionClient registration)
        {
            IEnumerable<string> rows = registration.Authorizations.Select(static used => used.AuthorizationId);
            return registration.AuthorizationId is { } latest
                ? rows.Prepend(latest).Distinct(StringComparer.Ordinal)
                : rows.Distinct(StringComparer.Ordinal);
        }

        /// <summary>Delivers one logout token; failures are audited, never thrown.</summary>
        private async Task DeliverAsync(
            string subject, IdentitySessionClient registration, CancellationToken cancellationToken)
        {
            try
            {
                object? application = await applicationManager.FindByClientIdAsync(registration.ClientId, cancellationToken);
                if (application is null)
                {
                    return;
                }

                ImmutableDictionary<string, JsonElement> properties =
                    await applicationManager.GetPropertiesAsync(application, cancellationToken);
                string? endpoint = TellmaClientProperties.Get(properties, TellmaClientProperties.BackchannelLogoutUri);
                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    return;
                }

                string logoutToken = tokenFactory.Create(registration.ClientId, subject, registration.Sid);

                using HttpClient client = httpClientFactory.CreateClient(HttpClientName);
                using HttpResponseMessage response = await client.PostAsync(
                    new Uri(endpoint, UriKind.Absolute),
                    new FormUrlEncodedContent(new Dictionary<string, string> { ["logout_token"] = logoutToken }),
                    cancellationToken);

                response.EnsureSuccessStatusCode();
                await sessionRegistry.MarkNotifiedAsync(registration.Sid, registration.ClientId, cancellationToken);

                await auditLogger.LogAsync(
                    new AuditEventEntry
                    {
                        Action = AuditActions.BackchannelLogoutSent,
                        Subject = subject,
                        ClientId = registration.ClientId,
                        Sid = registration.Sid,
                        Outcome = "success",
                    },
                    cancellationToken);
                metrics.BackchannelLogoutDelivery("success");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                BackchannelLogoutLog.DeliveryFailed(logger, exception, registration.ClientId, registration.Sid);
                await auditLogger.LogAsync(
                    new AuditEventEntry
                    {
                        Action = AuditActions.BackchannelLogoutFailed,
                        Subject = subject,
                        ClientId = registration.ClientId,
                        Sid = registration.Sid,
                        Outcome = "failure",
                    },
                    cancellationToken);
                metrics.BackchannelLogoutDelivery("failure");
            }
        }
    }

    /// <summary>Source-generated log messages for <see cref="BackchannelLogoutService" />.</summary>
    internal static partial class BackchannelLogoutLog
    {
        /// <summary>A logout-token delivery failed after retries.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The delivery failure.</param>
        /// <param name="clientId">The target client.</param>
        /// <param name="sid">The terminated session.</param>
        [LoggerMessage(Level = LogLevel.Warning, Message = "Back-channel logout delivery to {ClientId} for session {Sid} failed.")]
        public static partial void DeliveryFailed(ILogger logger, Exception exception, string clientId, string sid);

        /// <summary>A logout's revocation of an authorization was refused, and is tried again.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="authorizationId">The authorization being revoked.</param>
        /// <param name="sid">The terminated session.</param>
        /// <param name="attempt">The attempt that was refused.</param>
        /// <param name="maxAttempts">The bound on attempts.</param>
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Revoking authorization {AuthorizationId} for the logout of session {Sid} was refused on attempt {Attempt} of {MaxAttempts}, usually because a concurrent grant changed it; reading it again.")]
        public static partial void LostRevoke(ILogger logger, string authorizationId, string sid, int attempt, int maxAttempts);

        /// <summary>A logout gave up revoking an authorization, which stays valid.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="authorizationId">The authorization left valid.</param>
        /// <param name="clientId">The client the authorization belongs to.</param>
        /// <param name="sid">The terminated session.</param>
        /// <param name="attempts">The attempts made.</param>
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Revoking authorization {AuthorizationId} of client {ClientId} for the logout of session {Sid} was refused {Attempts} times; it stays valid, and the refresh tokens it backs keep renewing until it is revoked.")]
        public static partial void GaveUpRevoke(ILogger logger, string authorizationId, string clientId, string sid, int attempts);
    }
}
