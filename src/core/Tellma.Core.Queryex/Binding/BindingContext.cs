// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using Tellma.Core.Queryex.Binding.Inference;

namespace Tellma.Core.Queryex.Binding
{
    /// <summary>How well an argument satisfied the type a position wanted.</summary>
    internal enum CoercionCost
    {
        /// <summary>It already had that type.</summary>
        Exact = 0,

        /// <summary>A written literal was read at that type.</summary>
        Coerced = 1,
    }

    /// <summary>
    ///     Everything binding one expression list depends on besides the text itself.
    /// </summary>
    /// <remarks>
    ///     Every field here is part of the cache key for a bound expression. Two of them are facts
    ///     about the execution rather than about the text — whether there will be a signed-in user,
    ///     and whether the enclosing query groups by anything — and both change what the analysis
    ///     concludes, so a bound tree computed under one cannot be reused under the other.
    /// </remarks>
    internal sealed record BindingContext
    {
        /// <summary>The schema, or null when binding without one.</summary>
        internal QueryexSchema? Schema { get; init; }

        /// <summary>The entity paths resolve from, or null when binding without a schema.</summary>
        internal EntityDescriptor? Root { get; init; }

        /// <summary>The position the expression is being compiled for.</summary>
        internal required QueryexMode Mode { get; init; }

        /// <summary>Whether execution will have a signed-in user.</summary>
        internal bool HasUser { get; init; } = true;

        /// <summary>Whether the enclosing query groups by anything.</summary>
        internal bool HasGroupingKeys { get; init; }

        /// <summary>The declared parameters, by name.</summary>
        internal FrozenDictionary<string, ParameterSymbol> Parameters { get; init; } =
            FrozenDictionary<string, ParameterSymbol>.Empty;

        /// <summary>
        ///     The shared inference state, when parameters are being inferred rather than declared.
        /// </summary>
        /// <remarks>
        ///     Set only while an authoring tool is discovering what an expression refers to. Where
        ///     it is set an undeclared parameter is not an error but an unknown to be solved; where
        ///     it is not, an undeclared parameter is a diagnostic, because a stored expression must
        ///     not acquire a type by accident at run time.
        /// </remarks>
        internal InferenceContext? Inference { get; init; }

        /// <summary>Whether paths can be resolved at all.</summary>
        internal bool CanResolvePaths => Schema is not null && Root is not null;

        /// <summary>Whether the position permits aggregations.</summary>
        internal bool AllowsAggregation => Mode.Grouping == QueryexGrouping.Group;
    }
}
