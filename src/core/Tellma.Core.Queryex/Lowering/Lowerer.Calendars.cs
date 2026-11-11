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
    ///     Calendar functions counted in a table calendar, lowered into lookups on the host's month
    ///     table.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A table calendar's answer is read off one or two rows of the month table: the month a
    ///         date falls in, and for some functions a second month found from the first by its year
    ///         and number. How those rows are reached depends on where the call sits.
    ///     </para>
    ///     <para>
    ///         At row grain — an operand reads a path and none aggregates — each lookup is a lateral
    ///         source beside the statement's joins, so a grouping key or a filter reads a column of
    ///         it. A call that reads no row is the same for every row and is computed once, into a
    ///         variable declared ahead of the statement. Anything else — an operand that aggregates,
    ///         or a statement with no row source — computes the lookups in place, as a scalar
    ///         subquery that nests one level per row it reads.
    ///     </para>
    /// </remarks>
    internal sealed partial class Lowerer
    {
        /// <summary>How a second lookup's month is found from a first one's.</summary>
        private enum MonthStep
        {
            /// <summary>The first month of the same year.</summary>
            YearStart,

            /// <summary>The first month of the same quarter.</summary>
            QuarterStart,

            /// <summary>A number of months later.</summary>
            MonthsLater,

            /// <summary>The same month a number of years later.</summary>
            YearsLater,

            /// <summary>The same month in another lookup's year.</summary>
            SameMonthOfYear,
        }

        /// <summary>The month a date falls in, with the date in the two forms the answers read it in.</summary>
        /// <param name="Lookup">The lookup of the month.</param>
        /// <param name="Date">The date, atomic, with whatever time of day it has.</param>
        /// <param name="Day">The date without its time, atomic, which is what the month is sought by.</param>
        private sealed record DateLookup(CalendarLookup Lookup, PlanValue Date, PlanValue Day);

        /// <summary>A lateral month lookup by date, recorded so an equal one reuses it.</summary>
        /// <param name="Base">The root the operand's paths resolve from.</param>
        /// <param name="Operand">The date operand, as bound.</param>
        /// <param name="Found">The lookup.</param>
        private sealed record DateLookupEntry(JoinNode? Base, TypedExpr Operand, DateLookup Found);

        /// <summary>A lateral month lookup by year and month, recorded so an equal one reuses it.</summary>
        /// <param name="From">The lookup it is found from.</param>
        /// <param name="Other">The second lookup it reads, when it reads one.</param>
        /// <param name="Step">How it is found.</param>
        /// <param name="Base">The root the count's paths resolve from.</param>
        /// <param name="Count">The count of units, as bound, when there is one.</param>
        /// <param name="Found">The lookup.</param>
        private sealed record MonthLookupEntry(
            CalendarLookup From,
            CalendarLookup? Other,
            MonthStep Step,
            JoinNode? Base,
            TypedExpr? Count,
            CalendarLookup Found);

        /// <summary>One table-calendar call being lowered, and the form it is lowered in.</summary>
        private sealed class CalendarBuild
        {
            /// <summary>Initializes the build.</summary>
            /// <param name="source">The month table.</param>
            /// <param name="code">The calendar code, as the table holds it.</param>
            /// <param name="span">Where the call sits in the input.</param>
            /// <param name="scalar">Whether the lookups are computed in place rather than as lateral sources.</param>
            internal CalendarBuild(string source, string code, QueryexSpan span, bool scalar)
            {
                Source = source;
                Code = code;
                Span = span;
                Levels = scalar ? [] : null;
            }

            /// <summary>The month table.</summary>
            internal string Source { get; }

            /// <summary>The calendar code, as the table holds it.</summary>
            internal string Code { get; }

            /// <summary>Where the call sits in the input.</summary>
            internal QueryexSpan Span { get; }

            /// <summary>The scalar form's levels, outermost first; null for the lateral form.</summary>
            internal List<CalendarLookup>? Levels { get; }

            /// <summary>The calendar code's parameter, once the first operand has been lowered.</summary>
            internal PlanValue? Calendar { get; set; }
        }

        /// <summary>The lateral lookups by date, by the sequence of one-row sources they were made in.</summary>
        private readonly Dictionary<List<LateralSource>, List<DateLookupEntry>> _dateLookups =
            new(ReferenceEqualityComparer.Instance);

        /// <summary>The lateral lookups by year and month.</summary>
        private readonly List<MonthLookupEntry> _monthLookups = [];

        /// <summary>Lowers a calendar function counted in a table calendar.</summary>
        /// <param name="call">The bound call.</param>
        /// <param name="strategy">The function's strategy, which says what it computes.</param>
        /// <param name="code">The calendar code, as written.</param>
        /// <returns>The lowered value.</returns>
        private PlanValue TableCalendarValue(TypedCall call, CalendarStrategy strategy, string code)
        {
            // The binder accepts a table calendar only where the schema maps it, so a missing map
            // here is an engine defect rather than anything a user could write.
            string source = _schema?.CalendarSource(code)
                ?? throw new InvalidOperationException("A table calendar with no month table reached lowering.");
            string canonical = CalendarCodes.Tabular.First(
                tabular => string.Equals(tabular, code, StringComparison.OrdinalIgnoreCase));

            // The form follows the operands: everything before the calendar code.
            bool aggregates = false;
            bool readsARow = false;
            for (int index = 0; index < strategy.SelectorIndex; index++)
            {
                aggregates |= call.Arguments[index].ContainsAggregate;
                readsARow |= call.Arguments[index].ContainsPath;
            }

            if (aggregates || (readsARow && !_bindingsAllowed) || (!readsARow && Root is null))
            {
                // Nothing lateral can compute a value from a group, and a statement with no row
                // source has nothing to apply one to; a row-grain operand where no lateral source
                // is in scope is computed in place too.
                return ScalarCalendar(call, strategy.Operation, source, canonical);
            }

            // A row-grain call reads lateral lookups; one that reads no row is computed once.
            return readsARow
                ? Compose(call, strategy.Operation, new CalendarBuild(source, canonical, call.Span, scalar: false))
                : HoistedCalendar(call, strategy.Operation, source, canonical);
        }

        /// <summary>Computes a table-calendar call that reads no row once, before the statement.</summary>
        /// <param name="call">The bound call.</param>
        /// <param name="operation">What the call computes.</param>
        /// <param name="source">The month table.</param>
        /// <param name="code">The calendar code, as the table holds it.</param>
        /// <returns>A reference to the variable it is computed into.</returns>
        /// <remarks>
        ///     One variable per distinct call: a call written twice is computed once, which is also
        ///     what makes a filter and a grouping key that share a period boundary agree.
        /// </remarks>
        private PlanVariableRef HoistedCalendar(TypedCall call, CalendarOperation operation, string source, string code)
        {
            foreach ((TypedCall prior, ComputedVariable existing) in _calendarVariables)
            {
                if (TypedEquivalence.AreEquivalent(prior, call))
                {
                    return new PlanVariableRef(call.Span, existing);
                }
            }

            // The declaration runs ahead of the statement, where no lateral source is in scope yet,
            // so nothing it reads may name one.
            bool savedBindings = _bindingsAllowed;
            _bindingsAllowed = false;
            PlanValue initializer = ScalarCalendar(call, operation, source, code);
            _bindingsAllowed = savedBindings;

            ComputedVariable variable = new(initializer);
            _variables.Add(variable);
            _calendarVariables.Add((call, variable));
            return new PlanVariableRef(call.Span, variable);
        }

        /// <summary>Computes a table-calendar call in place, as a scalar subquery.</summary>
        /// <param name="call">The bound call.</param>
        /// <param name="operation">What the call computes.</param>
        /// <param name="source">The month table.</param>
        /// <param name="code">The calendar code, as the table holds it.</param>
        /// <returns>The lowered value.</returns>
        private PlanScalarLookup ScalarCalendar(TypedCall call, CalendarOperation operation, string source, string code)
        {
            CalendarBuild build = new(source, code, call.Span, scalar: true);
            PlanValue result = Compose(call, operation, build);
            return new PlanScalarLookup(call.Span, [.. build.Levels!], result);
        }

        /// <summary>Composes a call's answer from the months it looks up.</summary>
        /// <param name="call">The bound call.</param>
        /// <param name="operation">What the call computes.</param>
        /// <param name="build">The form the lookups are made in.</param>
        /// <returns>The answer, reading the lookups' columns.</returns>
        private PlanValue Compose(TypedCall call, CalendarOperation operation, CalendarBuild build)
        {
            QueryexSpan span = call.Span;
            DateLookup first = ByDate(build, call.Arguments[0]);
            CalendarLookup month = first.Lookup;

            switch (operation)
            {
                case CalendarOperation.Year:
                    return Column(span, month, CalendarColumn.Year);

                case CalendarOperation.Month:
                    return Column(span, month, CalendarColumn.Month);

                case CalendarOperation.Quarter:
                    return Compute(span, BoundType.Numeric, UmAlQuraArithmetic.Quarter, Column(span, month, CalendarColumn.Month));

                case CalendarOperation.Day:
                    return Compute(
                        span,
                        BoundType.Numeric,
                        UmAlQuraArithmetic.DayOfMonth,
                        Column(span, month, CalendarColumn.StartDate),
                        first.Day);

                case CalendarOperation.Week:
                    {
                        // Sevens from the year's first day, which is the first day of its first month.
                        CalendarLookup year = YearStart(build, month);
                        return Compute(
                            span,
                            BoundType.Numeric,
                            UmAlQuraArithmetic.Week,
                            Column(span, year, CalendarColumn.StartDate),
                            first.Day);
                    }

                case CalendarOperation.StartOfYear:
                    return Column(span, YearStart(build, month), CalendarColumn.StartDate);

                case CalendarOperation.StartOfQuarter:
                    {
                        CalendarLookup quarter = ByMonth(
                            build,
                            MonthStep.QuarterStart,
                            month,
                            other: null,
                            count: null,
                            countTwice: false,
                            _ => (
                                Column(span, month, CalendarColumn.Year),
                                Compute(span, BoundType.Numeric, UmAlQuraArithmetic.QuarterFirstMonth, Column(span, month, CalendarColumn.Month))));
                        return Column(span, quarter, CalendarColumn.StartDate);
                    }

                case CalendarOperation.StartOfMonth:
                    return Column(span, month, CalendarColumn.StartDate);

                case CalendarOperation.AddMonths:
                    {
                        // The count is read by both the year and the month the target is sought by.
                        CalendarLookup target = ByMonth(
                            build,
                            MonthStep.MonthsLater,
                            month,
                            other: null,
                            call.Arguments[1],
                            countTwice: true,
                            count => (
                                Compute(span, BoundType.Numeric, UmAlQuraArithmetic.MonthsLaterYear, Column(span, month, CalendarColumn.Year), Column(span, month, CalendarColumn.Month), count!),
                                Compute(span, BoundType.Numeric, UmAlQuraArithmetic.MonthsLaterMonth, Column(span, month, CalendarColumn.Year), Column(span, month, CalendarColumn.Month), count!)));
                        return Shifted(span, call.Type, first, target);
                    }

                case CalendarOperation.AddYears:
                    {
                        CalendarLookup target = ByMonth(
                            build,
                            MonthStep.YearsLater,
                            month,
                            other: null,
                            call.Arguments[1],
                            countTwice: false,
                            count => (
                                Compute(span, BoundType.Numeric, UmAlQuraArithmetic.YearsLater, Column(span, month, CalendarColumn.Year), count!),
                                Column(span, month, CalendarColumn.Month)));
                        return Shifted(span, call.Type, first, target);
                    }

                case CalendarOperation.DiffMonths:
                    {
                        // Moving the first date by the months crossed lands it in the second date's own
                        // month, so that month is the target and needs no lookup of its own.
                        DateLookup second = ByDate(build, call.Arguments[1]);
                        return Compute(
                            span,
                            BoundType.Numeric,
                            UmAlQuraArithmetic.DiffMonths,
                            Column(span, second.Lookup, CalendarColumn.Year),
                            Column(span, second.Lookup, CalendarColumn.Month),
                            Column(span, month, CalendarColumn.Year),
                            Column(span, month, CalendarColumn.Month),
                            Column(span, month, CalendarColumn.StartDate),
                            Column(span, second.Lookup, CalendarColumn.StartDate),
                            first.Day,
                            Column(span, second.Lookup, CalendarColumn.EndDate),
                            first.Date,
                            second.Date);
                    }

                case CalendarOperation.DiffYears:
                    {
                        // Moving the first date by the years crossed lands it in its own month of the
                        // second date's year.
                        DateLookup second = ByDate(build, call.Arguments[1]);
                        CalendarLookup target = ByMonth(
                            build,
                            MonthStep.SameMonthOfYear,
                            month,
                            second.Lookup,
                            count: null,
                            countTwice: false,
                            _ => (Column(span, second.Lookup, CalendarColumn.Year), Column(span, month, CalendarColumn.Month)));
                        return Compute(
                            span,
                            BoundType.Numeric,
                            UmAlQuraArithmetic.DiffYears,
                            Column(span, second.Lookup, CalendarColumn.Year),
                            Column(span, month, CalendarColumn.Year),
                            Column(span, month, CalendarColumn.StartDate),
                            Column(span, target, CalendarColumn.StartDate),
                            first.Day,
                            Column(span, target, CalendarColumn.EndDate),
                            first.Date,
                            second.Date);
                    }

                default:
                    throw new InvalidOperationException("A calendar operation with no table-calendar form reached lowering.");
            }
        }

        /// <summary>Finds the first month of the year a month belongs to.</summary>
        /// <param name="build">The form the lookups are made in.</param>
        /// <param name="month">The month.</param>
        /// <returns>The lookup of the year's first month.</returns>
        private CalendarLookup YearStart(CalendarBuild build, CalendarLookup month)
        {
            return ByMonth(
                build,
                MonthStep.YearStart,
                month,
                other: null,
                count: null,
                countTwice: false,
                _ => (
                    Column(build.Span, month, CalendarColumn.Year),
                    Compute(build.Span, BoundType.Numeric, UmAlQuraArithmetic.FirstMonth)));
        }

        /// <summary>A date moved into another month, keeping its day where the month is long enough.</summary>
        /// <param name="span">Where the call sits in the input.</param>
        /// <param name="type">The result's type in the language.</param>
        /// <param name="from">The date and the month it falls in.</param>
        /// <param name="target">The month it is moved into.</param>
        /// <returns>The moved date, with the time of day the date had.</returns>
        private static PlanTemplateValue Shifted(QueryexSpan span, BoundType type, DateLookup from, CalendarLookup target)
        {
            return Compute(
                span,
                type,
                UmAlQuraArithmetic.Shift,
                Column(span, from.Lookup, CalendarColumn.StartDate),
                Column(span, target, CalendarColumn.StartDate),
                from.Day,
                Column(span, target, CalendarColumn.EndDate),
                from.Date);
        }

        /// <summary>Finds the month a date falls in.</summary>
        /// <param name="build">The form the lookup is made in.</param>
        /// <param name="operand">The date operand.</param>
        /// <returns>The lookup, with the date in the forms the answers read it in.</returns>
        /// <remarks>
        ///     The operand is lowered once and written as it is only where it is atomic; anything else
        ///     is given a name first. A date with a time of day is sought by its date, which is given
        ///     a name of its own, so the lookup compares a date with dates.
        /// </remarks>
        private DateLookup ByDate(CalendarBuild build, TypedExpr operand)
        {
            if (build.Levels is null && _dateLookups.TryGetValue(_scope, out List<DateLookupEntry>? known))
            {
                // The same operand under the same root, in the same sequence of sources, is the
                // same month, whichever call asked for it first.
                foreach (DateLookupEntry entry in known)
                {
                    if (ReferenceEquals(entry.Base, _base) && TypedEquivalence.AreEquivalent(entry.Operand, operand))
                    {
                        // The call compares through the calendar code the shared lookup already
                        // binds, rather than binding it again.
                        build.Calendar ??= entry.Found.Lookup.Calendar;
                        return entry.Found;
                    }
                }
            }

            List<ValueBinding> named = [];
            PlanValue date = LowerValueAgainst(operand, null);
            if (!date.IsAtomic)
            {
                date = Name(build, date, named);
            }

            PlanValue day = date.Type == BoundType.Date
                ? date
                : Name(build, Compute(date.Span, BoundType.Date, UmAlQuraArithmetic.ToDate, date), named);

            // The calendar code's parameter follows the first operand's, in the order they are
            // written.
            build.Calendar ??= CalendarCode(build.Span, build.Code);
            CalendarLookup lookup = new(build.Source, build.Calendar, day);
            DateLookup found = new(lookup, date, day);
            Place(build, lookup, named);

            if (build.Levels is null)
            {
                if (!_dateLookups.TryGetValue(_scope, out List<DateLookupEntry>? entries))
                {
                    entries = [];
                    _dateLookups.Add(_scope, entries);
                }

                entries.Add(new DateLookupEntry(_base, operand, found));
            }

            return found;
        }

        /// <summary>Finds a month from another month's year and number.</summary>
        /// <param name="build">The form the lookup is made in.</param>
        /// <param name="step">How the month is found.</param>
        /// <param name="from">The month it is found from.</param>
        /// <param name="other">A second month it reads, when it reads one.</param>
        /// <param name="count">The count of units, when the step takes one.</param>
        /// <param name="countTwice">Whether the count is written twice, and so has to be atomic.</param>
        /// <param name="place">Writes the year and the month sought, from the lowered count.</param>
        /// <returns>The lookup.</returns>
        private CalendarLookup ByMonth(
            CalendarBuild build,
            MonthStep step,
            CalendarLookup from,
            CalendarLookup? other,
            TypedExpr? count,
            bool countTwice,
            Func<PlanValue?, (PlanValue Year, PlanValue Month)> place)
        {
            if (build.Levels is null)
            {
                foreach (MonthLookupEntry entry in _monthLookups)
                {
                    if (ReferenceEquals(entry.From, from)
                        && ReferenceEquals(entry.Other, other)
                        && entry.Step == step
                        && ReferenceEquals(entry.Base, _base)
                        && (count is null ? entry.Count is null : TypedEquivalence.AreEquivalent(entry.Count, count)))
                    {
                        return entry.Found;
                    }
                }
            }

            List<ValueBinding> named = [];
            PlanValue? lowered = null;
            if (count is not null)
            {
                lowered = LowerValueAgainst(count, null);
                if (countTwice && !lowered.IsAtomic)
                {
                    lowered = Name(build, lowered, named);
                }
            }

            // A month found from another is in the same calendar, compared through the same code.
            (PlanValue year, PlanValue month) = place(lowered);
            CalendarLookup lookup = new(build.Source, from.Calendar, year, month);
            Place(build, lookup, named);

            if (build.Levels is null)
            {
                _monthLookups.Add(new MonthLookupEntry(from, other, step, _base, count, lookup));
            }

            return lookup;
        }

        /// <summary>Gives a value a name where the lookup that reads it will be written.</summary>
        /// <param name="build">The form the lookup is made in.</param>
        /// <param name="value">The value.</param>
        /// <param name="named">The names the lookup being made carries, in the scalar form.</param>
        /// <returns>A reference to the name.</returns>
        /// <remarks>
        ///     In the lateral form, beside the statement's other sources, ahead of the lookup that
        ///     reads it. In the scalar form, inside the level being made, which every level nested in
        ///     it and the result can read too.
        /// </remarks>
        private PlanBindingRef Name(CalendarBuild build, PlanValue value, List<ValueBinding> named)
        {
            if (build.Levels is null)
            {
                return Bind(value, _scope);
            }

            ValueBinding binding = new(value);
            named.Add(binding);
            return new PlanBindingRef(value.Span, binding);
        }

        /// <summary>Records a lookup where the form writes it.</summary>
        /// <param name="build">The form the lookup is made in.</param>
        /// <param name="lookup">The lookup.</param>
        /// <param name="named">The names it carries, in the scalar form.</param>
        private void Place(CalendarBuild build, CalendarLookup lookup, List<ValueBinding> named)
        {
            if (build.Levels is null)
            {
                _scope.Add(lookup);
                return;
            }

            lookup.Bindings.AddRange(named);
            build.Levels.Add(lookup);
        }

        /// <summary>The parameter a calendar code is compared through.</summary>
        /// <param name="span">Where the call sits in the input.</param>
        /// <param name="code">The code, as the table holds it.</param>
        /// <returns>The parameter.</returns>
        /// <remarks>
        ///     Bound like a literal compared against the table's narrow code column, so the backend
        ///     compares it without converting the column.
        /// </remarks>
        private PlanParameterRef CalendarCode(QueryexSpan span, string code)
        {
            QueryexStoreType store = StoreTypes.Adopted(BoundType.String, code, 0, 0, QueryexStoreType.QxVarChar(2));
            return new PlanParameterRef(
                span,
                Slot(BoundType.String, store, QueryexParameterOrigin.Literal, code, null, span),
                Here(span));
        }

        /// <summary>A column of a lookup.</summary>
        /// <param name="span">Where the call it serves sits in the input.</param>
        /// <param name="lookup">The lookup.</param>
        /// <param name="column">The column.</param>
        /// <returns>The column.</returns>
        private static PlanLookupColumn Column(QueryexSpan span, CalendarLookup lookup, CalendarColumn column)
        {
            return new PlanLookupColumn(span, lookup, column);
        }

        /// <summary>A value computed by one of the table calendars' patterns.</summary>
        /// <param name="span">Where the call it serves sits in the input.</param>
        /// <param name="type">The value's type in the language.</param>
        /// <param name="template">The pattern.</param>
        /// <param name="operands">The operands, all atomic or computed from atomic ones.</param>
        /// <returns>The value.</returns>
        private static PlanTemplateValue Compute(QueryexSpan span, BoundType type, EmitTemplate template, params PlanNode[] operands)
        {
            ImmutableArray<PlanNode> lowered = [.. operands];
            return new PlanTemplateValue(span, type, ResultOf(template, type, lowered), template, lowered);
        }
    }
}
