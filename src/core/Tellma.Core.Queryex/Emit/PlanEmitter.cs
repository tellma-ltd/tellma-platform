// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Lowering;

namespace Tellma.Core.Queryex.Emit
{
    /// <summary>
    ///     Writes one lowered expression as SQL.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Makes no decisions about meaning. Which guards a comparison needs, which operands had
    ///         to be given a name, and whether something is read as a truth value or as a scalar were
    ///         all settled while lowering; what is left is the backend's spelling of it, plus the
    ///         three choices that depend on how a value is stored rather than on what it means.
    ///     </para>
    ///     <para>
    ///         Parentheses are structural rather than minimal: every composite is written inside its
    ///         own pair, whether or not precedence would have made them redundant. A difference in
    ///         the output is then exactly a difference in the plan, which is what makes a stored
    ///         snapshot readable as a diff of the query.
    ///     </para>
    /// </remarks>
    internal sealed class PlanEmitter
    {
        /// <summary>The alias the lookup inside a hoisted declaration uses.</summary>
        private const string LookupAlias = "x";

        /// <summary>The column name a value binding is written under.</summary>
        internal const string BindingColumn = "v";

        /// <summary>Where the SQL is collected.</summary>
        private readonly SqlWriter _sql;

        /// <summary>Initializes an emitter.</summary>
        /// <param name="sql">Where the SQL is collected.</param>
        internal PlanEmitter(SqlWriter sql)
        {
            _sql = sql;
        }

        /// <summary>Writes one value.</summary>
        /// <param name="value">The value.</param>
        internal void Value(PlanValue value)
        {
            switch (value)
            {
                case PlanColumn column:
                    _sql.Quoted(column.Table.Alias);
                    _sql.Write('.');
                    _sql.Quoted(column.Property.Column);
                    return;

                case PlanParameterRef parameter:
                    _sql.Write(parameter.Slot.Name);
                    return;

                case PlanVariableRef variable:
                    _sql.Write(variable.Variable.Name);
                    return;

                case PlanBindingRef binding:
                    _sql.Quoted(binding.Binding.Alias);
                    _sql.Write('.');
                    _sql.Quoted(BindingColumn);
                    return;

                case PlanNullValue absent:
                    WriteAbsent(absent);
                    return;

                case PlanBoolConstant constant:
                    _sql.Write(constant.Value ? '1' : '0');
                    return;

                case PlanNegate negate:
                    _sql.Write("(-");
                    Value(negate.Operand);
                    _sql.Write(')');
                    return;

                case PlanArithmetic arithmetic:
                    WriteArithmetic(arithmetic);
                    return;

                case PlanAggregate aggregate:
                    WriteAggregate(aggregate);
                    return;

                case PlanValueOfPredicate truth:
                    _sql.Write("CASE WHEN ");
                    Predicate(truth.Inner);
                    _sql.Write(" THEN 1 ELSE 0 END");
                    return;

                case PlanTemplateValue template:
                    WriteTemplate(template.Template, template.Operands);
                    return;

                default:
                    return;
            }
        }

        /// <summary>Writes one predicate.</summary>
        /// <param name="predicate">The predicate.</param>
        internal void Predicate(PlanPredicate predicate)
        {
            switch (predicate)
            {
                case PlanConstantPredicate constant:

                    // Written as a comparison of two engine-authored numbers rather than as the
                    // backend's own keywords, which it does not accept in every position a predicate
                    // may sit in.
                    _sql.Write(constant.Value ? "(1 = 1)" : "(1 = 0)");
                    return;

                case PlanJunction junction:
                    WriteJunction(junction);
                    return;

                case PlanNegation negation:
                    _sql.Write("NOT (");
                    Predicate(negation.Operand);
                    _sql.Write(')');
                    return;

                case PlanCompare compare:
                    _sql.Write('(');
                    Value(compare.Left);
                    _sql.Write(OperatorOf(compare.Comparison));
                    Value(compare.Right);
                    _sql.Write(')');
                    return;

                case PlanIsNull test:
                    _sql.Write('(');
                    Value(test.Value);
                    _sql.Write(test.Negated ? " IS NOT NULL)" : " IS NULL)");
                    return;

                case PlanIn membership:
                    WriteIn(membership);
                    return;

                case PlanSetComparison set:
                    WriteSetComparison(set);
                    return;

                case PlanSetMembership set:
                    WriteSetMembership(set);
                    return;

                case PlanHierarchyTest hierarchy:
                    WriteHierarchyTest(hierarchy);
                    return;

                case PlanPredicateOfValue truth:
                    WriteTruthOfValue(truth);
                    return;

                case PlanTemplatePredicate template:
                    WriteTemplate(template.Template, template.Operands);
                    return;

                default:
                    return;
            }
        }

        /// <summary>Writes the declaration of one hoisted lookup.</summary>
        /// <param name="variable">The variable.</param>
        internal void Declaration(HoistedVariable variable)
        {
            _sql.Write("DECLARE ");
            _sql.Write(variable.Name);
            _sql.Write(' ');
            _sql.Type(StoreTypes.Default(variable.Type, null, 0, 0));

            // A scalar subquery rather than an assignment: where the uniqueness the schema promised
            // does not hold, this fails loudly instead of silently picking one of the matching rows
            // and producing a different answer on a different day.
            _sql.Write(" = (SELECT ");
            _sql.Quoted(LookupAlias);
            _sql.Write('.');
            _sql.Quoted(variable.NodeProperty.Column);
            _sql.Write(" FROM ");
            _sql.Write(variable.LookupEntity.Source);
            _sql.Write(" AS ");
            _sql.Quoted(LookupAlias);
            _sql.Write(" WHERE ");
            _sql.Quoted(LookupAlias);
            _sql.Write('.');
            _sql.Quoted(variable.LookupProperty.Column);
            _sql.Write(" = ");
            Value(variable.Key);
            _sql.Write(");");
            _sql.Line();
        }

        /// <summary>The backend's spelling of one comparison.</summary>
        /// <param name="comparison">The comparison.</param>
        /// <returns>The operator, with the spaces around it.</returns>
        private static string OperatorOf(PlanComparison comparison)
        {
            return comparison switch
            {
                PlanComparison.Equal => " = ",
                PlanComparison.NotEqual => " <> ",
                PlanComparison.Less => " < ",
                PlanComparison.LessOrEqual => " <= ",
                PlanComparison.Greater => " > ",
                PlanComparison.GreaterOrEqual => " >= ",
                _ => " = ",
            };
        }

        /// <summary>Writes an absent value, carrying the type it stands in for.</summary>
        /// <param name="absent">The node.</param>
        private void WriteAbsent(PlanNullValue absent)
        {
            if (absent.Type == BoundType.Null)
            {
                _sql.Write("NULL");
                return;
            }

            // Typed rather than bare, because several positions — an aggregation's input among them
            // — reject a value whose type the backend cannot work out.
            _sql.Write("CAST(NULL AS ");
            _sql.Type(absent.StoreType);
            _sql.Write(')');
        }

        /// <summary>Writes arithmetic, or a join of two pieces of text.</summary>
        /// <param name="arithmetic">The node.</param>
        private void WriteArithmetic(PlanArithmetic arithmetic)
        {
            _sql.Write('(');
            WriteLeftOperand(arithmetic);
            _sql.Write(arithmetic.Operator switch
            {
                ArithmeticOperator.Subtract => " - ",
                ArithmeticOperator.Multiply => " * ",
                ArithmeticOperator.Divide => " / ",
                ArithmeticOperator.Remainder => " % ",

                // Joining text and adding numbers are the same operator to the backend, which is
                // why the two are one arm here and two in the language.
                ArithmeticOperator.Add or ArithmeticOperator.Concat => " + ",
                _ => " + ",
            });

            Value(arithmetic.Right);
            _sql.Write(')');
        }

        /// <summary>Writes the left operand of arithmetic, widened where it has to be.</summary>
        /// <param name="arithmetic">The node.</param>
        private void WriteLeftOperand(PlanArithmetic arithmetic)
        {
            bool wholeDivision = arithmetic.Operator == ArithmeticOperator.Divide
                && SqlTypes.IsWhole(arithmetic.Left.StoreType.Family)
                && SqlTypes.IsWhole(arithmetic.Right.StoreType.Family);

            if (wholeDivision)
            {
                // Two whole numbers divide to a whole number, throwing the remainder away. Widening
                // one side first is lossless — there is no fractional part to round — and it is what
                // buys the fractional digits the language promises.
                _sql.Write("CAST(");
                Value(arithmetic.Left);
                _sql.Write(" AS decimal(19, 0))");
                return;
            }

            bool growsPastBound = arithmetic.Operator == ArithmeticOperator.Concat
                && arithmetic.StoreType.Size is null
                && arithmetic.Left.StoreType.Size is not null;

            if (growsPastBound)
            {
                // Joining two values that are each as wide as their type allows clips the result
                // back to that width rather than growing it. Promoting one side to the unbounded
                // form is what stops text disappearing without a word being said about it.
                _sql.Write("CAST(");
                Value(arithmetic.Left);
                _sql.Write(" AS ");
                _sql.Type(arithmetic.StoreType);
                _sql.Write(')');
                return;
            }

            Value(arithmetic.Left);
        }

        /// <summary>Writes an aggregation.</summary>
        /// <param name="aggregate">The node.</param>
        private void WriteAggregate(PlanAggregate aggregate)
        {
            if (aggregate.Argument is null)
            {
                WriteCountOfRows(aggregate);
                return;
            }

            bool boolean = aggregate.ArgumentType == BoundType.Bool
                && aggregate.Kind is AggregateKind.Minimum or AggregateKind.Maximum;

            if (boolean)
            {
                // The backend refuses a least-or-greatest over its own boolean type outright, so the
                // value goes through the narrowest whole-number type and comes back again.
                _sql.Write("CAST(");
            }

            _sql.Write(aggregate.Kind switch
            {
                AggregateKind.Count => "COUNT_BIG(",
                AggregateKind.Sum => "SUM(",
                AggregateKind.Average => "AVG(",
                AggregateKind.Maximum => "MAX(",
                AggregateKind.Minimum => "MIN(",
                _ => "MIN(",
            });

            string? widening = WideningOf(aggregate, boolean);
            if (widening is not null)
            {
                _sql.Write("CAST(");
            }

            WriteConditioned(aggregate);

            if (widening is not null)
            {
                _sql.Write(widening);
            }

            _sql.Write(')');
            if (boolean)
            {
                _sql.Write(" AS bit)");
            }
        }

        /// <summary>The conversion an aggregation's input needs, or null when it needs none.</summary>
        /// <param name="aggregate">The node.</param>
        /// <param name="boolean">Whether the input is a truth value.</param>
        /// <returns>The closing text of the conversion, or null.</returns>
        private static string? WideningOf(PlanAggregate aggregate, bool boolean)
        {
            return boolean
                ? " AS tinyint)"
                : SqlTypes.IsWhole(aggregate.Argument!.StoreType.Family)
                    ? WholeWideningOf(aggregate.Kind)
                    : null;
        }

        /// <summary>The conversion an aggregation over whole numbers needs.</summary>
        /// <param name="kind">Which aggregate is computed.</param>
        /// <returns>The closing text of the conversion, or null where none is needed.</returns>
        private static string? WholeWideningOf(AggregateKind kind)
        {
            return kind switch
            {
                // A running total over whole numbers overflows the type it started in long before
                // the values themselves become unreasonable.
                AggregateKind.Sum => " AS decimal(38, 0))",

                // An average over whole numbers is itself a whole number unless one side is
                // fractional first, which would throw away exactly the digits an average is for.
                AggregateKind.Average => " AS decimal(19, 0))",

                // Counting produces a number of its own, and the least or greatest of a set of whole
                // numbers is one of them, so neither can grow past the type it started in.
                AggregateKind.Count or AggregateKind.Minimum or AggregateKind.Maximum => null,
                _ => null,
            };
        }

        /// <summary>Writes an aggregation's input, restricted to the rows it was told to read.</summary>
        /// <param name="aggregate">The node.</param>
        private void WriteConditioned(PlanAggregate aggregate)
        {
            if (aggregate.Condition is null)
            {
                Value(aggregate.Argument!);
                return;
            }

            // Rows the condition excludes contribute no value at all rather than a zero, which is
            // what makes a filtered aggregate over a group that matched nothing yield nothing.
            _sql.Write("CASE WHEN ");
            Predicate(aggregate.Condition);
            _sql.Write(" THEN ");
            Value(aggregate.Argument!);
            _sql.Write(" END");
        }

        /// <summary>Writes a count that reads no value of its own.</summary>
        /// <param name="aggregate">The node.</param>
        private void WriteCountOfRows(PlanAggregate aggregate)
        {
            // The wide form: the narrow one stops at a little over two billion rows and raises
            // rather than returning a number, which is a limit a ledger reaches.
            _sql.Write("COUNT_BIG(");
            if (aggregate.Condition is null)
            {
                _sql.Write("*)");
                return;
            }

            _sql.Write("CASE WHEN ");
            Predicate(aggregate.Condition);
            _sql.Write(" THEN 1 END)");
        }

        /// <summary>Writes a conjunction or a disjunction.</summary>
        /// <param name="junction">The node.</param>
        private void WriteJunction(PlanJunction junction)
        {
            _sql.Write('(');
            for (int index = 0; index < junction.Operands.Length; index++)
            {
                if (index > 0)
                {
                    _sql.Write(junction.IsConjunction ? " AND " : " OR ");
                }

                Predicate(junction.Operands[index]);
            }

            _sql.Write(')');
        }

        /// <summary>Writes a set-membership test the backend can seek on.</summary>
        /// <param name="membership">The node.</param>
        private void WriteIn(PlanIn membership)
        {
            _sql.Write('(');
            Value(membership.Value);
            _sql.Write(" IN (");
            for (int index = 0; index < membership.Elements.Length; index++)
            {
                if (index > 0)
                {
                    _sql.Write(", ");
                }

                Value(membership.Elements[index]);
            }

            _sql.Write("))");
        }

        /// <summary>Writes an equality that mentions each of its operands once.</summary>
        /// <param name="comparison">The node.</param>
        /// <remarks>
        ///     Set intersection already treats two missing values as one value, which is exactly the
        ///     equality this language defines — so the form needs no guards, and therefore needs
        ///     neither operand a second time.
        /// </remarks>
        private void WriteSetComparison(PlanSetComparison comparison)
        {
            if (comparison.Negated)
            {
                _sql.Write("NOT ");
            }

            _sql.Write("EXISTS (SELECT ");
            Value(comparison.Left);
            _sql.Write(" INTERSECT SELECT ");
            Value(comparison.Right);
            _sql.Write(')');
        }

        /// <summary>Writes a membership test that mentions its value once.</summary>
        /// <param name="membership">The node.</param>
        private void WriteSetMembership(PlanSetMembership membership)
        {
            _sql.Write("EXISTS (SELECT ");
            Value(membership.Value);
            _sql.Write(" INTERSECT ");

            bool several = membership.Elements.Length > 1;
            if (several)
            {
                // Parenthesised, because intersection binds tighter than union: without it the test
                // would intersect with the first element alone and merely append the rest.
                _sql.Write('(');
            }

            for (int index = 0; index < membership.Elements.Length; index++)
            {
                if (index > 0)
                {
                    _sql.Write(" UNION ALL ");
                }

                _sql.Write("SELECT ");
                Value(membership.Elements[index]);
            }

            if (several)
            {
                _sql.Write(')');
            }

            _sql.Write(')');
        }

        /// <summary>Writes a hierarchy test.</summary>
        /// <param name="test">The node.</param>
        private void WriteHierarchyTest(PlanHierarchyTest test)
        {
            // Looking up rather than down is the same test with the two positions exchanged, which
            // is why the backend needs only the one method.
            PlanValue receiver = test.Direction == HierarchyDirection.Descendant ? test.Node : test.Key;
            PlanValue argument = test.Direction == HierarchyDirection.Descendant ? test.Key : test.Node;

            _sql.Write('(');
            Value(receiver);
            _sql.Write(".IsDescendantOf(");
            Value(argument);
            _sql.Write(") = 1)");
        }

        /// <summary>Writes a scalar read as a truth value.</summary>
        /// <param name="truth">The node.</param>
        private void WriteTruthOfValue(PlanPredicateOfValue truth)
        {
            _sql.Write('(');
            if (truth.Guard == PlanBoolGuard.SubstituteAbsent)
            {
                _sql.Write("ISNULL(");
                Value(truth.Inner);
                _sql.Write(", 0) = 1)");
                return;
            }

            if (truth.Guard == PlanBoolGuard.TestPresence)
            {
                Value(truth.Inner);
                _sql.Write(" IS NOT NULL AND ");
            }

            Value(truth.Inner);
            _sql.Write(" = 1)");
        }

        /// <summary>Writes one of the function library's patterns.</summary>
        /// <param name="template">The pattern.</param>
        /// <param name="operands">The operands, positional to the pattern's placeholders.</param>
        private void WriteTemplate(EmitTemplate template, ImmutableArray<PlanNode> operands)
        {
            foreach (EmitSegment segment in template.Segments)
            {
                if (segment.Kind == EmitSegmentKind.Sql)
                {
                    _sql.Write(segment.Text);
                    continue;
                }

                if (segment.Kind == EmitSegmentKind.Argument)
                {
                    WriteOperand(operands, segment.ArgumentIndex, segment.Shape);
                    continue;
                }

                for (int index = segment.ArgumentIndex; index < operands.Length; index++)
                {
                    if (index > segment.ArgumentIndex)
                    {
                        _sql.Write(segment.Text);
                    }

                    WriteOperand(operands, index, segment.Shape);
                }
            }
        }

        /// <summary>Writes one operand of a pattern.</summary>
        /// <param name="operands">The operands.</param>
        /// <param name="index">Which one.</param>
        /// <param name="shape">How the pattern consumes it.</param>
        private void WriteOperand(ImmutableArray<PlanNode> operands, int index, EmitShape shape)
        {
            if (index >= operands.Length)
            {
                return;
            }

            if (shape == EmitShape.Predicate && operands[index] is PlanPredicate predicate)
            {
                Predicate(predicate);
                return;
            }

            if (operands[index] is PlanValue value)
            {
                Value(value);
            }
        }
    }
}
