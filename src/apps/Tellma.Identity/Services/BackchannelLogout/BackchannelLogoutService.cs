// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using System.Collections.Immutable;
using System.Text.Json;
using Tellma.Identity.Data.Entities;
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
    /// <param name="logger">Delivery diagnostics.</param>
    public sealed class BackchannelLogoutService(
        ISessionRegistry sessionRegistry,
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictAuthorizationManager authorizationManager,
        LogoutTokenFactory tokenFactory,
        IHttpClientFactory httpClientFactory,
        IAuditLogger auditLogger,
        ILogger<BackchannelLogoutService> logger) : IBackchannelLogoutService
    {
        /// <summary>The named <see cref="HttpClient" /> deliveries go through.</summary>
        public const string HttpClientName = "tellma-identity-backchannel-logout";

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

        /// <summary>Revokes each registration's grant and fans logout tokens out in parallel.</summary>
        private async Task NotifyAsync(
            string subject, IReadOnlyList<IdentitySessionClient> clients, CancellationToken cancellationToken)
        {
            // Revoke the backing authorizations first: renewal stops even if a delivery fails.
            foreach (IdentitySessionClient registration in clients)
            {
                if (registration.AuthorizationId is null)
                {
                    continue;
                }

                object? authorization = await authorizationManager.FindByIdAsync(registration.AuthorizationId, cancellationToken);
                if (authorization is not null)
                {
                    await authorizationManager.TryRevokeAsync(authorization, cancellationToken);
                }
            }

            await Task.WhenAll(clients.Select(registration => DeliverAsync(subject, registration, cancellationToken)));
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
    }
}
