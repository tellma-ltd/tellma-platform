// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.AspNetCore.Tests.Infrastructure
{
    /// <summary>A session termination listener that records what it is told.</summary>
    internal sealed class RecordingListener : ISessionTerminationListener
    {
        /// <summary>The ended sessions, in the order told.</summary>
        public ConcurrentQueue<(string Subject, IReadOnlyList<string> Handles)> Ended { get; } = new();

        /// <summary>Every handle told, flattened.</summary>
        public IReadOnlyList<string> Handles => [.. Ended.SelectMany(static ended => ended.Handles)];

        /// <inheritdoc />
        public Task SessionsTerminatedAsync(string subject, IReadOnlyList<string> sessionHandles)
        {
            Ended.Enqueue((subject, [.. sessionHandles]));
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task TenantAccessRevokedAsync(int tenantId, string subject)
        {
            return Task.CompletedTask;
        }
    }
}
