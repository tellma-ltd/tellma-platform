// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     A built-in schedule a feature declares: a row of <c>core.Schedules</c> in the reserved id
    ///     band, seeded by the migrations together with its scheduler state.
    /// </summary>
    /// <param name="Id">The row's id, in the reserved band.</param>
    /// <param name="HandlerKey">The key of the handler whose jobs the schedule enqueues; also the row's code and name.</param>
    /// <param name="Cron">The five-field Unix cron expression.</param>
    /// <param name="Arguments">The arguments each job receives, serialised as JSON; null for none.</param>
    /// <param name="TimeZoneId">A zone of the platform's time-zone set; null for the tenant's zone at fire time.</param>
    public sealed record BuiltInScheduleContributionItem(int Id, string HandlerKey, string Cron, object? Arguments, string? TimeZoneId)
        : FeatureContributionItem;
}
