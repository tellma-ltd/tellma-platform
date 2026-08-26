// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data.SqlTypes;
using System.Globalization;
using Tellma.Core.Queryex;
using Tellma.Core.Queryex.Binding;

namespace Tellma.Queryex.Testing.Semantics
{
    /// <summary>The function library, read a second time.</summary>
    /// <remarks>
    ///     Written from what each function is supposed to mean, not from the SQL that implements it.
    ///     Two of the backend's habits are reproduced deliberately rather than idealised, because
    ///     the language adopts them: differences between dates count boundaries crossed rather than
    ///     time elapsed, and adding a fractional number of units drops the fraction.
    /// </remarks>
    internal sealed partial class Interpreter
    {
        /// <summary>The day the calendar arithmetic counts from, which is a Monday.</summary>
        private static readonly DateOnly Epoch = new(1, 1, 1);

        /// <summary>The rows an aggregation reads, when one is being evaluated.</summary>
        internal IReadOnlyList<LedgerRow>? Group { get; set; }

        /// <summary>Every function this reading covers, for the completeness check.</summary>
        internal static IReadOnlySet<string> Covered { get; } = new HashSet<string>(
            [
                "year", "quarter", "month", "day", "week", "weekday",
                "hour", "minute", "second",
                "startOfYear", "startOfMonth", "startOfWeek", "startOfDay",
                "addYears", "addMonths", "addDays", "addHours", "addMinutes", "addSeconds",
                "diffYears", "diffMonths", "diffDays", "diffHours", "diffMinutes", "diffSeconds",
                "local", "today", "now", "me",
                "if", "coalesce", "cast",
                "abs", "floor", "ceiling", "round",
                "length", "trim", "upper", "lower", "left", "right", "substring", "replace",
                "contains", "startsWith", "endsWith",
                "descendantOf", "ancestorOf",
                "sum", "avg", "min", "max", "count",
            ],
            StringComparer.OrdinalIgnoreCase);

        /// <summary>Evaluates one call.</summary>
        /// <param name="call">The call.</param>
        /// <returns>Its value.</returns>
        /// <exception cref="NotSupportedException">
        ///     The registry declares a function this reading does not cover, which a dedicated check
        ///     turns into a build failure rather than a surprise at run time.
        /// </exception>
        private QxValue Call(TypedCall call)
        {
            string name = call.Definition.Name;
            QueryexType result = Named(call.Type);

            return name.ToUpperInvariant() switch
            {
                "TODAY" => QxValue.Date(_context.Today),
                "NOW" => QxValue.Instant(_context.Now),
                "ME" => _context.UserId is int user
                    ? QxValue.Number(user)
                    : QxValue.Absent(QueryexType.QxNumeric),
                "IF" => Truth(call.Arguments[0])
                    ? Evaluate(call.Arguments[1])
                    : Evaluate(call.Arguments[2]),
                "COALESCE" => FirstPresent(call, result),
                "CAST" => Convert(Evaluate(call.Arguments[0]), result),
                "SUM" or "AVG" or "MIN" or "MAX" or "COUNT" => Aggregate(call, name, result),
                "DESCENDANTOF" => QxValue.Flag(Related(call, descending: true)),
                "ANCESTOROF" => QxValue.Flag(Related(call, descending: false)),
                _ => Scalar(call, name, result),
            };
        }

        /// <summary>Evaluates a call whose arguments all have to be present.</summary>
        /// <param name="call">The call.</param>
        /// <param name="name">The function's name.</param>
        /// <param name="result">The result type.</param>
        /// <returns>Its value.</returns>
        private QxValue Scalar(TypedCall call, string name, QueryexType result)
        {
            var arguments = new QxValue[call.Arguments.Length];
            bool absent = false;
            for (int index = 0; index < arguments.Length; index++)
            {
                arguments[index] = Evaluate(call.Arguments[index]);
                absent |= arguments[index].IsAbsent;
            }

            if (absent)
            {
                // Every function left here yields nothing when anything it was given is missing,
                // except the matching predicates, which answer no rather than nothing.
                return name is "contains" or "startsWith" or "endsWith"
                    ? QxValue.Flag(false)
                    : QxValue.Absent(result);
            }

            return name.ToUpperInvariant() switch
            {
                "YEAR" => QxValue.Number(Calendar(arguments[0]).Year),
                "QUARTER" => QxValue.Number(((Calendar(arguments[0]).Month - 1) / 3) + 1),
                "MONTH" => QxValue.Number(Calendar(arguments[0]).Month),
                "DAY" => QxValue.Number(Calendar(arguments[0]).Day),
                "WEEK" => QxValue.Number(IsoWeek(Calendar(arguments[0]))),
                "WEEKDAY" => QxValue.Number(((Calendar(arguments[0]).DayNumber - Epoch.DayNumber) % 7) + 1),
                "HOUR" => QxValue.Number(TimeOf(arguments[0]).Hour),
                "MINUTE" => QxValue.Number(TimeOf(arguments[0]).Minute),
                "SECOND" => QxValue.Number(TimeOf(arguments[0]).Second),
                "STARTOFYEAR" => QxValue.Date(new DateOnly(Calendar(arguments[0]).Year, 1, 1)),
                "STARTOFMONTH" => QxValue.Date(
                    new DateOnly(Calendar(arguments[0]).Year, Calendar(arguments[0]).Month, 1)),
                "STARTOFWEEK" => QxValue.Date(StartOfWeek(Calendar(arguments[0]))),
                "STARTOFDAY" => QxValue.Date(Calendar(arguments[0])),
                "ADDYEARS" => Shifted(arguments[0], Whole(arguments[1]), years: true),
                "ADDMONTHS" => Shifted(arguments[0], Whole(arguments[1]), years: false),
                "ADDDAYS" => Elapsed(arguments[0], TimeSpan.FromDays(Whole(arguments[1]))),
                "ADDHOURS" => Elapsed(arguments[0], TimeSpan.FromHours(Whole(arguments[1]))),
                "ADDMINUTES" => Elapsed(arguments[0], TimeSpan.FromMinutes(Whole(arguments[1]))),
                "ADDSECONDS" => Elapsed(arguments[0], TimeSpan.FromSeconds(Whole(arguments[1]))),
                "DIFFYEARS" => QxValue.Number(CompletedUnits(arguments[0], arguments[1], years: true)),
                "DIFFMONTHS" => QxValue.Number(CompletedUnits(arguments[0], arguments[1], years: false)),
                "DIFFDAYS" => QxValue.Number(Quotient(Boundaries(arguments[0], arguments[1], 3600), 24)),
                "DIFFHOURS" => QxValue.Number(Quotient(Boundaries(arguments[0], arguments[1], 60), 60)),
                "DIFFMINUTES" => QxValue.Number(Quotient(Boundaries(arguments[0], arguments[1], 1), 60)),
                "DIFFSECONDS" => QxValue.Number(Boundaries(arguments[0], arguments[1], 1)),
                "LOCAL" => QxValue.Moment(Local(arguments[0], call)),
                "ABS" => QxValue.Number(SqlDecimal.Abs(arguments[0].AsNumber)),
                "FLOOR" => QxValue.Number(SqlDecimal.Floor(arguments[0].AsNumber)),
                "CEILING" => QxValue.Number(SqlDecimal.Ceiling(arguments[0].AsNumber)),
                "ROUND" => QxValue.Number(SqlDecimal.Round(arguments[0].AsNumber, Whole(arguments[1]))),
                "LENGTH" => QxValue.Number(Collation.Length(arguments[0].AsText)),
                "TRIM" => QxValue.Text(arguments[0].AsText.Trim(' ')),
                "UPPER" => QxValue.Text(arguments[0].AsText.ToUpperInvariant()),
                "LOWER" => QxValue.Text(arguments[0].AsText.ToLowerInvariant()),
                "LEFT" => QxValue.Text(Left(arguments[0].AsText, Whole(arguments[1]))),
                "RIGHT" => QxValue.Text(Right(arguments[0].AsText, Whole(arguments[1]))),
                "SUBSTRING" => QxValue.Text(Substring(arguments)),
                "REPLACE" => QxValue.Text(Replace(arguments[0].AsText, arguments[1].AsText, arguments[2].AsText)),
                "CONTAINS" => QxValue.Flag(Collation.IndexOf(arguments[0].AsText, arguments[1].AsText) > 0),
                "STARTSWITH" => QxValue.Flag(Collation.IndexOf(arguments[0].AsText, arguments[1].AsText) == 1),
                "ENDSWITH" => QxValue.Flag(EndsWith(arguments[0].AsText, arguments[1].AsText)),
                _ => throw new NotSupportedException(
                    "The reference reading of the language does not cover '" + name + "'."),
            };
        }

        /// <summary>The first argument that has a value.</summary>
        /// <param name="call">The call.</param>
        /// <param name="result">The result type.</param>
        /// <returns>Its value, or absence when none has one.</returns>
        private QxValue FirstPresent(TypedCall call, QueryexType result)
        {
            var answer = QxValue.Absent(result);
            foreach (TypedExpr argument in call.Arguments)
            {
                // Every argument is still evaluated, so a claim about one after the first present
                // value is checkable even though its value cannot be the answer.
                QxValue candidate = Evaluate(argument);
                if (answer.IsAbsent && !candidate.IsAbsent)
                {
                    answer = candidate;
                }
            }

            return answer;
        }

        /// <summary>The calendar date a value sits on.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The date.</returns>
        private static DateOnly Calendar(QxValue value)
        {
            return value.Type == QueryexType.QxDate ? value.AsDate : DateOnly.FromDateTime(value.AsMoment);
        }

        /// <summary>The time of day a value sits at.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The time.</returns>
        private static TimeOnly TimeOf(QxValue value)
        {
            return value.Type == QueryexType.QxDate ? default : TimeOnly.FromDateTime(value.AsMoment);
        }

        /// <summary>The Monday of the week a date falls in.</summary>
        /// <param name="date">The date.</param>
        /// <returns>The Monday.</returns>
        private static DateOnly StartOfWeek(DateOnly date)
        {
            int weeks = (date.DayNumber - Epoch.DayNumber) / 7;
            return Epoch.AddDays(weeks * 7);
        }

        /// <summary>The week of the year a date falls in, counted the international way.</summary>
        /// <param name="date">The date.</param>
        /// <returns>The week number.</returns>
        private static int IsoWeek(DateOnly date)
        {
            return ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue));
        }

        /// <summary>A number read as a count of whole units.</summary>
        /// <param name="value">The number.</param>
        /// <returns>The count, with any fraction dropped.</returns>
        /// <remarks>Toward zero, which is what the backend does and therefore what the language does.</remarks>
        private static int Whole(QxValue value)
        {
            return (int)SqlDecimal.Truncate(value.AsNumber, 0).Value;
        }

        /// <summary>Moves a calendar value by whole years or months.</summary>
        /// <param name="value">The value.</param>
        /// <param name="count">How many units.</param>
        /// <param name="years">Whether the unit is a year.</param>
        /// <returns>The moved value.</returns>
        private static QxValue Shifted(QxValue value, int count, bool years)
        {
            if (value.Type == QueryexType.QxDate)
            {
                DateOnly date = value.AsDate;
                return QxValue.Date(years ? date.AddYears(count) : date.AddMonths(count));
            }

            DateTime moment = value.AsMoment;
            return QxValue.Moment(years ? moment.AddYears(count) : moment.AddMonths(count));
        }

        /// <summary>Moves an instant by an elapsed amount.</summary>
        /// <param name="value">The value.</param>
        /// <param name="amount">How much.</param>
        /// <returns>The moved value.</returns>
        private static QxValue Elapsed(QxValue value, TimeSpan amount)
        {
            return value.Type switch
            {
                QueryexType.QxDate => QxValue.Date(value.AsDate.AddDays((int)amount.TotalDays)),
                QueryexType.QxDateTime => QxValue.Moment(value.AsMoment.Add(amount)),
                QueryexType.QxDateTimeOffset or QueryexType.QxBool or QueryexType.QxNumeric
                    or QueryexType.QxString or QueryexType.QxGuid or QueryexType.QxHierarchyId
                    or QueryexType.QxGeography => QxValue.Instant(value.AsInstant.Add(amount)),
                _ => QxValue.Instant(value.AsInstant.Add(amount)),
            };
        }

        /// <summary>How many whole years or months separate two calendar values.</summary>
        /// <param name="from">The earlier value.</param>
        /// <param name="to">The later value.</param>
        /// <param name="years">Whether the unit is a year.</param>
        /// <returns>The count, which is negative when the second value is the earlier one.</returns>
        /// <remarks>
        ///     Whole units, not boundaries crossed: the last day of one year to the first of the next
        ///     is no years at all, and reading it as one would make an age wrong for everybody who
        ///     has not had their birthday yet.
        /// </remarks>
        private static int CompletedUnits(QxValue from, QxValue to, bool years)
        {
            DateTime start = AsMoment(from);
            DateTime end = AsMoment(to);
            int crossings = years
                ? end.Year - start.Year
                : ((end.Year - start.Year) * 12) + end.Month - start.Month;

            DateTime reached = years ? start.AddYears(crossings) : start.AddMonths(crossings);
            return crossings switch
            {
                > 0 when reached > end => crossings - 1,
                < 0 when reached < end => crossings + 1,
                _ => crossings,
            };
        }

        /// <summary>How many boundaries of a given size two values are apart.</summary>
        /// <param name="from">The earlier value.</param>
        /// <param name="to">The later value.</param>
        /// <param name="seconds">How many seconds one boundary spans.</param>
        /// <returns>The count.</returns>
        /// <remarks>
        ///     Boundaries crossed rather than time elapsed, which is the backend's rule: two values
        ///     a minute apart across the hour mark are one hour apart by this count.
        /// </remarks>
        private static long Boundaries(QxValue from, QxValue to, int seconds)
        {
            long start = Seconds(from) / seconds;
            long end = Seconds(to) / seconds;
            return end - start;
        }

        /// <summary>A value as a whole number of seconds since the epoch.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The count, rounded down.</returns>
        private static long Seconds(QxValue value)
        {
            DateTime moment = AsMoment(value);
            return (long)Math.Floor((moment - DateTime.MinValue).TotalSeconds);
        }

        /// <summary>A calendar or instant value as a moment, for the counting functions.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The moment.</returns>
        private static DateTime AsMoment(QxValue value)
        {
            return value.Type switch
            {
                QueryexType.QxDate => value.AsDate.ToDateTime(TimeOnly.MinValue),
                QueryexType.QxDateTime => value.AsMoment,
                QueryexType.QxDateTimeOffset or QueryexType.QxBool or QueryexType.QxNumeric
                    or QueryexType.QxString or QueryexType.QxGuid or QueryexType.QxHierarchyId
                    or QueryexType.QxGeography => value.AsInstant.UtcDateTime,
                _ => value.AsInstant.UtcDateTime,
            };
        }

        /// <summary>Divides a count of subunits into units, keeping the fraction.</summary>
        /// <param name="count">The count.</param>
        /// <param name="per">How many subunits make a unit.</param>
        /// <returns>The quotient.</returns>
        private static SqlDecimal Quotient(long count, int per)
        {
            var widened = SqlDecimal.ConvertToPrecScale(new SqlDecimal(count), 26, 6);
            return widened / new SqlDecimal(per);
        }

        /// <summary>Reads an instant in a named zone.</summary>
        /// <param name="value">The instant.</param>
        /// <param name="call">The call, which may name the zone.</param>
        /// <returns>The local date and time.</returns>
        private DateTime Local(QxValue value, TypedCall call)
        {
            TimeZoneInfo zone = _context.TimeZone;
            if (call.Arguments.Length > 1 && call.Selectors.Length > 1 && call.Selectors[1] is string named)
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(named);
            }

            return TimeZoneInfo.ConvertTime(value.AsInstant, zone).DateTime;
        }

        /// <summary>The leftmost characters of a value.</summary>
        /// <param name="text">The value.</param>
        /// <param name="count">How many.</param>
        /// <returns>The result.</returns>
        private static string Left(string text, int count)
        {
            return count <= 0 ? string.Empty : text[..Math.Min(count, text.Length)];
        }

        /// <summary>The rightmost characters of a value.</summary>
        /// <param name="text">The value.</param>
        /// <param name="count">How many.</param>
        /// <returns>The result.</returns>
        private static string Right(string text, int count)
        {
            return count <= 0 ? string.Empty : text[Math.Max(0, text.Length - count)..];
        }

        /// <summary>Part of a value, counting from one.</summary>
        /// <param name="arguments">The value, the starting position, and optionally the length.</param>
        /// <returns>The result.</returns>
        private static string Substring(QxValue[] arguments)
        {
            string text = arguments[0].AsText;
            int start = Whole(arguments[1]);
            int length = arguments.Length > 2 ? Whole(arguments[2]) : int.MaxValue;

            // A start before the beginning consumes part of the requested length, which is what the
            // backend does with it.
            if (start < 1)
            {
                length = length == int.MaxValue ? int.MaxValue : length + start - 1;
                start = 1;
            }

            if (length <= 0 || start > text.Length)
            {
                return string.Empty;
            }

            int available = text.Length - start + 1;
            return text.Substring(start - 1, Math.Min(length, available));
        }

        /// <summary>Every occurrence of one value inside another, replaced.</summary>
        /// <param name="text">The value searched.</param>
        /// <param name="needle">The value replaced.</param>
        /// <param name="replacement">What replaces it.</param>
        /// <returns>The result.</returns>
        private static string Replace(string text, string needle, string replacement)
        {
            if (needle.Length == 0)
            {
                return text;
            }

            var result = new System.Text.StringBuilder();
            int index = 0;
            while (index < text.Length)
            {
                int found = Collation.IndexOf(text[index..], needle);
                if (found == 0)
                {
                    result.Append(text, index, text.Length - index);
                    break;
                }

                result.Append(text, index, found - 1).Append(replacement);
                index += found - 1 + needle.Length;
            }

            return result.ToString();
        }

        /// <summary>Whether a value ends with another.</summary>
        /// <param name="text">The value searched.</param>
        /// <param name="needle">The value looked for.</param>
        /// <returns>True when it does.</returns>
        /// <remarks>
        ///     Read as "occurs at the start when both are reversed", exactly as it is emitted, which
        ///     is what makes a trailing space part of what has to match.
        /// </remarks>
        private static bool EndsWith(string text, string needle)
        {
            char[] one = text.ToCharArray();
            char[] other = needle.ToCharArray();
            Array.Reverse(one);
            Array.Reverse(other);
            return Collation.IndexOf(new string(one), new string(other)) == 1;
        }

        /// <summary>Whether a row is at, above, or below one of a set of keyed rows.</summary>
        /// <param name="call">The call.</param>
        /// <param name="descending">Whether the test looks downward.</param>
        /// <returns>True when it is.</returns>
        private bool Related(TypedCall call, bool descending)
        {
            if (call.Arguments[0] is not TypedPath keyPath)
            {
                return false;
            }

            LedgerRow? current = _row;
            foreach (NavigationDescriptor navigation in keyPath.Navigations)
            {
                current = LedgerData.Find(navigation.Target, current[navigation.ForeignKey.Name]);
                if (current is null)
                {
                    return false;
                }
            }

            EntityDescriptor entity = current.Entity;
            if (entity.TreeNode is null)
            {
                return false;
            }

            QxValue node = current[entity.TreeNode.Name];
            if (node.IsAbsent)
            {
                return false;
            }

            for (int index = 1; index < call.Arguments.Length; index++)
            {
                QxValue key = Evaluate(call.Arguments[index]);
                LedgerRow? keyed = Keyed(entity, keyPath.Property, key);
                if (keyed is null)
                {
                    // A key that names no row contributes nothing, which is what keeps the answer
                    // false rather than universally true when nothing matches.
                    continue;
                }

                QxValue other = keyed[entity.TreeNode.Name];
                if (other.IsAbsent)
                {
                    continue;
                }

                bool related = descending
                    ? node.AsNode.StartsWith(other.AsNode, StringComparison.Ordinal)
                    : other.AsNode.StartsWith(node.AsNode, StringComparison.Ordinal);

                if (related)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The row of an entity whose property carries a given value.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="property">The property.</param>
        /// <param name="key">The value.</param>
        /// <returns>The row, or null.</returns>
        private static LedgerRow? Keyed(EntityDescriptor entity, PropertyDescriptor property, QxValue key)
        {
            if (key.IsAbsent)
            {
                return null;
            }

            foreach (LedgerRow row in LedgerData.Rows(entity))
            {
                QxValue candidate = row[property.Name];
                if (!candidate.IsAbsent && Order(candidate, key) == 0)
                {
                    return row;
                }
            }

            return null;
        }

        /// <summary>Evaluates an aggregation over the rows of the group.</summary>
        /// <param name="call">The call.</param>
        /// <param name="name">The function's name.</param>
        /// <param name="result">The result type.</param>
        /// <returns>Its value.</returns>
        private QxValue Aggregate(TypedCall call, string name, QueryexType result)
        {
            IReadOnlyList<LedgerRow> rows = Group ?? [_row];
            List<QxValue> values = [];
            long counted = 0;

            foreach (LedgerRow row in rows)
            {
                Interpreter reading = new(row, _context) { Group = null };
                if (call.Arguments.Length > 1 && !reading.Truth(call.Arguments[1]))
                {
                    continue;
                }

                if (call.Arguments.Length == 0)
                {
                    counted++;
                    continue;
                }

                QxValue value = reading.Evaluate(call.Arguments[0]);
                if (value.IsAbsent)
                {
                    continue;
                }

                counted++;
                values.Add(value);
            }

            if (string.Equals(name, "count", StringComparison.OrdinalIgnoreCase))
            {
                return QxValue.Number(counted);
            }

            if (values.Count == 0)
            {
                // Nothing to aggregate yields nothing, which is why a filtered aggregate over a group
                // that matched nothing is possibly absent however present its input is.
                return QxValue.Absent(result);
            }

            return name.ToUpperInvariant() switch
            {
                "SUM" => QxValue.Number(Total(values)),
                "AVG" => QxValue.Number(Average(values)),
                "MIN" => Extreme(values, least: true),
                "MAX" => Extreme(values, least: false),
                _ => QxValue.Absent(result),
            };
        }

        /// <summary>The total of a set of numbers.</summary>
        /// <param name="values">The numbers.</param>
        /// <returns>The total.</returns>
        private static SqlDecimal Total(List<QxValue> values)
        {
            SqlDecimal total = new(0);
            foreach (QxValue value in values)
            {
                total += value.AsNumber;
            }

            return total;
        }

        /// <summary>The average of a set of numbers.</summary>
        /// <param name="values">The numbers.</param>
        /// <returns>The average.</returns>
        private static SqlDecimal Average(List<QxValue> values)
        {
            var total = SqlDecimal.ConvertToPrecScale(
                Total(values),
                38,
                Math.Max(6, (int)values[0].AsNumber.Scale));

            return total / new SqlDecimal(values.Count);
        }

        /// <summary>The least or greatest of a set of values.</summary>
        /// <param name="values">The values.</param>
        /// <param name="least">Whether the least is wanted.</param>
        /// <returns>The value.</returns>
        private static QxValue Extreme(List<QxValue> values, bool least)
        {
            QxValue chosen = values[0];
            foreach (QxValue value in values)
            {
                int order = Order(value, chosen);
                if (least ? order < 0 : order > 0)
                {
                    chosen = value;
                }
            }

            return chosen;
        }

        /// <summary>Converts a value to another type.</summary>
        /// <param name="value">The value.</param>
        /// <param name="target">The target type.</param>
        /// <returns>The converted value.</returns>
        private static QxValue Convert(QxValue value, QueryexType target)
        {
            return value.IsAbsent || value.Type == target
                ? (value.IsAbsent ? QxValue.Absent(target) : value)
                : Converted(value, target);
        }

        /// <summary>Converts a present value to a different type.</summary>
        /// <param name="value">The value.</param>
        /// <param name="target">The target type.</param>
        /// <returns>The converted value.</returns>
        private static QxValue Converted(QxValue value, QueryexType target)
        {
            return target switch
            {
                QueryexType.QxString => QxValue.Text(Written(value)),
                QueryexType.QxNumeric => value.Type == QueryexType.QxBool
                    ? QxValue.Number(value.AsFlag ? 1 : 0)
                    : QxValue.Number(SqlDecimal.ConvertToPrecScale(SqlDecimal.Parse(value.AsText), 38, 6)),
                QueryexType.QxBool => QxValue.Flag((value.AsNumber != 0).IsTrue),
                QueryexType.QxGuid => QxValue.Identifier(Guid.Parse(value.AsText, CultureInfo.InvariantCulture)),
                QueryexType.QxDate => QxValue.Date(AsDate(value)),
                QueryexType.QxDateTime => QxValue.Moment(AsMoment(value)),
                QueryexType.QxDateTimeOffset => QxValue.Instant(
                    DateTimeOffset.Parse(value.AsText, CultureInfo.InvariantCulture)),
                QueryexType.QxHierarchyId or QueryexType.QxGeography => QxValue.Absent(target),
                _ => QxValue.Absent(target),
            };
        }

        /// <summary>A value read as a date.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The date.</returns>
        private static DateOnly AsDate(QxValue value)
        {
            return value.Type switch
            {
                QueryexType.QxString => DateOnly.ParseExact(value.AsText, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                QueryexType.QxDateTime => DateOnly.FromDateTime(value.AsMoment),
                QueryexType.QxDateTimeOffset => DateOnly.FromDateTime(value.AsInstant.DateTime),
                QueryexType.QxDate or QueryexType.QxBool or QueryexType.QxNumeric
                    or QueryexType.QxGuid or QueryexType.QxHierarchyId
                    or QueryexType.QxGeography => value.AsDate,
                _ => value.AsDate,
            };
        }

        /// <summary>A value written out as text, the way the language renders it.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The written form.</returns>
        private static string Written(QxValue value)
        {
            return value.Type switch
            {
                QueryexType.QxNumeric => value.AsNumber.ToString(),
                QueryexType.QxBool => value.AsFlag ? "true" : "false",
                QueryexType.QxGuid => value.AsIdentifier.ToString("D", CultureInfo.InvariantCulture),
                QueryexType.QxDate => value.AsDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                QueryexType.QxDateTime => Rendered(value.AsMoment),
                QueryexType.QxDateTimeOffset => Rendered(value.AsInstant.DateTime)
                    + value.AsInstant.Offset.ToString("hh\\:mm", CultureInfo.InvariantCulture)
                        .Insert(0, value.AsInstant.Offset < TimeSpan.Zero ? "-" : "+"),
                QueryexType.QxString or QueryexType.QxHierarchyId
                    or QueryexType.QxGeography => value.AsText,
                _ => value.AsText,
            };
        }

        /// <summary>A moment written the international way, without needless fractional digits.</summary>
        /// <param name="moment">The moment.</param>
        /// <returns>The written form.</returns>
        private static string Rendered(DateTime moment)
        {
            string written = moment.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture);
            written = written.TrimEnd('0');
            return written.EndsWith('.') ? written[..^1] : written;
        }
    }
}
