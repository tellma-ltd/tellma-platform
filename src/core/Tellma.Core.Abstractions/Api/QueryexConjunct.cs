// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     One top-level <c>and</c> atom of a filter that compares a path or an expression with a
    ///     literal, which an editor can render as a chip.
    /// </summary>
    /// <param name="Filter">The index of the filter in the request.</param>
    /// <param name="Path">The compared path, dotted (<c>Invoice.CustomerId</c>); null when an expression is compared.</param>
    /// <param name="Expression">The compared expression's canonical text; null when a path is compared.</param>
    /// <param name="Op">The comparison: <c>=</c>, <c>!=</c>, <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c>, <c>&gt;=</c> or <c>descendantOf</c>.</param>
    /// <param name="Value">The literal in the wire's scalar encoding; null for a null literal.</param>
    /// <param name="Type">The type of the compared path or expression.</param>
    public sealed record QueryexConjunct(int Filter, string? Path, string? Expression, string Op, JsonElement? Value, QueryexType Type);
}
