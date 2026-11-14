// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     Decides which paths of a caller's query text the caller may traverse: a navigation reaching
    ///     an entity the caller may not read is refused, except through the entity's related
    ///     projection. The query path, reports and the client's discovery use the same rule.
    /// </summary>
    public interface IQueryTraversalRule
    {
        /// <summary>Checks every path a query's text traverses against the caller's read decisions.</summary>
        /// <param name="root">The query's root entity name.</param>
        /// <param name="discovery">What discovering the query's text found: the paths it uses and where.</param>
        /// <param name="decisions">The caller's read decision per entity name, evaluated before the call.</param>
        /// <returns>Every traversed path the decisions do not admit; empty when all pass.</returns>
        IReadOnlyList<TraversalRefusal> Check(string root, DiscoveryResult discovery, Func<string, AccessDecision> decisions);
    }
}
