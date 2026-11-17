// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     Decides which paths of a caller's query text the caller may traverse: a navigation reaching
    ///     an entity the caller may not read is refused, except through the entity's related
    ///     projection. A path that reads a sensitive entity beyond that projection asks for the
    ///     step-up a direct read of the entity asks for. The query path, reports and the client's
    ///     discovery use the same rule.
    /// </summary>
    public interface IQueryTraversalRule
    {
        /// <summary>
        ///     Checks every path a query's text traverses against the caller's read decisions, then
        ///     holds the admitted paths to the step-up of the sensitive entities they read beyond their
        ///     related projection.
        /// </summary>
        /// <remarks>
        ///     The step-up is asked for only when no path is refused, as a direct read asks for it
        ///     only once the read is granted, and never of the platform's own work. It is part of the
        ///     check, not left to the caller, so no consumer can read a sensitive entity through a
        ///     path that a direct read of it would refuse.
        /// </remarks>
        /// <param name="root">The query's root entity name.</param>
        /// <param name="discovery">What discovering the query's text found: the paths it uses and where.</param>
        /// <param name="decisions">The caller's read decision per entity name, evaluated before the call.</param>
        /// <returns>Every traversed path the decisions do not admit; empty when all pass.</returns>
        /// <exception cref="StepUpRequiredException">
        ///     No path is refused, and one reads a sensitive entity beyond its projection under a
        ///     sign-in below the step-up bar.
        /// </exception>
        /// <exception cref="IndividualRequiredException">The same, for a service account, which cannot step up.</exception>
        IReadOnlyList<TraversalRefusal> Check(string root, DiscoveryResult discovery, Func<string, AccessDecision> decisions);
    }
}
