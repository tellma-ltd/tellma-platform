// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Data.Batches
{
    /// <summary>What the executor does with a failed round trip.</summary>
    internal enum RetryAction
    {
        /// <summary>The failure surfaces, translated when the server raised it.</summary>
        Surface,

        /// <summary>The whole round trip is sent again, when retries are left.</summary>
        Rerun,

        /// <summary>The commit probe decides: committed, not committed and re-run, or unknown.</summary>
        Probe,

        /// <summary>Nothing can tell whether the batch committed: <see cref="DataAccessAmbiguousException" /> with <see cref="CommitVerdict.Unknown" />.</summary>
        Unknown,

        /// <summary>The failure is transient but the round trip is never re-run: <see cref="DataAccessRetryExhaustedException" />.</summary>
        GiveUp,
    }

    // The rules are spec 0012 §6.5's.
    /// <summary>The executor's retry rules: what a failure leads to, and how long to wait before a re-run.</summary>
    internal static class RetryRules
    {
        /// <summary>
        ///     The re-runs a read or validation batch gets after its command timed out: one. A query that
        ///     outran its timeout under load is likely to again, and each try holds the caller for the
        ///     whole timeout while it adds load to a server that is already slow.
        /// </summary>
        public const int ReadTimeoutReruns = 1;

        /// <summary>Each re-run waits this many times longer than the one before.</summary>
        private const double Growth = 4;

        /// <summary>The jitter's half-width: a delay varies by up to a quarter either way.</summary>
        private const double Jitter = 0.25;

        /// <summary>Decides what a failed round trip leads to.</summary>
        /// <param name="kind">The failure's kind.</param>
        /// <param name="sent">Whether the batch's command was sent; nothing ran before it was.</param>
        /// <param name="mode">The batch's transaction mode.</param>
        /// <param name="committing">Whether an explicit transaction's commit failed, after the body ran.</param>
        /// <param name="mayRunTwice">Whether every statement may run twice.</param>
        /// <param name="probe">Whether the commit probe can answer: a persist batch in its own transaction that declared its inserted rows.</param>
        /// <param name="writes">Whether the batch declares a write.</param>
        /// <returns>The action.</returns>
        public static RetryAction Decide(FailureKind kind, bool sent, TransactionMode mode, bool committing, bool mayRunTwice, bool probe, bool writes)
        {
            // --- Before the send nothing ran: a transient failure (an open, a begin) is safe to repeat ---
            return !sent ? (kind == FailureKind.Reported ? RetryAction.Rerun : RetryAction.Surface)

                // --- An explicit transaction is never re-run: the caller re-runs its whole unit of work ---
                : mode == TransactionMode.Explicit ? ExplicitAfterSend(kind, committing)
                : AfterSend(kind, mayRunTwice, probe, writes);
        }

        /// <summary>
        ///     Whether a batch whose command timed out may be re-run once more: a read or validation
        ///     batch within <see cref="ReadTimeoutReruns" />, any other within the attempts allowed.
        /// </summary>
        /// <param name="purpose">The batch's purpose.</param>
        /// <param name="timeoutReruns">The re-runs that already followed a timeout.</param>
        /// <returns>Whether the re-run is within the budget.</returns>
        public static bool MayRerunAfterTimeout(BatchPurpose purpose, int timeoutReruns)
        {
            return purpose is not (BatchPurpose.Read or BatchPurpose.Validate) || timeoutReruns < ReadTimeoutReruns;
        }

        /// <summary>
        ///     The wait before a re-run: the base delay times four to the power of the re-runs before
        ///     it, varied by up to a quarter either way, so 50, 200 and 800 ms from a 50 ms base.
        /// </summary>
        /// <param name="retry">The re-run, from 1.</param>
        /// <param name="baseDelay">The first re-run's delay before jitter.</param>
        /// <param name="unit">A uniform value in [0, 1) that picks the jitter.</param>
        /// <returns>The delay.</returns>
        public static TimeSpan Delay(int retry, TimeSpan baseDelay, double unit)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(retry, 1);
            ArgumentOutOfRangeException.ThrowIfNegative(unit);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(unit, 1);

            double factor = Math.Pow(Growth, retry - 1) * (1 - Jitter + (2 * Jitter * unit));
            return baseDelay * factor;
        }

        /// <summary>What a failure of an explicit-transaction batch after the send leads to.</summary>
        /// <param name="kind">The failure's kind.</param>
        /// <param name="committing">Whether the commit failed, after the body ran.</param>
        /// <returns>The action.</returns>
        private static RetryAction ExplicitAfterSend(FailureKind kind, bool committing)
        {
            // A lost commit may have committed; a transient failure before it rolled back, but only the
            // caller can re-run what else its transaction did.
            return committing ? RetryAction.Unknown
                : kind is FailureKind.Reported or FailureKind.Ambiguous ? RetryAction.GiveUp
                : RetryAction.Surface;
        }

        /// <summary>What a failure of a batch in its own or no transaction after the send leads to.</summary>
        /// <param name="kind">The failure's kind.</param>
        /// <param name="mayRunTwice">Whether every statement may run twice.</param>
        /// <param name="probe">Whether the commit probe can answer.</param>
        /// <param name="writes">Whether the batch declares a write.</param>
        /// <returns>The action.</returns>
        private static RetryAction AfterSend(FailureKind kind, bool mayRunTwice, bool probe, bool writes)
        {
            return kind switch
            {
                // Under XACT_ABORT the server rolled the batch back, so the whole of it runs again.
                FailureKind.Reported => RetryAction.Rerun,

                // Whether the batch committed is unknown: re-run what may run twice, else probe.
                FailureKind.Ambiguous => mayRunTwice ? RetryAction.Rerun : probe ? RetryAction.Probe : RetryAction.Unknown,

                // A defect after the send: a write may have committed before the client saw the
                // failure, and a defect would fail again, so it is never re-run.
                FailureKind.Defect => !writes ? RetryAction.Surface : probe ? RetryAction.Probe : RetryAction.Unknown,
                FailureKind.Final => RetryAction.Surface,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a failure kind."),
            };
        }
    }
}
