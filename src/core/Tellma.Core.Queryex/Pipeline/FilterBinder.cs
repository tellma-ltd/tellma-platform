// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Diagnostics;
using Tellma.Core.Queryex.Nullity;
using Tellma.Core.Queryex.Syntax;

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
    ///     <para>
    ///         Where the compilation leaves parameters out, the tree is folded before any of it is
    ///         bound, exactly as one text holding the same connectives would be: every leaf is parsed
    ///         and folded on its own under the parity of the negations above it, what settled is
    ///         absorbed by the connectives around it, and only what is left is bound. A leaf that
    ///         folded away is never bound, so nothing is reported about it.
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
        /// <param name="omitted">The parameters the compilation leaves out, when it leaves any.</param>
        /// <returns>
        ///     The bound predicate. Null when a leaf failed, in which case the sink holds why, and
        ///     null when the omitted parameters folded the whole tree to true, in which case it
        ///     filters nothing. Every leaf, rebase and semi-join in it carries the input it was
        ///     written in, so lowering can name it.
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
            NullityMap nullity,
            OmittedParameters? omitted = null)
        {
            TreeBinder binder = new(compiler, limits, sink, budget, nullity);
            if (omitted is null || omitted.IsEmpty)
            {
                return binder.Bind(tree, context, location);
            }

            // Folded whole before anything is bound, so what is reported never depends on the order
            // the children of a connective happen to stand in.
            TreeFolder folder = new(compiler, limits, sink, budget, omitted);
            Reduced reduced = folder.Reduce(tree, location, negated: false);
            return folder.Failed ? null : binder.Bind(reduced, context);
        }

        /// <summary>One node of a filter tree after the omitted parameters were folded out of it.</summary>
        private abstract record Reduced;

        /// <summary>A part of the tree that folded to a truth value.</summary>
        /// <param name="Value">The truth value.</param>
        /// <remarks>
        ///     Absorbed by whatever connective stands above it, so one survives only as the whole
        ///     tree.
        /// </remarks>
        private sealed record ReducedConstant(bool Value) : Reduced;

        /// <summary>A leaf an expression is left of.</summary>
        /// <param name="Leaf">The leaf as the caller composed it.</param>
        /// <param name="Syntax">The parse of its text, folded.</param>
        /// <param name="Folding">What was folded out of it, or null when nothing was.</param>
        /// <param name="Location">Which input the leaf is.</param>
        private sealed record ReducedLeaf(
            FilterTree.LeafNode Leaf,
            ExpressionListSyntax Syntax,
            AppliedFolding? Folding,
            DiagnosticLocation Location) : Reduced;

        /// <summary>A negation whose operand is left.</summary>
        /// <param name="Operand">The operand.</param>
        private sealed record ReducedNot(Reduced Operand) : Reduced;

        /// <summary>A connective and the operands that are left of it.</summary>
        /// <param name="Operator">The connective.</param>
        /// <param name="Operands">The operands; none only where the caller composed none.</param>
        private sealed record ReducedJunction(LogicalOperator Operator, ImmutableArray<Reduced> Operands) : Reduced;

        /// <summary>A rebase or a semi-join that mentions no omitted parameter, and binds as composed.</summary>
        /// <param name="Node">The node as the caller composed it.</param>
        /// <param name="Location">Which input the node is.</param>
        private sealed record ReducedStructure(FilterTree Node, DiagnosticLocation Location) : Reduced;

        /// <summary>A leaf whose text could not be read.</summary>
        private sealed record ReducedFailure : Reduced;

        /// <summary>One folding of one filter tree.</summary>
        /// <param name="compiler">The stage runner.</param>
        /// <param name="limits">The ceilings for this call site.</param>
        /// <param name="sink">Where to record problems.</param>
        /// <param name="budget">The ceilings shared across the compilation.</param>
        /// <param name="omitted">The parameters the compilation leaves out.</param>
        private sealed class TreeFolder(
            ExpressionCompiler compiler,
            QueryexLimits limits,
            DiagnosticSink sink,
            CompilationBudget budget,
            OmittedParameters omitted)
        {
            /// <summary>Whether some leaf's text could not be read.</summary>
            internal bool Failed { get; private set; }

            /// <summary>Folds one node of the tree, by its kind.</summary>
            /// <param name="tree">The node.</param>
            /// <param name="location">Which input the node is.</param>
            /// <param name="negated">Whether an odd number of negations stand above the node.</param>
            /// <returns>What is left of it.</returns>
            /// <remarks>
            ///     Recursive, which the tree's construction-time depth ceiling keeps bounded.
            /// </remarks>
            internal Reduced Reduce(FilterTree tree, DiagnosticLocation location, bool negated)
            {
                switch (tree)
                {
                    case FilterTree.LeafNode leaf:
                        return ReduceLeaf(leaf, location, negated);

                    case FilterTree.NotNode negation:

                        // A negation node counts exactly as one written in a leaf does: it flips the
                        // polarity beneath it, and flips whatever settles there.
                        Reduced operand = Reduce(
                            negation.Operand,
                            location.Child(FilterConnective.Not, 0),
                            !negated);

                        return operand switch
                        {
                            ReducedConstant constant => new ReducedConstant(!constant.Value),
                            ReducedFailure => operand,
                            _ => new ReducedNot(operand),
                        };

                    case FilterTree.AndNode conjunction:
                        return ReduceJunction(
                            conjunction.Children,
                            LogicalOperator.And,
                            FilterConnective.And,
                            location,
                            negated);

                    case FilterTree.OrNode disjunction:
                        return ReduceJunction(
                            disjunction.Children,
                            LogicalOperator.Or,
                            FilterConnective.Or,
                            location,
                            negated);

                    // A rebase and a semi-join are each one atom: either nothing inside mentions an
                    // omitted parameter and the node binds as composed, or something does and the
                    // whole node stops deciding anything.
                    case FilterTree.ViaNode via:
                        return Mentions(via.Inner, location.Via(via.Navigation))
                            ? new ReducedConstant(!negated)
                            : new ReducedStructure(tree, location);

                    case FilterTree.ExistsNode exists:
                        return Mentions(exists.Inner, location.Exists(exists.Entity))
                            ? new ReducedConstant(!negated)
                            : new ReducedStructure(tree, location);

                    default:
                        return new ReducedStructure(tree, location);
                }
            }

            /// <summary>Parses one leaf and folds it.</summary>
            /// <param name="leaf">The leaf.</param>
            /// <param name="location">Which input the leaf is.</param>
            /// <param name="negated">Whether an odd number of negations stand above the leaf.</param>
            /// <returns>What is left of it.</returns>
            private Reduced ReduceLeaf(FilterTree.LeafNode leaf, DiagnosticLocation location, bool negated)
            {
                if (!TryParse(leaf, location, out ExpressionListSyntax? syntax))
                {
                    return new ReducedFailure();
                }

                FoldedPredicateList folded = omitted.FoldPredicate(syntax, negated, out AppliedFolding? folding);
                return folded.Constant is bool settled
                    ? new ReducedConstant(settled)
                    : new ReducedLeaf(leaf, folded.List!, folding, location);
            }

            /// <summary>Folds every child of a connective and absorbs what settled.</summary>
            /// <param name="children">The children.</param>
            /// <param name="op">The connective.</param>
            /// <param name="connective">The connective, as a location component.</param>
            /// <param name="location">Which input the connective is.</param>
            /// <param name="negated">Whether an odd number of negations stand above the connective.</param>
            /// <returns>What is left of it.</returns>
            /// <remarks>
            ///     Every child is folded even after one has settled the connective, so a text that
            ///     cannot be read is reported wherever it sits. A connective the caller composed with
            ///     no children at all is not something folding produced, and is left for binding to
            ///     give its identity as it always has.
            /// </remarks>
            private Reduced ReduceJunction(
                IReadOnlyList<FilterTree> children,
                LogicalOperator op,
                FilterConnective connective,
                DiagnosticLocation location,
                bool negated)
            {
                bool identity = op == LogicalOperator.And;
                bool settled = false;
                bool failed = false;

                ImmutableArray<Reduced>.Builder operands = ImmutableArray.CreateBuilder<Reduced>(children.Count);
                for (int index = 0; index < children.Count; index++)
                {
                    Reduced operand = Reduce(children[index], location.Child(connective, index), negated);
                    switch (operand)
                    {
                        case ReducedFailure:
                            failed = true;
                            break;

                        // The identity leaves the other operands; the other truth value settles the
                        // whole connective.
                        case ReducedConstant constant:
                            settled |= constant.Value != identity;
                            break;

                        default:
                            operands.Add(operand);
                            break;
                    }
                }

                if (failed)
                {
                    return new ReducedFailure();
                }

                if (settled)
                {
                    return new ReducedConstant(!identity);
                }

                if (children.Count > 0 && operands.Count == 0)
                {
                    // Every child was absorbed, which leaves the connective's identity.
                    return new ReducedConstant(identity);
                }

                return operands.Count == 1 ? operands[0] : new ReducedJunction(op, operands.ToImmutable());
            }

            /// <summary>Whether any leaf under a node refers to an omitted parameter.</summary>
            /// <param name="tree">The node.</param>
            /// <param name="location">Which input the node is.</param>
            /// <returns>True when one does.</returns>
            /// <remarks>
            ///     Every leaf is parsed whatever the earlier ones answered, so a text that cannot be
            ///     read is reported whether or not the node around it folds away.
            /// </remarks>
            private bool Mentions(FilterTree tree, DiagnosticLocation location)
            {
                return tree switch
                {
                    FilterTree.LeafNode leaf => TryParse(leaf, location, out ExpressionListSyntax? syntax)
                        && !omitted.ReferredToBy(syntax).IsEmpty,
                    FilterTree.NotNode negation => Mentions(
                        negation.Operand,
                        location.Child(FilterConnective.Not, 0)),
                    FilterTree.AndNode conjunction => MentionsAny(
                        conjunction.Children,
                        FilterConnective.And,
                        location),
                    FilterTree.OrNode disjunction => MentionsAny(
                        disjunction.Children,
                        FilterConnective.Or,
                        location),
                    FilterTree.ViaNode via => Mentions(via.Inner, location.Via(via.Navigation)),
                    FilterTree.ExistsNode exists => Mentions(exists.Inner, location.Exists(exists.Entity)),
                    _ => false,
                };
            }

            /// <summary>Whether any leaf under any child of a connective refers to an omitted parameter.</summary>
            /// <param name="children">The children.</param>
            /// <param name="connective">The connective, as a location component.</param>
            /// <param name="location">Which input the connective is.</param>
            /// <returns>True when one does.</returns>
            private bool MentionsAny(
                IReadOnlyList<FilterTree> children,
                FilterConnective connective,
                DiagnosticLocation location)
            {
                bool mentions = false;
                for (int index = 0; index < children.Count; index++)
                {
                    mentions |= Mentions(children[index], location.Child(connective, index));
                }

                return mentions;
            }

            /// <summary>Parses one leaf's text.</summary>
            /// <param name="leaf">The leaf.</param>
            /// <param name="location">Which input the leaf is.</param>
            /// <param name="syntax">The parse, when the text parsed.</param>
            /// <returns>True when the text parsed.</returns>
            private bool TryParse(
                FilterTree.LeafNode leaf,
                DiagnosticLocation location,
                [NotNullWhen(true)] out ExpressionListSyntax? syntax)
            {
                syntax = null;

                // A blown ceiling has already said so, and bounds the work done from then on.
                if (budget.Exceeded
                    || !compiler.TryParse(leaf.Text, limits, sink, location, out syntax)
                    || syntax is null)
                {
                    Failed = true;
                    return false;
                }

                return true;
            }
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

            /// <summary>Binds what folding left of a tree.</summary>
            /// <param name="reduced">What is left.</param>
            /// <param name="context">What the leaves depend on besides their own text.</param>
            /// <returns>
            ///     The bound predicate, or null when a leaf failed or the whole tree folded to true.
            /// </returns>
            internal TypedExpr? Bind(Reduced reduced, BindingContext context)
            {
                return reduced switch
                {
                    // Nothing is left to hold: the tree filters nothing, and is no predicate.
                    ReducedConstant { Value: true } => null,

                    // A whole tree stands under no negation, where everything folding settles is
                    // true, so this is not expected to be reached. It is bound as what it says
                    // rather than assumed away: a filter that denies every row must never be
                    // mistaken for one that is not there.
                    ReducedConstant => Constant(false),
                    ReducedLeaf leaf => Place(leaf.Leaf, leaf.Syntax, leaf.Folding, context, leaf.Location),
                    ReducedNot negation => Negated(Bind(negation.Operand, context)),
                    ReducedJunction junction => Join(junction, context),
                    ReducedStructure structure => Bind(structure.Node, context, structure.Location),
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
                return Negated(Bind(negation.Operand, context, location.Child(FilterConnective.Not, 0)));
            }

            /// <summary>Negates a bound operand.</summary>
            /// <param name="operand">The operand, or null when it failed.</param>
            /// <returns>The bound predicate, or null when the operand failed.</returns>
            private TypedNot? Negated(TypedExpr? operand)
            {
                if (operand is null)
                {
                    return null;
                }

                TypedNot not = new(operand.Span, operand);
                nullity.Set(not, QueryexNullity.NotNull);
                return not;
            }

            /// <summary>A truth value standing where a predicate would.</summary>
            /// <param name="value">The truth value.</param>
            /// <returns>The bound literal.</returns>
            private TypedLiteral Constant(bool value)
            {
                TypedLiteral literal = new(BoundType.Bool, default, value);
                nullity.Set(literal, QueryexNullity.NotNull);
                return literal;
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

                return compiler.TryParse(leaf.Text, limits, sink, location, out ExpressionListSyntax? syntax)
                    && syntax is not null
                        ? Place(leaf, syntax, null, context, location)
                        : null;
            }

            /// <summary>Binds one parsed leaf and puts it at its place in the tree.</summary>
            /// <param name="leaf">The leaf.</param>
            /// <param name="syntax">The parse of its text, folded when anything was omitted from it.</param>
            /// <param name="folding">What was folded out of the parse, or null when nothing was.</param>
            /// <param name="context">What the leaf depends on besides its own text.</param>
            /// <param name="location">Which input this leaf is.</param>
            /// <returns>The bound predicate, at its place in the tree, or null when it failed.</returns>
            private TypedFilterLeaf? Place(
                FilterTree.LeafNode leaf,
                ExpressionListSyntax syntax,
                AppliedFolding? folding,
                BindingContext context,
                DiagnosticLocation location)
            {
                if (budget.Exceeded)
                {
                    return null;
                }

                // A stamped leaf binds under the version stored beside it, which is what it was
                // validated under; an unstamped one takes the compilation's.
                BindingContext leafContext = leaf.QueryexVersion is int stamped && stamped != context.QueryexVersion
                    ? context with { QueryexVersion = stamped }
                    : context;

                // Success is measured against what this leaf added to the sink, which the leaves of
                // one tree share.
                int before = sink.Count;
                BoundExpression bound = compiler.Bind(
                    leaf.Text,
                    syntax,
                    leafContext,
                    directions: false,
                    limits,
                    sink,
                    location,
                    folding);

                if (sink.Count != before || bound.Items.Length != 1)
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
                return Join(
                    children.Count,
                    index => Bind(children[index], context, location.Child(connective, index)),
                    op);
            }

            /// <summary>Binds the operands folding left of a connective and joins the results.</summary>
            /// <param name="junction">The connective.</param>
            /// <param name="context">What the leaves depend on besides their own text.</param>
            /// <returns>The bound predicate, or null when an operand failed.</returns>
            private TypedExpr? Join(ReducedJunction junction, BindingContext context)
            {
                return Join(
                    junction.Operands.Length,
                    index => Bind(junction.Operands[index], context),
                    junction.Operator);
            }

            /// <summary>Binds every operand of a connective and joins the results.</summary>
            /// <param name="count">How many operands there are.</param>
            /// <param name="bind">What binds the operand at a position.</param>
            /// <param name="op">The connective.</param>
            /// <returns>The bound predicate, or null when an operand failed.</returns>
            private TypedExpr? Join(int count, Func<int, TypedExpr?> bind, LogicalOperator op)
            {
                if (count == 0)
                {
                    return Constant(op == LogicalOperator.And);
                }

                ImmutableArray<TypedExpr>.Builder operands = ImmutableArray.CreateBuilder<TypedExpr>(count);

                bool failed = false;
                for (int index = 0; index < count; index++)
                {
                    TypedExpr? operand = bind(index);

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
