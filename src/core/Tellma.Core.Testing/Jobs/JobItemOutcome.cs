// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Jobs;

namespace Tellma.Core.Testing.Jobs
{
    /// <summary>The outcome a job handler marked one item with, as <see cref="JobItemProbe.OutcomeOf{TItem}" /> reads it.</summary>
    /// <param name="Kind">Which of succeed, retry and fail the handler called last.</param>
    /// <param name="RetryAfter">A retry's delay, shortened to the ceiling as the item stores it; null for the handler's backoff, or for another kind.</param>
    /// <param name="Error">The error a retry or a failure recorded; null for none.</param>
    public sealed record JobItemOutcome(JobItemOutcomeKind Kind, TimeSpan? RetryAfter, JobError? Error);
}
