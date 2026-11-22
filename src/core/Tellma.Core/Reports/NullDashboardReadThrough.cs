// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Reports;
using Tellma.Core.Queryex;

namespace Tellma.Core.Reports
{
    // Spec 0033 §5.7: until the dashboards' feature registers its own, no report is shown on any
    // dashboard: no run reads through one, and no delete is blocked by one.
    /// <summary>The read-through of a composition without dashboards: no dashboard is readable and none shows a report.</summary>
    internal sealed class NullDashboardReadThrough : IDashboardReadThrough
    {
        /// <inheritdoc />
        public Task<FilterTree?> ReadThroughAsync(string dashboardParameter)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(dashboardParameter);
            return Task.FromResult<FilterTree?>(null);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<(int ReportId, int Id, string? Title)>> DashboardsUsing(IReadOnlyList<int> reportIds)
        {
            ArgumentNullException.ThrowIfNull(reportIds);
            return Task.FromResult<IReadOnlyList<(int ReportId, int Id, string? Title)>>([]);
        }
    }
}
