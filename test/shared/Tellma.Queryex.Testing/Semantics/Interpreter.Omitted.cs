// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex.Binding;

namespace Tellma.Queryex.Testing.Semantics
{
    /// <summary>
    ///     The second reading of a predicate some of whose parameters were left out.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Read from what leaving a parameter out means, never from the tree the compiler
    ///         rewrites: the predicate is bound here with every parameter still in it, and the rule
    ///         is applied while it is evaluated. A parameter left out imposes no restriction, so the
    ///         smallest part of a predicate that mentions one stops deciding anything — it holds
    ///         where it is asserted and fails where it is denied — and one that is merely an element
    ///         of a list leaves the list.
    ///     </para>
    ///     <para>
    ///         Nothing here is remembered per node. What a part of a predicate comes to depends on
    ///         how many negations stand above it, and one bound text can sit at two places in a
    ///         filter that differ in exactly that.
    ///     </para>
    /// </remarks>
    internal sealed partial class Interpreter
    {
        /// <summary>The functions that range over the rows of a group.</summary>
        private static readonly HashSet<string> Aggregations = new(
            ["sum", "avg", "min", "max", "count"],
            StringComparer.OrdinalIgnoreCase);

        /// <summary>The position of an aggregation's condition among its arguments.</summary>
        private const int ConditionIndex = 1;

        /// <summary>Reads a row-level or group-level predicate, or an aggregation's condition.</summary>
        /// <param name="predicate">The bound predicate.</param>
        /// <returns>Whether it holds, with every omitted parameter imposing no restriction.</returns>
        internal bool Decides(TypedExpr predicate)
        {
            // With nothing left out this is the plain reading, node for node.
            return _context.Omitted.Count == 0 ? Truth(predicate) : Holds(predicate, negated: false);
        }

        /// <summary>Reads one part of a predicate's skeleton.</summary>
        /// <param name="node">The part.</param>
        /// <param name="negated">Whether an odd number of negations stand above it.</param>
        /// <returns>Whether it holds.</returns>
        private bool Holds(TypedExpr node, bool negated)
        {
            return node switch
            {
                // The skeleton: where a leaf was placed, negations, and connectives.
                TypedFilterLeaf leaf => Holds(leaf.Expression, negated),
                TypedNot not => !Holds(not.Operand, !negated),
                TypedLogical logical => Junction(logical, negated),

                // A rebase and a semi-join are each one atom, whatever is composed inside them.
                TypedVia via => Lifted(via.Inner, everywhere: true, negated) ?? Truth(node),
                TypedExists exists => Lifted(exists.Inner, everywhere: true, negated) ?? Truth(node),

                // The two atoms that carry a list, which an omitted element simply leaves.
                TypedIn membership => Membership(membership, negated),
                TypedCall call when IsHierarchyPredicate(call) => Lineage(call, negated),

                // Every other atom.
                _ => Lifted(node, everywhere: false, negated) ?? Truth(node),
            };
        }

        /// <summary>Combines the parts of a connective.</summary>
        /// <param name="logical">The connective.</param>
        /// <param name="negated">Whether an odd number of negations stand above it.</param>
        /// <returns>Whether it holds.</returns>
        /// <remarks>
        ///     Every operand is read whatever the answer turns out to be, as in the plain reading.
        /// </remarks>
        private bool Junction(TypedLogical logical, bool negated)
        {
            bool conjunction = logical.Operator == LogicalOperator.And;
            bool result = conjunction;
            foreach (TypedExpr operand in logical.Operands)
            {
                bool truth = Holds(operand, negated);
                result = conjunction ? result && truth : result || truth;
            }

            return result;
        }

        /// <summary>What an atom comes to when it mentions an omitted parameter.</summary>
        /// <param name="atom">The atom, or the part of it to look in.</param>
        /// <param name="everywhere">
        ///     Whether a mention inside the condition of an aggregation counts. It does for a rebase
        ///     or a semi-join, which is one atom all the way down.
        /// </param>
        /// <param name="negated">Whether an odd number of negations stand above the atom.</param>
        /// <returns>
        ///     What lets every row through at that polarity — true where the atom is asserted, false
        ///     where it is denied — or null when the atom mentions no omitted parameter and decides
        ///     for itself.
        /// </returns>
        private bool? Lifted(TypedExpr atom, bool everywhere, bool negated)
        {
            return Mentions(atom, everywhere) ? !negated : null;
        }

        /// <summary>Reads a membership test that is an atom of the skeleton.</summary>
        /// <param name="membership">The test.</param>
        /// <param name="negated">Whether an odd number of negations stand above it.</param>
        /// <returns>Whether it holds.</returns>
        private bool Membership(TypedIn membership, bool negated)
        {
            bool emptied = true;
            bool mentions = Mentions(membership.Value, everywhere: false);
            foreach (TypedExpr element in membership.Elements)
            {
                if (IsOmittedParameter(element))
                {
                    continue;
                }

                emptied = false;
                mentions |= Mentions(element, everywhere: false);
            }

            // A list every element has left restricts nothing, and neither does a test that still
            // mentions an omitted parameter somewhere other than as a bare element.
            return emptied || mentions ? !negated : Contains(membership, skipOmitted: true);
        }

        /// <summary>Reads a hierarchy predicate that is an atom of the skeleton.</summary>
        /// <param name="call">The call.</param>
        /// <param name="negated">Whether an odd number of negations stand above it.</param>
        /// <returns>Whether it holds.</returns>
        private bool Lineage(TypedCall call, bool negated)
        {
            // The first argument names the row; the keys are everything after it.
            bool emptied = call.Arguments.Length > 1;
            bool mentions = call.Arguments.Length > 0 && Mentions(call.Arguments[0], everywhere: false);
            for (int index = 1; index < call.Arguments.Length; index++)
            {
                if (IsOmittedParameter(call.Arguments[index]))
                {
                    continue;
                }

                emptied = false;
                mentions |= Mentions(call.Arguments[index], everywhere: false);
            }

            bool descending = string.Equals(call.Definition.Name, "descendantOf", StringComparison.OrdinalIgnoreCase);
            return emptied || mentions ? !negated : Related(call, descending, skipOmitted: true);
        }

        /// <summary>Whether a node is one of the two predicates that take a list of keys.</summary>
        /// <param name="call">The call.</param>
        /// <returns>True when it is.</returns>
        private static bool IsHierarchyPredicate(TypedCall call)
        {
            return string.Equals(call.Definition.Name, "descendantOf", StringComparison.OrdinalIgnoreCase)
                || string.Equals(call.Definition.Name, "ancestorOf", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Whether a node is, by itself, a parameter that was left out.</summary>
        /// <param name="node">The node.</param>
        /// <returns>True when it is.</returns>
        private bool IsOmittedParameter(TypedExpr node)
        {
            return node is TypedParameter parameter && _context.Omitted.Contains(parameter.Symbol.Name);
        }

        /// <summary>Whether a node mentions a parameter that was left out.</summary>
        /// <param name="node">The node.</param>
        /// <param name="everywhere">Whether a mention inside the condition of an aggregation counts.</param>
        /// <returns>True when it does.</returns>
        /// <remarks>
        ///     The condition of an aggregation is a predicate of its own: it decides which rows the
        ///     aggregation reads, and a parameter left out of it lifts that restriction rather than
        ///     whatever the aggregation's result is compared with.
        /// </remarks>
        private bool Mentions(TypedExpr node, bool everywhere)
        {
            if (IsOmittedParameter(node))
            {
                return true;
            }

            bool aggregation = !everywhere
                && node is TypedCall call
                && Aggregations.Contains(call.Definition.Name);

            IReadOnlyList<TypedExpr> children = node.Children;
            for (int index = 0; index < children.Count; index++)
            {
                if (aggregation && index == ConditionIndex)
                {
                    continue;
                }

                if (Mentions(children[index], everywhere))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
