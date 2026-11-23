// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Reports
{
    /// <summary>
    ///     How reports reach into dashboards: the reports a dashboard's widgets show, which a run may
    ///     read through the dashboard, and the dashboards that show given reports, which block their
    ///     delete. Implemented by the dashboards' feature; without it, no report is shown on any
    ///     dashboard. Scoped: it answers for the scope's caller.
    /// </summary>
    public interface IDashboardReadThrough
    {
        /// <summary>
        ///     The filter, rooted at the reports, of the reports a widget of a dashboard shows, when the
        ///     caller may read that dashboard. A run admits a report through it; it is never an access
        ///     criterion on the reports.
        /// </summary>
        /// <param name="dashboardParameter">The name of the query parameter that holds the dashboard's id.</param>
        /// <returns>The filter; null when the caller may read no dashboard.</returns>
        Task<FilterTree?> ReadThroughAsync(string dashboardParameter);

        /// <summary>The dashboards whose widgets show any of the given reports.</summary>
        /// <param name="reportIds">The reports.</param>
        /// <returns>
        ///     One entry per report and dashboard: the report, the dashboard, and the dashboard's title
        ///     in the request's content slot, null for a dashboard the caller cannot read.
        /// </returns>
        Task<IReadOnlyList<(int ReportId, int Id, string? Title)>> DashboardsUsing(IReadOnlyList<int> reportIds);
    }
}
