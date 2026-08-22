// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex.Syntax
{
    /// <summary>
    ///     Compares parse trees for structural equivalence.
    /// </summary>
    /// <remarks>
    ///     Ignores every span and sees through parentheses, which is what makes canonical printing
    ///     genuinely canonical rather than merely faithful: reprinting a tree may lose a redundant
    ///     grouping, and reparsing the result must still land on the same tree.
    /// </remarks>
    internal static class SyntaxEquivalence
    {
        /// <summary>Whether two trees denote the same expression.</summary>
        /// <param name="first">The first tree.</param>
        /// <param name="second">The second tree.</param>
        /// <returns>True when they are structurally equivalent.</returns>
        internal static bool AreEquivalent(SyntaxNode? first, SyntaxNode? second)
        {
            if (ReferenceEquals(first, second))
            {
                return true;
            }

            if (first is null || second is null)
            {
                return false;
            }

            SyntaxNode left = SyntaxPrinter.Unwrap(first);
            SyntaxNode right = SyntaxPrinter.Unwrap(second);

            // The hash only rejects unequal trees cheaply; equality is always decided by the
            // comparison below, never by the hash.
            return left.Kind == right.Kind
                && left.StructuralHash == right.StructuralHash
                && MatchesByKind(left, right);
        }

        /// <summary>Compares two nodes already known to share a kind.</summary>
        /// <param name="left">The first node, with parentheses already peeled.</param>
        /// <param name="right">The second node, with parentheses already peeled.</param>
        /// <returns>True when they are structurally equivalent.</returns>
        private static bool MatchesByKind(SyntaxNode left, SyntaxNode right)
        {
            return (left, right) switch
            {
                (NumberSyntax a, NumberSyntax b) =>
                    a.Scale == b.Scale && a.Value.CompareTo(b.Value) == 0,
                (StringSyntax a, StringSyntax b) =>
                    string.Equals(a.Value, b.Value, StringComparison.Ordinal),
                (BooleanSyntax a, BooleanSyntax b) => a.Value == b.Value,
                (NullSyntax, NullSyntax) => true,
                (ParameterSyntax a, ParameterSyntax b) =>
                    string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
                (PathSyntax a, PathSyntax b) => SegmentsMatch(a, b),
                (ErrorSyntax a, ErrorSyntax b) =>
                    string.Equals(a.SourceSlice, b.SourceSlice, StringComparison.Ordinal),
                (CallSyntax a, CallSyntax b) =>
                    string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
                    && ChildrenMatch(a.Arguments, b.Arguments),
                (UnarySyntax a, UnarySyntax b) =>
                    a.OperatorKind == b.OperatorKind && AreEquivalent(a.Operand, b.Operand),
                (BinarySyntax a, BinarySyntax b) =>
                    a.OperatorKind == b.OperatorKind
                    && AreEquivalent(a.Left, b.Left)
                    && AreEquivalent(a.Right, b.Right),
                (InSyntax a, InSyntax b) =>
                    AreEquivalent(a.Value, b.Value) && ChildrenMatch(a.Elements, b.Elements),
                (IsNullSyntax a, IsNullSyntax b) =>
                    a.Negated == b.Negated && AreEquivalent(a.Operand, b.Operand),
                (ExpressionItemSyntax a, ExpressionItemSyntax b) =>
                    a.Direction == b.Direction && AreEquivalent(a.Expression, b.Expression),
                (ExpressionListSyntax a, ExpressionListSyntax b) =>
                    ChildrenMatch(a.Items, b.Items),
                _ => false,
            };
        }

        /// <summary>Whether two paths name the same segments.</summary>
        /// <param name="first">The first path.</param>
        /// <param name="second">The second path.</param>
        /// <returns>True when they match.</returns>
        private static bool SegmentsMatch(PathSyntax first, PathSyntax second)
        {
            if (first.Segments.Count != second.Segments.Count)
            {
                return false;
            }

            for (int index = 0; index < first.Segments.Count; index++)
            {
                if (!string.Equals(
                    first.Segments[index].Name,
                    second.Segments[index].Name,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether two child runs match pairwise, in order.</summary>
        /// <typeparam name="T">The node type.</typeparam>
        /// <param name="first">The first run.</param>
        /// <param name="second">The second run.</param>
        /// <returns>True when they match.</returns>
        private static bool ChildrenMatch<T>(IReadOnlyList<T> first, IReadOnlyList<T> second)
            where T : SyntaxNode
        {
            if (first.Count != second.Count)
            {
                return false;
            }

            for (int index = 0; index < first.Count; index++)
            {
                if (!AreEquivalent(first[index], second[index]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
