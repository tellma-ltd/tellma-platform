// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Queryex;

namespace Tellma.Core.Excel
{
    // Spec 0019 §12.1: the exports' and imports' self-scope. A criterion alone makes the decision a
    // filtered one (spec 0014 §5.1), so every member reads and deletes the rows they created with no
    // role grant ("My exports", "My imports"), and a stored grant widens each by the decision's
    // disjunction. Neither stack registers a save, so the criteria name no other action.
    /// <summary>The criteria every self-scoped Excel artifact shares: its creator reads and deletes it.</summary>
    internal static class ExcelAccessCriteria
    {
        /// <summary>The reason key the criteria answer a "why" with.</summary>
        public const string SelfReason = "self";

        /// <summary>The criteria, built once: the same trees for every caller and both resources.</summary>
        public static readonly AccessCriterion[] SelfScope = Build();

        /// <summary>Builds the criteria over the filter of the rows the caller created, which <c>me()</c> binds.</summary>
        /// <returns>The read and delete criteria.</returns>
        private static AccessCriterion[] Build()
        {
            var created = FilterTree.Leaf("CreatedById = me()");
            return
            [
                new(AccessActions.Read, created, SelfReason),
                new(AccessActions.Delete, created, SelfReason),
            ];
        }
    }
}
