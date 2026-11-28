// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;
using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     Renders a filter tree as readable text, for the "why" answer of a bespoke grant. The text is
    ///     for display only: it is never parsed back, and a navigation or a semi-join, which Queryex
    ///     has no syntax for, reads as a call-like form.
    /// </summary>
    internal static class FilterTreeText
    {
        /// <summary>Renders a tree.</summary>
        /// <param name="tree">The tree.</param>
        /// <returns>The text: a leaf as written, every connective's operands parenthesized.</returns>
        public static string Render(FilterTree tree)
        {
            ArgumentNullException.ThrowIfNull(tree);

            StringBuilder text = new();
            Append(text, tree);
            return text.ToString();
        }

        /// <summary>Appends one node's text.</summary>
        /// <param name="text">The text so far.</param>
        /// <param name="tree">The node.</param>
        /// <remarks>Recursive; the tree's depth is bounded by its own ceiling, so the recursion is too.</remarks>
        private static void Append(StringBuilder text, FilterTree tree)
        {
            switch (tree)
            {
                // --- A leaf reads as written ---
                case FilterTree.LeafNode leaf:
                    _ = text.Append(leaf.Text);
                    break;

                // --- The connectives: an empty conjunction is true, an empty disjunction false ---
                case FilterTree.AndNode and:
                    AppendJoined(text, and.Children, " and ", "true");
                    break;
                case FilterTree.OrNode or:
                    AppendJoined(text, or.Children, " or ", "false");
                    break;
                case FilterTree.NotNode not:
                    _ = text.Append("not (");
                    Append(text, not.Operand);
                    _ = text.Append(')');
                    break;

                // --- The structural nodes, which have no Queryex syntax ---
                case FilterTree.ViaNode via:
                    _ = text.Append("via(").Append(via.Navigation).Append(", ");
                    Append(text, via.Inner);
                    _ = text.Append(')');
                    break;
                case FilterTree.ExistsNode exists:
                    _ = text.Append("exists(").Append(exists.Entity).Append(", ").Append(exists.Navigation).Append(", ");
                    Append(text, exists.Inner);
                    _ = text.Append(')');
                    break;
                default:
                    throw new ArgumentException($"The filter tree node {tree.GetType().Name} has no rendering.", nameof(tree));
            }
        }

        /// <summary>Appends a connective's operands, each parenthesized.</summary>
        /// <param name="text">The text so far.</param>
        /// <param name="children">The operands.</param>
        /// <param name="separator">The connective between two operands.</param>
        /// <param name="empty">The text of the connective without operands.</param>
        private static void AppendJoined(StringBuilder text, IReadOnlyList<FilterTree> children, string separator, string empty)
        {
            if (children.Count == 0)
            {
                _ = text.Append(empty);
                return;
            }

            for (int index = 0; index < children.Count; index++)
            {
                _ = text.Append(index == 0 ? "(" : separator + "(");
                Append(text, children[index]);
                _ = text.Append(')');
            }
        }
    }
}
