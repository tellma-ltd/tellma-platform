// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Api;

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     One drifted permission of a role as the role's page shows it: a stored permission that
    ///     grants nothing against the deployment's securables and the tenant's current schema, and
    ///     why. The roles' <c>problems</c> details extra lists one per drifted permission, in
    ///     permission order.
    /// </summary>
    /// <param name="PermissionId">The id of the stored permission.</param>
    /// <param name="Code">Why it grants nothing.</param>
    /// <param name="Diagnostics">
    ///     The diagnostics of an invalid filter, located in the filter's text as the role editor's
    ///     filter check locates them; empty for every other code.
    /// </param>
    public sealed record PermissionProblem(int PermissionId, AccessProblemCode Code, IReadOnlyList<QueryDiagnostic> Diagnostics)
    {
        /// <summary>The diagnostics of an invalid filter; empty for every other code.</summary>
        public IReadOnlyList<QueryDiagnostic> Diagnostics { get; } = AccessLists.Copy(Diagnostics, nameof(Diagnostics));
    }
}
