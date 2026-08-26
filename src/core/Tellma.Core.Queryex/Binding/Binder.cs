// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex.Binding.Inference;
using Tellma.Core.Queryex.Diagnostics;
using Tellma.Core.Queryex.Functions;
using Tellma.Core.Queryex.Syntax;

namespace Tellma.Core.Queryex.Binding
{
    /// <summary>
    ///     Turns a parse tree into a typed one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Two mutually recursive judgements. Asking what a node's type is on its own reports
    ///         what it finds — an unknown property, an unknown function — because those are true
    ///         whatever the surrounding expression wanted. Asking whether a node can be read at a
    ///         particular type says nothing at all, because the caller is usually trying several
    ///         possibilities and only one of them failing is not news.
    ///     </para>
    ///     <para>
    ///         Both are memoized on node identity, and that is a requirement rather than a tuning
    ///         choice: the nodes that propagate a demand into their operands nest, and without the
    ///         memo the same subtree would be re-examined once per surrounding layer, making the
    ///         work exponential in nesting depth instead of linear in node count.
    ///     </para>
    /// </remarks>
    internal sealed partial class Binder
    {
        /// <summary>What binding one node at one requested type concluded.</summary>
        /// <param name="Success">Whether the node can be read at that type.</param>
        /// <param name="Node">The bound node, when it can.</param>
        /// <param name="Cost">How well it fitted.</param>
        private readonly record struct CheckOutcome(bool Success, TypedExpr? Node, CoercionCost Cost);

        /// <summary>What a memo entry is keyed on.</summary>
        /// <param name="Node">The syntax node, compared by identity.</param>
        /// <param name="Expected">The type that was asked for.</param>
        private readonly record struct CheckKey(SyntaxNode Node, BoundType Expected);

        /// <summary>What binding one node on its own concluded.</summary>
        private readonly Dictionary<SyntaxNode, TypedExpr> _synthesized =
            new(ReferenceEqualityComparer.Instance);

        /// <summary>What binding one node at one requested type concluded.</summary>
        private readonly Dictionary<CheckKey, CheckOutcome> _checked = [];

        /// <summary>What the expression depends on besides its own text.</summary>
        private readonly BindingContext _context;

        /// <summary>Where to report problems.</summary>
        private readonly DiagnosticScope _scope;

        /// <summary>How deeply inside aggregations the current node sits.</summary>
        private int _aggregateDepth;

        /// <summary>
        ///     How deeply inside a speculative attempt the current node sits.
        /// </summary>
        /// <remarks>
        ///     While an overload or a shared type is being tried out, a failure is a step in the
        ///     search rather than a problem with the expression, and reporting it would blame the
        ///     author for the compiler's own backtracking.
        /// </remarks>
        private int _speculation;

        /// <summary>
        ///     How deeply inside an attempt whose demands are not yet owed the current node sits.
        /// </summary>
        private int _attempts;

        /// <summary>
        ///     Demands recorded inside an attempt that has not been accepted or abandoned yet.
        /// </summary>
        /// <remarks>
        ///     An attempt binds whole subtrees, and the constructs inside them settle and record
        ///     what they asked of the parameters they contain. When the enclosing attempt is then
        ///     abandoned for another type, those demands were asked by a reading that no longer
        ///     exists — so they are held here until the attempt is accepted, and dropped with it
        ///     when it is not.
        /// </remarks>
        private readonly List<(ImmutableArray<TypedExpr> Operands, BoundType Agreed)> _pending = [];

        /// <summary>Initializes a binder over one expression list.</summary>
        /// <param name="context">What the expression depends on besides its own text.</param>
        /// <param name="scope">Where to report problems.</param>
        internal Binder(BindingContext context, in DiagnosticScope scope)
        {
            _context = context;
            _scope = scope;
        }

        /// <summary>Whether the list being bound is an ordering list.</summary>
        private bool _ordering;

        /// <summary>Binds a whole expression list.</summary>
        /// <param name="list">The parsed list.</param>
        /// <param name="directions">Whether direction suffixes are accepted here.</param>
        /// <returns>The bound items.</returns>
        internal ImmutableArray<BoundItem> BindList(ExpressionListSyntax list, bool directions)
        {
            // Direction suffixes are accepted exactly where the list is an ordering list, so this is
            // also how the item rules learn that every item here has to be sortable.
            _ordering = directions;

            // A predicate position takes exactly one expression, and a direction belongs to an
            // ordering term. Both depend on the position rather than on the grammar, which is why
            // they are settled here rather than while parsing.
            if (_context.Mode.Shape == QueryexShape.Predicate && list.Items.Count > 1)
            {
                _scope.Report(DiagnosticCodes.PredicateListNotPermitted, list.Items[1].Span);
            }

            ImmutableArray<BoundItem>.Builder items = ImmutableArray.CreateBuilder<BoundItem>(list.Items.Count);
            foreach (ExpressionItemSyntax item in list.Items)
            {
                if (item.Direction != QueryexDirection.None && !directions)
                {
                    _scope.Report(DiagnosticCodes.DirectionNotPermitted, item.DirectionSpan);
                }

                items.Add(new BoundItem(BindItem(item), item.Span, item.Direction));
            }

            return items.ToImmutable();
        }

        /// <summary>Binds one item and checks what its position requires of it.</summary>
        /// <param name="item">The parsed item.</param>
        /// <returns>The bound expression.</returns>
        private TypedExpr BindItem(ExpressionItemSyntax item)
        {
            TypedExpr bound = _context.Mode.Shape == QueryexShape.Predicate
                ? CheckOrError(item.Expression, BoundType.Bool, DiagnosticCodes.MustBePredicate)
                : Synth(item.Expression);

            if (bound.Type == BoundType.Error)
            {
                return bound;
            }

            CheckItemRules(item, bound);
            return bound;
        }

        /// <summary>Checks the rules an item has to satisfy in its position.</summary>
        /// <param name="item">The parsed item.</param>
        /// <param name="bound">The bound expression.</param>
        private void CheckItemRules(ExpressionItemSyntax item, TypedExpr bound)
        {
            if (_context.Mode.Shape == QueryexShape.Value && bound.Type == BoundType.Null)
            {
                // An item whose only possible value is absent has no column type to give a result
                // set, and guessing one would be worse than asking.
                _scope.Report(DiagnosticCodes.NoDeterminableType, item.Expression.Span);
                return;
            }

            // Every item of an ordering list, not only the ones that carry a direction: an item
            // without one still sorts, and a bare spatial column would reach the backend and be
            // refused there instead of here.
            if (_ordering
                && bound.Type != BoundType.Error
                && !BoundTypes.Admits(TypeMask.Ordered, bound.Type))
            {
                ReportType(DiagnosticCodes.OperandTypeNotValid, item.Expression.Span, bound.Type);
            }

            if (_context.Mode.Grouping != QueryexGrouping.Group)
            {
                return;
            }

            bool pathOutside = HasPathOutsideAggregate(bound);

            if (_context.Mode.Shape == QueryexShape.Predicate)
            {
                if (pathOutside)
                {
                    // A group-level predicate that reads an ungrouped column is not expressible, and
                    // that is the right outcome: whoever wrote it meant a grouping key or the
                    // row-level filter.
                    _scope.Report(DiagnosticCodes.PathOutsideAggregation, item.Expression.Span);
                }

                return;
            }

            if (bound.ContainsAggregate && pathOutside)
            {
                // Neither a grouping key nor a measure: there is no grouping under which reading a
                // column beside an aggregate over the same rows is well defined.
                _scope.Report(DiagnosticCodes.MixedPathsInsideAndOutsideAggregation, item.Expression.Span);
                return;
            }

            if (!bound.ContainsAggregate
                && bound.ContainsPath
                && !BoundTypes.Admits(TypeMask.Equatable, bound.Type))
            {
                // An aggregation-free item that reads a column becomes a grouping key, and the
                // backend cannot group by a value it cannot compare.
                ReportType(DiagnosticCodes.OperandTypeNotValid, item.Expression.Span, bound.Type);
            }
        }

        /// <summary>Binds a node at a required type, reporting a given code when it will not.</summary>
        /// <param name="node">The node.</param>
        /// <param name="expected">The required type.</param>
        /// <param name="code">The code to report.</param>
        /// <returns>The bound node, or an error node.</returns>
        private TypedExpr CheckOrError(SyntaxNode node, BoundType expected, string code)
        {
            if (TryCheck(node, expected, out TypedExpr bound, out _))
            {
                return bound;
            }

            TypedExpr synthesized = Synth(node);
            if (synthesized.Type != BoundType.Error)
            {
                ReportType(code, node.Span, synthesized.Type);
            }

            return new TypedError(node.Span);
        }

        /// <summary>What type a node has on its own.</summary>
        /// <param name="node">The node.</param>
        /// <returns>The bound node.</returns>
        internal TypedExpr Synth(SyntaxNode node)
        {
            if (_synthesized.TryGetValue(node, out TypedExpr? cached))
            {
                return cached;
            }

            TypedExpr bound = SynthCore(node);
            _synthesized[node] = bound;
            return bound;
        }

        /// <summary>Works out what type a node has on its own.</summary>
        /// <param name="node">The node.</param>
        /// <returns>The bound node.</returns>
        private TypedExpr SynthCore(SyntaxNode node)
        {
            return node switch
            {
                ParenthesizedSyntax parenthesized => Synth(parenthesized.Inner),
                NumberSyntax number => new TypedLiteral(
                                        BoundType.Numeric,
                                        number.Span,
                                        number.Value,
                                        number.Precision,
                                        number.Scale),
                StringSyntax text => new TypedLiteral(BoundType.String, text.Span, text.Value),
                BooleanSyntax boolean => new TypedLiteral(BoundType.Bool, boolean.Span, boolean.Value),
                NullSyntax nothing => new TypedNull(BoundType.Null, nothing.Span),
                ParameterSyntax parameter => BindParameter(parameter),
                PathSyntax path => BindPath(path),
                CallSyntax call => ResolveCall(call, expected: null),
                UnarySyntax unary => BindUnary(unary),
                BinarySyntax binary => BindBinary(binary),
                InSyntax membership => BindIn(membership),
                IsNullSyntax absence => new TypedIsNull(absence.Span, Synth(absence.Operand), absence.Negated),
                _ => new TypedError(node.Span),
            };
        }

        /// <summary>Whether a node can be read at a given type.</summary>
        /// <param name="node">The node.</param>
        /// <param name="expected">The type to read it at.</param>
        /// <param name="bound">The bound node, when it can.</param>
        /// <param name="cost">How well it fitted.</param>
        /// <returns>True when it can.</returns>
        internal bool TryCheck(SyntaxNode node, BoundType expected, out TypedExpr bound, out CoercionCost cost)
        {
            CheckKey key = new(node, expected);
            if (_checked.TryGetValue(key, out CheckOutcome cached))
            {
                bound = cached.Node ?? new TypedError(node.Span);
                cost = cached.Cost;
                return cached.Success;
            }

            CheckOutcome outcome = CheckCore(node, expected);
            _checked[key] = outcome;
            bound = outcome.Node ?? new TypedError(node.Span);
            cost = outcome.Cost;
            return outcome.Success;
        }

        /// <summary>Works out whether a node can be read at a given type.</summary>
        /// <param name="node">The node.</param>
        /// <param name="expected">The type to read it at.</param>
        /// <returns>What it concluded.</returns>
        private CheckOutcome CheckCore(SyntaxNode node, BoundType expected)
        {
            switch (node)
            {
                case ParenthesizedSyntax parenthesized:
                    return CheckCore(parenthesized.Inner, expected);

                case NullSyntax nothing:

                    // An absent value can be read at any type but a truth value: the language has no
                    // absent truth value to read it as, and inventing one would put a third
                    // possibility into predicates that are promised to have two.
                    return expected == BoundType.Bool
                        ? new CheckOutcome(false, null, CoercionCost.Exact)
                        : new CheckOutcome(
                            true,
                            new TypedNull(expected, nothing.Span),
                            CoercionCost.Coerced);

                case StringSyntax text when expected != BoundType.String:
                    return CheckStringLiteral(text, expected);

                case ParameterSyntax parameter when IsBeingInferred(parameter.Name):

                    // A parameter whose type is still being worked out fits wherever it is put. What
                    // each position demanded is recorded once the surrounding construct has settled,
                    // so that a demand from an overload that lost is never counted.
                    return new CheckOutcome(
                        true,
                        new TypedParameter(
                            parameter.Span,
                            new ParameterSymbol(parameter.Name, expected, QueryexNullity.Nullable)),
                        CoercionCost.Exact);

                case CallSyntax call when IsCheckableCall(call):
                    return CheckCall(call, expected);

                default:
                    TypedExpr synthesized = Synth(node);
                    return synthesized.Type == expected || synthesized.Type == BoundType.Error
                        ? new CheckOutcome(true, synthesized, CoercionCost.Exact)
                        : new CheckOutcome(false, null, CoercionCost.Exact);
            }
        }

        /// <summary>Reads a written string literal at a date or identifier type.</summary>
        /// <param name="text">The literal.</param>
        /// <param name="expected">The type to read it at.</param>
        /// <returns>What it concluded.</returns>
        /// <remarks>
        ///     Only a written literal converts. Text that arrived in a column, a parameter, or a
        ///     computed value is converted explicitly or not at all, so no data-dependent parse
        ///     failure can hide inside an expression that looked like a comparison.
        /// </remarks>
        private static CheckOutcome CheckStringLiteral(StringSyntax text, BoundType expected)
        {
            object? value = expected switch
            {
                BoundType.Date when LiteralCoercions.TryParseDate(text.Value, out DateOnly date) => date,
                BoundType.DateTime when LiteralCoercions.TryParseDateTime(text.Value, out DateTime moment) => moment,
                BoundType.DateTimeOffset when LiteralCoercions.TryParseDateTimeOffset(text.Value, out DateTimeOffset instant) => instant,
                BoundType.Guid when LiteralCoercions.TryParseGuid(text.Value, out Guid identifier) => identifier,
                BoundType.Bool or BoundType.Numeric or BoundType.String or BoundType.HierarchyId
                    or BoundType.Geography or BoundType.Null or BoundType.Error => null,
                _ => null,
            };

            return value is null
                ? new CheckOutcome(false, null, CoercionCost.Exact)
                : new CheckOutcome(
                    true,
                    new TypedLiteral(expected, text.Span, value),
                    CoercionCost.Coerced);
        }

        /// <summary>Reads a call that propagates a demand into its arguments.</summary>
        /// <param name="call">The call.</param>
        /// <param name="expected">The type to read it at.</param>
        /// <returns>What it concluded.</returns>
        private CheckOutcome CheckCall(CallSyntax call, BoundType expected)
        {
            _speculation++;
            try
            {
                TypedExpr bound = ResolveCall(call, expected);
                return bound.Type == expected || bound.Type == BoundType.Error
                    ? new CheckOutcome(true, bound, CoercionCost.Exact)
                    : new CheckOutcome(false, null, CoercionCost.Exact);
            }
            finally
            {
                _speculation--;
            }
        }

        /// <summary>Whether a call propagates a demand into its arguments.</summary>
        /// <param name="call">The call.</param>
        /// <returns>True when at least one overload does.</returns>
        private static bool IsCheckableCall(CallSyntax call)
        {
            FunctionDefinition? definition = FunctionRegistry.Find(call.Name);
            return definition is not null
                && definition.Signatures.Any(static signature => signature.IsCheckable);
        }

        /// <summary>Binds a named parameter.</summary>
        /// <param name="parameter">The parameter.</param>
        /// <returns>The bound node.</returns>
        private TypedExpr BindParameter(ParameterSyntax parameter)
        {
            if (_context.Parameters.TryGetValue(parameter.Name, out ParameterSymbol? declared))
            {
                return new TypedParameter(parameter.Span, declared);
            }

            if (_context.Inference is InferenceContext inference)
            {
                // Where the parameter was written is counted off the parse instead of here: binding
                // reaches a node once per demand it is checked against, and while an overload is
                // being tried it may reach one that ends up losing.
                BoundType? solved = inference.SolvedType(parameter.Name);
                return new TypedParameter(
                    parameter.Span,
                    new ParameterSymbol(
                        parameter.Name,
                        solved ?? BoundType.Error,
                        QueryexNullity.Nullable));
            }

            Report(
                DiagnosticCodes.UndeclaredParameter,
                parameter.Span,
                DiagnosticArgumentNames.Name,
                parameter.Name,
                always: true);

            return new TypedError(parameter.Span);
        }

        /// <summary>Resolves a path against the schema.</summary>
        /// <param name="path">The path.</param>
        /// <returns>The bound node.</returns>
        private TypedExpr BindPath(PathSyntax path)
        {
            if (!_context.CanResolvePaths)
            {
                // Without a schema there is nothing to resolve against, and saying so for every
                // path would drown out the lexical facts discovery exists to report.
                return new TypedError(path.Span);
            }

            EntityDescriptor entity = _context.Root!;
            ImmutableArray<NavigationDescriptor>.Builder navigations =
                ImmutableArray.CreateBuilder<NavigationDescriptor>(path.Segments.Count - 1);

            for (int index = 0; index < path.Segments.Count - 1; index++)
            {
                PathSegment segment = path.Segments[index];
                NavigationDescriptor? navigation = entity.FindNavigation(segment.Name);
                if (navigation is null)
                {
                    ReportUnknownMember(entity, segment);
                    return new TypedError(path.Span);
                }

                navigations.Add(navigation);
                entity = navigation.Target;
            }

            PathSegment last = path.Segments[^1];
            PropertyDescriptor? property = entity.FindProperty(last.Name);
            if (property is null)
            {
                if (entity.FindNavigation(last.Name) is not null)
                {
                    // A path has to arrive at a value. Stopping at a navigation names a row, and a
                    // row is not something an expression can be. Named, and named on its entity,
                    // because a host composing the message has only what is passed here to work
                    // from — and "this is a navigation" is no use without saying which.
                    Report(
                        DiagnosticCodes.PathEndsAtNavigation,
                        last.Span,
                        always: true,
                        new KeyValuePair<string, string>(DiagnosticArgumentNames.Name, last.Name),
                        new KeyValuePair<string, string>(DiagnosticArgumentNames.Entity, entity.Name));
                }
                else
                {
                    ReportUnknownMember(entity, last);
                }

                return new TypedError(path.Span);
            }

            return new TypedPath(
                path.Span,
                navigations.ToImmutable(),
                property,
                [.. path.Segments.Select(static segment => segment.Name)]);
        }

        /// <summary>Reports a segment that names nothing the entity declares.</summary>
        /// <param name="entity">The entity the segment was looked for on.</param>
        /// <param name="segment">The segment.</param>
        private void ReportUnknownMember(EntityDescriptor entity, PathSegment segment)
        {
            // Reported against the segment rather than the whole path, so a long path points at the
            // one step that went wrong.
            Report(
                DiagnosticCodes.UnknownProperty,
                segment.Span,
                always: true,
                new KeyValuePair<string, string>(DiagnosticArgumentNames.Name, segment.Name),
                new KeyValuePair<string, string>(DiagnosticArgumentNames.Entity, entity.Name));
        }

        /// <summary>Binds a prefix operator application.</summary>
        /// <param name="unary">The application.</param>
        /// <returns>The bound node.</returns>
        private TypedExpr BindUnary(UnarySyntax unary)
        {
            return unary.OperatorKind == UnaryOperatorKind.Negate
                ? RequireType(unary.Operand, BoundType.Numeric, out TypedExpr operand)
                    ? new TypedNegate(unary.Span, operand)
                    : new TypedError(unary.Span)
                : RequireType(unary.Operand, BoundType.Bool, out TypedExpr condition)
                ? new TypedNot(unary.Span, condition)
                : new TypedError(unary.Span);
        }

        /// <summary>Binds an infix operator application.</summary>
        /// <param name="binary">The application.</param>
        /// <returns>The bound node.</returns>
        private TypedExpr BindBinary(BinarySyntax binary)
        {
            return binary.OperatorKind switch
            {
                BinaryOperatorKind.And or BinaryOperatorKind.Or => BindLogical(binary),
                BinaryOperatorKind.Concat => BindArithmetic(binary, BoundType.String, ArithmeticOperator.Concat),
                BinaryOperatorKind.Add => BindArithmetic(binary, BoundType.Numeric, ArithmeticOperator.Add),
                BinaryOperatorKind.Subtract => BindArithmetic(binary, BoundType.Numeric, ArithmeticOperator.Subtract),
                BinaryOperatorKind.Multiply => BindArithmetic(binary, BoundType.Numeric, ArithmeticOperator.Multiply),
                BinaryOperatorKind.Divide => BindArithmetic(binary, BoundType.Numeric, ArithmeticOperator.Divide),
                BinaryOperatorKind.Remainder => BindArithmetic(binary, BoundType.Numeric, ArithmeticOperator.Remainder),
                BinaryOperatorKind.Equal => BindComparison(binary, ComparisonOperator.Equal),
                BinaryOperatorKind.NotEqual => BindComparison(binary, ComparisonOperator.NotEqual),
                BinaryOperatorKind.Less => BindComparison(binary, ComparisonOperator.Less),
                BinaryOperatorKind.LessOrEqual => BindComparison(binary, ComparisonOperator.LessOrEqual),
                BinaryOperatorKind.Greater => BindComparison(binary, ComparisonOperator.Greater),
                BinaryOperatorKind.GreaterOrEqual => BindComparison(binary, ComparisonOperator.GreaterOrEqual),
                _ => new TypedError(binary.Span),
            };
        }

        /// <summary>Binds a conjunction or a disjunction, flattening nested ones.</summary>
        /// <param name="binary">The application.</param>
        /// <returns>The bound node.</returns>
        private TypedExpr BindLogical(BinarySyntax binary)
        {
            LogicalOperator op = binary.OperatorKind == BinaryOperatorKind.And
                ? LogicalOperator.And
                : LogicalOperator.Or;

            if (!RequireType(binary.Left, BoundType.Bool, out TypedExpr left)
                | !RequireType(binary.Right, BoundType.Bool, out TypedExpr right))
            {
                return new TypedError(binary.Span);
            }

            ImmutableArray<TypedExpr>.Builder operands = ImmutableArray.CreateBuilder<TypedExpr>();
            AppendLogical(operands, left, op);
            AppendLogical(operands, right, op);

            return new TypedLogical(binary.Span, op, operands.ToImmutable());
        }

        /// <summary>Adds an operand to a connective, flattening it when it is the same connective.</summary>
        /// <param name="operands">The operands being collected.</param>
        /// <param name="operand">The operand to add.</param>
        /// <param name="op">The connective being built.</param>
        private static void AppendLogical(
            ImmutableArray<TypedExpr>.Builder operands,
            TypedExpr operand,
            LogicalOperator op)
        {
            if (operand is TypedLogical nested && nested.Operator == op)
            {
                operands.AddRange(nested.Operands);
                return;
            }

            operands.Add(operand);
        }

        /// <summary>Binds arithmetic or concatenation.</summary>
        /// <param name="binary">The application.</param>
        /// <param name="operandType">The type both operands must have.</param>
        /// <param name="op">The operator.</param>
        /// <returns>The bound node.</returns>
        private TypedExpr BindArithmetic(BinarySyntax binary, BoundType operandType, ArithmeticOperator op)
        {
            // Both sides are checked independently against one fixed type rather than unified,
            // because addition is arithmetic only and concatenation is the separate operator: no
            // operand is ever typed on speculation about what the other one turned out to be.
            return !RequireType(binary.Left, operandType, out TypedExpr left)
                | !RequireType(binary.Right, operandType, out TypedExpr right)
                ? new TypedError(binary.Span)
                : new TypedArithmetic(binary.Span, op, left, right);
        }

        /// <summary>Binds a comparison.</summary>
        /// <param name="binary">The application.</param>
        /// <param name="op">The operator, already resolved.</param>
        /// <returns>The bound node.</returns>
        private TypedExpr BindComparison(BinarySyntax binary, ComparisonOperator op)
        {
            bool ordering = op is not (ComparisonOperator.Equal or ComparisonOperator.NotEqual);
            TypeMask required = ordering ? TypeMask.Ordered : TypeMask.Equatable;

            if (!Unify([binary.Left, binary.Right], required, null, binary.OperatorSpan, out BoundType agreed, out ImmutableArray<TypedExpr> operands))
            {
                return new TypedError(binary.Span);
            }

            _ = agreed;
            return new TypedComparison(binary.Span, op, operands[0], operands[1]);
        }

        /// <summary>Binds a set-membership test.</summary>
        /// <param name="membership">The test.</param>
        /// <returns>The bound node.</returns>
        private TypedExpr BindIn(InSyntax membership)
        {
            List<SyntaxNode> operands = [membership.Value, .. membership.Elements];
            return !Unify(operands, TypeMask.Equatable, null, membership.KeywordSpan, out _, out ImmutableArray<TypedExpr> bound)
                ? new TypedError(membership.Span)
                : new TypedIn(membership.Span, bound[0], [.. bound.Skip(1)]);
        }

        /// <summary>Binds a node at a required type, reporting when it will not.</summary>
        /// <param name="node">The node.</param>
        /// <param name="expected">The required type.</param>
        /// <param name="bound">The bound node, when it binds.</param>
        /// <returns>True when it binds.</returns>
        private bool RequireType(SyntaxNode node, BoundType expected, out TypedExpr bound)
        {
            if (TryCheck(node, expected, out bound, out _))
            {
                NoteInference([bound], expected);
                return true;
            }

            TypedExpr synthesized = Synth(node);
            if (synthesized.Type != BoundType.Error)
            {
                // Reported even while an overload search is in progress. What an operator demands
                // of its operand is fixed by the operator, so an operand that does not meet it is
                // wrong under every candidate — and the result is memoised, so a demand withheld
                // here is withheld from every later reader of the same operand too.
                ReportType(DiagnosticCodes.OperandTypeNotValid, node.Span, synthesized.Type, always: true);
            }

            bound = new TypedError(node.Span);
            return false;
        }

        /// <summary>
        ///     Folds a group of operands to one type they all agree on.
        /// </summary>
        /// <param name="operands">The operands.</param>
        /// <param name="allowed">The types the position permits.</param>
        /// <param name="seed">A type the surrounding expression already demands, when there is one.</param>
        /// <param name="operatorSpan">Where to report a type that the position does not permit.</param>
        /// <param name="agreed">The agreed type.</param>
        /// <param name="bound">The bound operands.</param>
        /// <returns>True when they agree.</returns>
        /// <remarks>
        ///     Seeded from the first operand that has a type of its own, then retried against a later
        ///     operand's type when that first guess turns out not to fit. The retry is what makes a
        ///     date written as text work on either side of a comparison, and it costs nothing: every
        ///     re-examination it needs is already in the memo.
        /// </remarks>
        private bool Unify(
            List<SyntaxNode> operands,
            TypeMask allowed,
            BoundType? seed,
            QueryexSpan operatorSpan,
            out BoundType agreed,
            out ImmutableArray<TypedExpr> bound)
        {
            agreed = BoundType.Error;
            bound = [];

            BoundType? current = seed;
            if (current is null)
            {
                foreach (SyntaxNode operand in operands)
                {
                    BoundType candidate = ProbeType(operand);
                    if (candidate is not (BoundType.Null or BoundType.Error))
                    {
                        current = candidate;
                        break;
                    }
                }
            }

            if (current is null)
            {
                // Nothing here determines a type. That is not a failure — the absent-value literal
                // compares with itself perfectly well — so the group settles on the absent type and
                // the constant result folds away later.
                agreed = BoundType.Null;
                bound = [.. operands.Select(Synth)];

                // Recorded even so, because parameters that determine nothing here still determine
                // each other: whatever a later clause settles on for one of them settles it for all.
                NoteInference(bound, BoundType.Null);
                return true;
            }

            if (TryBindAll(operands, current.Value, out ImmutableArray<TypedExpr> attempt, out int failed))
            {
                return Accept(current.Value, allowed, attempt, operatorSpan, out agreed, out bound);
            }

            BoundType alternative = ProbeType(operands[failed]);
            if (alternative is not (BoundType.Null or BoundType.Error)
                && alternative != current.Value
                && TryBindAll(operands, alternative, out attempt, out failed))
            {
                return Accept(alternative, allowed, attempt, operatorSpan, out agreed, out bound);
            }

            ReportType(DiagnosticCodes.IncompatibleOperandTypes, operands[failed].Span, ProbeType(operands[failed]));
            return false;
        }

        /// <summary>Accepts an agreed type, or rejects it as one the position does not permit.</summary>
        /// <param name="candidate">The agreed type.</param>
        /// <param name="allowed">The types the position permits.</param>
        /// <param name="attempt">The bound operands.</param>
        /// <param name="operatorSpan">Where to report.</param>
        /// <param name="agreed">The agreed type, when accepted.</param>
        /// <param name="bound">The bound operands, when accepted.</param>
        /// <returns>True when accepted.</returns>
        private bool Accept(
            BoundType candidate,
            TypeMask allowed,
            ImmutableArray<TypedExpr> attempt,
            QueryexSpan operatorSpan,
            out BoundType agreed,
            out ImmutableArray<TypedExpr> bound)
        {
            agreed = candidate;
            bound = attempt;

            // A group in which nothing has a type of its own is not a type error: comparing one
            // absent value with another is perfectly well defined, and the whole construct folds to
            // a constant later on.
            if (candidate == BoundType.Null || BoundTypes.Admits(allowed, candidate))
            {
                NoteInference(attempt, candidate);
                return true;
            }

            // Reported against the operator rather than an operand: neither operand is wrong on its
            // own, it is this operator that has nothing to do with values of that type.
            ReportType(DiagnosticCodes.OperandTypeNotValid, operatorSpan, candidate);
            agreed = BoundType.Error;
            bound = [];
            return false;
        }

        /// <summary>Tries to read every operand at one type.</summary>
        /// <param name="operands">The operands.</param>
        /// <param name="type">The type.</param>
        /// <param name="bound">The bound operands, when they all read.</param>
        /// <param name="failed">The first operand that would not, when one would not.</param>
        /// <returns>True when they all read.</returns>
        private bool TryBindAll(
            List<SyntaxNode> operands,
            BoundType type,
            out ImmutableArray<TypedExpr> bound,
            out int failed)
        {
            // What the operands demand of the parameters inside them is only owed if this reading
            // is the one that survives. Held aside for the duration, so that an attempt abandoned
            // for another type does not leave a demand behind that nothing in the final tree asked
            // for — which would otherwise surface as a parameter conflicting with itself.
            int mark = _pending.Count;
            _attempts++;

            ImmutableArray<TypedExpr>.Builder builder = ImmutableArray.CreateBuilder<TypedExpr>(operands.Count);
            for (int index = 0; index < operands.Count; index++)
            {
                if (!TryCheck(operands[index], type, out TypedExpr operand, out _))
                {
                    _attempts--;
                    _pending.RemoveRange(mark, _pending.Count - mark);
                    bound = [];
                    failed = index;
                    return false;
                }

                builder.Add(operand);
            }

            _attempts--;
            FlushInference();

            bound = builder.ToImmutable();
            failed = -1;
            return true;
        }

        /// <summary>The type an operand has on its own, without owing what that reading demanded.</summary>
        /// <param name="operand">The operand.</param>
        /// <returns>Its type.</returns>
        /// <remarks>
        ///     Used where only the type is wanted and the tree behind it is thrown away — seeding a
        ///     unification, and naming the type that would not fit. Whatever that reading asked of
        ///     the parameters inside it was asked by a reading nothing kept, so it is not owed: the
        ///     binding that survives asks again, at the type that survived.
        /// </remarks>
        private BoundType ProbeType(SyntaxNode operand)
        {
            int mark = _pending.Count;
            _attempts++;
            try
            {
                return Synth(operand).Type;
            }
            finally
            {
                _attempts--;
                _pending.RemoveRange(mark, _pending.Count - mark);
            }
        }

        /// <summary>Applies the demands of every attempt that has now been accepted.</summary>
        /// <remarks>
        ///     Applied in the order they were recorded, so which use of a parameter settles its type
        ///     and which ones are measured against that answer does not depend on how the binder
        ///     happened to search.
        /// </remarks>
        private void FlushInference()
        {
            if (_attempts > 0 || _pending.Count == 0)
            {
                return;
            }

            (ImmutableArray<TypedExpr> Operands, BoundType Agreed)[] owed = [.. _pending];
            _pending.Clear();
            foreach ((ImmutableArray<TypedExpr> operands, BoundType agreed) in owed)
            {
                ApplyInference(operands, agreed);
            }
        }

        /// <summary>Whether a subtree reads a path from outside every aggregation in it.</summary>
        /// <param name="node">The subtree.</param>
        /// <returns>True when it does.</returns>
        private static bool HasPathOutsideAggregate(TypedExpr node)
        {
            if (node is TypedCall { IsAggregate: true })
            {
                // Inside an aggregation a path reads the rows of the group, which is exactly what
                // makes it a measure rather than a key.
                return false;
            }

            if (node is TypedPath)
            {
                return true;
            }

            foreach (TypedExpr child in node.Children)
            {
                if (HasPathOutsideAggregate(child))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether a parameter's type is being worked out rather than declared.</summary>
        /// <param name="name">The parameter name.</param>
        /// <returns>True when it is.</returns>
        private bool IsBeingInferred(string name)
        {
            return _context.Inference is not null && !_context.Parameters.ContainsKey(name);
        }

        /// <summary>Records what a settled construct demanded of the parameters in it.</summary>
        /// <param name="operands">The bound operands.</param>
        /// <param name="agreed">The type they agreed on.</param>
        /// <remarks>
        ///     Called once the construct has settled rather than while its possibilities are being
        ///     tried, so a demand from an overload that ultimately lost never counts against the
        ///     author. Where nothing determined a type, the parameters are linked instead: that
        ///     records the one relationship a clause-at-a-time pass would throw away, and it is
        ///     exactly the relationship that lets a parameter be typed from another clause.
        /// </remarks>
        private void NoteInference(ImmutableArray<TypedExpr> operands, BoundType agreed)
        {
            if (_context.Inference is null)
            {
                return;
            }

            if (_attempts > 0)
            {
                // Inside an attempt that may yet be abandoned, so not owed until it is accepted.
                _pending.Add((operands, agreed));
                return;
            }

            ApplyInference(operands, agreed);
        }

        /// <summary>Applies a settled construct's demands to the parameters in it.</summary>
        /// <param name="operands">The bound operands.</param>
        /// <param name="agreed">The type they agreed on.</param>
        private void ApplyInference(ImmutableArray<TypedExpr> operands, BoundType agreed)
        {
            if (_context.Inference is not InferenceContext inference)
            {
                return;
            }

            InferenceVariable? previous = null;
            foreach (TypedExpr operand in operands)
            {
                if (operand is not TypedParameter parameter || !IsBeingInferred(parameter.Symbol.Name))
                {
                    continue;
                }

                InferenceVariable variable = inference.GetOrCreate(parameter.Symbol.Name);
                if (agreed is BoundType.Null or BoundType.Error)
                {
                    previous?.Union(variable);
                    previous = variable;
                    continue;
                }

                variable.Demand(agreed, Site(operand.Span));
            }
        }

        /// <summary>Builds a discovery site for a span in the input being bound.</summary>
        /// <param name="span">The span.</param>
        /// <returns>The site.</returns>
        private QueryexTextSite Site(QueryexSpan span)
        {
            return new QueryexTextSite(_scope.Location.Text, span);
        }

        /// <summary>Reports a problem that names a type.</summary>
        /// <param name="code">The diagnostic code.</param>
        /// <param name="span">The offending range.</param>
        /// <param name="type">The type to name.</param>
        /// <param name="always">Whether to report even while speculating.</param>
        private void ReportType(string code, QueryexSpan span, BoundType type, bool always = false)
        {
            Report(
                code,
                span,
                DiagnosticArgumentNames.Type,
                type is BoundType.Null or BoundType.Error ? "null" : BoundTypes.Name(type),
                always);
        }

        /// <summary>Reports a problem with one named argument.</summary>
        /// <param name="code">The diagnostic code.</param>
        /// <param name="span">The offending range.</param>
        /// <param name="name">The argument name.</param>
        /// <param name="value">The argument value.</param>
        /// <param name="always">Whether to report even while speculating.</param>
        private void Report(string code, QueryexSpan span, string name, string value, bool always = false)
        {
            Report(code, span, always, new KeyValuePair<string, string>(name, value));
        }

        /// <summary>Reports a problem.</summary>
        /// <param name="code">The diagnostic code.</param>
        /// <param name="span">The offending range.</param>
        /// <param name="always">Whether to report even while speculating.</param>
        /// <param name="arguments">Named values for message composition.</param>
        private void Report(
            string code,
            QueryexSpan span,
            bool always = false,
            params KeyValuePair<string, string>[] arguments)
        {
            // A fact about the node itself — a name that does not exist — is true whatever the
            // surrounding expression wanted, so it is reported even mid-search. A fact about how the
            // node fitted is not.
            if (always || _speculation == 0)
            {
                _scope.Report(code, span, arguments);
            }
        }
    }
}
