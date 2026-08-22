// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using static System.FormattableString;

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     A predicate composed structurally from independent expression leaves.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Plain data: serializable, loggable, diffable. The engine parses and binds each leaf
    ///         itself and then combines the bound trees, so a caller never produces or consumes a
    ///         bound tree and never concatenates text.
    ///     </para>
    ///     <para>
    ///         That the conjunction happens over complete trees is what makes composition safe: no
    ///         operator inside a leaf can bind more tightly than the connective above it. Textual
    ///         concatenation cannot promise that, and gets it wrong in exactly the case that
    ///         matters — appending a criterion <c>C</c> to a user filter of <c>A or B</c> yields
    ///         <c>A or B and C</c>, which grants access to every row matching <c>A</c>.
    ///     </para>
    /// </remarks>
    public abstract class FilterTree
    {
        /// <summary>
        ///     The deepest a composed tree may be.
        /// </summary>
        /// <remarks>
        ///     Composition nests as deep as there are criteria to combine, which in practice is two
        ///     or three: a reader's own filter under a conjunction with a disjunction of the
        ///     criteria they are permitted by. The ceiling is here because the engine reads this tree
        ///     by recursion in several places, one of them before it has looked at any of the
        ///     caller's limits, and an arbitrarily deep tree would end those walks on the stack
        ///     rather than in a diagnostic.
        /// </remarks>
        public const int MaxDepth = 64;

        /// <summary>Initializes the node. The set of node kinds is closed.</summary>
        /// <param name="depth">The height of this subtree, this node included.</param>
        private protected FilterTree(int depth)
        {
            Depth = depth;
        }

        /// <summary>The height of this subtree, this node included.</summary>
        internal int Depth { get; }

        /// <summary>A single expression.</summary>
        /// <param name="text">The expression text, which must not be empty or whitespace.</param>
        /// <returns>The leaf node.</returns>
        /// <exception cref="ArgumentException">
        ///     <paramref name="text" /> is null, empty, or whitespace. "Empty means unrestricted" is
        ///     the right reading for a user's filter and a catastrophic one for an access-control
        ///     criterion, and this type cannot tell the two apart; a caller with nothing to
        ///     contribute omits the node instead.
        /// </exception>
        public static FilterTree Leaf(string text)
        {
            return string.IsNullOrWhiteSpace(text)
                ? throw new ArgumentException(
                    "A filter leaf must carry an expression. Omit the node instead of passing empty text.",
                    nameof(text))
                : new LeafNode(text);
        }

        /// <summary>The height one node adds to the deepest of its children.</summary>
        /// <param name="children">The children.</param>
        /// <param name="parameterName">The caller's parameter name, for the exception.</param>
        /// <returns>The node's own height.</returns>
        /// <exception cref="ArgumentException">The result would exceed the ceiling.</exception>
        private static int DepthOf(IReadOnlyList<FilterTree> children, string parameterName)
        {
            int deepest = 0;
            foreach (FilterTree child in children)
            {
                deepest = Math.Max(deepest, child.Depth);
            }

            // Rejected as it is built rather than when it is compiled, so that a caller assembling a
            // tree in a loop learns at the step that went wrong.
            return deepest < MaxDepth
                ? deepest + 1
                : throw new ArgumentException(
                    Invariant($"A composed filter must not nest deeper than {MaxDepth} levels."),
                    parameterName);
        }

        /// <summary>The conjunction of the given children.</summary>
        /// <param name="children">The conjuncts. An empty set is <c>true</c>.</param>
        /// <returns>The conjunction node.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="children" /> is null.</exception>
        /// <exception cref="ArgumentException">
        ///     A child is null, or the result would nest deeper than <see cref="MaxDepth" />.
        /// </exception>
        public static FilterTree And(IReadOnlyList<FilterTree> children)
        {
            FilterTree[] copy = Copy(children, nameof(children));
            return new AndNode(copy, DepthOf(copy, nameof(children)));
        }

        /// <summary>The disjunction of the given children.</summary>
        /// <param name="children">
        ///     The disjuncts. An empty set is <c>false</c>: an empty set of permissions must deny
        ///     access, not grant it. A fold with the wrong identity is a privilege-escalation bug,
        ///     which is why the value is fixed here rather than left to each caller's helper.
        /// </param>
        /// <returns>The disjunction node.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="children" /> is null.</exception>
        /// <exception cref="ArgumentException">
        ///     A child is null, or the result would nest deeper than <see cref="MaxDepth" />.
        /// </exception>
        public static FilterTree Or(IReadOnlyList<FilterTree> children)
        {
            FilterTree[] copy = Copy(children, nameof(children));
            return new OrNode(copy, DepthOf(copy, nameof(children)));
        }

        /// <summary>The negation of the given operand.</summary>
        /// <param name="operand">The operand to negate.</param>
        /// <returns>The negation node.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="operand" /> is null.</exception>
        /// <exception cref="ArgumentException">
        ///     The result would nest deeper than <see cref="MaxDepth" />.
        /// </exception>
        public static FilterTree Not(FilterTree operand)
        {
            ArgumentNullException.ThrowIfNull(operand);

            return new NotNode(operand, DepthOf([operand], nameof(operand)));
        }

        /// <summary>Validates a child list and takes a defensive copy of it.</summary>
        /// <param name="children">The children supplied by the caller.</param>
        /// <param name="parameterName">The caller's parameter name, for the exception.</param>
        /// <returns>An immutable copy.</returns>
        private static FilterTree[] Copy(IReadOnlyList<FilterTree> children, string parameterName)
        {
            ArgumentNullException.ThrowIfNull(children, parameterName);

            // Copied rather than captured: a tree is compiled and cached, and a caller that kept a
            // mutable list could otherwise change what a cached compilation was keyed on.
            FilterTree[] copy = [.. children];
            foreach (FilterTree child in copy)
            {
                if (child is null)
                {
                    throw new ArgumentException("A filter tree child must not be null.", parameterName);
                }
            }

            return copy;
        }

        /// <summary>A single expression, parsed and bound on its own.</summary>
        public sealed class LeafNode : FilterTree
        {
            /// <summary>Initializes the leaf.</summary>
            /// <param name="text">The expression text.</param>
            internal LeafNode(string text)
                : base(1)
            {
                Text = text;
            }

            /// <summary>The expression text.</summary>
            public string Text { get; }
        }

        /// <summary>The conjunction of zero or more children. Empty is <c>true</c>.</summary>
        public sealed class AndNode : FilterTree
        {
            /// <summary>Initializes the conjunction.</summary>
            /// <param name="children">The conjuncts.</param>
            /// <param name="depth">The height of this subtree.</param>
            internal AndNode(IReadOnlyList<FilterTree> children, int depth)
                : base(depth)
            {
                Children = children;
            }

            /// <summary>The conjuncts, in the order the caller supplied them.</summary>
            public IReadOnlyList<FilterTree> Children { get; }
        }

        /// <summary>The disjunction of zero or more children. Empty is <c>false</c>.</summary>
        public sealed class OrNode : FilterTree
        {
            /// <summary>Initializes the disjunction.</summary>
            /// <param name="children">The disjuncts.</param>
            /// <param name="depth">The height of this subtree.</param>
            internal OrNode(IReadOnlyList<FilterTree> children, int depth)
                : base(depth)
            {
                Children = children;
            }

            /// <summary>The disjuncts, in the order the caller supplied them.</summary>
            public IReadOnlyList<FilterTree> Children { get; }
        }

        /// <summary>The negation of one operand.</summary>
        public sealed class NotNode : FilterTree
        {
            /// <summary>Initializes the negation.</summary>
            /// <param name="operand">The operand.</param>
            /// <param name="depth">The height of this subtree.</param>
            internal NotNode(FilterTree operand, int depth)
                : base(depth)
            {
                Operand = operand;
            }

            /// <summary>The negated operand.</summary>
            public FilterTree Operand { get; }
        }
    }
}
