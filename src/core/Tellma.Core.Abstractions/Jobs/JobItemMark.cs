// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>The explicit outcomes a handler marks an item with.</summary>
    internal enum JobItemMarkKind
    {
        /// <summary><see cref="JobItem{TItem}.Succeed" />.</summary>
        Succeeded,

        /// <summary><see cref="JobItem{TItem}.Retry" />.</summary>
        Retry,

        /// <summary><see cref="JobItem{TItem}.Fail" />.</summary>
        Failed,
    }

    /// <summary>
    ///     The outcome a handler marked an item with, as the worker reads it after the handler
    ///     returns: the last call wins.
    /// </summary>
    /// <param name="Kind">The outcome.</param>
    /// <param name="RetryAfter">
    ///     For a retry, the delay the handler asked for, at most <see cref="JobLimits.MaxRetryDelay" />;
    ///     null for the handler's backoff, and for the other outcomes.
    /// </param>
    /// <param name="Error">The error of a failure, or of a retry that named one; null otherwise.</param>
    /// <param name="ErrorArgumentsJson">The error's arguments as the job table stores them; null when it has none.</param>
    internal sealed record JobItemMark(JobItemMarkKind Kind, TimeSpan? RetryAfter, JobError? Error, string? ErrorArgumentsJson)
    {
        /// <summary>The mark of <see cref="JobItem{TItem}.Succeed" />, shared since it carries nothing.</summary>
        public static JobItemMark Succeeded { get; } = new(JobItemMarkKind.Succeeded, null, null, null);
    }
}
