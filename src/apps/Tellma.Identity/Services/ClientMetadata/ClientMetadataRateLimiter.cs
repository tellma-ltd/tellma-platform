// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using Tellma.Identity.Options;

namespace Tellma.Identity.Services.ClientMetadata
{
    /// <summary>
    ///     The per-host fetch limit: at most
    ///     <see cref="TellmaIdentityClientMetadataOptions.FetchesPerHostPerMinute" /> fetches to
    ///     one host in any sliding minute, documents and key sets together. It is per instance:
    ///     a deployment of several instances sends a host at most that many per instance.
    /// </summary>
    /// <param name="options">The engine options.</param>
    /// <param name="timeProvider">The clock the window slides on.</param>
    public sealed class ClientMetadataRateLimiter(IOptions<TellmaIdentityOptions> options, TimeProvider timeProvider)
    {
        /// <summary>The window the limit counts over.</summary>
        public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

        /// <summary>
        ///     How many hosts are tracked before the table is swept of hosts with no fetch in the
        ///     window. While every tracked host is still inside its window, a new host is refused,
        ///     so the table stays bounded under a flood of distinct hosts.
        /// </summary>
        public const int MaxTrackedHosts = 10_000;

        /// <summary>Guards <see cref="_hosts" />.</summary>
        private readonly Lock _gate = new();

        /// <summary>Each tracked host's fetch times inside the window, oldest first, keyed by host (compared ignoring case).</summary>
        private readonly Dictionary<string, Queue<DateTimeOffset>> _hosts = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Takes one fetch from a host's quota.</summary>
        /// <param name="host">The host the fetch is sent to.</param>
        /// <returns>True when the fetch may be sent; false when the host's quota for the window is spent.</returns>
        public bool TryAcquire(string host)
        {
            ArgumentException.ThrowIfNullOrEmpty(host);

            int limit = options.Value.ClientMetadata.FetchesPerHostPerMinute;
            DateTimeOffset now = timeProvider.GetUtcNow();
            DateTimeOffset windowStart = now - Window;

            lock (_gate)
            {
                if (!_hosts.TryGetValue(host, out Queue<DateTimeOffset>? fetches))
                {
                    if (_hosts.Count >= MaxTrackedHosts && !SweepIdleHosts(windowStart))
                    {
                        return false;
                    }

                    fetches = new Queue<DateTimeOffset>();
                    _hosts[host] = fetches;
                }

                Expire(fetches, windowStart);
                if (fetches.Count >= limit)
                {
                    return false;
                }

                fetches.Enqueue(now);
                return true;
            }
        }

        /// <summary>Drops the fetch times that have left the window.</summary>
        private static void Expire(Queue<DateTimeOffset> fetches, DateTimeOffset windowStart)
        {
            while (fetches.Count > 0 && fetches.Peek() <= windowStart)
            {
                fetches.Dequeue();
            }
        }

        /// <summary>Removes the hosts with no fetch left in the window.</summary>
        /// <returns>Whether any room was made.</returns>
        private bool SweepIdleHosts(DateTimeOffset windowStart)
        {
            List<string> idle = [];
            foreach ((string host, Queue<DateTimeOffset> fetches) in _hosts)
            {
                Expire(fetches, windowStart);
                if (fetches.Count == 0)
                {
                    idle.Add(host);
                }
            }

            foreach (string host in idle)
            {
                _hosts.Remove(host);
            }

            return idle.Count > 0;
        }
    }
}
