// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>What a raw statement appended through <see cref="IDataBatch.Sql" /> declares about itself.</summary>
    public sealed record SqlOptions
    {
        /// <summary>The options of a read that returns no result set.</summary>
        public static SqlOptions None { get; } = new();

        /// <summary>Creates the options.</summary>
        /// <param name="writes">
        ///     The tables the statement writes; required for any text that inserts, updates or
        ///     deletes, because the batch's version-tag bumps and the write audit are derived from it.
        ///     Null or empty for a read.
        /// </param>
        /// <param name="idempotent">
        ///     Whether running the statement twice has the effect of running it once, which lets an
        ///     identical statement collapse into one and the executor re-run the round trip after a
        ///     dropped connection.
        /// </param>
        /// <param name="resultSets">How many result sets the statement returns; zero or more.</param>
        /// <param name="userIds">
        ///     A table variable or <c>IdList</c> parameter the batch declared that holds the ids of the
        ///     users the statement affects; required when <paramref name="writes" /> touches a table
        ///     that carries a user-level tag rule, because those bumps cannot be derived from raw text.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="resultSets" /> is negative.</exception>
        /// <exception cref="ArgumentException"><paramref name="writes" /> holds a null entry.</exception>
        public SqlOptions(IReadOnlySet<TableName>? writes = null, bool idempotent = false, int resultSets = 0, SqlIdentifier? userIds = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(resultSets);
            if (writes is not null && writes.Contains(null!))
            {
                throw new ArgumentException("The written tables must not hold a null entry.", nameof(writes));
            }

            Writes = writes is null || writes.Count == 0 ? [] : writes.ToFrozenSet();
            Idempotent = idempotent;
            ResultSets = resultSets;
            UserIds = userIds;
        }

        /// <summary>The tables the statement writes; empty for a read.</summary>
        public IReadOnlySet<TableName> Writes { get; }

        /// <summary>Whether running the statement twice has the effect of running it once.</summary>
        public bool Idempotent { get; }

        /// <summary>How many result sets the statement returns.</summary>
        public int ResultSets { get; }

        /// <summary>The batch-local table holding the ids of the users the statement affects, if any.</summary>
        public SqlIdentifier? UserIds { get; }

        /// <summary>
        ///     The options of a self-service write: the calling user's own rows, so the affected
        ///     users are the one-row table <c>@tm_callerIds</c> the connect prologue declares.
        /// </summary>
        /// <param name="writes">The tables the statement writes.</param>
        /// <returns>The options.</returns>
        public static SqlOptions ForCaller(IReadOnlySet<TableName> writes)
        {
            ArgumentNullException.ThrowIfNull(writes);
            return new SqlOptions(writes, userIds: new SqlIdentifier(ReservedSqlNames.CallerIds));
        }

        /// <inheritdoc />
        public bool Equals(SqlOptions? other)
        {
            return other is not null
                && Writes.SetEquals(other.Writes)
                && Idempotent == other.Idempotent
                && ResultSets == other.ResultSets
                && UserIds == other.UserIds;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            // Order-independent over the set, so equal sets hash alike.
            int writes = 0;
            foreach (TableName table in Writes)
            {
                writes ^= table.GetHashCode();
            }

            return HashCode.Combine(writes, Writes.Count, Idempotent, ResultSets, UserIds);
        }
    }
}
