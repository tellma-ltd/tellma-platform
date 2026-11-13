// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.AspNetCore.Sessions;

namespace Tellma.Core.AspNetCore.Tests.Infrastructure
{
    /// <summary>
    ///     The catalog's session table in memory, with the statements' semantics: reads skip expired
    ///     rows, a renewal is guarded by the tokens version, a replace is a compare-and-set, and the
    ///     sweep deletes rows expired for more than a day, in batches.
    /// </summary>
    /// <param name="time">The clock that stands in for the database's.</param>
    internal sealed class InMemorySessionRows(TimeProvider time) : ISessionRows
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, Row> _rows = new(StringComparer.Ordinal);

        /// <summary>How many rows the table holds, expired ones included.</summary>
        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _rows.Count;
                }
            }
        }

        /// <summary>How many of the next token replacements fail as an unreachable catalog would.</summary>
        public int FailingReplacements { get; set; }

        /// <summary>How many token replacements were attempted, failed ones included.</summary>
        public int ReplaceAttempts { get; private set; }

        /// <summary>How many renewals were written.</summary>
        public int Renewals { get; private set; }

        /// <summary>A row's expiry, or null when there is no row.</summary>
        /// <param name="key">The key.</param>
        /// <returns>The expiry.</returns>
        public DateTimeOffset? ExpiresAt(string key)
        {
            lock (_gate)
            {
                return _rows.TryGetValue(key, out Row? row) ? row.ExpiresAt : null;
            }
        }

        /// <summary>A row's tokens version, or null when there is no row.</summary>
        /// <param name="key">The key.</param>
        /// <returns>The version.</returns>
        public int? TokensVersion(string key)
        {
            lock (_gate)
            {
                return _rows.TryGetValue(key, out Row? row) ? row.TokensVersion : null;
            }
        }

        /// <summary>The keys of the rows, for a test that holds none of them otherwise.</summary>
        /// <returns>The keys.</returns>
        public IReadOnlyList<string> Keys()
        {
            lock (_gate)
            {
                return [.. _rows.Keys];
            }
        }

        /// <summary>Adds a row with any expiry, for the sweep's tests.</summary>
        /// <param name="key">The key.</param>
        /// <param name="subject">The subject.</param>
        /// <param name="expiresAt">The expiry.</param>
        public void Seed(string key, string subject, DateTimeOffset expiresAt)
        {
            lock (_gate)
            {
                _rows[key] = new Row(SessionKeys.HandleOf(key), subject, null, [], expiresAt, 0);
            }
        }

        /// <inheritdoc />
        public Task InsertAsync(string key, string handle, string subject, string? sid, byte[] ticket, DateTimeOffset expiresAt, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                // The primary key refuses a second row with the same key.
                if (!_rows.TryAdd(key, new Row(handle, subject, sid, ticket, expiresAt, 0)))
                {
                    throw new InvalidOperationException("A session row with this key already exists.");
                }
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<SessionRow?> ReadAsync(string key, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return Task.FromResult(_rows.TryGetValue(key, out Row? row) && row.ExpiresAt > time.GetUtcNow()
                    ? new SessionRow(row.Subject, row.Sid, row.Ticket, row.TokensVersion, row.ExpiresAt)
                    : null);
            }
        }

        /// <inheritdoc />
        public Task<bool> RenewAsync(string key, byte[] ticket, DateTimeOffset expiresAt, int tokensVersion, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (!_rows.TryGetValue(key, out Row? row) || row.TokensVersion != tokensVersion)
                {
                    return Task.FromResult(false);
                }

                _rows[key] = row with { Ticket = ticket, ExpiresAt = expiresAt };
                Renewals++;
                return Task.FromResult(true);
            }
        }

        /// <inheritdoc />
        public Task<int?> ReplaceAsync(string key, byte[] ticket, DateTimeOffset expiresAt, int expectedVersion, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                ReplaceAttempts++;
                if (FailingReplacements > 0)
                {
                    FailingReplacements--;
                    throw new InvalidOperationException("The catalog cannot be reached.");
                }

                if (!_rows.TryGetValue(key, out Row? row) || row.TokensVersion != expectedVersion)
                {
                    return Task.FromResult<int?>(null);
                }

                _rows[key] = row with { Ticket = ticket, ExpiresAt = expiresAt, TokensVersion = expectedVersion + 1 };
                return Task.FromResult<int?>(expectedVersion + 1);
            }
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return Task.FromResult(_rows.Remove(key));
            }
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<EndedSession>> DeleteBySidAsync(string sid, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<EndedSession>>(DeleteWhere(row => string.Equals(row.Sid, sid, StringComparison.Ordinal)));
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<EndedSession>> DeleteBySubjectAsync(string subject, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<EndedSession>>(DeleteWhere(row => string.Equals(row.Subject, subject, StringComparison.Ordinal)));
        }

        /// <inheritdoc />
        public Task<int> SweepBatchAsync(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                DateTimeOffset cutoff = time.GetUtcNow().AddDays(-1);
                string[] expired = [.. _rows.Where(pair => pair.Value.ExpiresAt < cutoff).Select(static pair => pair.Key).Take(SqlSessionRows.SweepBatchSize)];
                foreach (string key in expired)
                {
                    _ = _rows.Remove(key);
                }

                return Task.FromResult(expired.Length);
            }
        }

        /// <summary>Deletes the rows a predicate matches, answering what was deleted.</summary>
        /// <param name="predicate">Which rows.</param>
        /// <returns>The deleted sessions.</returns>
        private List<EndedSession> DeleteWhere(Func<Row, bool> predicate)
        {
            lock (_gate)
            {
                List<EndedSession> ended = [];
                foreach (KeyValuePair<string, Row> pair in _rows.Where(pair => predicate(pair.Value)).ToList())
                {
                    _ = _rows.Remove(pair.Key);
                    ended.Add(new EndedSession(pair.Key, pair.Value.Handle, pair.Value.Subject));
                }

                return ended;
            }
        }

        /// <summary>One row.</summary>
        /// <param name="Handle">The handle.</param>
        /// <param name="Subject">The subject.</param>
        /// <param name="Sid">The identity server's session id.</param>
        /// <param name="Ticket">The protected ticket.</param>
        /// <param name="ExpiresAt">The expiry.</param>
        /// <param name="TokensVersion">The tokens version.</param>
        private sealed record Row(string Handle, string Subject, string? Sid, byte[] Ticket, DateTimeOffset ExpiresAt, int TokensVersion);
    }
}
