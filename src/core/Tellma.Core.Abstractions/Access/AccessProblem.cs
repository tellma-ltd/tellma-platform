// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     A stored permission that grants nothing, and why. A permission reached through several
    ///     memberships is one problem.
    /// </summary>
    /// <remarks>
    ///     The pair says which decisions the permission would have reached: a decision lists the
    ///     problems whose pair matches it as a grant's would, so "why can't I" names the broken
    ///     permission.
    /// </remarks>
    /// <param name="PermissionId">The id of the stored permission.</param>
    /// <param name="RoleId">The id of the role that holds it.</param>
    /// <param name="Resource">
    ///     The permission's resource: the registered spelling when it resolves, through an alias
    ///     included, else as stored.
    /// </param>
    /// <param name="Action">The permission's action: the registered spelling when it resolves, else as stored.</param>
    /// <param name="Code">Why it grants nothing.</param>
    /// <param name="Diagnostics">The engine's diagnostics of an invalid filter; empty for every other code.</param>
    public sealed record AccessProblem(
        int PermissionId,
        int RoleId,
        string Resource,
        string Action,
        AccessProblemCode Code,
        IReadOnlyList<QueryexDiagnostic> Diagnostics)
    {
        /// <summary>The permission's resource.</summary>
        public string Resource { get; } = Resource ?? throw new ArgumentNullException(nameof(Resource));

        /// <summary>The permission's action.</summary>
        public string Action { get; } = Action ?? throw new ArgumentNullException(nameof(Action));

        /// <summary>The engine's diagnostics of an invalid filter; empty for every other code.</summary>
        public IReadOnlyList<QueryexDiagnostic> Diagnostics { get; } = AccessLists.Copy(Diagnostics, nameof(Diagnostics));
    }
}
