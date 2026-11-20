// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>Where something is written in the request's clauses, located as a query diagnostic is.</summary>
    /// <param name="Clause">The request clause, as a query diagnostic names it: <c>select</c>, <c>filters[i]</c>, <c>orderBy</c> or <c>having</c>.</param>
    /// <param name="Location">Which item of the clause the range indexes into, such as <c>Select[2]</c>; null when the clause is a single expression.</param>
    /// <param name="Start">The zero-based offset of the range within the clause's text.</param>
    /// <param name="Length">The length of the range; may be zero.</param>
    public sealed record QueryexSite(string Clause, string? Location, int Start, int Length);
}
