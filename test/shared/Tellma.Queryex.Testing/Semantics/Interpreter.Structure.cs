// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;
using Tellma.Core.Queryex.Binding;

namespace Tellma.Queryex.Testing.Semantics
{
    /// <summary>
    ///     The two filter constructs that read another row than the one being looked at: a rebase onto
    ///     the row a navigation path reaches, and a semi-join over another entity's rows.
    /// </summary>
    /// <remarks>
    ///     Read from what each construct means, row by row, never from the joins and subqueries the
    ///     compiler builds for them: a rebase follows its navigations through the fixture's rows, and a
    ///     semi-join looks through every row of the searched entity for one that points here.
    /// </remarks>
    internal sealed partial class Interpreter
    {
        /// <summary>Whether a rebased criterion holds for this row.</summary>
        /// <param name="via">The bound node.</param>
        /// <returns>True when the path reaches a row and the criterion holds for that row.</returns>
        /// <remarks>
        ///     A path that reaches nothing makes the node false, which is what its meaning says and
        ///     what makes its negation true for exactly those rows.
        /// </remarks>
        private bool Rebased(TypedVia via)
        {
            LedgerRow? current = _row;
            foreach (NavigationDescriptor navigation in via.Navigations)
            {
                current = LedgerData.Find(navigation.Target, current[navigation.ForeignKey.Name]);
                if (current is null)
                {
                    return false;
                }
            }

            // A reading of its own: the criterion is read against another row, and what it evaluated
            // to there says nothing about this one.
            return new Interpreter(current, _context).Truth(via.Inner);
        }

        /// <summary>Whether some row of the searched entity points here and satisfies the criterion.</summary>
        /// <param name="exists">The bound node.</param>
        /// <returns>True when such a row exists.</returns>
        /// <remarks>
        ///     Every pointing row's criterion is evaluated whatever the earlier ones said, for the same
        ///     reason every operand of a conjunction is: nothing is skipped because something else
        ///     already decided the answer.
        /// </remarks>
        private bool Found(TypedExists exists)
        {
            QxValue key = _row[_row.Entity.Key.Name];
            bool found = false;
            foreach (LedgerRow candidate in LedgerData.Rows(exists.Entity))
            {
                // A row whose pointer is absent points at nothing, and a row without a key is
                // pointed at by nothing.
                QxValue pointer = candidate[exists.Navigation.ForeignKey.Name];
                if (pointer.IsAbsent || key.IsAbsent || Order(pointer, key) != 0)
                {
                    continue;
                }

                found |= new Interpreter(candidate, _context).Truth(exists.Inner);
            }

            return found;
        }
    }
}
