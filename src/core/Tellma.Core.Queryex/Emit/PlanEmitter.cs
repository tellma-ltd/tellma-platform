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
        /// <summary>The alias the lookup inside a hoisted hierarchy declaration uses.</summary>
        private const string LookupAlias = "x";

        /// <summary>The alias of the month table inside a lateral calendar lookup.</summary>
        private const string MonthAlias = "M";

        /// <summary>The month table's columns, in the order a lookup selects them.</summary>
        private static readonly CalendarColumn[] MonthColumnOrder =
            [CalendarColumn.Year, CalendarColumn.Month, CalendarColumn.StartDate, CalendarColumn.EndDate];

        /// <summary>The column name a value binding is written under.</summary>
        internal const string BindingColumn = "v";

        /// <summary>The column a host's table of keys holds them in.</summary>
        internal const string KeySetColumn = "Id";

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

                case PlanUndeclaredColumn undeclared:
                    _sql.Quoted(undeclared.Table.Alias);
                    _sql.Write('.');
                    _sql.Quoted(undeclared.Column);
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

                case PlanLookupColumn column:
                    _sql.Quoted(column.Lookup.Alias);
                    _sql.Write('.');
                    _sql.Quoted(ColumnName(column.Column));
                    return;

                case PlanScalarLookup scalar:
                    _sql.Write('(');
                    WriteLevel(scalar, 0);
                    _sql.Write(')');
                    return;

                case PlanNamedScalar named:
                    WriteNamedScalar(named);
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

                case PlanKeySetRestriction restriction:
                    WriteKeySetRestriction(restriction);
                    return;

                case PlanExists exists:
                    WriteExists(exists.Subquery);
                    return;

                default:
                    return;
            }
        }

        /// <summary>Writes one join of a join tree, without the line break that ends it.</summary>
        /// <param name="join">The join, already named.</param>
        /// <remarks>
        ///     The condition equates the target's key with the foreign key of the node it was reached
        ///     from, which is the statement's root, an earlier join, or a subquery's source.
        /// </remarks>
        internal void Join(JoinNode join)
        {
            _sql.Write(join.IsInner ? "INNER JOIN " : "LEFT JOIN ");
            _sql.Write(join.Entity.Source);
            _sql.Write(" AS ");
            _sql.Quoted(join.Alias);
            _sql.Write(" ON ");
            _sql.Quoted(join.Alias);
            _sql.Write('.');
            _sql.Quoted(join.Entity.Key.Column);
            _sql.Write(" = ");
            _sql.Quoted(join.Parent!.Alias);
            _sql.Write('.');
            _sql.Quoted(join.Navigation!.ForeignKey.Column);
        }

        /// <summary>Writes one one-row source, without the line break that ends it.</summary>
        /// <param name="lateral">The source, already named.</param>
        internal void Lateral(LateralSource lateral)
        {
            switch (lateral)
            {
                case ValueBinding binding:
                    Binding(binding);
                    return;

                case CalendarLookup lookup:
                    WriteLateralLookup(lookup);
                    return;

                default:
                    return;
            }
        }

        /// <summary>Writes one value binding, without the line break that ends it.</summary>
        /// <param name="binding">The binding, already named.</param>
        /// <remarks>
        ///     A one-row lateral source: it names a value the guards can mention as often as they need
        ///     to without the expression behind it being written more than once.
        /// </remarks>
        private void Binding(ValueBinding binding)
        {
            _sql.Write("CROSS APPLY (VALUES (");
            Value(binding.Value);
            _sql.Write(")) AS ");
            _sql.Quoted(binding.Alias);
            _sql.Write(" (");
            _sql.Quoted(BindingColumn);
            _sql.Write(')');
        }

        /// <summary>Writes one calendar lookup as a lateral source, without the line break that ends it.</summary>
        /// <param name="lookup">The lookup, already named.</param>
        /// <remarks>
        ///     An outer apply, so a row whose date the month table does not cover is kept and reads
        ///     every column of the lookup as absent. All four columns are selected, whichever the
        ///     statement reads: the backend reads only the ones referenced, and a fixed list keeps the
        ///     text the same however the lookup is used.
        /// </remarks>
        private void WriteLateralLookup(CalendarLookup lookup)
        {
            _sql.Write("OUTER APPLY (SELECT ");
            MonthColumns(MonthAlias);
            _sql.Write(" FROM ");
            if (lookup.Date is null)
            {
                WriteMonthByNumber(lookup, MonthAlias);
            }
            else
            {
                WriteMonthByDate(lookup, MonthAlias);
            }

            _sql.Write(") AS ");
            _sql.Quoted(lookup.Alias);
        }

        /// <summary>Writes one level of a scalar calendar lookup and everything nested in it.</summary>
        /// <param name="scalar">The scalar lookup.</param>
        /// <param name="index">The level, outermost first.</param>
        /// <remarks>
        ///     Each level selects the next one, and the innermost selects the result, so every level
        ///     and the result read the rows of all the levels around them. A level's bindings sit in
        ///     its own source, where the levels inside it can read them too.
        /// </remarks>
        private void WriteLevel(PlanScalarLookup scalar, int index)
        {
            CalendarLookup level = scalar.Levels[index];
            _sql.Write("SELECT ");
            if (index + 1 < scalar.Levels.Length)
            {
                _sql.Write('(');
                WriteLevel(scalar, index + 1);
                _sql.Write(')');
            }
            else
            {
                Value(scalar.Result);
            }

            _sql.Write(" FROM ");
            if (level.Date is null)
            {
                WriteMonthByNumber(level, level.Alias);
                return;
            }

            // The values the date is computed from come first, so the month sought from it can read
            // them: the first as a source of its own, the rest and the month applied to it.
            for (int binding = 0; binding < level.Bindings.Count; binding++)
            {
                if (binding == 0)
                {
                    BindingSource(level.Bindings[binding]);
                }
                else
                {
                    _sql.Write(' ');
                    Binding(level.Bindings[binding]);
                }
            }

            if (level.Bindings.Count > 0)
            {
                _sql.Write(" CROSS APPLY ");
            }

            WriteMonthByDate(level, level.Alias);
        }

        /// <summary>
        ///     Writes the month a date falls in as a source, and the condition that keeps it, starting
        ///     with the source.
        /// </summary>
        /// <param name="lookup">The lookup by date.</param>
        /// <param name="alias">The alias the month is read under.</param>
        /// <remarks>
        ///     The last month that began on or before the date, kept only when the date falls before
        ///     that month's end. The ordering and the first row are what let the host's unique index
        ///     on the calendar and first day answer with one seek, and the end is checked on the one
        ///     row found rather than inside the seek: checked inside, a date past the table's last
        ///     month would read every month backward looking for one that ends after it.
        /// </remarks>
        private void WriteMonthByDate(CalendarLookup lookup, string alias)
        {
            PlanValue date = lookup.Date!;
            _sql.Write("(SELECT TOP (1) ");
            MonthColumns(alias);
            _sql.Write(" FROM ");
            _sql.Write(lookup.Source);
            _sql.Write(" AS ");
            _sql.Quoted(alias);
            _sql.Write(" WHERE ");
            MonthColumn(alias, "Calendar");
            _sql.Write(" = ");
            Value(lookup.Calendar);
            _sql.Write(" AND ");
            MonthColumn(alias, ColumnName(CalendarColumn.StartDate));
            _sql.Write(" <= ");
            Value(date);
            _sql.Write(" ORDER BY ");
            MonthColumn(alias, ColumnName(CalendarColumn.StartDate));
            _sql.Write(" DESC) AS ");
            _sql.Quoted(alias);
            _sql.Write(" WHERE ");
            Value(date);
            _sql.Write(" < ");
            MonthColumn(alias, ColumnName(CalendarColumn.EndDate));
        }

        /// <summary>
        ///     Writes the month a year and a month number name, with the lookup's bindings, starting
        ///     with the source.
        /// </summary>
        /// <param name="lookup">The lookup by year and month.</param>
        /// <param name="alias">The alias the month table is read under.</param>
        /// <remarks>An equality on the table's key, which holds at most one row.</remarks>
        private void WriteMonthByNumber(CalendarLookup lookup, string alias)
        {
            _sql.Write(lookup.Source);
            _sql.Write(" AS ");
            _sql.Quoted(alias);
            foreach (ValueBinding binding in lookup.Bindings)
            {
                _sql.Write(' ');
                Binding(binding);
            }

            _sql.Write(" WHERE ");
            MonthColumn(alias, "Calendar");
            _sql.Write(" = ");
            Value(lookup.Calendar);
            _sql.Write(" AND ");
            MonthColumn(alias, ColumnName(CalendarColumn.Year));
            _sql.Write(" = ");
            Value(lookup.Year!);
            _sql.Write(" AND ");
            MonthColumn(alias, ColumnName(CalendarColumn.Month));
            _sql.Write(" = ");
            Value(lookup.Month!);
        }

        /// <summary>Writes the month table's four columns under an alias, separated by commas.</summary>
        /// <param name="alias">The alias.</param>
        private void MonthColumns(string alias)
        {
            for (int index = 0; index < MonthColumnOrder.Length; index++)
            {
                if (index > 0)
                {
                    _sql.Write(", ");
                }

                MonthColumn(alias, ColumnName(MonthColumnOrder[index]));
            }
        }

        /// <summary>Writes a value binding as the first source of a FROM clause.</summary>
        /// <param name="binding">The binding, already named.</param>
        private void BindingSource(ValueBinding binding)
        {
            _sql.Write("(VALUES (");
            Value(binding.Value);
            _sql.Write(")) AS ");
            _sql.Quoted(binding.Alias);
            _sql.Write(" (");
            _sql.Quoted(BindingColumn);
            _sql.Write(')');
        }

        /// <summary>Writes a value computed over operands named once, as a scalar subquery.</summary>
        /// <param name="named">The value, its operands already named.</param>
        /// <remarks>
        ///     The first operand is the subquery's source and each further one is applied to it, so
        ///     every operand is written exactly once and the pattern reads each by name.
        /// </remarks>
        private void WriteNamedScalar(PlanNamedScalar named)
        {
            _sql.Write("(SELECT ");
            Value(named.Result);
            _sql.Write(" FROM ");
            BindingSource(named.Bindings[0]);
            for (int index = 1; index < named.Bindings.Length; index++)
            {
                _sql.Write(' ');
                Binding(named.Bindings[index]);
            }

            _sql.Write(')');
        }

        /// <summary>Writes one column of the month table under an alias.</summary>
        /// <param name="alias">The alias.</param>
        /// <param name="name">The column's name.</param>
        private void MonthColumn(string alias, string name)
        {
            _sql.Quoted(alias);
            _sql.Write('.');
            _sql.Quoted(name);
        }

        /// <summary>The name of a month table column a lookup reads.</summary>
        /// <param name="column">The column.</param>
        /// <returns>The name, as the host's table declares it.</returns>
        private static string ColumnName(CalendarColumn column)
        {
            return column switch
            {
                CalendarColumn.Year => "Year",
                CalendarColumn.Month => "Month",
                CalendarColumn.StartDate => "StartDate",
                CalendarColumn.EndDate => "EndDate",
                _ => throw new ArgumentOutOfRangeException(nameof(column)),
            };
        }

        /// <summary>Writes a semi-join's subquery inside its existence test.</summary>
        /// <param name="subquery">The subquery, already named.</param>
        /// <remarks>
        ///     On one line, like every other predicate, with its source, joins and one-row sources in
        ///     the order the statement writes its own. It selects a constant: an existence test reads no
        ///     column of the row it finds.
        /// </remarks>
        private void WriteExists(PlanSubquery subquery)
        {
            _sql.Write("EXISTS (SELECT 1 FROM ");
            _sql.Write(subquery.Root.Entity.Source);
            _sql.Write(" AS ");
            _sql.Quoted(subquery.Root.Alias);

            foreach (JoinNode join in subquery.Joins)
            {
                _sql.Write(' ');
                Join(join);
            }

            foreach (LateralSource lateral in subquery.Laterals)
            {
                _sql.Write(' ');
                Lateral(lateral);
            }

            _sql.Write(" WHERE ");
            Predicate(subquery.Where!);
            _sql.Write(')');
        }

        /// <summary>Writes a row's key tested against a table of keys the host supplies.</summary>
        /// <param name="restriction">The node.</param>
        /// <remarks>
        ///     The table's name is written as the host supplied it, which its form was checked to make
        ///     safe: a marker and an identifier, with nothing that could end either one.
        /// </remarks>
        private void WriteKeySetRestriction(PlanKeySetRestriction restriction)
        {
            _sql.Write('(');
            Value(restriction.Key);
            _sql.Write(" IN (SELECT ");
            _sql.Quoted(KeySetColumn);
            _sql.Write(" FROM ");
            _sql.Write(restriction.TableSource);
            _sql.Write("))");
        }

        /// <summary>Writes the declaration of one hoisted value.</summary>
        /// <param name="variable">The variable.</param>
        internal void Declaration(HoistedVariable variable)
        {
            _sql.Write("DECLARE ");
            _sql.Write(variable.Name);
            _sql.Write(' ');
            _sql.Type(variable.StoreType);

            if (variable is ComputedVariable computed)
            {
                // The initializer is a scalar subquery already, and brings its own parentheses.
                _sql.Write(" = ");
                Value(computed.Value);
                _sql.Write(';');
                _sql.Line();
                return;
            }

            var lookup = (NodeLookupVariable)variable;

            // A scalar subquery rather than an assignment: where the uniqueness the schema promised
            // does not hold, this fails loudly instead of silently picking one of the matching rows
            // and producing a different answer on a different day.
            _sql.Write(" = (SELECT ");
            _sql.Quoted(LookupAlias);
            _sql.Write('.');
            _sql.Quoted(lookup.NodeProperty.Column);
            _sql.Write(" FROM ");
            _sql.Write(lookup.LookupEntity.Source);
            _sql.Write(" AS ");
            _sql.Quoted(LookupAlias);
            _sql.Write(" WHERE ");
            _sql.Quoted(LookupAlias);
            _sql.Write('.');
            _sql.Quoted(lookup.LookupProperty.Column);
            _sql.Write(" = ");
            Value(lookup.Key);
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
