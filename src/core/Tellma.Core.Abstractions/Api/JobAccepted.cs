// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     The answer of an operation that hands its work to a background job: the job to follow. The
    ///     web edge answers it with 202 and carries the polling target in the body, not in a
    ///     <c>Location</c> header.
    /// </summary>
    /// <param name="JobId">The job's id.</param>
    /// <param name="ResourceId">
    ///     The row the job works on when one exists already, such as an import's row; null for an
    ///     export, whose row the job inserts when it completes, and for a schedule's immediate run.
    /// </param>
    public sealed record JobAccepted(int JobId, int? ResourceId);
}
