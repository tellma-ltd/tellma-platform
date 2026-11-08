// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex.Binding;

namespace Tellma.Core.Queryex.Lowering
{
    /// <summary>
    ///     The two filter constructs that change what a criterion's paths resolve from: a rebase onto
    ///     the row a navigation path reaches, and a semi-join over another entity's rows.
    /// </summary>
    internal sealed partial class Lowerer
    {
        /// <summary>Lowers a criterion rebased onto the row a navigation path reaches.</summary>
        /// <param name="via">The bound node.</param>
        /// <returns>The lowered predicate.</returns>
        /// <remarks>
        ///     <para>
        ///         The path is walked through the join tree the current root belongs to, and the
        ///         criterion is lowered from where it ends, so a rebased criterion and a select item
        ///         reading the same navigation share one join.
        ///     </para>
        ///     <para>
        ///         The criterion's nullity was worked out against the target, which is sound only where
        ///         the target row exists. An inner path always reaches one; an outer path may reach
        ///         none, so a test for the target's key comes first. Where it is absent the conjunction
        ///         is false whatever the criterion would have said — so the node is two-valued and
        ///         negates soundly, and a row with no target is outside the rebased criterion and inside
        ///         its negation.
        ///     </para>
        /// </remarks>
        private PlanPredicate LowerVia(TypedVia via)
        {
            JoinNode target = Walk(via.Navigations, via.Span);

            JoinNode saved = _base;
            _base = target;
            PlanPredicate inner = LowerPredicate(via.Inner);
            _base = saved;

            if (target.IsInner)
            {
                return inner;
            }

            if (inner is PlanConstantPredicate { Value: false } && _folding)
            {
                // Nothing to test for: a criterion that holds for no target row holds for no row.
                return inner;
            }

            PlanIsNull present = new(via.Span, new PlanColumn(via.Span, target, target.Entity.Key), negated: true);
            return inner is PlanConstantPredicate { Value: true } && _folding
                ? present
                : new PlanJunction(via.Span, isConjunction: true, [present, inner]);
        }

        /// <summary>Lowers a semi-join over the rows of another entity.</summary>
        /// <param name="exists">The bound node.</param>
        /// <returns>The lowered predicate.</returns>
        /// <remarks>
        ///     <para>
        ///         The criterion is lowered into a subquery of its own: its paths walk the subquery's
        ///         join tree and the values it names are bound in the subquery's source, because both
        ///         are read once per searched row rather than once per statement row. Its parameters
        ///         are the statement's, so a value the criterion shares with the rest of the query is
        ///         one parameter.
        ///     </para>
        ///     <para>
        ///         The correlation compares the searched row's foreign key with the current root's key.
        ///         A foreign key that is absent satisfies no comparison, which is exactly a row that
        ///         points at nothing.
        ///     </para>
        /// </remarks>
        private PlanPredicate LowerExists(TypedExists exists)
        {
            PlanSubquery subquery = new(exists.Entity, new PlanOrigin(exists.Span, _location));
            _subqueries.Add(subquery);

            // The criterion reads the searched rows, where a binding is always at row grain.
            JoinNode outer = _base;
            List<ValueBinding> savedBindings = _scopeBindings;
            bool savedAllowed = _bindingsAllowed;
            _base = subquery.Root;
            _scopeBindings = subquery.Pending;
            _bindingsAllowed = true;

            PlanPredicate inner = LowerPredicate(exists.Inner);

            _base = outer;
            _scopeBindings = savedBindings;
            _bindingsAllowed = savedAllowed;

            if (inner is PlanConstantPredicate { Value: false } && _folding)
            {
                // No row satisfies a criterion that is false, so none exists, and the subquery that
                // would search for one is never written.
                return inner;
            }

            PlanCompare correlation = new(
                exists.Span,
                PlanComparison.Equal,
                new PlanColumn(exists.Span, subquery.Root, exists.Navigation.ForeignKey),
                new PlanColumn(exists.Span, outer, outer.Entity.Key));

            subquery.Where = inner is PlanConstantPredicate { Value: true } && _folding
                ? correlation
                : new PlanJunction(exists.Span, isConjunction: true, [correlation, inner]);

            return new PlanExists(subquery);
        }
    }
}
