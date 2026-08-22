// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;

namespace Tellma.Queryex.Testing.Corpus
{
    /// <summary>
    ///     One expression, and what compiling it is supposed to produce.
    /// </summary>
    /// <remarks>
    ///     Declared as data rather than written out as one test method each, so that the same case
    ///     can be checked in several ways — against the compiler, against the reference
    ///     implementation of the semantics, and against a real server — without any of them
    ///     restating what the case is.
    /// </remarks>
    public sealed record ExpressionCase
    {
        /// <summary>
        ///     A stable name for this case. Also the name of its stored snapshot, so renaming one is
        ///     a visible change rather than a silently orphaned file.
        /// </summary>
        public required string Id { get; init; }

        /// <summary>The expression text.</summary>
        public required string Text { get; init; }

        /// <summary>The position the expression is written for.</summary>
        public QueryexMode Mode { get; init; } = QueryexMode.Value;

        /// <summary>The entity paths resolve from.</summary>
        public string Root { get; init; } = "Invoice";

        /// <summary>Whether direction suffixes are accepted.</summary>
        public bool Directions { get; init; }

        /// <summary>Whether execution will have a signed-in user.</summary>
        public bool HasUser { get; init; } = true;

        /// <summary>Whether the enclosing query groups by anything.</summary>
        public bool HasGroupingKeys { get; init; }

        /// <summary>The declared parameters.</summary>
        public IReadOnlyList<QueryexParameterDeclaration> Parameters { get; init; } = [];

        /// <summary>The ceilings, when this case is about one of them.</summary>
        public QueryexLimits Limits { get; init; } = QueryexLimits.Default;

        /// <summary>
        ///     The diagnostic codes the case expects, in no particular order. Empty means the
        ///     expression is expected to compile.
        /// </summary>
        public IReadOnlyList<string> Diagnostics { get; init; } = [];

        /// <summary>The type the single item is expected to have, when the case pins one.</summary>
        public QueryexType? Type { get; init; }

        /// <summary>The nullity the single item is expected to have, when the case pins one.</summary>
        public QueryexNullity? Nullity { get; init; }

        /// <summary>The number of items the list is expected to have, when the case pins it.</summary>
        public int? Items { get; init; }

        /// <summary>Returns the case's name, so a failing theory names itself.</summary>
        /// <returns>The identifier.</returns>
        public override string ToString()
        {
            return Id;
        }
    }
}
