// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Diagnostics;
using Tellma.Core.Queryex.Nullity;

namespace Tellma.Core.Queryex.Pipeline
{
    /// <summary>
    ///     Binds a structurally composed predicate.
    /// </summary>
    /// <remarks>
    ///     Each leaf is parsed and bound entirely on its own, and only the finished trees are joined
    ///     together. That is the whole point of composing structurally rather than textually: no
    ///     operator inside a leaf can reach outside it, so conjoining an access-control criterion
    ///     onto a filter of <c>A or B</c> cannot quietly become <c>A or (B and C)</c> and hand back
    ///     every row that matches <c>A</c>.
    /// </remarks>
    internal static class FilterBinder
    {
        /// <summary>Binds a whole filter tree into one predicate.</summary>
        /// <param name="compiler">The stage runner, which remembers what it has already done.</param>
        /// <param name="tree">The tree.</param>
        /// <param name="context">What the leaves depend on besides their own text.</param>
        /// <param name="limits">The ceilings for this call site.</param>
        /// <param name="sink">Where to record problems.</param>
        /// <param name="location">Which clause the tree belongs to.</param>
        /// <param name="budget">The ceilings shared across the compilation.</param>
        /// <param name="nullity">The answers for every leaf, merged.</param>
        /// <returns>The bound predicate, or null when a leaf failed.</returns>
        internal static TypedExpr? Bind(
            ExpressionCompiler compiler,
            FilterTree tree,
            BindingContext context,
            QueryexLimits limits,
            DiagnosticSink sink,
            DiagnosticLocation location,
            CompilationBudget budget,
            NullityMap nullity)
        {
            return tree switch
            {
                FilterTree.LeafNode leaf =>
                    BindLeaf(compiler, leaf, context, limits, sink, location, budget, nullity),
                FilterTree.NotNode negation =>
                    Negate(compiler, negation, context, limits, sink, location, budget, nullity),
                FilterTree.AndNode conjunction => Join(
                    compiler,
                    conjunction.Children,
                    LogicalOperator.And,
                    FilterConnective.And,
                    context,
                    limits,
                    sink,
                    location,
                    budget,
                    nullity),
                FilterTree.OrNode disjunction => Join(
                    compiler,
                    disjunction.Children,
                    LogicalOperator.Or,
                    FilterConnective.Or,
                    context,
                    limits,
                    sink,
                    location,
                    budget,
                    nullity),
                _ => null,
            };
        }

        /// <summary>Binds a negation.</summary>
        /// <param name="compiler">The stage runner.</param>
        /// <param name="negation">The negation.</param>
        /// <param name="context">What the leaves depend on besides their own text.</param>
        /// <param name="limits">The ceilings for this call site.</param>
        /// <param name="sink">Where to record problems.</param>
        /// <param name="location">Which clause the tree belongs to.</param>
        /// <param name="budget">The ceilings shared across the compilation.</param>
        /// <param name="nullity">The answers for every leaf, merged.</param>
        /// <returns>The bound predicate, or null when the operand failed.</returns>
        private static TypedNot? Negate(
            ExpressionCompiler compiler,
            FilterTree.NotNode negation,
            BindingContext context,
            QueryexLimits limits,
            DiagnosticSink sink,
            DiagnosticLocation location,
            CompilationBudget budget,
            NullityMap nullity)
        {
            TypedExpr? operand = Bind(
                compiler,
                negation.Operand,
                context,
                limits,
                sink,
                location.Child(FilterConnective.Not, 0),
                budget,
                nullity);

            if (operand is null)
            {
                return null;
            }

            TypedNot not = new(operand.Span, operand);
            nullity.Set(not, QueryexNullity.NotNull);
            return not;
        }

        /// <summary>Binds one leaf.</summary>
        /// <param name="compiler">The stage runner.</param>
        /// <param name="leaf">The leaf.</param>
        /// <param name="context">What the leaf depends on besides its own text.</param>
        /// <param name="limits">The ceilings for this call site.</param>
        /// <param name="sink">Where to record problems.</param>
        /// <param name="location">Which input this leaf is.</param>
        /// <param name="budget">The ceilings shared across the compilation.</param>
        /// <param name="nullity">The answers for every leaf, merged.</param>
        /// <returns>The bound predicate, or null when it failed.</returns>
        private static TypedExpr? BindLeaf(
            ExpressionCompiler compiler,
            FilterTree.LeafNode leaf,
            BindingContext context,
            QueryexLimits limits,
            DiagnosticSink sink,
            DiagnosticLocation location,
            CompilationBudget budget,
            NullityMap nullity)
        {
            if (budget.Exceeded)
            {
                // The ceiling is already blown and has already said so. Parsing and binding this
                // leaf could only add to a total that is over, so the ceiling bounds the work done
                // rather than merely describing it afterwards.
                return null;
            }

            bool compiled = compiler.TryCompile(
                leaf.Text,
                context,
                directions: false,
                limits,
                sink,
                location,
                out _,
                out BoundExpression? bound);

            if (!compiled || bound is null || bound.Items.Length != 1)
            {
                return null;
            }

            budget.TryConsumeTypedNodes(bound.NodeCount, bound.Items[0].Span, sink.Scope(location));
            nullity.Absorb(bound.Nullity);
            return bound.Items[0].Expression;
        }

        /// <summary>Binds every child of a connective and joins the results.</summary>
        /// <param name="compiler">The stage runner.</param>
        /// <param name="children">The children.</param>
        /// <param name="op">The connective.</param>
        /// <param name="connective">The connective, as a location component.</param>
        /// <param name="context">What the leaves depend on besides their own text.</param>
        /// <param name="limits">The ceilings for this call site.</param>
        /// <param name="sink">Where to record problems.</param>
        /// <param name="location">Which clause the tree belongs to.</param>
        /// <param name="budget">The ceilings shared across the compilation.</param>
        /// <param name="nullity">The answers for every leaf, merged.</param>
        /// <returns>The bound predicate, or null when a child failed.</returns>
        /// <remarks>
        ///     An empty conjunction is true and an empty disjunction is false. The second is the one
        ///     that matters: an empty set of permissions has to deny access, and folding it with the
        ///     other identity would grant it instead.
        /// </remarks>
        private static TypedExpr? Join(
            ExpressionCompiler compiler,
            IReadOnlyList<FilterTree> children,
            LogicalOperator op,
            FilterConnective connective,
            BindingContext context,
            QueryexLimits limits,
            DiagnosticSink sink,
            DiagnosticLocation location,
            CompilationBudget budget,
            NullityMap nullity)
        {
            if (children.Count == 0)
            {
                TypedLiteral identity = new(BoundType.Bool, default, op == LogicalOperator.And);
                nullity.Set(identity, QueryexNullity.NotNull);
                return identity;
            }

            ImmutableArray<TypedExpr>.Builder operands =
                ImmutableArray.CreateBuilder<TypedExpr>(children.Count);

            bool failed = false;
            for (int index = 0; index < children.Count; index++)
            {
                TypedExpr? operand = Bind(
                    compiler,
                    children[index],
                    context,
                    limits,
                    sink,
                    location.Child(connective, index),
                    budget,
                    nullity);

                // Every child is bound even after one has failed, because a caller fixing a stored
                // definition wants to see everything wrong with it rather than one thing at a time.
                // A blown ceiling is the one exception: it is not a fault in this child, and the
                // remaining children would only add to a total that is already over.
                if (operand is null)
                {
                    failed = true;
                    if (budget.Exceeded)
                    {
                        break;
                    }

                    continue;
                }

                operands.Add(operand);
            }

            if (failed)
            {
                return null;
            }

            if (operands.Count == 1)
            {
                return operands[0];
            }

            TypedLogical logical = new(default, op, operands.ToImmutable());
            nullity.Set(logical, QueryexNullity.NotNull);
            return logical;
        }
    }
}
