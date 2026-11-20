// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>One path the clauses read.</summary>
    /// <param name="Segments">The path's segments, from the root.</param>
    /// <param name="Site">Where the path is written.</param>
    /// <param name="Entity">The entity the last segment is read on.</param>
    /// <param name="Reachable">Whether the caller may read through the path; false when a query would refuse it.</param>
    public sealed record QueryexPathInfo(IReadOnlyList<string> Segments, QueryexSite Site, string Entity, bool Reachable);
}
