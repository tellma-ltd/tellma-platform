// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Emit;

namespace Tellma.Core.Queryex.Functions
{
    /// <summary>
    ///     Every function the language has, as declarations.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This file is the table. Adding a function is an entry here plus, where it needs one,
    ///         an emission pattern on the same line — never a change to the lexer, the parser, the
    ///         binder, or the nullity pass.
    ///     </para>
    ///     <para>
    ///         Two conventions run through it. Where a function takes an optional trailing argument,
    ///         each arity is its own entry rather than one entry with an optional parameter, because
    ///         the emission differs and each entry should carry exactly one. And a date argument is
    ///         declared with a shared type variable wherever two of them have to agree, so mixing a
    ///         date with an offset-carrying instant is a type error rather than a silently wrong
    ///         answer.
    ///     </para>
    /// </remarks>
    internal static class FunctionLibrary
    {
        /// <summary>The engine-authored anchor every day-counting formula measures from.</summary>
        /// <remarks>
        ///     The first day the backend's date type can represent, and a Monday, so counting days
        ///     from it never goes negative and the remainder operator's sign never matters. Written
        ///     in the unseparated form, which no session setting can reinterpret, and typed as a
        ///     date so it never forces a conversion the older datetime type could not survive.
        /// </remarks>
        private const string Epoch = "CONVERT(date, '00010101')";

        /// <summary>Every definition, in a stable order.</summary>
        /// <returns>The definitions.</returns>
        internal static IEnumerable<FunctionDefinition> All()
        {
            foreach (FunctionDefinition definition in Aggregations())
            {
                yield return definition;
            }

            foreach (FunctionDefinition definition in Zones())
            {
                yield return definition;
            }

            foreach (FunctionDefinition definition in CalendarOperations())
            {
                yield return definition;
            }

            foreach (FunctionDefinition definition in InstantOperations())
            {
                yield return definition;
            }

            foreach (FunctionDefinition definition in Context())
            {
                yield return definition;
            }

            foreach (FunctionDefinition definition in Conditionals())
            {
                yield return definition;
            }

            foreach (FunctionDefinition definition in Conversions())
            {
                yield return definition;
            }

            foreach (FunctionDefinition definition in NumericOperations())
            {
                yield return definition;
            }

            foreach (FunctionDefinition definition in StringOperations())
            {
                yield return definition;
            }

            foreach (FunctionDefinition definition in Hierarchy())
            {
                yield return definition;
            }
        }

        /// <summary>The aggregations.</summary>
        /// <returns>The definitions.</returns>
        /// <remarks>
        ///     The optional final argument is a per-row condition; rows failing it contribute
        ///     nothing. None of these carries a fixed pattern, because their SQL depends on how the
        ///     argument is stored rather than on its type in the language — a sum over an integer
        ///     column overflows unless it is widened, an average over one truncates unless it is,
        ///     and the backend refuses a least-or-greatest over its own boolean type outright.
        /// </remarks>
        private static IEnumerable<FunctionDefinition> Aggregations()
        {
            yield return Define(
                "count",
                FunctionCategory.Aggregate,
                Signature(
                    [],
                    TypeSpecs.Numeric,
                    NullityRule.CountAggregate,
                    AggregateStrategy.Of(AggregateKind.Count, hasArgument: false, hasCondition: false)),
                Signature(
                    [Parameter("x", TypeSpecs.Any("T"))],
                    TypeSpecs.Numeric,
                    NullityRule.CountAggregate,
                    AggregateStrategy.Of(AggregateKind.Count, hasArgument: true, hasCondition: false)),
                Signature(
                    [Parameter("x", TypeSpecs.Any("T")), Parameter("condition", TypeSpecs.Bool)],
                    TypeSpecs.Numeric,
                    NullityRule.CountAggregate,
                    AggregateStrategy.Of(AggregateKind.Count, hasArgument: true, hasCondition: true)));

            yield return NumericAggregate("sum", AggregateKind.Sum);
            yield return NumericAggregate("avg", AggregateKind.Average);
            yield return OrderedAggregate("min", AggregateKind.Minimum);
            yield return OrderedAggregate("max", AggregateKind.Maximum);
        }

        /// <summary>An aggregation over numbers.</summary>
        /// <param name="name">The function name.</param>
        /// <param name="kind">Which aggregate it computes.</param>
        /// <returns>The definition.</returns>
        private static FunctionDefinition NumericAggregate(string name, AggregateKind kind)
        {
            return Define(
                name,
                FunctionCategory.Aggregate,
                Signature(
                    [Parameter("x", TypeSpecs.Numeric)],
                    TypeSpecs.Numeric,
                    NullityRule.Aggregate,
                    AggregateStrategy.Of(kind, hasArgument: true, hasCondition: false)),
                Signature(
                    [Parameter("x", TypeSpecs.Numeric), Parameter("condition", TypeSpecs.Bool)],
                    TypeSpecs.Numeric,
                    NullityRule.Aggregate,
                    AggregateStrategy.Of(kind, hasArgument: true, hasCondition: true)));
        }

        /// <summary>An aggregation that picks an extreme value.</summary>
        /// <param name="name">The function name.</param>
        /// <param name="kind">Which aggregate it computes.</param>
        /// <returns>The definition.</returns>
        private static FunctionDefinition OrderedAggregate(string name, AggregateKind kind)
        {
            return Define(
                name,
                FunctionCategory.Aggregate,
                Signature(
                    [Parameter("x", TypeSpecs.Ordered("T"), propagates: true)],
                    TypeSpecs.Ordered("T"),
                    NullityRule.Aggregate,
                    AggregateStrategy.Of(kind, hasArgument: true, hasCondition: false)),
                Signature(
                    [
                        Parameter("x", TypeSpecs.Ordered("T"), propagates: true),
                        Parameter("condition", TypeSpecs.Bool),
                    ],
                    TypeSpecs.Ordered("T"),
                    NullityRule.Aggregate,
                    AggregateStrategy.Of(kind, hasArgument: true, hasCondition: true)));
        }

        /// <summary>Making a zone explicit.</summary>
        /// <returns>The definitions.</returns>
        /// <remarks>
        ///     A calendar boundary is a position in someone's local time, so an instant that carries
        ///     its own offset has to be read in a named zone before a year or an hour can be taken
        ///     from it. Turning that into a compile error with one obvious fix is what stops a
        ///     report from being wrong row by row in a way nobody would notice.
        /// </remarks>
        private static IEnumerable<FunctionDefinition> Zones()
        {
            yield return Define(
                "local",
                FunctionCategory.Scalar,
                Signature(
                    [Parameter("d", TypeSpecs.OffsetDateTime)],
                    TypeSpecs.LocalDateTime,
                    NullityRule.Propagate(0),
                    Sql.Value("CAST({0} AT TIME ZONE {1} AS datetime2(7))"),
                    [new SyntheticArgument(QueryexParameterOrigin.TimeZone, BoundType.String)]),
                Signature(
                    [
                        Parameter("d", TypeSpecs.OffsetDateTime),
                        Parameter("zone", TypeSpecs.Selector, constraint: ParameterConstraint.LiteralOnly(consumed: true)),
                    ],
                    TypeSpecs.LocalDateTime,
                    NullityRule.Propagate(0),
                    Sql.Value("CAST({0} AT TIME ZONE {2} AS datetime2(7))"),
                    [new SyntheticArgument(QueryexParameterOrigin.Literal, BoundType.String, FromParameter: 1)]));
        }

        /// <summary>The calendar operations.</summary>
        /// <returns>The definitions.</returns>
        private static IEnumerable<FunctionDefinition> CalendarOperations()
        {
            yield return DatePart("year", "YEAR");
            yield return DatePart("quarter", "QUARTER");
            yield return DatePart("month", "MONTH");
            yield return DatePart("day", "DAY");

            // The week of the year, counted the way the international standard does, which no
            // session setting can move.
            yield return DatePart("week", "ISO_WEEK");

            // Counted from a Monday epoch rather than read off the backend's own weekday part,
            // which follows a session setting and would make the same expression mean different
            // things on two connections.
            yield return Define(
                "weekday",
                FunctionCategory.Scalar,
                Signature(
                    [Parameter("d", TypeSpecs.Calendar("T"))],
                    TypeSpecs.Numeric,
                    NullityRule.Propagate(0),
                    Sql.Value($"((DATEDIFF(DAY, {Epoch}, {{0}}) % 7) + 1)", QueryexStoreType.QxInt)));

            yield return TimePart("hour", "HOUR");
            yield return TimePart("minute", "MINUTE");
            yield return TimePart("second", "SECOND");

            yield return Truncation(
                "startOfYear",
                $"DATEADD(YEAR, DATEDIFF(YEAR, {Epoch}, {{0}}), {Epoch})");

            yield return Truncation(
                "startOfMonth",
                $"DATEADD(MONTH, DATEDIFF(MONTH, {Epoch}, {{0}}), {Epoch})");

            // Whole weeks since a Monday epoch, which lands on the same or preceding Monday and
            // mentions the argument once, where the obvious subtract-the-weekday form mentions it
            // twice.
            yield return Define(
                "startOfWeek",
                FunctionCategory.Scalar,
                Signature(
                    [Parameter("d", TypeSpecs.Calendar("T"))],
                    TypeSpecs.Date,
                    NullityRule.Propagate(0),
                    Sql.Value($"DATEADD(DAY, (DATEDIFF(DAY, {Epoch}, {{0}}) / 7) * 7, {Epoch})")));

            yield return Define(
                "startOfDay",
                FunctionCategory.Scalar,
                Signature(
                    [Parameter("d", TypeSpecs.Calendar("T"))],
                    TypeSpecs.Date,
                    NullityRule.Propagate(0),
                    Sql.Value("CAST({0} AS date)")));

            yield return CalendarAdd("addYears", "YEAR");
            yield return CalendarAdd("addMonths", "MONTH");
            yield return CalendarDifference("diffYears", "YEAR");
            yield return CalendarDifference("diffMonths", "MONTH");
        }

        /// <summary>A component read out of a zone-resolved date.</summary>
        /// <param name="name">The function name.</param>
        /// <param name="part">The backend's name for the component.</param>
        /// <returns>The definition.</returns>
        private static FunctionDefinition DatePart(string name, string part)
        {
            return Define(
                name,
                FunctionCategory.Scalar,
                Signature(
                    [Parameter("d", TypeSpecs.Calendar("T"))],
                    TypeSpecs.Numeric,
                    NullityRule.Propagate(0),
                    Sql.Value($"DATEPART({part}, {{0}})", QueryexStoreType.QxInt)),
                Signature(
                    [Parameter("d", TypeSpecs.Calendar("T")), CalendarParameter()],
                    TypeSpecs.Numeric,
                    NullityRule.Propagate(0),
                    Sql.Selected(1, QueryexStoreType.QxInt, (CalendarCodes.Gregorian, $"DATEPART({part}, {{0}})"))));
        }

        /// <summary>A component read out of a clock reading.</summary>
        /// <param name="name">The function name.</param>
        /// <param name="part">The backend's name for the component.</param>
        /// <returns>The definition.</returns>
        private static FunctionDefinition TimePart(string name, string part)
        {
            return Define(
                name,
                FunctionCategory.Scalar,
                Signature(
                    [Parameter("d", TypeSpecs.TimeOfDay)],
                    TypeSpecs.Numeric,
                    NullityRule.Propagate(0),
                    Sql.Value($"DATEPART({part}, {{0}})", QueryexStoreType.QxInt)));
        }

        /// <summary>A truncation to a period boundary.</summary>
        /// <param name="name">The function name.</param>
        /// <param name="pattern">The emission pattern.</param>
        /// <returns>The definition.</returns>
        private static FunctionDefinition Truncation(string name, string pattern)
        {
            return Define(
                name,
                FunctionCategory.Scalar,
                Signature(
                    [Parameter("d", TypeSpecs.Calendar("T"))],
                    TypeSpecs.Date,
                    NullityRule.Propagate(0),
                    Sql.Value(pattern)),
                Signature(
                    [Parameter("d", TypeSpecs.Calendar("T")), CalendarParameter()],
                    TypeSpecs.Date,
                    NullityRule.Propagate(0),
                    Sql.Selected(1, (CalendarCodes.Gregorian, pattern))));
        }

        /// <summary>Adding a variable-length unit, which is calendar-dependent.</summary>
        /// <param name="name">The function name.</param>
        /// <param name="unit">The backend's name for the unit.</param>
        /// <returns>The definition.</returns>
        private static FunctionDefinition CalendarAdd(string name, string unit)
        {
            return Define(
                name,
                FunctionCategory.Scalar,
                Signature(
                    [Parameter("d", TypeSpecs.Calendar("T")), Parameter("n", TypeSpecs.Numeric)],
                    TypeSpecs.Calendar("T"),
                    NullityRule.Union(0, 1),
                    Sql.Value($"DATEADD({unit}, {{1}}, {{0}})")),
                Signature(
                    [
                        Parameter("d", TypeSpecs.Calendar("T")),
                        Parameter("n", TypeSpecs.Numeric),
                        CalendarParameter(),
                    ],
                    TypeSpecs.Calendar("T"),
                    NullityRule.Union(0, 1),
                    Sql.Selected(2, (CalendarCodes.Gregorian, $"DATEADD({unit}, {{1}}, {{0}})"))));
        }

        /// <summary>Counting whole elapsed calendar units between two dates.</summary>
        /// <param name="name">The function name.</param>
        /// <param name="unit">The backend's name for the unit.</param>
        /// <returns>The definition.</returns>
        /// <remarks>
        ///     Whole units, not boundary crossings: the last day of one year to the first of the
        ///     next is zero whole years, and reading it as one would make an age wrong for everybody
        ///     who has not had their birthday yet. The backend counts boundaries, so the count is
        ///     corrected by checking whether the unit actually completed.
        /// </remarks>
        private static FunctionDefinition CalendarDifference(string name, string unit)
        {
            string crossings = $"DATEDIFF({unit}, {{0}}, {{1}})";
            string pattern =
                $"({crossings} + CASE"
                + $" WHEN {crossings} > 0 AND DATEADD({unit}, {crossings}, {{0}}) > {{1}} THEN -1"
                + $" WHEN {crossings} < 0 AND DATEADD({unit}, {crossings}, {{0}}) < {{1}} THEN 1"
                + " ELSE 0 END)";

            return Define(
                name,
                FunctionCategory.Scalar,
                Signature(
                    [Parameter("d1", TypeSpecs.Calendar("T")), Parameter("d2", TypeSpecs.Calendar("T"))],
                    TypeSpecs.Numeric,
                    NullityRule.Union(0, 1),
                    Sql.Value(pattern, QueryexStoreType.QxInt)),
                Signature(
                    [
                        Parameter("d1", TypeSpecs.Calendar("T")),
                        Parameter("d2", TypeSpecs.Calendar("T")),
                        CalendarParameter(),
                    ],
                    TypeSpecs.Numeric,
                    NullityRule.Union(0, 1),
                    Sql.Selected(2, QueryexStoreType.QxInt, (CalendarCodes.Gregorian, pattern))));
        }

        /// <summary>The elapsed-time operations, which need no zone.</summary>
        /// <returns>The definitions.</returns>
        private static IEnumerable<FunctionDefinition> InstantOperations()
        {
            yield return InstantAdd("addDays", "DAY");
            yield return InstantAdd("addHours", "HOUR");
            yield return InstantAdd("addMinutes", "MINUTE");
            yield return InstantAdd("addSeconds", "SECOND");

            // Measured in the next finer unit and divided, which is what gives a fractional answer.
            // Counted in the backend's wide difference form throughout: a narrow count of seconds
            // overflows inside ordinary date ranges, and that overflow would surface as a backend
            // error from an expression that is perfectly valid.
            yield return InstantDifference("diffDays", "HOUR", "24.0");
            yield return InstantDifference("diffHours", "MINUTE", "60.0");
            yield return InstantDifference("diffMinutes", "SECOND", "60.0");

            yield return Define(
                "diffSeconds",
                FunctionCategory.Scalar,
                Signature(
                    [Parameter("d1", TypeSpecs.Instant("T")), Parameter("d2", TypeSpecs.Instant("T"))],
                    TypeSpecs.Numeric,
                    NullityRule.Union(0, 1),
                    Sql.Value("DATEDIFF_BIG(SECOND, {0}, {1})", QueryexStoreType.QxBigInt)));
        }

        /// <summary>Adding a fixed-length unit.</summary>
        /// <param name="name">The function name.</param>
        /// <param name="unit">The backend's name for the unit.</param>
        /// <returns>The definition.</returns>
        private static FunctionDefinition InstantAdd(string name, string unit)
        {
            return Define(
                name,
                FunctionCategory.Scalar,
                Signature(
                    [Parameter("d", TypeSpecs.Instant("T")), Parameter("n", TypeSpecs.Numeric)],
                    TypeSpecs.Instant("T"),
                    NullityRule.Union(0, 1),
                    Sql.Value($"DATEADD({unit}, {{1}}, {{0}})")));
        }

        /// <summary>Measuring elapsed time.</summary>
        /// <param name="name">The function name.</param>
        /// <param name="subunit">The finer unit the count is taken in.</param>
        /// <param name="perUnit">How many of that finer unit make one of the answer's.</param>
        /// <returns>The definition.</returns>
        private static FunctionDefinition InstantDifference(string name, string subunit, string perUnit)
        {
            return Define(
                name,
                FunctionCategory.Scalar,
                Signature(
                    [Parameter("d1", TypeSpecs.Instant("T")), Parameter("d2", TypeSpecs.Instant("T"))],
                    TypeSpecs.Numeric,
                    NullityRule.Union(0, 1),
                    Sql.Value($"(DATEDIFF_BIG({subunit}, {{0}}, {{1}}) / {perUnit})", QueryexStoreType.QxDecimal(26, 6))));
        }

        /// <summary>The values the host supplies at execution.</summary>
        /// <returns>The definitions.</returns>
        private static IEnumerable<FunctionDefinition> Context()
        {
            yield return Define(
                "today",
                FunctionCategory.Scalar,
                Signature(
                    [],
                    TypeSpecs.Date,
                    NullityRule.Always(QueryexNullity.NotNull),
                    Sql.Context(QueryexParameterOrigin.Today)));

            yield return Define(
                "now",
                FunctionCategory.Scalar,
                Signature(
                    [],
                    TypeSpecs.OffsetDateTime,
                    NullityRule.Always(QueryexNullity.NotNull),
                    Sql.Context(QueryexParameterOrigin.Now)));

            yield return Define(
                "me",
                FunctionCategory.Scalar,
                Signature(
                    [],
                    TypeSpecs.Numeric,
                    NullityRule.ContextUser,
                    Sql.Context(QueryexParameterOrigin.UserId)));
        }

        /// <summary>Choosing between values.</summary>
        /// <returns>The definitions.</returns>
        private static IEnumerable<FunctionDefinition> Conditionals()
        {
            yield return Define(
                "if",
                FunctionCategory.Scalar,
                Signature(
                    [
                        Parameter("condition", TypeSpecs.Bool),
                        Parameter("whenTrue", TypeSpecs.Any("T"), propagates: true),
                        Parameter("whenFalse", TypeSpecs.Any("T"), propagates: true),
                    ],
                    TypeSpecs.Any("T"),
                    NullityRule.Conditional,

                    // The condition is read as a truth value and both branches as scalars, whatever
                    // position the call itself sits in. Declared here, on the pattern, rather than
                    // special-cased where positions are assigned.
                    Sql.Value("CASE WHEN {0:p} THEN {1} ELSE {2} END")));

            yield return Define(
                "coalesce",
                FunctionCategory.Scalar,
                Signature(
                    [
                        Parameter("x", TypeSpecs.Any("T"), propagates: true),
                        Parameter("rest", TypeSpecs.Any("T"), propagates: true, rest: true),
                    ],
                    TypeSpecs.Any("T"),
                    NullityRule.Coalesce,
                    Sql.Value("COALESCE({0}, {1*, })")));
        }

        /// <summary>Conversion between types.</summary>
        /// <returns>The definitions.</returns>
        private static IEnumerable<FunctionDefinition> Conversions()
        {
            yield return Define(
                "cast",
                FunctionCategory.Scalar,
                Signature(
                    [
                        Parameter("x", TypeSpecs.Any("T"), propagates: true),
                        Parameter(
                            "type",
                            TypeSpecs.Selector,
                            constraint: ParameterConstraint.MemberOf(CastRules.TargetNames)),
                    ],
                    TypeSpecs.FromSelector(1, TypeMask.Any),
                    NullityRule.Propagate(0),
                    CastStrategy.Instance));
        }

        /// <summary>Arithmetic on numbers.</summary>
        /// <returns>The definitions.</returns>
        private static IEnumerable<FunctionDefinition> NumericOperations()
        {
            yield return Unary("abs", "ABS({0})", TypeSpecs.Numeric, TypeSpecs.Numeric, follows: 0);
            yield return Unary("floor", "FLOOR({0})", TypeSpecs.Numeric, TypeSpecs.Numeric, follows: 0);
            yield return Unary("ceiling", "CEILING({0})", TypeSpecs.Numeric, TypeSpecs.Numeric, follows: 0);

            yield return Define(
                "round",
                FunctionCategory.Scalar,
                Signature(
                    [
                        Parameter("x", TypeSpecs.Numeric),

                        // Statically known so the emission is fixed, but still a value the backend
                        // receives rather than a selector consumed at compile time.
                        Parameter(
                            "digits",
                            TypeSpecs.Numeric,
                            constraint: ParameterConstraint.LiteralOnly(consumed: false)),
                    ],
                    TypeSpecs.Numeric,
                    NullityRule.Propagate(0),
                    Sql.ValueLike("ROUND({0}, {1})", 0)));
        }

        /// <summary>Operations on text.</summary>
        /// <returns>The definitions.</returns>
        private static IEnumerable<FunctionDefinition> StringOperations()
        {
            yield return Unary("length", "LEN({0})", TypeSpecs.Text, TypeSpecs.Numeric, store: QueryexStoreType.QxBigInt);
            yield return Unary("trim", "TRIM({0})", TypeSpecs.Text, TypeSpecs.Text, follows: 0);
            yield return Unary("upper", "UPPER({0})", TypeSpecs.Text, TypeSpecs.Text, follows: 0);
            yield return Unary("lower", "LOWER({0})", TypeSpecs.Text, TypeSpecs.Text, follows: 0);

            yield return Define(
                "left",
                FunctionCategory.Scalar,
                Signature(
                    [Parameter("s", TypeSpecs.Text), Parameter("n", TypeSpecs.Numeric)],
                    TypeSpecs.Text,

                    // Both arguments, not just the text: the backend yields nothing when the count
                    // is missing, and a result claimed always present would have its guards omitted.
                    NullityRule.Union(0, 1),
                    Sql.ValueLike("LEFT({0}, {1})", 0)));

            yield return Define(
                "right",
                FunctionCategory.Scalar,
                Signature(
                    [Parameter("s", TypeSpecs.Text), Parameter("n", TypeSpecs.Numeric)],
                    TypeSpecs.Text,
                    NullityRule.Union(0, 1),
                    Sql.ValueLike("RIGHT({0}, {1})", 0)));

            yield return Define(
                "substring",
                FunctionCategory.Scalar,
                Signature(
                    [Parameter("s", TypeSpecs.Text), Parameter("start", TypeSpecs.Numeric)],
                    TypeSpecs.Text,
                    NullityRule.Union(0, 1),
                    Sql.ValueLike("SUBSTRING({0}, {1}, 2147483647)", 0)),
                Signature(
                    [
                        Parameter("s", TypeSpecs.Text),
                        Parameter("start", TypeSpecs.Numeric),
                        Parameter("length", TypeSpecs.Numeric),
                    ],
                    TypeSpecs.Text,
                    NullityRule.Union(0, 1, 2),
                    Sql.ValueLike("SUBSTRING({0}, {1}, {2})", 0)));

            yield return Define(
                "replace",
                FunctionCategory.Scalar,
                Signature(
                    [
                        Parameter("s", TypeSpecs.Text),
                        Parameter("old", TypeSpecs.Text),
                        Parameter("new", TypeSpecs.Text),
                    ],
                    TypeSpecs.Text,
                    NullityRule.Union(0, 1, 2),
                    Sql.ValueLike("REPLACE({0}, {1}, {2})", 0)));

            // Matched with a mechanism that has no pattern metacharacters at all, so a needle is
            // taken literally and nothing anywhere has to escape one. Escaping computed patterns
            // inside SQL is exactly the fragile rewriting this avoids: a missed case there is
            // silently wrong rows, where this is at worst a visibly slower scan.
            yield return SearchPredicate("contains", "ISNULL(CHARINDEX({1}, {0}), 0) > 0");
            yield return SearchPredicate("startsWith", "ISNULL(CHARINDEX({1}, {0}), 0) = 1");
            yield return SearchPredicate("endsWith", "ISNULL(CHARINDEX(REVERSE({1}), REVERSE({0})), 0) = 1");
        }

        /// <summary>A total search predicate over text.</summary>
        /// <param name="name">The function name.</param>
        /// <param name="found">The pattern that tests for a non-empty needle.</param>
        /// <returns>The definition.</returns>
        /// <remarks>
        ///     Two things the obvious form gets wrong, both handled here. An absent operand would
        ///     make the test neither true nor false, which survives a negation and quietly drops the
        ///     row; and the backend reports an empty needle as not found, where every reading of the
        ///     language says the empty string is a part of any text there is.
        /// </remarks>
        private static FunctionDefinition SearchPredicate(string name, string found)
        {
            string pattern = $"({found} OR (ISNULL(DATALENGTH({{1}}), 1) = 0 AND {{0}} IS NOT NULL))";

            return Define(
                name,
                FunctionCategory.Scalar,
                Signature(
                    [Parameter("s", TypeSpecs.Text), Parameter("part", TypeSpecs.Text)],
                    TypeSpecs.Bool,
                    NullityRule.AlwaysNotNull,
                    Sql.Predicate(pattern)));
        }

        /// <summary>The hierarchy predicates.</summary>
        /// <returns>The definitions.</returns>
        /// <remarks>
        ///     Both are reflexive — a row is its own ancestor and its own descendant — and both take
        ///     any number of keys, matching a row that relates to any of them. A key that identifies
        ///     no row contributes nothing, so with every key unmatched the answer is false; there is
        ///     no key value that makes either predicate universally true.
        /// </remarks>
        private static IEnumerable<FunctionDefinition> Hierarchy()
        {
            yield return HierarchyPredicate("descendantOf", HierarchyStrategy.Descendant);
            yield return HierarchyPredicate("ancestorOf", HierarchyStrategy.Ancestor);
        }

        /// <summary>One hierarchy predicate.</summary>
        /// <param name="name">The function name.</param>
        /// <param name="strategy">Which way it looks.</param>
        /// <returns>The definition.</returns>
        private static FunctionDefinition HierarchyPredicate(string name, EmitStrategy strategy)
        {
            return Define(
                name,
                FunctionCategory.Scalar,
                Signature(
                    [
                        Parameter("key", TypeSpecs.Any("K"), constraint: ParameterConstraint.TreeNodeKeyPath),
                        Parameter(
                            "keys",
                            TypeSpecs.Any("K"),
                            rest: true,
                            constraint: ParameterConstraint.PathFree),
                    ],
                    TypeSpecs.Bool,
                    NullityRule.AlwaysNotNull,
                    strategy));
        }

        /// <summary>A one-argument function with a fixed emission.</summary>
        /// <param name="name">The function name.</param>
        /// <param name="pattern">The emission pattern.</param>
        /// <param name="argument">The argument's type.</param>
        /// <param name="result">The result type.</param>
        /// <param name="store">The type the backend holds the result in, when the SQL fixes it.</param>
        /// <param name="follows">The argument whose stored type the result takes, when it takes one.</param>
        /// <returns>The definition.</returns>
        private static FunctionDefinition Unary(
            string name,
            string pattern,
            TypeSpec argument,
            TypeSpec result,
            QueryexStoreType? store = null,
            int? follows = null)
        {
            EmitStrategy emit = store is QueryexStoreType fixedStore
                ? Sql.Value(pattern, fixedStore)
                : follows is int source ? Sql.ValueLike(pattern, source) : Sql.Value(pattern);

            return Define(
                name,
                FunctionCategory.Scalar,
                Signature([Parameter("x", argument)], result, NullityRule.Propagate(0), emit));
        }

        /// <summary>The calendar selector every calendar operation shares.</summary>
        /// <returns>The parameter.</returns>
        private static FunctionParameter CalendarParameter()
        {
            return Parameter(
                "calendar",
                TypeSpecs.Selector,
                constraint: ParameterConstraint.MemberOf(CalendarCodes.Accepted));
        }

        /// <summary>Builds a parameter.</summary>
        /// <param name="name">The parameter's name, for diagnostics.</param>
        /// <param name="type">The type it accepts.</param>
        /// <param name="propagates">Whether a demand on the call reaches this argument.</param>
        /// <param name="rest">Whether this is a variadic tail.</param>
        /// <param name="constraint">What else the argument has to satisfy.</param>
        /// <returns>The parameter.</returns>
        private static FunctionParameter Parameter(
            string name,
            TypeSpec type,
            bool propagates = false,
            bool rest = false,
            ParameterConstraint? constraint = null)
        {
            return new FunctionParameter(name, type)
            {
                Propagates = propagates,
                Rest = rest,
                Constraint = constraint ?? ParameterConstraint.None,
            };
        }

        /// <summary>Builds a signature.</summary>
        /// <param name="parameters">The declared parameters.</param>
        /// <param name="returns">The result type.</param>
        /// <param name="nullity">How the result's nullity follows from the arguments'.</param>
        /// <param name="emit">How the call becomes SQL.</param>
        /// <param name="synthetic">Extra arguments the engine supplies itself.</param>
        /// <returns>The signature.</returns>
        private static FunctionSignature Signature(
            ImmutableArray<FunctionParameter> parameters,
            TypeSpec returns,
            NullityRule nullity,
            EmitStrategy emit,
            ImmutableArray<SyntheticArgument> synthetic = default)
        {
            return new FunctionSignature
            {
                Parameters = parameters,
                Returns = returns,
                Nullity = nullity,
                Emit = emit,
                Synthetic = synthetic.IsDefault ? [] : synthetic,
            };
        }

        /// <summary>Builds a definition.</summary>
        /// <param name="name">The function name.</param>
        /// <param name="category">Whether it reads one row or a whole group.</param>
        /// <param name="signatures">The overloads.</param>
        /// <returns>The definition.</returns>
        private static FunctionDefinition Define(
            string name,
            FunctionCategory category,
            params FunctionSignature[] signatures)
        {
            return new FunctionDefinition(name, category, [.. signatures]);
        }
    }
}
