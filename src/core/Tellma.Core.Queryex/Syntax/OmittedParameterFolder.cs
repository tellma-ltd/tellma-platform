// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using Tellma.Core.Queryex.Functions;

namespace Tellma.Core.Queryex.Syntax
{
    /// <summary>
    ///     What folding one predicate came to: a truth value already settled, or the expression that
    ///     is left.
    /// </summary>
    /// <param name="Constant">The settled truth value, or null when an expression is left.</param>
    /// <param name="Node">The expression that is left, or null when the predicate is settled.</param>
    internal readonly record struct FoldedPredicate(bool? Constant, SyntaxNode? Node)
    {
        /// <summary>Whether the predicate folded away entirely.</summary>
        internal bool IsConstant => Constant is not null;

        /// <summary>A predicate that folded to a truth value.</summary>
        /// <param name="value">The truth value.</param>
        /// <returns>The outcome.</returns>
        internal static FoldedPredicate Settled(bool value)
        {
            return new FoldedPredicate(value, null);
        }

        /// <summary>A predicate an expression is left of.</summary>
        /// <param name="node">The expression.</param>
        /// <returns>The outcome.</returns>
        internal static FoldedPredicate Left(SyntaxNode node)
        {
            return new FoldedPredicate(null, node);
        }
    }

    /// <summary>
    ///     What folding one predicate text came to: a truth value already settled, or the list that
    ///     is left to bind.
    /// </summary>
    /// <param name="Constant">The settled truth value, or null when a list is left.</param>
    /// <param name="List">The list that is left, or null when the text is settled.</param>
    internal readonly record struct FoldedPredicateList(bool? Constant, ExpressionListSyntax? List);

    /// <summary>
    ///     Folds the parameters a caller left out away from a parse tree, before anything is bound.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A parameter left out imposes no restriction. In a predicate that means the smallest
    ///         part that mentions it stops deciding anything: read along the predicate's skeleton —
    ///         its conjunctions, disjunctions and negations — each such part becomes the truth value
    ///         that lets every row through, which is true under an even number of negations and false
    ///         under an odd one, and the connectives around it then absorb it. Everywhere else the
    ///         parameter is the absent value of its declared type, written the way an author would
    ///         write it.
    ///     </para>
    ///     <para>
    ///         Purely syntactic, and that is what makes one implementation serve every consumer: the
    ///         result is a parse tree like any other, so it binds, prints and compares with the
    ///         machinery that already exists. Every rebuilt node keeps the span of the node it
    ///         replaces, so a diagnostic about what is left still points into the text as written.
    ///         A tree that mentions no omitted parameter comes back as the same instance.
    ///     </para>
    ///     <para>
    ///         Immutable once built, and so safe to share between compilations.
    ///     </para>
    /// </remarks>
    internal sealed class OmittedParameterFolder
    {
        /// <summary>The conversion's name, as an author writes it.</summary>
        private const string Conversion = "cast";

        /// <summary>The position of an aggregation's condition among its arguments.</summary>
        private const int ConditionIndex = 1;

        /// <summary>
        ///     The name each omitted parameter's declared type goes by as a conversion target, by
        ///     parameter name ignoring case.
        /// </summary>
        private readonly FrozenDictionary<string, string> _targets;

        /// <summary>Initializes a folder over one set of omitted parameters.</summary>
        /// <param name="targets">
        ///     The conversion target each omitted parameter's declared type goes by, keyed by the
        ///     parameter's name ignoring case.
        /// </param>
        internal OmittedParameterFolder(FrozenDictionary<string, string> targets)
        {
            _targets = targets;
        }

        /// <summary>Folds a list read as values: a select list or an ordering list.</summary>
        /// <param name="list">The parsed list.</param>
        /// <returns>The folded list, or the same instance when nothing in it changed.</returns>
        /// <remarks>
        ///     Each item is a value position, so an omitted parameter in it is the absent value of
        ///     its type — except inside the condition of an aggregation, which is a predicate
        ///     wherever the aggregation sits and folds as one.
        /// </remarks>
        internal ExpressionListSyntax FoldValues(ExpressionListSyntax list)
        {
            ExpressionItemSyntax[]? items = null;
            for (int index = 0; index < list.Items.Count; index++)
            {
                ExpressionItemSyntax item = list.Items[index];
                SyntaxNode folded = FoldValue(item.Expression);
                if (ReferenceEquals(folded, item.Expression))
                {
                    continue;
                }

                items ??= [.. list.Items];
                items[index] = new ExpressionItemSyntax(item.Span, folded, item.Direction, item.DirectionSpan);
            }

            return items is null ? list : Rebuilt(list, items);
        }

        /// <summary>Folds a text read as one predicate: a filter leaf or a group-level one.</summary>
        /// <param name="list">The parsed text.</param>
        /// <param name="negated">
        ///     Whether an odd number of negations stand above the text in the tree it was composed
        ///     into.
        /// </param>
        /// <returns>
        ///     The truth value the whole text folded to, or the list that is left — the same instance
        ///     when nothing in it changed.
        /// </returns>
        /// <remarks>
        ///     A predicate is one expression, and a text that is not — a list, or an item carrying a
        ///     direction — is a mistake binding reports. Such a text is never settled here: each item
        ///     folds on its own and a settled one is left as the literal it folded to, so the list
        ///     still reaches the binder and the mistake is still reported.
        /// </remarks>
        internal FoldedPredicateList FoldPredicate(ExpressionListSyntax list, bool negated)
        {
            bool single = list.Items.Count == 1 && list.Items[0].Direction == QueryexDirection.None;

            ExpressionItemSyntax[]? items = null;
            for (int index = 0; index < list.Items.Count; index++)
            {
                ExpressionItemSyntax item = list.Items[index];
                FoldedPredicate folded = FoldPredicate(item.Expression, negated);
                if (folded.Constant is bool constant)
                {
                    if (single)
                    {
                        return new FoldedPredicateList(constant, null);
                    }

                    folded = FoldedPredicate.Left(new BooleanSyntax(item.Expression.Span, constant));
                }

                if (ReferenceEquals(folded.Node, item.Expression))
                {
                    continue;
                }

                items ??= [.. list.Items];
                items[index] = new ExpressionItemSyntax(item.Span, folded.Node!, item.Direction, item.DirectionSpan);
            }

            return new FoldedPredicateList(null, items is null ? list : Rebuilt(list, items));
        }

        /// <summary>Folds one expression read as a predicate, along its skeleton.</summary>
        /// <param name="node">The expression.</param>
        /// <param name="negated">Whether an odd number of negations stand above it.</param>
        /// <returns>The truth value it folded to, or the expression that is left.</returns>
        /// <remarks>
        ///     Recursive, which the parser's depth ceiling keeps bounded. Only a truth value that
        ///     folding itself produced is absorbed: a literal the author wrote stays where it was
        ///     written, so the text that is left is the author's own apart from what was omitted.
        /// </remarks>
        internal FoldedPredicate FoldPredicate(SyntaxNode node, bool negated)
        {
            return node switch
            {
                ParenthesizedSyntax parenthesized => FoldGrouping(parenthesized, negated),
                UnarySyntax { OperatorKind: UnaryOperatorKind.Not } negation => FoldNegation(negation, negated),
                BinarySyntax { OperatorKind: BinaryOperatorKind.And or BinaryOperatorKind.Or } logical =>
                    FoldConnective(logical, negated),
                _ => FoldAtom(node, negated),
            };
        }

        /// <summary>Folds a parenthesized predicate, which is transparent to the skeleton.</summary>
        /// <param name="grouping">The grouping.</param>
        /// <param name="negated">Whether an odd number of negations stand above it.</param>
        /// <returns>The truth value it folded to, or the expression that is left.</returns>
        private FoldedPredicate FoldGrouping(ParenthesizedSyntax grouping, bool negated)
        {
            FoldedPredicate inner = FoldPredicate(grouping.Inner, negated);
            return inner.IsConstant
                ? inner
                : ReferenceEquals(inner.Node, grouping.Inner)
                    ? FoldedPredicate.Left(grouping)
                    : FoldedPredicate.Left(new ParenthesizedSyntax(grouping.Span, inner.Node!));
        }

        /// <summary>Folds a negation.</summary>
        /// <param name="negation">The negation.</param>
        /// <param name="negated">Whether an odd number of negations stand above it.</param>
        /// <returns>The truth value it folded to, or the expression that is left.</returns>
        private FoldedPredicate FoldNegation(UnarySyntax negation, bool negated)
        {
            // A negation flips the polarity below it, and flips whatever settles there.
            FoldedPredicate operand = FoldPredicate(negation.Operand, !negated);
            return operand.Constant is bool settled
                ? FoldedPredicate.Settled(!settled)
                : ReferenceEquals(operand.Node, negation.Operand)
                    ? FoldedPredicate.Left(negation)
                    : FoldedPredicate.Left(new UnarySyntax(
                        negation.Span,
                        UnaryOperatorKind.Not,
                        negation.OperatorSpan,
                        operand.Node!));
        }

        /// <summary>Folds a conjunction or a disjunction, absorbing what settled beneath it.</summary>
        /// <param name="logical">The connective.</param>
        /// <param name="negated">Whether an odd number of negations stand above it.</param>
        /// <returns>The truth value it folded to, or the expression that is left.</returns>
        private FoldedPredicate FoldConnective(BinarySyntax logical, bool negated)
        {
            bool conjunction = logical.OperatorKind == BinaryOperatorKind.And;
            FoldedPredicate left = FoldPredicate(logical.Left, negated);
            FoldedPredicate right = FoldPredicate(logical.Right, negated);

            // One false settles a conjunction and one true a disjunction; the other truth value is
            // the connective's identity and simply leaves the other operand.
            return left.Constant is bool first
                ? first == conjunction ? right : left
                : right.Constant is bool second
                    ? second == conjunction ? left : right
                    : FoldedPredicate.Left(Reassembled(logical, left.Node!, right.Node!));
        }

        /// <summary>A connective over what is left of its two operands.</summary>
        /// <param name="logical">The connective as parsed.</param>
        /// <param name="left">What is left of its first operand.</param>
        /// <param name="right">What is left of its second operand.</param>
        /// <returns>The connective, or the same instance when neither operand changed.</returns>
        private static BinarySyntax Reassembled(BinarySyntax logical, SyntaxNode left, SyntaxNode right)
        {
            return ReferenceEquals(left, logical.Left) && ReferenceEquals(right, logical.Right)
                ? logical
                : new BinarySyntax(logical.Span, logical.OperatorKind, logical.OperatorSpan, left, right);
        }

        /// <summary>Folds one atom of a predicate's skeleton.</summary>
        /// <param name="node">The atom: anything that is not a connective, a negation or a grouping.</param>
        /// <param name="negated">Whether an odd number of negations stand above it.</param>
        /// <returns>The truth value it folded to, or the atom that is left.</returns>
        private FoldedPredicate FoldAtom(SyntaxNode node, bool negated)
        {
            // First the lists: an omitted parameter that is one element among several restricts
            // nothing, so it leaves the list, and a list left empty settles the atom.
            SyntaxNode? atom = DropListElements(node);
            if (atom is null || MentionsOmitted(atom))
            {
                // What lets every row through: true where the atom is asserted, false where it is
                // denied.
                return FoldedPredicate.Settled(!negated);
            }

            // What is left mentions an omitted parameter only inside the condition of an
            // aggregation, which folds on its own.
            return FoldedPredicate.Left(FoldValue(atom));
        }

        /// <summary>
        ///     Drops the bare omitted parameters from the list of an atom that is a membership test or
        ///     a hierarchy predicate.
        /// </summary>
        /// <param name="node">The atom.</param>
        /// <returns>
        ///     The atom without them — the same instance when none was dropped — or null when the
        ///     list is left empty.
        /// </returns>
        private SyntaxNode? DropListElements(SyntaxNode node)
        {
            if (node is InSyntax membership)
            {
                List<SyntaxNode>? kept = Kept(membership.Elements, first: 0);
                return kept is null
                    ? node
                    : kept.Count == 0
                        ? null
                        : new InSyntax(membership.Span, membership.KeywordSpan, membership.Value, kept);
            }

            if (node is CallSyntax call && IsHierarchyPredicate(call.Name) && call.Arguments.Count > 1)
            {
                // The first argument names the row; the keys are everything after it.
                List<SyntaxNode>? kept = Kept(call.Arguments, first: 1);
                return kept is null
                    ? node
                    : kept.Count == 0
                        ? null
                        : new CallSyntax(call.Span, call.Name, call.NameSpan, [call.Arguments[0], .. kept]);
            }

            return node;
        }

        /// <summary>The elements of a list that are not bare omitted parameters.</summary>
        /// <param name="elements">The list, possibly behind arguments that are not part of it.</param>
        /// <param name="first">Where the list starts.</param>
        /// <returns>The elements that stay, or null when every element does.</returns>
        private List<SyntaxNode>? Kept(IReadOnlyList<SyntaxNode> elements, int first)
        {
            List<SyntaxNode>? kept = null;
            for (int index = first; index < elements.Count; index++)
            {
                bool dropped = SyntaxPrinter.Unwrap(elements[index]) is ParameterSyntax parameter
                    && _targets.ContainsKey(parameter.Name);

                if (dropped && kept is null)
                {
                    kept = [];
                    for (int earlier = first; earlier < index; earlier++)
                    {
                        kept.Add(elements[earlier]);
                    }
                }
                else if (!dropped)
                {
                    kept?.Add(elements[index]);
                }
            }

            return kept;
        }

        /// <summary>
        ///     Whether an expression mentions an omitted parameter anywhere but inside the condition
        ///     of an aggregation.
        /// </summary>
        /// <param name="node">The expression.</param>
        /// <returns>True when it does.</returns>
        /// <remarks>
        ///     An aggregation's condition is a predicate of its own: it decides which rows the
        ///     aggregation reads, and an omitted parameter in it lifts that restriction rather than
        ///     whatever the aggregation's result is then compared with.
        /// </remarks>
        private bool MentionsOmitted(SyntaxNode node)
        {
            if (node is ParameterSyntax parameter)
            {
                return _targets.ContainsKey(parameter.Name);
            }

            bool aggregation = node is CallSyntax call && FunctionRegistry.IsAggregate(call.Name);
            IReadOnlyList<SyntaxNode> children = node.Children;
            for (int index = 0; index < children.Count; index++)
            {
                if (aggregation && index == ConditionIndex)
                {
                    continue;
                }

                if (MentionsOmitted(children[index]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Folds one expression read as a value.</summary>
        /// <param name="node">The expression.</param>
        /// <returns>The folded expression, or the same instance when nothing in it changed.</returns>
        internal SyntaxNode FoldValue(SyntaxNode node)
        {
            switch (node)
            {
                case ParameterSyntax parameter when _targets.TryGetValue(parameter.Name, out string? target):

                    // The absent value of the parameter's type, exactly as an author gives a bare
                    // absent value a type. Every part carries the parameter's own span, so whatever
                    // is said about it points at the parameter.
                    return new CallSyntax(
                        parameter.Span,
                        Conversion,
                        parameter.Span,
                        [new NullSyntax(parameter.Span), new StringSyntax(parameter.Span, target)],
                        standsForParameter: true);

                case ParenthesizedSyntax parenthesized:
                    SyntaxNode inner = FoldValue(parenthesized.Inner);
                    return ReferenceEquals(inner, parenthesized.Inner)
                        ? node
                        : new ParenthesizedSyntax(parenthesized.Span, inner);

                case UnarySyntax unary:
                    SyntaxNode negated = FoldValue(unary.Operand);
                    return ReferenceEquals(negated, unary.Operand)
                        ? node
                        : new UnarySyntax(unary.Span, unary.OperatorKind, unary.OperatorSpan, negated);

                case BinarySyntax binary:
                    SyntaxNode left = FoldValue(binary.Left);
                    SyntaxNode right = FoldValue(binary.Right);
                    return ReferenceEquals(left, binary.Left) && ReferenceEquals(right, binary.Right)
                        ? node
                        : new BinarySyntax(binary.Span, binary.OperatorKind, binary.OperatorSpan, left, right);

                case IsNullSyntax absence:
                    SyntaxNode tested = FoldValue(absence.Operand);
                    return ReferenceEquals(tested, absence.Operand)
                        ? node
                        : new IsNullSyntax(absence.Span, absence.KeywordSpan, tested, absence.Negated);

                case InSyntax membership:
                    SyntaxNode value = FoldValue(membership.Value);
                    SyntaxNode[]? elements = FoldValues(membership.Elements, condition: -1);
                    return ReferenceEquals(value, membership.Value) && elements is null
                        ? node
                        : new InSyntax(
                            membership.Span,
                            membership.KeywordSpan,
                            value,
                            elements ?? membership.Elements);

                case CallSyntax call:

                    // An aggregation's second argument says which rows it reads, so it is a predicate
                    // wherever the aggregation sits, and its own skeleton starts un-negated.
                    int condition = FunctionRegistry.IsAggregate(call.Name) ? ConditionIndex : -1;
                    SyntaxNode[]? arguments = FoldValues(call.Arguments, condition);
                    return arguments is null
                        ? node
                        : new CallSyntax(call.Span, call.Name, call.NameSpan, arguments);

                default:
                    return node;
            }
        }

        /// <summary>Folds every operand of a node.</summary>
        /// <param name="operands">The operands.</param>
        /// <param name="condition">The position that is an aggregation's condition, or -1.</param>
        /// <returns>The folded operands, or null when none changed.</returns>
        private SyntaxNode[]? FoldValues(IReadOnlyList<SyntaxNode> operands, int condition)
        {
            SyntaxNode[]? folded = null;
            for (int index = 0; index < operands.Count; index++)
            {
                SyntaxNode operand = operands[index];
                SyntaxNode result = index == condition ? FoldCondition(operand) : FoldValue(operand);
                if (ReferenceEquals(result, operand))
                {
                    continue;
                }

                folded ??= [.. operands];
                folded[index] = result;
            }

            return folded;
        }

        /// <summary>Folds the condition of an aggregation.</summary>
        /// <param name="condition">The condition.</param>
        /// <returns>The folded condition, or the same instance when nothing in it changed.</returns>
        private SyntaxNode FoldCondition(SyntaxNode condition)
        {
            FoldedPredicate folded = FoldPredicate(condition, negated: false);
            return folded.Constant is bool settled
                ? new BooleanSyntax(condition.Span, settled)
                : folded.Node!;
        }

        /// <summary>Whether a name is one of the two hierarchy predicates, which take a list of keys.</summary>
        /// <param name="name">The function name as written.</param>
        /// <returns>True when it is.</returns>
        private static bool IsHierarchyPredicate(string name)
        {
            return name.Equals("descendantOf", StringComparison.OrdinalIgnoreCase)
                || name.Equals("ancestorOf", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Builds the list a set of folded items makes.</summary>
        /// <param name="original">The list as parsed.</param>
        /// <param name="items">The items, some of them folded.</param>
        /// <returns>The list.</returns>
        private static ExpressionListSyntax Rebuilt(ExpressionListSyntax original, ExpressionItemSyntax[] items)
        {
            SortedSet<string> parameters = new(StringComparer.OrdinalIgnoreCase);
            foreach (ExpressionItemSyntax item in items)
            {
                Collect(item, parameters);
            }

            return new ExpressionListSyntax(original.Span, items, [.. parameters]);
        }

        /// <summary>
        ///     How many more nodes a folded list has than it would have had if every absent value
        ///     in it were still the one parameter it replaced.
        /// </summary>
        /// <param name="list">A list, folded or not.</param>
        /// <returns>The surplus; zero for a list no absent value was written into.</returns>
        /// <remarks>
        ///     Writing the absent value out takes a conversion, an absent literal and a type name
        ///     where the parameter was a single node. A ceiling on the size of a query is a ceiling
        ///     on what its author wrote, and a query that fits it must go on fitting it whichever of
        ///     its parameters a caller leaves out — so whoever counts a folded list against one
        ///     takes this off first.
        /// </remarks>
        internal static int Surplus(ExpressionListSyntax list)
        {
            int surplus = 0;
            for (int index = 0; index < list.Items.Count; index++)
            {
                surplus += Surplus(list.Items[index].Expression);
            }

            return surplus;
        }

        /// <summary>The surplus of one subtree.</summary>
        /// <param name="node">The subtree.</param>
        /// <returns>The nodes it has beyond one for each absent value written into it.</returns>
        private static int Surplus(SyntaxNode node)
        {
            if (node is CallSyntax { StandsForParameter: true })
            {
                // Everything in the subtree but the one node the parameter was.
                return node.NodeCount - 1;
            }

            int surplus = 0;
            IReadOnlyList<SyntaxNode> children = node.Children;
            for (int index = 0; index < children.Count; index++)
            {
                surplus += Surplus(children[index]);
            }

            return surplus;
        }

        /// <summary>Collects every parameter name a tree still mentions.</summary>
        /// <param name="node">The tree.</param>
        /// <param name="parameters">The names found so far.</param>
        private static void Collect(SyntaxNode node, SortedSet<string> parameters)
        {
            if (node is ParameterSyntax parameter)
            {
                parameters.Add(parameter.Name);
                return;
            }

            IReadOnlyList<SyntaxNode> children = node.Children;
            for (int index = 0; index < children.Count; index++)
            {
                Collect(children[index], parameters);
            }
        }
    }
}
