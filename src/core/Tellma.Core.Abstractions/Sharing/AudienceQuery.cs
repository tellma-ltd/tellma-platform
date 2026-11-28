// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;

namespace Tellma.Core.Abstractions.Sharing
{
    /// <summary>What an audience is resolved for: one shareable row, the data its audience needs, and the rows whose audience also sees it.</summary>
    /// <param name="Resource">The shareable row's resource, such as <c>core.Report</c>.</param>
    /// <param name="Id">The shareable row's id.</param>
    /// <param name="Data">
    ///     The securables a member must hold to see the row's data, such as the report's root
    ///     entity's <c>Read</c>; empty to count no data readers. The audience's <c>CannotRead</c>
    ///     counts run parallel to it.
    /// </param>
    /// <param name="ThroughResource">The resource of <paramref name="ThroughIds" />, a shareable one; null when there are none.</param>
    /// <param name="ThroughIds">
    ///     The rows whose audience also sees this one, such as the dashboards that show a report; null
    ///     or empty for none. Their own grants count, and nothing beyond them is followed.
    /// </param>
    public sealed record AudienceQuery(
        string Resource,
        int Id,
        IReadOnlyList<SecurableRef> Data,
        string? ThroughResource,
        IReadOnlyList<int>? ThroughIds);
}
