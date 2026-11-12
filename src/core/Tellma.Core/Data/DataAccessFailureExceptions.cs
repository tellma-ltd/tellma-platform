// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;

namespace Tellma.Core.Data
{
    /// <summary>
    ///     A round trip failed with a transient error, or lost its connection, on every attempt the
    ///     executor allows, or once on a batch the executor never re-runs. Nothing it carried is
    ///     committed.
    /// </summary>
    /// <param name="innerException">The last attempt's failure.</param>
    /// <param name="attempts">How many times the round trip was sent.</param>
    public sealed class DataAccessRetryExhaustedException(Exception innerException, int attempts)
        : DataAccessException(
            string.Create(CultureInfo.InvariantCulture, $"The round trip failed transiently on each of its {attempts} attempts."),
            innerException: innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
        /// <summary>How many times the round trip was sent.</summary>
        public int Attempts { get; } = attempts;
    }

    /// <summary>
    ///     A writing round trip failed where its batch may have committed, and it could not be re-run:
    ///     it lost its connection, its reading failed, it raised an error that need not have aborted
    ///     it, it was cancelled once sent, or an earlier attempt re-run without a verdict may have
    ///     committed. <see cref="Verdict" /> says what the commit probe found:
    ///     <see cref="CommitVerdict.Committed" /> when the write succeeded and only its results were
    ///     lost, <see cref="CommitVerdict.Unknown" /> when nothing could tell.
    /// </summary>
    /// <param name="innerException">The failure that made the outcome ambiguous.</param>
    /// <param name="verdict">The commit probe's verdict.</param>
    public sealed class DataAccessAmbiguousException(Exception innerException, CommitVerdict verdict)
        : DataAccessException(
            $"The round trip failed where its batch may have committed; the commit probe's verdict is {verdict}.",
            innerException: innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
        /// <summary>The commit probe's verdict.</summary>
        public CommitVerdict Verdict { get; } = verdict;
    }

    /// <summary>Whether the first attempt of an ambiguous persist round trip committed.</summary>
    public enum CommitVerdict
    {
        /// <summary>Every row the attempt inserted exists: it committed, and only its results were lost.</summary>
        Committed,

        /// <summary>No row the attempt inserted exists: it rolled back, and the round trip may be re-run.</summary>
        NotCommitted,

        /// <summary>Nothing could tell: the batch inserted no row to probe for, the probe failed, or no probe ran.</summary>
        Unknown,
    }
}
