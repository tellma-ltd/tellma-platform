// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Tellma.Core.AspNetCore.Realtime
{
    /// <summary>
    ///     The platform's one realtime hub, at <c>/{tenantId}/hub</c>: a server-to-client channel of
    ///     client events. It declares no method a client can invoke, so every invocation is refused.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A connection's groups are computed from the claims the hub's front door minted after the
    ///         tenant's access guard passed, never from anything the client sends. The hub reads them
    ///         from the connection's principal in every hosting mode: SignalR runs a hub in a service
    ///         scope of its own, whose request context stays tenantless.
    ///     </para>
    ///     <para>
    ///         A connection is added to the tracker before its groups, so a close that runs while it is
    ///         joining either finds it or finds it already refused.
    ///     </para>
    /// </remarks>
    /// <param name="tracker">The connections this instance hosts.</param>
    /// <param name="hosting">The hosting mode.</param>
    /// <param name="metrics">The realtime instruments.</param>
    /// <param name="logger">The hub's logger.</param>
    internal sealed class TellmaHub(HubConnectionTracker tracker, RealtimeHosting hosting, RealtimeMetrics metrics, ILogger<TellmaHub> logger) : Hub
    {
        /// <summary>The client method every event is sent through.</summary>
        public const string EventMethod = "event";

        /// <summary>Admits a connection the front door minted claims for, and joins it to its groups.</summary>
        /// <returns>A task that completes when the connection is admitted or refused.</returns>
        public override async Task OnConnectedAsync()
        {
            // --- The claims: the subject and the three hub ids, each exactly once ---
            if (!TellmaHubClaims.TryRead(Context.User, out HubConnectionClaims? claims))
            {
                // Only a connection that bypassed the front door, or a service token without the
                // claims, lacks them: it is closed without joining any group.
                metrics.RecordRefused();
                RealtimeLog.HubConnectionRefused(logger, "claims-missing");
                Context.Abort();
                return;
            }

            using IDisposable? scope = logger.BeginScope(LogScope(claims));

            // --- Tracked first: a session or tenant that ended meanwhile refuses it here ---
            if (!tracker.TryAdd(Context, claims, out string? refusal))
            {
                metrics.RecordRefused();
                RealtimeLog.HubConnectionRefused(logger, refusal);
                Context.Abort();
                return;
            }

            // --- Then the groups, every one of them the claims' ---
            try
            {
                foreach (string group in HubGroups.Of(claims))
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, group, Context.ConnectionAborted).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (Context.ConnectionAborted.IsCancellationRequested)
            {
                // Closed while joining, by its client or by a close: nothing to admit.
                _ = tracker.Remove(Context.ConnectionId);
                return;
            }
            catch
            {
                // A connection in some of its groups and not others would miss events: it goes.
                _ = tracker.Remove(Context.ConnectionId);
                throw;
            }

            metrics.RecordConnected(hosting.ModeTag);
            RealtimeLog.HubConnected(logger, hosting.ModeTag);
        }

        /// <summary>Forgets a connection that ended.</summary>
        /// <param name="exception">What ended it, if it failed.</param>
        /// <returns>A completed task: the framework removes a connection from its groups itself.</returns>
        public override Task OnDisconnectedAsync(Exception? exception)
        {
            // Only a connection that was admitted is counted down.
            if (tracker.Remove(Context.ConnectionId) is { } claims)
            {
                using IDisposable? scope = logger.BeginScope(LogScope(claims));
                metrics.RecordDisconnected(hosting.ModeTag);
                RealtimeLog.HubDisconnected(logger, hosting.ModeTag);
            }

            return Task.CompletedTask;
        }

        /// <summary>The logger scope of a connection: its tenant and user.</summary>
        /// <param name="claims">The connection's claims.</param>
        /// <returns>The scope's members.</returns>
        private static KeyValuePair<string, object?>[] LogScope(HubConnectionClaims claims)
        {
            return [new("TenantId", claims.TenantId), new("UserId", claims.UserId)];
        }
    }
}
