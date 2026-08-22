// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Emit;
using Tellma.Core.Queryex.Functions;

namespace Tellma.Core.Queryex.Lowering
{
    /// <summary>
    ///     Function calls, lowered from what the registry declares about them.
    /// </summary>
    /// <remarks>
    ///     Nothing here is keyed on a function's name. Which SQL a call produces, how often each
    ///     argument appears in it, and whether each argument is read as a truth value or as a scalar
    ///     all come from the registered strategy, which is what makes adding a function an entry in
    ///     the registry and nothing more.
    /// </remarks>
    internal sealed partial class Lowerer
    {
        /// <summary>Lowers a call consumed as a scalar.</summary>
        /// <param name="call">The bound call.</param>
        /// <returns>The lowered value.</returns>
        private PlanValue LowerCallValue(TypedCall call)
        {
            EmitStrategy strategy = call.Signature.Emit;
            if (strategy is ContextStrategy context)
            {
                return ContextValue(call, context);
            }

            if (strategy is CastStrategy)
            {
                return ConversionValue(call);
            }

            if (strategy is AggregateStrategy aggregate)
            {
                return AggregateValue(call, aggregate);
            }

            if (strategy.ResultShape == EmitShape.Predicate)
            {
                return new PlanValueOfPredicate(call.Span, LowerCallPredicate(call));
            }

            EmitTemplate? template = strategy.TemplateFor(call.Selectors);
            if (template is null)
            {
                // No registered strategy leaves a scalar call without a pattern; anything that did
                // would be a registry defect rather than anything a user could write.
                return new PlanNullValue(call.Span, call.Type);
            }

            if (call.Signature.Nullity.Kind == NullityRuleKind.Conditional
                && call.Arguments.Length == 3
                && call.Arguments[0] is TypedLiteral { Value: bool taken })
            {
                // A condition that is already settled takes the whole call with it. Folding it here
                // rather than leaving the writer to emit a choice with only one reachable side is
                // what keeps the plan agreeing with what the nullity analysis concluded, and what
                // lets the join behind the unreachable branch disappear.
                return LowerValueAgainst(call.Arguments[taken ? 1 : 2], _hint);
            }

            ImmutableArray<PlanNode> operands = Operands(call, strategy);
            return new PlanTemplateValue(
                call.Span,
                call.Type,
                ResultOf(template, call.Type, operands),
                template,
                operands);
        }

        /// <summary>Lowers a call consumed as a truth value.</summary>
        /// <param name="call">The bound call.</param>
        /// <returns>The lowered predicate.</returns>
        private PlanPredicate LowerCallPredicate(TypedCall call)
        {
            EmitStrategy strategy = call.Signature.Emit;
            if (strategy is HierarchyStrategy hierarchy)
            {
                return HierarchyPredicate(call, hierarchy);
            }

            if (strategy.ResultShape == EmitShape.Value)
            {
                return ReadAsPredicate(call);
            }

            EmitTemplate? template = strategy.TemplateFor(call.Selectors);
            return template is null
                ? new PlanConstantPredicate(call.Span, false)
                : new PlanTemplatePredicate(call.Span, template, Operands(call, strategy));
        }

        /// <summary>Lowers a call whose value the host supplies at execution.</summary>
        /// <param name="call">The bound call.</param>
        /// <param name="context">The strategy.</param>
        /// <returns>The lowered value.</returns>
        /// <remarks>
        ///     One slot per kind of value across the whole statement, so every mention of the clock
        ///     within one query sees the same instant.
        /// </remarks>
        private PlanParameterRef ContextValue(TypedCall call, ContextStrategy context)
        {
            QueryexStoreType store = StoreTypes.Default(call.Type, null, 0, 0);
            return new PlanParameterRef(
                call.Span,
                Slot(call.Type, store, context.Origin, null, null, call.Span));
        }

        /// <summary>Lowers a conversion.</summary>
        /// <param name="call">The bound call.</param>
        /// <returns>The lowered value.</returns>
        private PlanValue ConversionValue(TypedCall call)
        {
            // The second argument names the target type and is used up choosing the conversion, so
            // it never becomes a value and never reaches the backend.
            TypedExpr source = call.Arguments[0];
            PlanValue value = LowerValueAgainst(source, null);
            EmitTemplate? template = CastRules.TemplateFor(source.Type, call.Type);
            if (template is not null)
            {
                return new PlanTemplateValue(
                    call.Span,
                    call.Type,
                    ResultOf(template, call.Type, [value]),
                    template,
                    [value]);
            }

            // Converting a type to itself leaves the value alone. Converting the absent value leaves
            // it alone too, but gives it the type it was converted to — which is the entire reason
            // anyone writes that conversion.
            return value is PlanNullValue ? new PlanNullValue(call.Span, call.Type) : value;
        }

        /// <summary>Lowers an aggregation.</summary>
        /// <param name="call">The bound call.</param>
        /// <param name="strategy">The strategy.</param>
        /// <returns>The lowered value.</returns>
        /// <remarks>
        ///     An aggregate reads the rows of its group, so its arguments are at row grain and a
        ///     lateral binding is referenceable from inside one even where it is not from outside.
        /// </remarks>
        private PlanAggregate AggregateValue(TypedCall call, AggregateStrategy strategy)
        {
            bool savedBindings = _bindingsAllowed;
            QueryexStoreType? savedHint = _hint;
            _bindingsAllowed = true;
            _hint = null;

            PlanValue? argument = strategy.HasArgument ? LowerValue(call.Arguments[0]) : null;
            PlanPredicate? condition = strategy.HasCondition ? LowerPredicate(call.Arguments[1]) : null;

            _bindingsAllowed = savedBindings;
            _hint = savedHint;

            BoundType argumentType = strategy.HasArgument ? call.Arguments[0].Type : BoundType.Numeric;
            return new PlanAggregate(call.Span, strategy.Kind, argument, condition, argumentType);
        }

        /// <summary>Lowers a hierarchy predicate.</summary>
        /// <param name="call">The bound call.</param>
        /// <param name="strategy">The strategy.</param>
        /// <returns>The lowered predicate.</returns>
        /// <remarks>
        ///     The first argument is read structurally rather than as a value: its entity says which
        ///     table a position is looked up in, its property says which column a key is matched
        ///     against, and the join it walks to is the one whose position is tested.
        /// </remarks>
        private PlanPredicate HierarchyPredicate(TypedCall call, HierarchyStrategy strategy)
        {
            if (call.Arguments[0] is not TypedPath keyPath)
            {
                return new PlanConstantPredicate(call.Span, false);
            }

            JoinNode table = Walk(keyPath.Navigations, keyPath.Span);
            if (table.Entity.TreeNode is not PropertyDescriptor treeNode)
            {
                return new PlanConstantPredicate(call.Span, false);
            }

            PlanColumn node = new(keyPath.Span, table, treeNode);

            // A position is certainly there only when the row holding it is: behind an optional
            // navigation there may be no row to read a position from at all.
            bool nodeIsCertain = table.IsInner && treeNode.IsNotNull;
            QueryexStoreType keyStore = StoreTypes.OfProperty(keyPath.Property);

            List<PlanPredicate> disjuncts = [];
            for (int index = 1; index < call.Arguments.Length; index++)
            {
                PlanValue key = LowerValueAgainst(call.Arguments[index], keyStore);
                HoistedVariable variable = Hoist(table.Entity, keyPath.Property, treeNode, key);
                PlanVariableRef looked = new(call.Arguments[index].Span, variable);

                ImmutableArray<PlanPredicate>.Builder parts =
                    ImmutableArray.CreateBuilder<PlanPredicate>(3);

                if (!nodeIsCertain)
                {
                    parts.Add(new PlanIsNull(node.Span, node, negated: true));
                }

                // A key that matched no row looks up to nothing, and a row is neither above nor
                // below nothing — which is what makes an unmatched key contribute no rows rather
                // than every row.
                parts.Add(new PlanIsNull(looked.Span, looked, negated: true));
                parts.Add(new PlanHierarchyTest(call.Span, strategy.Direction, node, looked));
                disjuncts.Add(new PlanJunction(call.Span, isConjunction: true, parts.ToImmutable()));
            }

            return disjuncts.Count switch
            {
                0 => new PlanConstantPredicate(call.Span, false),
                1 => disjuncts[0],
                _ => new PlanJunction(call.Span, isConjunction: false, [.. disjuncts]),
            };
        }

        /// <summary>Lowers a call's arguments into the positions its pattern reads them from.</summary>
        /// <param name="call">The bound call.</param>
        /// <param name="strategy">The strategy.</param>
        /// <returns>The operands, positional to the pattern's placeholders.</returns>
        private ImmutableArray<PlanNode> Operands(TypedCall call, EmitStrategy strategy)
        {
            var operands = new PlanNode[call.Arguments.Length + call.Synthetic.Length];
            TypedPath? sibling = ColumnAmong(call.Arguments);

            for (int index = 0; index < call.Arguments.Length; index++)
            {
                TypedExpr argument = call.Arguments[index];
                int uses = UseCountOf(call, strategy, index);
                if (uses == 0)
                {
                    // A selector chooses the pattern and is used up doing so. Nothing reads this
                    // position, and lowering the argument would claim a parameter for a value that
                    // is never written.
                    operands[index] = new PlanNullValue(argument.Span, BoundType.Null);
                    continue;
                }

                if (ShapeOf(call, strategy, index) == EmitShape.Predicate)
                {
                    operands[index] = LowerPredicate(argument);
                    continue;
                }

                PlanValue value = LowerValueAgainst(argument, HintFor(call, index, sibling));
                if (uses > 1)
                {
                    // Where a name cannot be given, the argument is written more than once. That is
                    // reachable only at group grain, where an argument is either an aggregation —
                    // which the backend computes once however many times it is written — or reads no
                    // column at all, and costs nothing to repeat.
                    TryShare(ref value);
                }

                operands[index] = value;
            }

            for (int index = 0; index < call.Synthetic.Length; index++)
            {
                ResolvedSynthetic synthetic = call.Synthetic[index];
                operands[call.Arguments.Length + index] = new PlanParameterRef(
                    call.Span,
                    Slot(
                        synthetic.Type,
                        StoreTypes.Default(synthetic.Type, synthetic.Value, 0, 0),
                        synthetic.Origin,
                        synthetic.Value,
                        null,
                        call.Span));
            }

            return ImmutableArray.Create(operands);
        }

        /// <summary>How often one argument appears in the SQL, variadic tails included.</summary>
        /// <param name="call">The bound call.</param>
        /// <param name="strategy">The strategy.</param>
        /// <param name="index">The argument position.</param>
        /// <returns>The count.</returns>
        private static int UseCountOf(TypedCall call, EmitStrategy strategy, int index)
        {
            return strategy.UseCountOf(Declared(call, index));
        }

        /// <summary>How one argument is consumed, variadic tails included.</summary>
        /// <param name="call">The bound call.</param>
        /// <param name="strategy">The strategy.</param>
        /// <param name="index">The argument position.</param>
        /// <returns>The shape.</returns>
        private static EmitShape ShapeOf(TypedCall call, EmitStrategy strategy, int index)
        {
            return strategy.ShapeOf(Declared(call, index));
        }

        /// <summary>The column an argument's value will be matched against, when there is one.</summary>
        /// <param name="call">The bound call.</param>
        /// <param name="index">The argument position.</param>
        /// <param name="sibling">The first argument that names a column, when one does.</param>
        /// <returns>That column's type, or null.</returns>
        /// <remarks>
        ///     <para>
        ///         An argument a demand travels into — the branches of a choice, the candidates of a
        ///         first-present-wins call — inherits whatever the call itself is being matched
        ///         against.
        ///     </para>
        ///     <para>
        ///         Everything else looks sideways instead: a written value beside a column of its own
        ///         type is going to be matched against that column, and binding it in some other
        ///         family would make the backend convert the column rather than the value. That is
        ///         what turns a search through an index into a read of the whole table.
        ///     </para>
        /// </remarks>
        private QueryexStoreType? HintFor(TypedCall call, int index, TypedPath? sibling)
        {
            ImmutableArray<FunctionParameter> parameters = call.Signature.Parameters;
            int declared = Declared(call, index);
            return declared < parameters.Length && parameters[declared].Propagates
                ? _hint
                : sibling is not null
                    && call.Arguments[index].Type == BoundTypes.FromPublic(sibling.Property.Type)
                        ? StoreTypes.OfProperty(sibling.Property)
                        : null;
        }

        /// <summary>The first argument that names a column, when one does.</summary>
        /// <param name="arguments">The arguments.</param>
        /// <returns>The path, or null when no argument is one.</returns>
        private static TypedPath? ColumnAmong(ImmutableArray<TypedExpr> arguments)
        {
            foreach (TypedExpr argument in arguments)
            {
                if (argument is TypedPath path)
                {
                    return path;
                }
            }

            return null;
        }

        /// <summary>The type the backend will hold a pattern's result in.</summary>
        /// <param name="template">The pattern.</param>
        /// <param name="type">The result's type in the language.</param>
        /// <param name="operands">The lowered operands.</param>
        /// <returns>The result type.</returns>
        /// <remarks>
        ///     A pattern that states its own result type is believed; one that says its result
        ///     follows an argument takes that argument's; and one that says neither takes the widest
        ///     of the operands that share its type in the language, falling back to the default for
        ///     that type. The fallback is the answer that widens nothing, which is the safe direction:
        ///     a total that then overflows is an error the backend raises, where a widening applied
        ///     to something fractional would round it and say nothing.
        /// </remarks>
        private static QueryexStoreType ResultOf(
            EmitTemplate template,
            BoundType type,
            ImmutableArray<PlanNode> operands)
        {
            if (template.Result is QueryexStoreType stated)
            {
                return stated;
            }

            if (template.Follows is int source
                && source < operands.Length
                && operands[source] is PlanValue followed)
            {
                return followed.StoreType;
            }

            QueryexStoreType? widest = null;
            foreach (PlanNode operand in operands)
            {
                if (operand is PlanValue value && value.Type == type)
                {
                    widest = widest is null
                        ? value.StoreType
                        : SqlTypes.Widest(widest.Value, value.StoreType);
                }
            }

            return widest ?? StoreTypes.Default(type, null, 0, 0);
        }

        /// <summary>Which declared parameter an argument position belongs to.</summary>
        /// <param name="call">The bound call.</param>
        /// <param name="index">The argument position.</param>
        /// <returns>The declared position, with a variadic tail folded onto its own.</returns>
        private static int Declared(TypedCall call, int index)
        {
            return call.RestStart >= 0 && index > call.RestStart ? call.RestStart : index;
        }
    }
}
