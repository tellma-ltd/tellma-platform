// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex.Binding;

namespace Tellma.Core.Queryex.Nullity
{
    /// <summary>The lattice the nullity analysis works over.</summary>
    internal static class NullityLattice
    {
        /// <summary>The wider of two answers.</summary>
        /// <param name="first">One answer.</param>
        /// <param name="second">The other.</param>
        /// <returns>The wider one.</returns>
        /// <remarks>
        ///     Absent-or-present sits above both certainties: knowing a value is definitely present
        ///     and knowing it is definitely absent are both stronger claims than not knowing.
        /// </remarks>
        internal static QueryexNullity Union(QueryexNullity first, QueryexNullity second)
        {
            return first == second ? first : QueryexNullity.Nullable;
        }

        /// <summary>The wider of a run of answers.</summary>
        /// <param name="values">The answers.</param>
        /// <returns>The widest one, or definitely present for an empty run.</returns>
        internal static QueryexNullity Union(IEnumerable<QueryexNullity> values)
        {
            QueryexNullity? result = null;
            foreach (QueryexNullity value in values)
            {
                result = result is null ? value : Union(result.Value, value);
            }

            return result ?? QueryexNullity.NotNull;
        }
    }

    /// <summary>
    ///     Each bound node's nullity, kept beside the tree rather than on it.
    /// </summary>
    /// <remarks>
    ///     Separate because it has to be inspectable on its own: the emitter omits guards on the
    ///     strength of it, and the claim it makes — that a node marked definitely present can never
    ///     evaluate to absent, for any database state the schema permits — is checked directly
    ///     against a second implementation of the language rather than taken on trust.
    /// </remarks>
    internal sealed class NullityMap
    {
        /// <summary>The answers, keyed by node identity.</summary>
        private readonly Dictionary<TypedExpr, QueryexNullity> _answers =
            new(ReferenceEqualityComparer.Instance);

        /// <summary>An empty map, for a tree that failed to bind.</summary>
        internal static NullityMap Empty { get; } = new NullityMap();

        /// <summary>The answer for a node.</summary>
        /// <param name="node">The node.</param>
        /// <returns>Its nullity.</returns>
        /// <remarks>
        ///     A node the analysis never visited answers absent-or-present, which is always the safe
        ///     direction: it costs a guard, never correctness.
        /// </remarks>
        internal QueryexNullity this[TypedExpr node] =>
            _answers.GetValueOrDefault(node, QueryexNullity.Nullable);

        /// <summary>Records the answer for a node.</summary>
        /// <param name="node">The node.</param>
        /// <param name="nullity">Its nullity.</param>
        internal void Set(TypedExpr node, QueryexNullity nullity)
        {
            _answers[node] = nullity;
        }

        /// <summary>Copies another map's answers into this one.</summary>
        /// <param name="other">The map to copy from.</param>
        internal void Absorb(NullityMap other)
        {
            foreach ((TypedExpr node, QueryexNullity nullity) in other._answers)
            {
                _answers[node] = nullity;
            }
        }

        /// <summary>Every node the analysis visited, with its answer.</summary>
        /// <returns>The answers.</returns>
        internal IEnumerable<KeyValuePair<TypedExpr, QueryexNullity>> Entries()
        {
            return _answers;
        }
    }
}
