// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Caching;
using Tellma.Core.Notifications;

namespace Tellma.Core.AspNetCore.Realtime
{
    /// <summary>
    ///     The hub connections this instance hosts, and the closes that end them: when sessions end,
    ///     when a user loses access to a tenant, and when a tenant is suspended, retired or leaves the
    ///     registry. Each close first sends <c>session.ended</c> to the affected group, on every
    ///     instance the hosting mode reaches, then aborts the group's connections this instance hosts.
    ///     A change to any other tenant state sends <c>cache.changed</c> for the tenant and closes
    ///     nothing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Sessions are named by their handles, never their keys. Every group is qualified by its
    ///         tenant, so the end of a session is sent to its group in each tenant it may hold a
    ///         connection in: those this instance holds one in, and the subject's memberships.
    ///     </para>
    ///     <para>
    ///         An ended session, a revoked (tenant, subject) and an unavailable tenant are remembered
    ///         for <see cref="MemoLifetime" />, and a connection that joins under one of them is
    ///         refused: its front door may have passed before the close was recorded. The memo and
    ///         the admission share one lock, so a connection is either seen by the close's snapshot or
    ///         refused by the memo.
    ///     </para>
    ///     <para>
    ///         The connections this instance hosts are told one by one rather than through the
    ///         group, and the group is told without them: on Azure SignalR Service a send to a
    ///         connection this instance hosts travels on that connection's own server connection, the
    ///         one its abort's close follows, so the event cannot be overtaken by the close; and a
    ///         connection admitted but still joining its groups is told all the same.
    ///     </para>
    /// </remarks>
    internal sealed class HubConnectionTracker : ISessionTerminationListener, ITenantStateListener
    {
        /// <summary>
        ///     How long a close is remembered against new connections: the five minutes for which an
        ///     Azure SignalR Service token can open a connection after its negotiate passed.
        /// </summary>
        public static readonly TimeSpan MemoLifetime = TimeSpan.FromMinutes(5);

        /// <summary>The <c>cache.changed</c> tag of the tenant's own state.</summary>
        public const string TenantCacheTag = "tenant";

        // --- What this instance hosts, keyed by connection id ---
        private readonly ConcurrentDictionary<string, TrackedConnection> _connections = new(StringComparer.Ordinal);

        // --- What was closed, until when; read and written under the gate ---
        private readonly Dictionary<string, DateTimeOffset> _endedSessions = new(StringComparer.Ordinal);
        private readonly Dictionary<(int TenantId, string Subject), DateTimeOffset> _revokedSubjects = [];
        private readonly Dictionary<int, DateTimeOffset> _unavailableTenants = [];
        private readonly Lock _gate = new();

        private readonly IHubContext<TellmaHub> _hub;
        private readonly ITenantMembershipDirectory _memberships;
        private readonly RealtimeMetrics _metrics;
        private readonly TimeProvider _time;
        private readonly ILogger<HubConnectionTracker> _logger;

        /// <summary>Creates the tracker.</summary>
        /// <param name="hub">The hub's context, which sends to groups on every instance the mode reaches.</param>
        /// <param name="memberships">The catalog's membership hints, the tenants a session may hold connections in.</param>
        /// <param name="metrics">The realtime instruments.</param>
        /// <param name="time">The clock the memo expires by.</param>
        /// <param name="logger">The tracker's logger.</param>
        public HubConnectionTracker(
            IHubContext<TellmaHub> hub,
            ITenantMembershipDirectory memberships,
            RealtimeMetrics metrics,
            TimeProvider time,
            ILogger<HubConnectionTracker> logger)
        {
            ArgumentNullException.ThrowIfNull(hub);
            ArgumentNullException.ThrowIfNull(memberships);
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(time);
            ArgumentNullException.ThrowIfNull(logger);

            _hub = hub;
            _memberships = memberships;
            _metrics = metrics;
            _time = time;
            _logger = logger;
        }

        /// <summary>How many connections this instance hosts.</summary>
        public int Count => _connections.Count;

        /// <summary>The ids of the connections this instance hosts.</summary>
        public IReadOnlyList<string> ConnectionIds => [.. _connections.Keys];

        /// <summary>Admits a connection, unless a close it would have been part of was recorded.</summary>
        /// <param name="context">The connection's hub context, which a close aborts.</param>
        /// <param name="claims">The connection's claims.</param>
        /// <param name="refusal">Why it was refused: the <c>session.ended</c> reason of the close.</param>
        /// <returns>True when the connection is admitted.</returns>
        public bool TryAdd(HubCallerContext context, HubConnectionClaims claims, [NotNullWhen(false)] out string? refusal)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(claims);

            lock (_gate)
            {
                DateTimeOffset now = _time.GetUtcNow();
                refusal = Live(_endedSessions, claims.SessionHandle, now) ? SessionEndedEvent.SessionTerminated
                    : Live(_revokedSubjects, (claims.TenantId, claims.Subject), now) ? SessionEndedEvent.AccessRevoked
                    : Live(_unavailableTenants, claims.TenantId, now) ? SessionEndedEvent.TenantUnavailable
                    : null;
                if (refusal is not null)
                {
                    return false;
                }

                _connections[context.ConnectionId] = new TrackedConnection(claims, context);
                return true;
            }
        }

        /// <summary>Forgets a connection.</summary>
        /// <param name="connectionId">The connection's id.</param>
        /// <returns>Its claims; null when it was not admitted or was already forgotten.</returns>
        public HubConnectionClaims? Remove(string connectionId)
        {
            return _connections.TryRemove(connectionId, out TrackedConnection? removed) ? removed.Claims : null;
        }

        /// <inheritdoc />
        public async Task SessionsTerminatedAsync(string subject, IReadOnlyList<string> sessionHandles)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(sessionHandles);
            HashSet<string> handles = [.. sessionHandles.Where(static handle => !string.IsNullOrEmpty(handle))];
            if (handles.Count == 0)
            {
                return;
            }

            // --- Remembered, then this instance's connections of those sessions ---
            Remember(_endedSessions, handles);
            List<TrackedConnection> local = Snapshot(c => c.Claims.Subject == subject && handles.Contains(c.Claims.SessionHandle));

            // --- The tenants the sessions may hold connections in: this instance's, and the subject's memberships ---
            SortedSet<int> tenants = [.. local.Select(static c => c.Claims.TenantId)];
            try
            {
                foreach (TenantMembership membership in await _memberships.ListAsync(subject).ConfigureAwait(false))
                {
                    _ = tenants.Add(membership.Tenant.Id);
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Another instance's connections in a tenant left out close when their tickets expire.
                RealtimeLog.HubSessionTenantsUnread(_logger, exception);
            }

            // --- Told first, in every tenant; then closed here, group by group ---
            foreach (int tenantId in tenants)
            {
                foreach (string handle in handles.Order(StringComparer.Ordinal))
                {
                    List<TrackedConnection> told = [.. local.Where(c => c.Claims.TenantId == tenantId && c.Claims.SessionHandle == handle)];
                    await TellAsync(HubGroups.Session(tenantId, handle), tenantId, told, SessionEndedEvent.SessionTerminated).ConfigureAwait(false);
                }
            }

            foreach (IGrouping<string, TrackedConnection> group in local.GroupBy(static c => HubGroups.Session(c.Claims.TenantId, c.Claims.SessionHandle)))
            {
                Close(group.Key, [.. group], SessionEndedEvent.SessionTerminated);
            }
        }

        /// <inheritdoc />
        public async Task TenantAccessRevokedAsync(int tenantId, string subject)
        {
            ArgumentNullException.ThrowIfNull(subject);

            // --- Remembered, told, then closed here ---
            Remember(_revokedSubjects, [(tenantId, subject)]);
            List<TrackedConnection> local = Snapshot(c => c.Claims.TenantId == tenantId && c.Claims.Subject == subject);
            string group = HubGroups.Subject(tenantId, subject);
            await TellAsync(group, tenantId, local, SessionEndedEvent.AccessRevoked).ConfigureAwait(false);
            Close(group, local, SessionEndedEvent.AccessRevoked);
        }

        /// <inheritdoc />
        public async Task OnStateChangedAsync(TenantDescriptor tenant, TenantState? previous)
        {
            ArgumentNullException.ThrowIfNull(tenant);

            // --- Suspended or retired: every connection of the tenant ends ---
            if (tenant.State is TenantState.Suspended or TenantState.Retired)
            {
                await CloseTenantAsync(tenant.Id).ConfigureAwait(false);
                return;
            }

            // --- Any other state: reads stay allowed, so open tabs only re-read the tenant ---
            lock (_gate)
            {
                _ = _unavailableTenants.Remove(tenant.Id);
            }

            ClientEventEnvelope envelope = new(VersionTagBumpContributor.CacheChangedEvent, tenant.Id, new CacheChangedPayload(TenantCacheTag, null));
            try
            {
                await _hub.Clients.Group(HubGroups.Tenant(tenant.Id)).SendAsync(TellmaHub.EventMethod, envelope).ConfigureAwait(false);
                _metrics.RecordEvent(envelope.Name);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The tabs re-read the tenant on their next sync instead.
                NotificationsLog.ClientEventFailed(_logger, exception, envelope.Name);
            }
        }

        /// <inheritdoc />
        public Task OnRemovedAsync(TenantDescriptor tenant)
        {
            ArgumentNullException.ThrowIfNull(tenant);

            // A tenant that left the snapshot answers as one that does not exist: it is unavailable.
            return CloseTenantAsync(tenant.Id);
        }

        /// <summary>Whether a remembered close is still in force.</summary>
        /// <typeparam name="TKey">The memo's key.</typeparam>
        /// <param name="memo">The memo.</param>
        /// <param name="key">The key.</param>
        /// <param name="now">The time now.</param>
        /// <returns>True when the key was closed less than <see cref="MemoLifetime" /> ago.</returns>
        private static bool Live<TKey>(Dictionary<TKey, DateTimeOffset> memo, TKey key, DateTimeOffset now)
            where TKey : notnull
        {
            return memo.TryGetValue(key, out DateTimeOffset until) && until > now;
        }

        /// <summary>Remembers closes, and forgets those that expired.</summary>
        /// <typeparam name="TKey">The memo's key.</typeparam>
        /// <param name="memo">The memo.</param>
        /// <param name="keys">What was closed.</param>
        private void Remember<TKey>(Dictionary<TKey, DateTimeOffset> memo, IEnumerable<TKey> keys)
            where TKey : notnull
        {
            lock (_gate)
            {
                DateTimeOffset now = _time.GetUtcNow();
                foreach (TKey expired in memo.Where(entry => entry.Value <= now).Select(static entry => entry.Key).ToList())
                {
                    _ = memo.Remove(expired);
                }

                foreach (TKey key in keys)
                {
                    memo[key] = now + MemoLifetime;
                }
            }
        }

        /// <summary>The connections this instance hosts that match, taken after the memo was written.</summary>
        /// <param name="match">Which connections.</param>
        /// <returns>The matching connections.</returns>
        private List<TrackedConnection> Snapshot(Func<TrackedConnection, bool> match)
        {
            // Under the gate, so no admission is halfway between the memo's check and the add.
            lock (_gate)
            {
                return [.. _connections.Values.Where(match)];
            }
        }

        /// <summary>Ends every connection of a tenant: told, then closed here.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>A task that completes when the connections are told and aborted.</returns>
        private async Task CloseTenantAsync(int tenantId)
        {
            Remember(_unavailableTenants, [tenantId]);
            List<TrackedConnection> local = Snapshot(c => c.Claims.TenantId == tenantId);
            string group = HubGroups.Tenant(tenantId);
            await TellAsync(group, tenantId, local, SessionEndedEvent.TenantUnavailable).ConfigureAwait(false);
            Close(group, local, SessionEndedEvent.TenantUnavailable);
        }

        /// <summary>
        ///     Sends <c>session.ended</c> to a group of one tenant: to the connections this instance
        ///     hosts one by one, and to the rest of the group on every instance the mode reaches.
        /// </summary>
        /// <param name="group">The group.</param>
        /// <param name="tenantId">The group's tenant, which the envelope names.</param>
        /// <param name="local">The group's connections on this instance, which the close aborts next.</param>
        /// <param name="reason">Why the connections close.</param>
        /// <returns>A task that never faults.</returns>
        private async Task TellAsync(string group, int tenantId, IReadOnlyList<TrackedConnection> local, string reason)
        {
            ClientEventEnvelope envelope = new(SessionEndedEvent.Name, tenantId, new SessionEndedPayload(reason));
            try
            {
                // --- The other instances' connections, through the group ---
                string[] localIds = [.. local.Select(static c => c.Context.ConnectionId)];
                await _hub.Clients.GroupExcept(group, localIds).SendAsync(TellmaHub.EventMethod, envelope).ConfigureAwait(false);

                // --- This instance's, each on its own connection, ahead of its abort ---
                foreach (string connectionId in localIds)
                {
                    await _hub.Clients.Client(connectionId).SendAsync(TellmaHub.EventMethod, envelope).ConfigureAwait(false);
                }

                _metrics.RecordEvent(envelope.Name);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A close goes ahead untold: the client sees the connection end, and the ticket's
                // expiry and the front door hold the rest.
                NotificationsLog.ClientEventFailed(_logger, exception, envelope.Name);
            }
        }

        /// <summary>Aborts a group's connections this instance hosts, counting and logging them.</summary>
        /// <param name="group">The group told.</param>
        /// <param name="connections">Its connections on this instance.</param>
        /// <param name="reason">Why.</param>
        private void Close(string group, IReadOnlyList<TrackedConnection> connections, string reason)
        {
            foreach (TrackedConnection connection in connections)
            {
                // The abort ends the hub's side in every mode; Azure SignalR Service's SDK then closes
                // the client's connection at the service.
                connection.Context.Abort();
            }

            _metrics.RecordClosed(reason, connections.Count);
            RealtimeLog.HubConnectionsClosed(_logger, group, connections.Count, reason);
        }

        /// <summary>A connection this instance hosts.</summary>
        /// <param name="Claims">Its claims.</param>
        /// <param name="Context">Its hub context.</param>
        private sealed record TrackedConnection(HubConnectionClaims Claims, HubCallerContext Context);
    }
}
