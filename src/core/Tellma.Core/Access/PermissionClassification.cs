// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Queryex;

namespace Tellma.Core.Access
{
    /// <summary>
    ///     What one stored permission grants: its pair as registered, with its filter and stamp, or the
    ///     problem that makes it grant nothing.
    /// </summary>
    /// <param name="Resource">The resource as registered, through an alias included, or the wildcard; as stored when it resolves to nothing.</param>
    /// <param name="Action">The action as registered, or the wildcard; as stored when it resolves to nothing.</param>
    /// <param name="Filter">The validated filter; null for an unrestricted grant and for a problem.</param>
    /// <param name="FilterQueryexVersion">The filter's stamp; null exactly when the filter is.</param>
    /// <param name="Problem">The problem; null for a permission that grants.</param>
    /// <param name="Diagnostics">The engine's diagnostics of an invalid filter; empty otherwise.</param>
    /// <param name="Failure">The exception the engine threw validating the filter, a defect; null otherwise.</param>
    internal sealed record PermissionClassification(
        string Resource,
        string Action,
        string? Filter,
        int? FilterQueryexVersion,
        AccessProblemCode? Problem,
        IReadOnlyList<QueryexDiagnostic> Diagnostics,
        Exception? Failure)
    {
        /// <summary>A permission that grants its pair.</summary>
        /// <param name="resource">The resource as registered, or the wildcard.</param>
        /// <param name="action">The action as registered, or the wildcard.</param>
        /// <param name="filter">The validated filter, or null.</param>
        /// <param name="version">Its stamp, or null.</param>
        /// <returns>The classification.</returns>
        public static PermissionClassification Grant(string resource, string action, string? filter, int? version)
        {
            return new PermissionClassification(resource, action, filter, version, null, [], null);
        }

        /// <summary>A permission that grants nothing.</summary>
        /// <param name="resource">The resource, as registered when it resolves.</param>
        /// <param name="action">The action, as registered when it resolves.</param>
        /// <param name="problem">Why.</param>
        /// <param name="diagnostics">The engine's diagnostics of an invalid filter.</param>
        /// <param name="failure">The exception the engine threw, if any.</param>
        /// <returns>The classification.</returns>
        public static PermissionClassification Drift(
            string resource,
            string action,
            AccessProblemCode problem,
            IReadOnlyList<QueryexDiagnostic>? diagnostics = null,
            Exception? failure = null)
        {
            return new PermissionClassification(resource, action, null, null, problem, diagnostics ?? [], failure);
        }
    }
}
