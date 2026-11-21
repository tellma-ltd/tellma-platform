// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex.Binding
{
    /// <summary>
    ///     Finds an operation that combines text read from columns of different collations, which the
    ///     backend refuses when the statement runs.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A column's text carries the column's collation: its own when it declares one, the
    ///         database's otherwise. A literal, a parameter and text computed from no column carry
    ///         none and take whichever they meet. Text from two columns of different collations has
    ///         none it can be compared or combined under, so a comparison, a membership test or a
    ///         call that receives both fails on the backend; found here, it is the author's error,
    ///         reported against the operation.
    ///     </para>
    ///     <para>
    ///         Text a call computes from its text arguments carries their collation onward, so the
    ///         conflict is found however deeply the two columns sit below the operation that joins
    ///         them.
    ///     </para>
    /// </remarks>
    internal static class CollationCheck
    {
        /// <summary>The label of a column that takes the database's collation.</summary>
        private const string DatabaseDefault = "";

        /// <summary>The first operation, in evaluation order, that combines text of different collations.</summary>
        /// <param name="expression">A bound expression free of errors.</param>
        /// <returns>The operation, or null when there is none.</returns>
        internal static TypedExpr? FirstConflict(TypedExpr expression)
        {
            ArgumentNullException.ThrowIfNull(expression);
            TypedExpr? conflict = null;
            _ = Label(expression, ref conflict);
            return conflict;
        }

        /// <summary>The collation a node's text carries, recording the first conflict found below or at it.</summary>
        /// <param name="node">The node.</param>
        /// <param name="conflict">The first conflicting operation found so far.</param>
        /// <returns>
        ///     The collation's name, <see cref="DatabaseDefault" /> for the database's, or null when the
        ///     node is not text or its text carries no collation of its own.
        /// </returns>
        private static string? Label(TypedExpr node, ref TypedExpr? conflict)
        {
            // --- A column carries its own collation, or the database's ---
            if (node is TypedPath path)
            {
                return path.Type == BoundType.String ? path.Property.Collation ?? DatabaseDefault : null;
            }

            // --- An operation: the collations its text operands carry must be one ---
            string? carried = null;
            IReadOnlyList<TypedExpr> children = node.Children;
            for (int index = 0; index < children.Count && conflict is null; index++)
            {
                string? label = Label(children[index], ref conflict);
                if (label is null)
                {
                    continue;
                }

                if (carried is not null && !string.Equals(carried, label, StringComparison.Ordinal))
                {
                    conflict = node;
                    return null;
                }

                carried = label;
            }

            return node.Type == BoundType.String ? carried : null;
        }
    }
}
