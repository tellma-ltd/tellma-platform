// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Sharing;

namespace Tellma.Core.Abstractions.Reports
{
    /// <summary>
    ///     A standard report a feature ships: kept in every tenant with no owner, read-only but
    ///     shareable, and published to the roles it names.
    /// </summary>
    /// <param name="Code">The report's code: the feature's name, a dot, and a name of the feature's own, such as <c>acme.sales-by-center</c>.</param>
    /// <param name="Root">The entity the report reads, by its name.</param>
    /// <param name="Kind">The report's kind.</param>
    /// <param name="DocumentResource">The name of the embedded JSON resource of the feature's assembly that holds the definition document.</param>
    /// <param name="TitleKey">The string resource key of the title, rendered in each tenant language.</param>
    /// <param name="DescriptionKey">The string resource key of the description; null for none.</param>
    /// <param name="PublishTo">The role grants, by role code, written when the report is first inserted.</param>
    /// <param name="Version">The seed's version, raised when its document or titles change, so the seed is written again.</param>
    public sealed record ReportSeed(
        string Code,
        string Root,
        ReportKind Kind,
        string DocumentResource,
        string TitleKey,
        string? DescriptionKey,
        IReadOnlyList<(string RoleCode, ShareAccess Access)> PublishTo,
        int Version);
}
