// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex.Binding;

namespace Tellma.Core.Queryex.Diagnostics
{
    /// <summary>
    ///     Which input of a composed filter each part of its bound predicate came from.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A filter tree is bound leaf by leaf, and each leaf reports its binding problems under
    ///         its own location. Lowering sees only the joined predicate, yet two of its ceilings — the
    ///         joins and the parameters — are counted there, and each names the input that pushed the
    ///         count over. This is what lets it name the leaf rather than the whole clause, so a span
    ///         that indexes one leaf's text is never reported against another's.
    ///     </para>
    ///     <para>
    ///         Kept beside the bound tree rather than on it, because a leaf's bound tree is cached on
    ///         its text and shared by every place that text appears. The first location recorded for
    ///         a node wins: that is where lowering first reaches it, which is also where every join and
    ///         parameter it shares with a later occurrence was created.
    ///     </para>
    /// </remarks>
    internal sealed class FilterLocations
    {
        /// <summary>The locations, keyed by node identity.</summary>
        private readonly Dictionary<TypedExpr, DiagnosticLocation> _locations =
            new(ReferenceEqualityComparer.Instance);

        /// <summary>Records where a node came from, unless an earlier occurrence already did.</summary>
        /// <param name="node">The bound node: a leaf's root, or a rebase or semi-join.</param>
        /// <param name="location">The input its subtree was written in.</param>
        internal void Record(TypedExpr node, DiagnosticLocation location)
        {
            _locations.TryAdd(node, location);
        }

        /// <summary>Where a node came from, when it is one whose location was recorded.</summary>
        /// <param name="node">The bound node.</param>
        /// <param name="location">The location, when there is one.</param>
        /// <returns>True when the node's location was recorded.</returns>
        internal bool TryFind(TypedExpr node, out DiagnosticLocation location)
        {
            return _locations.TryGetValue(node, out location);
        }
    }
}
