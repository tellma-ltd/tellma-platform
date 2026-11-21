// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Jobs;

namespace Tellma.Core.Abstractions.Notifications
{
    /// <summary>
    ///     One of the calling user's own jobs that has not ended, as the bell lists it: pending,
    ///     running, held or waiting for a confirmation, and requested by the user rather than fired by
    ///     a schedule.
    /// </summary>
    /// <param name="Id">The job's id.</param>
    /// <param name="HandlerKey">The key of the job's handler.</param>
    /// <param name="Status">The job's status.</param>
    /// <param name="ProgressPercent">The progress in percent; <see langword="null" /> when the handler reported none.</param>
    /// <param name="ProgressKey">The resource key of the progress message; <see langword="null" /> for none.</param>
    /// <param name="ProgressArgumentsJson">The progress message's arguments as a JSON object; <see langword="null" /> for none.</param>
    /// <param name="CancelRequestedAt">When a stop was requested; <see langword="null" /> when none was.</param>
    /// <param name="StartedAt">When the job first started running; <see langword="null" /> while it has not.</param>
    /// <param name="CreatedAt">When the job was requested.</param>
    /// <param name="LabelKey">The resource key of the job's own label; <see langword="null" /> for the handler's label.</param>
    /// <param name="LabelArgumentsJson">The label's arguments as a JSON object; <see langword="null" /> for none.</param>
    public sealed record InboxRunningJob(
        int Id,
        string HandlerKey,
        JobStatus Status,
        byte? ProgressPercent,
        string? ProgressKey,
        string? ProgressArgumentsJson,
        DateTimeOffset? CancelRequestedAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset CreatedAt,
        string? LabelKey,
        string? LabelArgumentsJson);
}
