// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     One query diagnostic as the wire carries it to an editor: the compiler's code, position and
    ///     arguments verbatim, plus the request clause it concerns. A query-invalid problem carries
    ///     one per diagnostic, and query discovery answers them for text being typed.
    /// </summary>
    /// <param name="Clause">
    ///     The request clause the diagnostic concerns, a closed list: <c>select</c>,
    ///     <c>filters[i]</c>, <c>namedFilters[i]</c>, <c>orderBy</c>, <c>having</c> and
    ///     <c>measures</c>.
    /// </param>
    /// <param name="Code">The diagnostic code: a compiler code such as <c>QX3001</c>, or one of the pipeline's <c>Query.*</c> validation codes.</param>
    /// <param name="Start">The zero-based offset of the offending range within the clause's text.</param>
    /// <param name="Length">The length of the offending range; may be zero.</param>
    /// <param name="Location">Which item of the clause the range indexes into, such as <c>Select[2]</c>; null when the clause is a single expression.</param>
    /// <param name="Arguments">
    ///     The named values the diagnostic's message renders from. They derive from the caller's own
    ///     input and are untrusted display data.
    /// </param>
    public sealed record QueryDiagnostic(
        string Clause, string Code, int Start, int Length, string? Location, IReadOnlyDictionary<string, object?> Arguments);
}
