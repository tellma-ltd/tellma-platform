// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>
    ///     The fixed limits of the job contract: the widths of the job table's columns and the ranges
    ///     a handler's declaration and a handler's calls are held to. They are not configuration: each
    ///     is the width of a column or a bound the statements rely on.
    /// </summary>
    public static class JobLimits
    {
        /// <summary>
        ///     The most characters a handler key, a label key, a progress message key or an error code
        ///     may have: the width of the job table's key columns.
        /// </summary>
        public const int MaxKeyLength = 64;

        /// <summary>
        ///     The most characters the arguments of a label, a progress message or an error may take once
        ///     serialised: the width of the job table's argument columns. More is a programming error,
        ///     refused with <see cref="ArgumentException" /> where the arguments are handed over.
        /// </summary>
        public const int MaxArgumentsLength = 4000;

        /// <summary>
        ///     The most bytes a job's or a schedule's handler arguments may take once serialised, counted
        ///     as UTF-8: 64 KB. More is a programming error at enqueue, refused with
        ///     <see cref="ArgumentException" />.
        /// </summary>
        public const int MaxHandlerArgumentsBytes = 65_536;

        /// <summary>The most characters a schedule's cron expression may have: the width of its column.</summary>
        public const int MaxCronExpressionLength = 128;

        /// <summary>The most jobs one claim of a handler's key may lease: the most a handler's batch size may be.</summary>
        public const int MaxBatchSize = 500;

        /// <summary>
        ///     The shortest lease a handler may declare, in seconds. The lease is renewed every fifth of
        ///     its length, so even the shortest survives one missed renewal.
        /// </summary>
        public const int MinLeaseSeconds = 15;

        /// <summary>The highest progress a job reports, in percent.</summary>
        public const byte MaxProgressPercent = 100;

        /// <summary>
        ///     The most bytes, counted as UTF-8, the warnings a job waits to have confirmed are kept in
        ///     once serialised: 64 KB. Past it, warnings are dropped from the end, but never the first of
        ///     a key, so the record names every key.
        /// </summary>
        public const int MaxPendingConfirmationsBytes = 65_536;

        /// <summary>The most row ids each recorded warning of a job waiting for confirmation keeps.</summary>
        public const int MaxPendingConfirmationIds = 100;

        /// <summary>
        ///     The most distinct warning keys one confirmation covers. A job that raises more fails
        ///     with <see cref="JobErrorCodes.TooManyConfirmations" /> instead of waiting.
        /// </summary>
        public const int MaxConfirmationKeys = 16;

        /// <summary>
        ///     The most characters a job's confirmed keys may take, comma-separated: the width of their
        ///     column. A job whose keys would take more fails with
        ///     <see cref="JobErrorCodes.TooManyConfirmations" /> instead of waiting.
        /// </summary>
        public const int MaxConfirmedKeysLength = 2000;

        /// <summary>The longest delay an explicit retry may ask for; a longer delay is shortened to it.</summary>
        public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromDays(30);
    }
}
