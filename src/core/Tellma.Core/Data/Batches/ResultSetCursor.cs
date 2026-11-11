// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data.Common;
using System.Globalization;

namespace Tellma.Core.Data.Batches
{
    /// <summary>
    ///     Hands a round trip's result sets to its statements one at a time, in order, and counts
    ///     them, so a statement that reads a set the batch did not return, or a set no statement
    ///     reads, is a defect reported by number rather than a misread.
    /// </summary>
    /// <param name="reader">The command's reader, positioned on the first result set if there is one.</param>
    internal sealed class ResultSetCursor(DbDataReader reader)
    {
        /// <summary>Whether the reader's first position has been examined.</summary>
        private bool _started;

        /// <summary>The result sets handed out so far.</summary>
        public int SetsRead { get; private set; }

        /// <summary>The rows the statements' readers read so far.</summary>
        public long RowsRead { get; private set; }

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

        /// <summary>Counts rows a statement's reader read.</summary>
        /// <param name="rows">The rows.</param>
        public void CountRows(long rows)
        {
            RowsRead += rows;
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
