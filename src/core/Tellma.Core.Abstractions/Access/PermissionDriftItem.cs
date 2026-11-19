// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Access
{
    // The drift item is spec 0014 §9.
    /// <summary>One stored permission that grants nothing, and why.</summary>
    /// <param name="PermissionId">The permission's id.</param>
    /// <param name="RoleId">The id of the role that holds it.</param>
    /// <param name="RoleCode">The role's code; null for a role without one.</param>
    /// <param name="Code">Why the permission grants nothing.</param>
    /// <param name="Diagnostics">
    ///     The query engine's diagnostics for <see cref="AccessProblemCode.FilterInvalid" />; empty
    ///     for every other problem.
    /// </param>
    public sealed record PermissionDriftItem(
        int PermissionId, int RoleId, string? RoleCode, AccessProblemCode Code, IReadOnlyList<QueryexDiagnostic> Diagnostics);
}
