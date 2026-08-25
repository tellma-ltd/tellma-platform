// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;

namespace Tellma.Queryex.Testing.Corpus
{
    /// <summary>One whole query, and what compiling it is supposed to produce.</summary>
    public sealed record QueryCase
    {
        /// <summary>
        ///     A stable name for this case, which is also the name of its stored snapshot.
        /// </summary>
        public required string Id { get; init; }

        /// <summary>The query.</summary>
        public required QuerySpec Spec { get; init; }

        /// <summary>The declared parameters.</summary>
        public IReadOnlyList<QueryexParameterDeclaration> Parameters { get; init; } = [];

        /// <summary>Whether execution will have a signed-in user.</summary>
        public bool HasUser { get; init; } = true;

        /// <summary>The ceilings, when this case is about one of them.</summary>
        public QueryexLimits Limits { get; init; } = QueryexLimits.Default;

        /// <summary>
        ///     The diagnostic codes the case expects. Empty means the query is expected to compile,
        ///     in which case its SQL is compared against a stored snapshot.
        /// </summary>
        public IReadOnlyList<string> Diagnostics { get; init; } = [];

        /// <summary>
        ///     Whether this case is expected to match no rows at all.
        /// </summary>
        /// <remarks>
        ///     Declared rather than discovered. Two implementations agree trivially on an empty
        ///     answer, so a case that matches nothing is evidence about nothing — and one becomes
        ///     that silently, the moment the fixture's rows drift away from what it asks for. A case
        ///     whose whole point is that it denies everything says so here; every other case is held
        ///     to returning something.
        /// </remarks>
        public bool MatchesNothing { get; init; }

        /// <summary>Returns the case's name, so a failing theory names itself.</summary>
        /// <returns>The identifier.</returns>
        public override string ToString()
        {
            return Id;
        }
    }
}
