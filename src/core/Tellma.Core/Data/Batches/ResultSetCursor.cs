// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data.Common;
using System.Globalization;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Data.Batches
{
    /// <summary>
    ///     Hands one attempt's result sets to its statements one at a time, in order, and counts them,
    ///     so a statement that reads a set the batch did not return, or a set no statement reads, is a
    ///     defect reported by number rather than a misread. It also holds what the attempt's readers
    ///     produced until the executor knows the attempt counts: the results they completed, and the
    ///     rows a statement selected right before it threw.
    /// </summary>
    /// <param name="reader">The command's reader, positioned on the first result set if there is one.</param>
    internal sealed class ResultSetCursor(DbDataReader reader)
    {
        /// <summary>The completions the readers staged, applied once the attempt is known to count.</summary>
        private readonly List<Action> _completions = [];

        /// <summary>Whether the reader's first position has been examined.</summary>
        private bool _started;

        /// <summary>Whether the reader stands on a set that was examined but not handed out.</summary>
        private bool _pending;

        /// <summary>The result sets handed out so far.</summary>
        public int SetsRead { get; private set; }

        /// <summary>The rows the statements' readers read so far.</summary>
        public long RowsRead { get; private set; }

        /// <summary>The conflict rows a concurrency guard reported before it threw, if one did.</summary>
        public IReadOnlyList<ConflictRow>? Conflicts { get; private set; }

        /// <summary>The ids a not-found check reported before it threw, if one did.</summary>
        public IReadOnlyList<long>? MissingIds { get; private set; }

        /// <summary>
        ///     Moves to the next result set. An error the server raised before it surfaces here, after
        ///     every earlier set was read.
        /// </summary>
        /// <param name="ordinal">The reading statement's ordinal, for the message.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The reader, positioned on the set.</returns>
        /// <exception cref="InvalidOperationException">No result set is left.</exception>
        public async ValueTask<DbDataReader> NextAsync(int ordinal, CancellationToken cancellationToken)
        {
            if (!await AdvanceAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Batch statement {ordinal} reads result set {SetsRead + 1}, but the round trip returned {SetsRead}. A statement's declared result-set count must match what its text returns."));
            }

            SetsRead++;
            return reader;
        }

        /// <summary>
        ///     Moves to the next result set when it is the reading statement's own, as the statement
        ///     tells by its shape, and otherwise leaves it for the statement it belongs to. A statement
        ///     that selects a set only before it throws (a concurrency guard's conflict rows, a
        ///     not-found check's missing ids) reads that set this way.
        /// </summary>
        /// <param name="isOwn">Whether a set, by its columns, is the statement's.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The reader positioned on the set, or null when the next set is not the statement's or none is left.</returns>
        public async ValueTask<DbDataReader?> NextIfAsync(Func<DbDataReader, bool> isOwn, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(isOwn);
            if (!await AdvanceAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            if (!isOwn(reader))
            {
                // The next NextAsync or EndAsync hands out this set instead of moving past it.
                _pending = true;
                return null;
            }

            SetsRead++;
            return reader;
        }

        /// <summary>Counts rows a statement's reader read.</summary>
        /// <param name="rows">The rows.</param>
        public void CountRows(long rows)
        {
            RowsRead += rows;
        }

        /// <summary>
        ///     Stages a statement's result: it completes once the attempt that read it is known to
        ///     count, so an attempt the executor re-runs completes nothing.
        /// </summary>
        /// <typeparam name="T">The result's type.</typeparam>
        /// <param name="result">The statement's handle.</param>
        /// <param name="value">The value read.</param>
        public void Complete<T>(BatchResult<T> result, T value)
        {
            ArgumentNullException.ThrowIfNull(result);
            _completions.Add(() => result.Complete(value));
        }

        /// <summary>
        ///     Stages a statement's result whose value is settled only when the attempt counts: a
        ///     result whose settling has effects beyond the batch, such as storing what was read in a
        ///     cache, has them once, and never for an attempt the executor re-runs or discards.
        /// </summary>
        /// <typeparam name="T">The result's type.</typeparam>
        /// <param name="result">The statement's handle.</param>
        /// <param name="settle">Settles the value, when the results complete.</param>
        public void Settle<T>(BatchResult<T> result, Func<T> settle)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(settle);
            _completions.Add(() => result.Complete(settle()));
        }

        /// <summary>Completes every result the attempt's readers staged, in the order they were read.</summary>
        public void ApplyCompletions()
        {
            foreach (Action completion in _completions)
            {
                completion();
            }

            _completions.Clear();
        }

        /// <summary>
        ///     Reads a concurrency guard's conflict set, on which the reader stands, and reports its
        ///     rows for the exception its <c>THROW</c> becomes. The set's columns are <c>Id</c>,
        ///     <c>Reason</c> (<c>'C'</c> or <c>'M'</c> for missing), <c>ModifiedAt</c>,
        ///     <c>ModifiedById</c>, <c>Name</c>, <c>Name2</c> and <c>Name3</c>.
        /// </summary>
        /// <param name="set">The reader, positioned on the conflict set.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>A task that completes when the set is read.</returns>
        public async ValueTask ReadConflictsAsync(DbDataReader set, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(set);
            int id = set.GetOrdinal("Id");
            int reason = set.GetOrdinal("Reason");
            int modifiedAt = set.GetOrdinal("ModifiedAt");
            int modifiedById = set.GetOrdinal("ModifiedById");
            int name = set.GetOrdinal("Name");
            int name2 = set.GetOrdinal("Name2");
            int name3 = set.GetOrdinal("Name3");

            List<ConflictRow> rows = [];
            while (await set.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(new ConflictRow(
                    Convert.ToInt64(set.GetValue(id), CultureInfo.InvariantCulture),
                    set.GetString(reason) == "M",
                    set.IsDBNull(modifiedAt) ? null : set.GetFieldValue<DateTimeOffset>(modifiedAt),
                    set.IsDBNull(modifiedById) ? null : set.GetInt32(modifiedById),
                    set.IsDBNull(name) ? null : set.GetString(name),
                    set.IsDBNull(name2) ? null : set.GetString(name2),
                    set.IsDBNull(name3) ? null : set.GetString(name3)));
            }

            CountRows(rows.Count);
            Conflicts = rows;
        }

        /// <summary>
        ///     Reads a not-found check's set of missing ids, on which the reader stands (one id
        ///     column, the first), and reports them for the exception its <c>THROW</c> becomes.
        /// </summary>
        /// <param name="set">The reader, positioned on the set.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>A task that completes when the set is read.</returns>
        public async ValueTask ReadMissingIdsAsync(DbDataReader set, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(set);
            List<long> ids = [];
            while (await set.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                ids.Add(Convert.ToInt64(set.GetValue(0), CultureInfo.InvariantCulture));
            }

            CountRows(ids.Count);
            MissingIds = ids;
        }

        /// <summary>
        ///     Confirms that no result set is left once every statement has read its own, which also
        ///     surfaces an error the server raised after the last set.
        /// </summary>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <exception cref="InvalidOperationException">A result set is left.</exception>
        public async ValueTask EndAsync(CancellationToken cancellationToken)
        {
            if (await AdvanceAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The round trip returned more result sets than the {SetsRead} its statements declare. A statement's declared result-set count must match what its text returns."));
            }
        }

        /// <summary>Moves the reader to the next unread result set.</summary>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>Whether there is one.</returns>
        private async ValueTask<bool> AdvanceAsync(CancellationToken cancellationToken)
        {
            // A set a statement examined and left for its owner is the next set as it stands.
            if (_pending)
            {
                _pending = false;
                return true;
            }

            // The reader starts on the first result set, if any: a set has at least one column,
            // while a round trip without one reports none.
            if (!_started)
            {
                _started = true;
                return reader.FieldCount > 0;
            }

            return await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
