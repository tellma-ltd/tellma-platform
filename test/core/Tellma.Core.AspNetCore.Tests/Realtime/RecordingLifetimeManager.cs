// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using Tellma.Core.AspNetCore.Realtime;
using Tellma.Testing.WebHost;

namespace Tellma.Core.AspNetCore.Tests.Realtime
{
    /// <summary>
    ///     The framework's in-process lifetime manager, recording the group sends made through it and
    ///     failing them on request, as a backplane or the service can fail a group send on its own;
    ///     and stalling the sends to chosen connections, as a client that stopped reading would.
    /// </summary>
    internal sealed class RecordingLifetimeManager : HubLifetimeManager<TellmaHub>
    {
        private readonly DefaultHubLifetimeManager<TellmaHub> _inner = new(NullLogger<DefaultHubLifetimeManager<TellmaHub>>.Instance);

        /// <summary>Orders the stalled sends' records, so each reads the ones before it whole.</summary>
        private readonly Lock _stalledGate = new();

        /// <summary>The groups sent to, in order, by a group or group-except send.</summary>
        public ConcurrentQueue<string> GroupSends { get; } = new();

        /// <summary>Whether a group or group-except send throws after it is recorded.</summary>
        public bool FailGroupSends { get; set; }

        /// <summary>The connections a send to which never completes, by connection id.</summary>
        public ConcurrentDictionary<string, bool> StalledConnections { get; } = new(StringComparer.Ordinal);

        /// <summary>The sends to stalled connections, in the order they started.</summary>
        public ConcurrentQueue<StalledSend> StalledSends { get; } = new();

        /// <summary>
        ///     Composes the manager into a host and makes the tracker see a mode that reaches every
        ///     instance, as a Redis backplane would: the sends still reach this host's connections alone.
        /// </summary>
        /// <param name="options">The host's options.</param>
        /// <returns>The manager, resolved by the hub once the host starts.</returns>
        public static RecordingLifetimeManager Compose(TestHostOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            RecordingLifetimeManager manager = new();
            options.ConfigureServices += services =>
            {
                _ = services.Replace(ServiceDescriptor.Singleton<HubLifetimeManager<TellmaHub>>(manager));
                _ = services.Replace(ServiceDescriptor.Singleton(ReachingEveryInstance()));
            };
            return manager;
        }

        /// <summary>A hosting the tracker reads as a Redis backplane, over the in-process manager.</summary>
        /// <returns>The hosting.</returns>
        public static RealtimeHosting ReachingEveryInstance()
        {
            return new RealtimeHosting(RealtimeMode.Redis, typeof(DefaultHubLifetimeManager<TellmaHub>), azureOptionsType: null);
        }

        /// <inheritdoc />
        public override Task OnConnectedAsync(HubConnectionContext connection)
        {
            return _inner.OnConnectedAsync(connection);
        }

        /// <inheritdoc />
        public override Task OnDisconnectedAsync(HubConnectionContext connection)
        {
            return _inner.OnDisconnectedAsync(connection);
        }

        /// <inheritdoc />
        public override Task SendAllAsync(string methodName, object?[] args, CancellationToken cancellationToken = default)
        {
            return _inner.SendAllAsync(methodName, args, cancellationToken);
        }

        /// <inheritdoc />
        public override Task SendAllExceptAsync(string methodName, object?[] args, IReadOnlyList<string> excludedConnectionIds, CancellationToken cancellationToken = default)
        {
            return _inner.SendAllExceptAsync(methodName, args, excludedConnectionIds, cancellationToken);
        }

        /// <inheritdoc />
        public override Task SendConnectionAsync(string connectionId, string methodName, object?[] args, CancellationToken cancellationToken = default)
        {
            if (!StalledConnections.ContainsKey(connectionId))
            {
                return _inner.SendConnectionAsync(connectionId, methodName, args, cancellationToken);
            }

            // --- A stalled send ignores its token too, as a transport write waiting on a full buffer does ---
            // It is recorded with whether every stalled send before it still waited on its bound
            // when it started: sends made side by side all start before the first bound runs out.
            bool earlierWaiting;
            lock (_stalledGate)
            {
                earlierWaiting = StalledSends.All(static send => !send.Bound.IsCancellationRequested);
                StalledSends.Enqueue(new StalledSend(connectionId, earlierWaiting, cancellationToken));
            }

            return new TaskCompletionSource().Task;
        }

        /// <inheritdoc />
        public override Task SendConnectionsAsync(IReadOnlyList<string> connectionIds, string methodName, object?[] args, CancellationToken cancellationToken = default)
        {
            return _inner.SendConnectionsAsync(connectionIds, methodName, args, cancellationToken);
        }

        /// <inheritdoc />
        public override Task SendGroupAsync(string groupName, string methodName, object?[] args, CancellationToken cancellationToken = default)
        {
            return Group(groupName) ?? _inner.SendGroupAsync(groupName, methodName, args, cancellationToken);
        }

        /// <inheritdoc />
        public override Task SendGroupsAsync(IReadOnlyList<string> groupNames, string methodName, object?[] args, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(groupNames);
            Task? failed = null;
            foreach (string group in groupNames)
            {
                failed ??= Group(group);
            }

            return failed ?? _inner.SendGroupsAsync(groupNames, methodName, args, cancellationToken);
        }

        /// <inheritdoc />
        public override Task SendGroupExceptAsync(string groupName, string methodName, object?[] args, IReadOnlyList<string> excludedConnectionIds, CancellationToken cancellationToken = default)
        {
            return Group(groupName) ?? _inner.SendGroupExceptAsync(groupName, methodName, args, excludedConnectionIds, cancellationToken);
        }

        /// <inheritdoc />
        public override Task SendUserAsync(string userId, string methodName, object?[] args, CancellationToken cancellationToken = default)
        {
            return _inner.SendUserAsync(userId, methodName, args, cancellationToken);
        }

        /// <inheritdoc />
        public override Task SendUsersAsync(IReadOnlyList<string> userIds, string methodName, object?[] args, CancellationToken cancellationToken = default)
        {
            return _inner.SendUsersAsync(userIds, methodName, args, cancellationToken);
        }

        /// <inheritdoc />
        public override Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            return _inner.AddToGroupAsync(connectionId, groupName, cancellationToken);
        }

        /// <inheritdoc />
        public override Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            return _inner.RemoveFromGroupAsync(connectionId, groupName, cancellationToken);
        }

        /// <summary>A send to a stalled connection, as it started.</summary>
        /// <param name="ConnectionId">The connection.</param>
        /// <param name="EarlierStillWaiting">Whether no stalled send before it had reached its bound when it started.</param>
        /// <param name="Bound">The send's token, which its bound cancels.</param>
        public sealed record StalledSend(string ConnectionId, bool EarlierStillWaiting, CancellationToken Bound);

        /// <summary>Records a group send.</summary>
        /// <param name="group">The group.</param>
        /// <returns>A faulted task when group sends fail; otherwise null, for the send to go ahead.</returns>
        private Task? Group(string group)
        {
            GroupSends.Enqueue(group);
            return FailGroupSends ? Task.FromException(new InvalidOperationException("No server connection is available to route the group send.")) : null;
        }
    }
}
