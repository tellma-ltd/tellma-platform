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
    ///     <para>
    ///         Each leaf is parsed and bound entirely on its own, and only the finished trees are
    ///         joined together. That is the whole point of composing structurally rather than
    ///         textually: no operator inside a leaf can reach outside it, so conjoining an
    ///         access-control criterion onto a filter of <c>A or B</c> cannot quietly become
    ///         <c>A or (B and C)</c> and hand back every row that matches <c>A</c>.
    ///     </para>
    ///     <para>
    ///         A rebase and a semi-join change what their criterion's paths resolve from: the
    ///         criterion is bound against the navigation's target, or against the searched entity,
    ///         rather than against the query's root. Nothing else about the leaves under them changes.
    ///     </para>
    /// </remarks>
    internal static class FilterBinder
    {
        /// <summary>The parameter name a caller error from a tree's structure is attributed to.</summary>
        private const string SpecParameter = "spec";

        /// <summary>Binds a whole filter tree into one predicate.</summary>
        /// <param name="compiler">The stage runner, which remembers what it has already done.</param>
        /// <param name="tree">The tree.</param>
        /// <param name="context">What the leaves depend on besides their own text.</param>
        /// <param name="limits">The ceilings for this call site.</param>
        /// <param name="sink">Where to record problems.</param>
        /// <param name="location">Which clause the tree belongs to.</param>
        /// <param name="budget">The ceilings shared across the compilation.</param>
        /// <param name="nullity">The answers for every leaf, merged.</param>
        /// <returns>
        ///     The bound predicate, or null when a leaf failed. Every leaf, rebase and semi-join in it
        ///     carries the input it was written in, so lowering can name it.
        /// </returns>
        /// <exception cref="ArgumentException">
        ///     A rebase or a semi-join does not fit the schema. A compilation has refused such a tree
        ///     before it gets here, so this is reached only by a caller that skipped that check.
        /// </exception>
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
            TreeBinder binder = new(compiler, limits, sink, budget, nullity);
            return binder.Bind(tree, context, location);
        }

        /// <summary>One binding of one filter tree, holding what every node of it shares.</summary>
        /// <param name="compiler">The stage runner.</param>
        /// <param name="limits">The ceilings for this call site.</param>
        /// <param name="sink">Where to record problems.</param>
        /// <param name="budget">The ceilings shared across the compilation.</param>
        /// <param name="nullity">The answers for every leaf, merged.</param>
        private sealed class TreeBinder(
            ExpressionCompiler compiler,
            QueryexLimits limits,
            DiagnosticSink sink,
            CompilationBudget budget,
            NullityMap nullity)
        {
            /// <summary>Binds one node of the tree, by its kind.</summary>
            /// <param name="tree">The node.</param>
            /// <param name="context">What its leaves depend on besides their own text.</param>
            /// <param name="location">Which input the node is.</param>
            /// <returns>The bound predicate, or null when a leaf failed.</returns>
            internal TypedExpr? Bind(FilterTree tree, BindingContext context, DiagnosticLocation location)
            {
                return tree switch
                {
                    FilterTree.LeafNode leaf => BindLeaf(leaf, context, location),
                    FilterTree.NotNode negation => Negate(negation, context, location),
                    FilterTree.AndNode conjunction => Join(
                        conjunction.Children,
                        LogicalOperator.And,
                        FilterConnective.And,
                        context,
                        location),
                    FilterTree.OrNode disjunction => Join(
                        disjunction.Children,
                        LogicalOperator.Or,
                        FilterConnective.Or,
                        context,
                        location),
                    FilterTree.ViaNode via => BindVia(via, context, location),
                    FilterTree.ExistsNode exists => BindExists(exists, context, location),
                    _ => null,
                };
            }

            /// <summary>Binds a negation.</summary>
            /// <param name="negation">The negation.</param>
            /// <param name="context">What the leaves depend on besides their own text.</param>
            /// <param name="location">Which input the negation is.</param>
            /// <returns>The bound predicate, or null when the operand failed.</returns>
            private TypedNot? Negate(FilterTree.NotNode negation, BindingContext context, DiagnosticLocation location)
            {
                TypedExpr? operand = Bind(negation.Operand, context, location.Child(FilterConnective.Not, 0));
                if (operand is null)
                {
                    return null;
                }

                TypedNot not = new(operand.Span, operand);
                nullity.Set(not, QueryexNullity.NotNull);
                return not;
            }

            /// <summary>Binds one leaf.</summary>
            /// <param name="leaf">The leaf.</param>
            /// <param name="context">What the leaf depends on besides its own text.</param>
            /// <param name="location">Which input this leaf is.</param>
            /// <returns>The bound predicate, at its place in the tree, or null when it failed.</returns>
            private TypedFilterLeaf? BindLeaf(FilterTree.LeafNode leaf, BindingContext context, DiagnosticLocation location)
            {
                if (budget.Exceeded)
                {
                    // The ceiling is already blown and has already said so. Parsing and binding this
                    // leaf could only add to a total that is over, so the ceiling bounds the work done
                    // rather than merely describing it afterwards.
                    return null;
                }

                // A stamped leaf binds under the version stored beside it, which is what it was
                // validated under; an unstamped one takes the compilation's.
                BindingContext leafContext = leaf.QueryexVersion is int stamped && stamped != context.QueryexVersion
                    ? context with { QueryexVersion = stamped }
                    : context;

                bool compiled = compiler.TryCompile(
                    leaf.Text,
                    leafContext,
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

                // The bound expression is shared by every place this text sits under this root; the
                // node around it is this place alone.
                TypedExpr expression = bound.Items[0].Expression;
                TypedFilterLeaf placed = new(expression, location);
                nullity.Set(placed, bound.Nullity[expression]);
                return placed;
            }

            /// <summary>Binds every child of a connective and joins the results.</summary>
            /// <param name="children">The children.</param>
            /// <param name="op">The connective.</param>
            /// <param name="connective">The connective, as a location component.</param>
            /// <param name="context">What the leaves depend on besides their own text.</param>
            /// <param name="location">Which input the connective is.</param>
            /// <returns>The bound predicate, or null when a child failed.</returns>
            /// <remarks>
            ///     An empty conjunction is true and an empty disjunction is false. The second is the
            ///     one that matters: an empty set of permissions has to deny access, and folding it
            ///     with the other identity would grant it instead.
            /// </remarks>
            private TypedExpr? Join(
                IReadOnlyList<FilterTree> children,
                LogicalOperator op,
                FilterConnective connective,
                BindingContext context,
                DiagnosticLocation location)
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
                    TypedExpr? operand = Bind(children[index], context, location.Child(connective, index));

                    // Every child is bound even after one has failed, because a caller fixing a
                    // stored definition wants to see everything wrong with it rather than one thing at
                    // a time. A blown ceiling is the one exception: it is not a fault in this child,
                    // and the remaining children would only add to a total that is already over.
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

            /// <summary>Binds a criterion rebased onto the row a navigation path reaches.</summary>
            /// <param name="via">The node.</param>
            /// <param name="context">What the leaves depend on besides their own text.</param>
            /// <param name="location">Which input the node is.</param>
            /// <returns>The bound predicate, or null when the criterion failed.</returns>
            /// <remarks>
            ///     The criterion is bound with the path's target as its root, and with nothing else
            ///     changed: its parameters, context functions and Queryex version are the
            ///     compilation's, so a leaf binds here exactly as it would on a query over the target.
            ///     Its nullity is therefore the target's own, which is sound wherever the target row
            ///     exists — and lowering makes sure the criterion is read only there.
            /// </remarks>
            private TypedVia? BindVia(FilterTree.ViaNode via, BindingContext context, DiagnosticLocation location)
            {
                ResolvedVia resolved = FilterStructure.ResolveVia(RootOf(context), via, location, SpecParameter);
                DiagnosticLocation inside = location.Via(via.Navigation);

                TypedExpr? inner = Bind(via.Inner, context with { Root = resolved.Target }, inside);
                if (inner is null)
                {
                    return null;
                }

                TypedVia node = new(resolved.Navigations, resolved.Segments, inner, inside);
                nullity.Set(node, QueryexNullity.NotNull);
                return node;
            }

            /// <summary>Binds a semi-join over the rows of another entity.</summary>
            /// <param name="exists">The node.</param>
            /// <param name="context">What the leaves depend on besides their own text.</param>
            /// <param name="location">Which input the node is.</param>
            /// <returns>The bound predicate, or null when the criterion failed.</returns>
            /// <remarks>
            ///     The criterion is bound with the searched entity as its root. Every row it is read
            ///     against exists — the subquery reads that entity's own rows — so its nullity needs no
            ///     guard of any kind.
            /// </remarks>
            private TypedExists? BindExists(
                FilterTree.ExistsNode exists,
                BindingContext context,
                DiagnosticLocation location)
            {
                ResolvedExists resolved = FilterStructure.ResolveExists(
                    context.Schema ?? throw Unanchored(),
                    RootOf(context),
                    exists,
                    location,
                    SpecParameter);

                DiagnosticLocation inside = location.Exists(exists.Entity);
                TypedExpr? inner = Bind(exists.Inner, context with { Root = resolved.Entity }, inside);
                if (inner is null)
                {
                    return null;
                }

                TypedExists node = new(resolved.Entity, resolved.Navigation, inner, inside);
                nullity.Set(node, QueryexNullity.NotNull);
                return node;
            }

            /// <summary>The entity the node's paths resolve from.</summary>
            /// <param name="context">The context.</param>
            /// <returns>The root.</returns>
            /// <exception cref="InvalidOperationException">The context carries no schema.</exception>
            private static EntityDescriptor RootOf(BindingContext context)
            {
                return context.Root ?? throw Unanchored();
            }

            /// <summary>The failure for a rebase or semi-join bound without a schema.</summary>
            /// <returns>The exception to throw.</returns>
            /// <remarks>
            ///     Unreachable: a compilation always has a schema, and discovery without one reads its
            ///     leaves without binding them.
            /// </remarks>
            private static InvalidOperationException Unanchored()
            {
                return new InvalidOperationException("A Via or an Exists needs a schema and a root to bind against.");
            }
        }
    }
}
