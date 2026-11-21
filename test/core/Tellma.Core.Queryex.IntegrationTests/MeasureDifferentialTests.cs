// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Collections.ObjectModel;
using System.Data.Common;
using System.Data.SqlTypes;
using System.Globalization;
using Tellma.Queryex.Testing.Probe;
using Tellma.Queryex.Testing.Schema;
using Tellma.Queryex.Testing.Semantics;

namespace Tellma.Core.Queryex.IntegrationTests
{
    /// <summary>
    ///     Evaluates measures over the partials the server returns and holds every result to the
    ///     server's own value for the same measure, digit for digit and at the same type.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Each measure is checked four ways. Per leaf group, the measure over that group's
    ///         partials is the value the server computed beside them. Per branch, the measure over
    ///         the branch's leaves combined is the value the server computes grouping by the branch.
    ///         Over everything, the measure over every leaf combined is the value the server computes
    ///         with no grouping at all. And over nothing, the measure over a cell that combines no
    ///         row is the value the server computes with no grouping over a filter no row passes.
    ///     </para>
    ///     <para>
    ///         Every partial the evaluator reads as a whole number is planned at the width of the
    ///         column the server returns, since that is the buffer a reader hands over.
    ///     </para>
    ///     <para>
    ///         Where the server fails a statement, the evaluation fails too, with the code the
    ///         server's error corresponds to: a division by zero for 8134 and an overflow for 8115.
    ///     </para>
    /// </remarks>
    /// <param name="server">The server the fixture put the rows on.</param>
    [Collection(SqlServerGroup.Name)]
    [Trait("Category", "Integration")]
    public sealed class MeasureDifferentialTests(LedgerDatabase server)
    {
        /// <summary>How many measures are generated.</summary>
        private const int GeneratedCount = 500;

        /// <summary>The seed the generated measures come from, so every run checks the same ones.</summary>
        private const int GeneratorSeed = 20_261_121;

        /// <summary>The backend's error for a division by zero.</summary>
        private const int DivideByZeroError = 8134;

        /// <summary>The backend's error for an arithmetic overflow.</summary>
        private const int OverflowError = 8115;

        /// <summary>Where the leaf statement holds the measure: after the branch and the leaf.</summary>
        private const int LeafMeasureColumn = 2;

        /// <summary>The engine under test.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>A row filter no fact row passes, for the grand total over nothing.</summary>
        private static readonly FilterTree NoRow = FilterTree.Leaf("Leaf < 0");

        /// <summary>The value the declared parameter is given.</summary>
        private static readonly decimal ParameterValue = 2.5m;

        /// <summary>The parameter a measure may read.</summary>
        private static readonly QueryexParameterDeclaration[] Parameters =
            [new QueryexParameterDeclaration("P", QueryexType.QxNumeric, IsNotNull: false)];

        /// <summary>What the statements' slots are bound with.</summary>
        private static readonly InterpreterContext Context = InterpreterContext.Fixed with
        {
            Parameters = new Dictionary<string, QxValue>(StringComparer.OrdinalIgnoreCase)
            {
                ["P"] = QxValue.Number(new SqlDecimal(ParameterValue)),
            },
        };

        /// <summary>The server the fixture put the rows on.</summary>
        private readonly LedgerDatabase _server = server;

        /// <summary>Measures written by hand, one or more for each rule of the evaluator.</summary>
        /// <returns>The measures.</returns>
        public static TheoryData<string> HandWritten()
        {
            return
            [
                // Partials, bare and combined.
                "sum(Amount)", "count()", "avg(Amount)", "avg(Qty)", "avg(Fine)", "avg(Tiny)", "max(Whole)",
                "sum(Amount - Cost) / sum(Amount)", "sum(Amount, Flag) / count(Label, Flag)", "sum(Small) / count()",

                // The type algebra: whole widths, scale collapse, literal scales and rounding.
                "max(Qty) * max(Qty)", "max(Tiny) + max(Tiny)", "max(Whole) * max(Whole)", "-min(Tiny)", "abs(min(Small))",
                "if(count() > 10, max(Qty), max(Big))", "min(Big) * 3 - max(Big)", "sum(Qty) + sum(Amount)",
                "sum(Qty) * 1.0000 + sum(Amount)", "sum(Rate) - sum(Amount)", "sum(Amount) * 0.5", "sum(Amount) * 1.25",
                "(sum(Amount) / sum(Cost)) * 100", "100 * sum(Amount) / sum(Cost)", "sum(Fine) * 100", "sum(Fine) * sum(Fine)",
                "if(count() > 0, sum(Amount) / sum(Cost), sum(Amount))", "coalesce(sum(Cost), sum(Amount))",
                "round(avg(Amount), 2)", "round(sum(Fine), 0)", "floor(avg(Rate))", "ceiling(sum(Fine))", "floor(max(Tiny))",

                // Guards and errors.
                "if(sum(Cost) = 0, null, sum(Amount) / sum(Cost))", "if(min(Qty) < 0, -min(Qty), min(Qty))",
                "sum(Amount) / 0", "sum(Amount) / count(Cost)",

                // Dates, the parameter and the context slots.
                "year(max(Day)) * 100 + month(max(Day))", "quarter(min(Day))", "day(max(Due))", "diffDays(min(Day), max(Day))",
                "diffHours(min(Day), max(Due))", "diffMinutes(min(Due), max(Due))", "diffSeconds(min(Day), max(Day))",
                "diffDays(max(Day), today())", "sum(Amount) + @P", "count() * me()", "coalesce(max(Due), min(Day))",

                // Other types.
                "max(Label)", "min(Label)", "if(sum(Amount) > 100000, 'High', 'Low')", "max(Flag)", "min(Flag)",
                "sum(Amount) > 0 and count(Cost) > 3", "max(Instant) < now()", "max(Day) >= '2024-02-29'",
                "max(Qty) in (1000, 999, 998)", "sum(Cost) is null", "min(Stamp) < max(Stamp) or max(Flag) = false",

                // Text lengths, counted in the width the server counts them in.
                "max(length(Label))", "max(length(Label)) * max(Qty) * max(Qty)", "min(length(Label)) + max(Tiny)",

                // Absence over no row at all, which a grouped statement never sees.
                "if(sum(Amount) is null, 0, 1)", "max(Day) is null", "coalesce(sum(Amount), -1)",
            ];
        }

        /// <summary>Every identity holds for each measure written by hand.</summary>
        /// <param name="measure">The measure.</param>
        /// <returns>A task.</returns>
        [Theory]
        [MemberData(nameof(HandWritten))]
        public async Task HandWrittenMeasure_HoldsEveryIdentity(string measure)
        {
            Outcome outcome = await CheckAsync(measure);
            Assert.True(outcome.Failure is null, outcome.Failure);
        }

        /// <summary>Every identity holds for each generated measure.</summary>
        /// <returns>A task.</returns>
        [Fact]
        public async Task GeneratedMeasures_HoldEveryIdentity()
        {
            MeasureGenerator generator = new(GeneratorSeed);
            List<string> failures = [];
            int compared = 0;
            for (int index = 0; index < GeneratedCount; index++)
            {
                string measure = generator.Next();
                Outcome outcome = await CheckAsync(measure);
                if (outcome.Failure is not null)
                {
                    failures.Add(outcome.Failure);
                }
                else if (outcome.ComparedValues)
                {
                    compared++;
                }
            }

            Assert.True(
                failures.Count == 0,
                failures.Count.ToString(CultureInfo.InvariantCulture) + " failed:\n" + string.Join("\n", failures.Take(25)));

            // A measure that fails on the server everywhere compares only errors. Most must compare
            // values, or the check would pass on the error path alone.
            Assert.True(
                compared * 2 >= GeneratedCount,
                "Only " + compared.ToString(CultureInfo.InvariantCulture) + " measures compared values at every level.");
        }

        /// <summary>Checks the four identities for one measure.</summary>
        /// <param name="measure">The measure.</param>
        /// <returns>What the check found.</returns>
        private async Task<Outcome> CheckAsync(string measure)
        {
            if (!TryCompile(measure, "Branch, Leaf, " + measure, measures: true, null, out CompiledQuery? leaf, out string? failure)
                || !TryCompile(measure, "Branch, Leaf", measures: true, null, out CompiledQuery? partials, out failure)
                || !TryCompile(measure, "Branch, " + measure, measures: false, null, out CompiledQuery? branch, out failure)
                || !TryCompile(measure, measure, measures: false, null, out CompiledQuery? total, out failure)
                || !TryCompile(measure, measure, measures: false, NoRow, out CompiledQuery? empty, out failure))
            {
                return new Outcome(failure, false);
            }

            // The partials come from the statement that also computes the measure, unless the
            // measure fails on the server; then from the same statement without it.
            Result leafResult = await RunAsync(leaf!, LeafMeasureColumn);
            Result source = leafResult.Error is null ? leafResult : await RunAsync(partials!, -1);
            if (source.Error is not null)
            {
                return new Outcome(measure + ": the partials alone fail on the server with " + source.Error, false);
            }

            // The two statements list the same partials, each at its own columns, every whole one at
            // the width the server returns it in.
            CompiledMeasures measures = (leafResult.Error is null ? leaf! : partials!).Measures!;
            IReadOnlyList<string?> widths = QueryexProbe.PartialWholeTypes(measures);
            for (int partial = 0; partial < widths.Count; partial++)
            {
                string returned = source.Schema[measures.Partials[partial].Ordinal].DataTypeName!;
                if (widths[partial] is string planned && planned != returned)
                {
                    return new Outcome(
                        measure + ": partial " + partial.ToString(CultureInfo.InvariantCulture) + " is planned as " + planned + ", and the server returns " + returned + ".",
                        false);
                }
            }

            MeasureEvaluator evaluator = measures.Prepare(new MeasureContext(
                [.. measures.Partials.Select(partial => DecimalType(source.Schema[partial.Ordinal]))],
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["P"] = ParameterValue },
                Context.Today,
                Context.Now,
                Context.UserId));

            // One cell per leaf, loaded column by column from buffers shaped as a reader's.
            PartialTable table = evaluator.CreateTable(source.Rows.Count + 8);
            int[] leaves = [.. source.Rows.Select(_ => table.AddCell())];
            string? loading = Capture(() => Load(table, leaves, measures, source));
            if (loading is not null)
            {
                return new Outcome(measure + ": loading the partials failed with " + loading, false);
            }

            // The leaf identity, group by group.
            string? mismatch = Compare(
                measure + " (leaf)",
                evaluator,
                table,
                [.. leaves.Select((cell, row) => (
                    cell,
                    leafResult.Error is null ? leafResult.Rows[row].Measure : null,
                    Describe(source.Rows[row], measures)))],
                leafResult);

            // The rollup identity: the leaves combined by the branch they sit in.
            Result branchResult = await RunAsync(branch!, 1);
            Dictionary<int, int> branches = [];
            string? merging = Capture(() =>
            {
                for (int row = 0; row < source.Rows.Count; row++)
                {
                    int key = (int)source.Rows[row].Values[0]!;
                    if (!branches.TryGetValue(key, out int cell))
                    {
                        branches[key] = cell = table.AddCell();
                    }

                    table.Merge(cell, leaves[row]);
                }
            });

            mismatch ??= merging is not null
                ? Matches(measure + " (branch merge)", merging, branchResult)
                : Compare(
                    measure + " (branch)",
                    evaluator,
                    table,
                    branchResult.Error is null
                        ? [.. branchResult.Rows.Select(row => (branches[(int)row.Values[0]!], row.Measure, "branch " + row.Values[0]))]
                        : [.. branches.Values.Select(cell => (cell, (object?)null, "a branch"))],
                    branchResult);

            // The grand total: every leaf combined.
            Result totalResult = await RunAsync(total!, 0);
            int everything = table.AddCell();
            merging = Capture(() =>
            {
                foreach (int cell in leaves)
                {
                    table.Merge(everything, cell);
                }
            });

            mismatch ??= merging is not null
                ? Matches(measure + " (total merge)", merging, totalResult)
                : Compare(
                    measure + " (total)",
                    evaluator,
                    table,
                    [(everything, totalResult.Error is null ? Assert.Single(totalResult.Rows).Measure : null, "every row")],
                    totalResult);

            // Over nothing: a cell nothing is combined into, against the grand total of no row.
            Result emptyResult = await RunAsync(empty!, 0);
            mismatch ??= Compare(
                measure + " (no row)",
                evaluator,
                table,
                [(table.AddCell(), emptyResult.Error is null ? Assert.Single(emptyResult.Rows).Measure : null, "no row")],
                emptyResult);

            return new Outcome(
                mismatch,
                leafResult.Error is null && branchResult.Error is null && totalResult.Error is null && emptyResult.Error is null);
        }

        /// <summary>Compiles one of a measure's statements.</summary>
        /// <param name="measure">The measure.</param>
        /// <param name="select">The select list.</param>
        /// <param name="measures">Whether the statement carries the measure's partials.</param>
        /// <param name="filter">The row filter, or null for every row.</param>
        /// <param name="query">The compiled statement.</param>
        /// <param name="failure">Why it did not compile.</param>
        /// <returns>True when it compiled.</returns>
        private static bool TryCompile(string measure, string select, bool measures, FilterTree? filter, out CompiledQuery? query, out string? failure)
        {
            QueryexResult<CompiledQuery> result = Engine.CompileQuery(
                new QuerySpec
                {
                    Root = MeasureFixture.Fact,
                    Aggregate = true,
                    Select = select,
                    Filter = filter,
                    Measures = measures ? measure : null,
                },
                new QueryCompilationOptions
                {
                    QueryexVersion = QueryexLanguage.Version,
                    Schema = MeasureFixture.Schema,
                    Parameters = Parameters,
                });

            query = result.Succeeded ? result.Value : null;
            failure = result.Succeeded
                ? null
                : measure + ": does not compile: " + string.Join(", ", result.Diagnostics.Select(d => d.Code + "@" + d.Location));
            return result.Succeeded;
        }

        /// <summary>Runs one statement.</summary>
        /// <param name="query">The statement.</param>
        /// <param name="measureColumn">The column that holds the measure, or -1 for none.</param>
        /// <returns>The rows, or the server's error.</returns>
        private async Task<Result> RunAsync(CompiledQuery query, int measureColumn)
        {
            await using SqlConnection connection = await _server.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = query.Sql;
            SqlBinding.Bind(command, query, Context);

            bool sharesColumn = query.Measures?.Partials.Any(partial => partial.Ordinal == measureColumn) == true;
            try
            {
                await using SqlDataReader reader = await command.ExecuteReaderAsync();
                ReadOnlyCollection<DbColumn> schema = reader.GetColumnSchema();
                List<Row> rows = [];
                while (await reader.ReadAsync())
                {
                    // The measure is read as the server's own value, at its type; everything else
                    // as a reader's buffers hold it.
                    object?[] values = new object?[reader.FieldCount];
                    for (int column = 0; column < values.Length; column++)
                    {
                        values[column] = column == measureColumn || reader.IsDBNull(column) ? null : reader.GetValue(column);
                    }

                    // A bare measure is its own partial, so its column is a buffer's too; any other
                    // measure's value can lie past what a reader's decimal holds.
                    object? measure = measureColumn >= 0 ? reader.GetSqlValue(measureColumn) : null;
                    if (sharesColumn && !IsAbsent(measure))
                    {
                        values[measureColumn] = Clr(measure!);
                    }

                    rows.Add(new Row(values, measure));
                }

                return new Result(schema, rows, null) { MeasureColumn = measureColumn };
            }
            catch (SqlException exception)
            {
                return new Result([], [], exception.Number);
            }
        }

        /// <summary>Loads every leaf row's partials into its cell, through one buffer per column.</summary>
        /// <param name="table">The table.</param>
        /// <param name="cells">Each row's cell.</param>
        /// <param name="measures">The measures.</param>
        /// <param name="source">The rows.</param>
        private static void Load(PartialTable table, int[] cells, CompiledMeasures measures, Result source)
        {
            for (int partial = 0; partial < measures.Partials.Count; partial++)
            {
                MeasurePartial described = measures.Partials[partial];
                Type type = described.Type == QueryexType.QxDate
                    ? typeof(DateOnly)
                    : source.Schema[described.Ordinal].DataType!;
                var buffer = Array.CreateInstance(type, source.Rows.Count);
                for (int row = 0; row < source.Rows.Count; row++)
                {
                    object? value = source.Rows[row].Values[described.Ordinal];
                    buffer.SetValue(value is DateTime day && type == typeof(DateOnly) ? DateOnly.FromDateTime(day) : value, row);
                }

                for (int row = 0; row < source.Rows.Count; row++)
                {
                    if (source.Rows[row].Values[described.Ordinal] is not null)
                    {
                        table.Add(cells[row], partial, buffer, row);
                    }
                }
            }
        }

        /// <summary>
        ///     Compares each cell's evaluation with the server's value, or the server's failure with
        ///     the evaluations'.
        /// </summary>
        /// <param name="what">The measure and the level, for the message.</param>
        /// <param name="evaluator">The evaluator.</param>
        /// <param name="table">The table.</param>
        /// <param name="cells">Each cell, with the server's value for it.</param>
        /// <param name="result">The server's result.</param>
        /// <returns>The first difference, or null when there is none.</returns>
        private static string? Compare(
            string what,
            MeasureEvaluator evaluator,
            PartialTable table,
            IReadOnlyList<(int Cell, object? Expected, string Where)> cells,
            Result result)
        {
            if (result.Error is int error)
            {
                // The server stops at the first group that fails, so some cell fails the same way.
                string? expected = Code(error);
                if (expected is null)
                {
                    return what + ": the server failed with " + error.ToString(CultureInfo.InvariantCulture) + ".";
                }

                List<string> codes = [];
                foreach ((int cell, _, _) in cells)
                {
                    string? code = Capture(() => evaluator.Evaluate(table, cell, 0));
                    if (code is not null)
                    {
                        codes.Add(code);
                    }
                }

                return codes.Contains(expected)
                    ? null
                    : what + ": the server failed with " + expected + ", the evaluator with " + (codes.Count == 0 ? "nothing" : string.Join(", ", codes.Distinct())) + ".";
            }

            DbColumn column = result.Schema[result.MeasureColumn];
            foreach ((int cell, object? expected, string where) in cells)
            {
                MeasureValue value;
                try
                {
                    value = evaluator.Evaluate(table, cell, 0);
                }
                catch (QueryexEvaluationException exception)
                {
                    return what + " at " + where + ": the server succeeded and the evaluator failed with " + exception.Code + ".";
                }

                string? difference = Difference(value, evaluator.ResultTypes[0], expected, column);
                if (difference is not null)
                {
                    return what + " at " + where + ": " + difference;
                }
            }

            return null;
        }

        /// <summary>Describes one leaf by its key and its partials, for a message.</summary>
        /// <param name="row">The leaf's row.</param>
        /// <param name="measures">The measures, which say where the partials are.</param>
        /// <returns>The description.</returns>
        private static string Describe(Row row, CompiledMeasures measures)
        {
            return "leaf " + Convert.ToString(row.Values[1], CultureInfo.InvariantCulture) + " with partials "
                + string.Join(", ", measures.Partials.Select(partial =>
                    Convert.ToString(row.Values[partial.Ordinal], CultureInfo.InvariantCulture) ?? "null"));
        }

        /// <summary>Holds a failure to combine partials to the server's failure for the same level.</summary>
        /// <param name="what">The measure and the level, for the message.</param>
        /// <param name="code">The evaluator's code.</param>
        /// <param name="result">The server's result.</param>
        /// <returns>The difference, or null when there is none.</returns>
        private static string? Matches(string what, string code, Result result)
        {
            return result.Error is int error && Code(error) == code
                ? null
                : what + ": combining failed with " + code + " where the server " + (result.Error is null ? "succeeded" : "failed with " + result.Error) + ".";
        }

        /// <summary>How one evaluated value differs from the server's.</summary>
        /// <param name="value">The evaluated value.</param>
        /// <param name="resultType">The evaluator's type for the measure.</param>
        /// <param name="expected">The server's value.</param>
        /// <param name="column">The server's column.</param>
        /// <returns>The difference, or null when there is none.</returns>
        private static string? Difference(MeasureValue value, QueryexNumericType? resultType, object? expected, DbColumn column)
        {
            // The server's own rendering, which for a number keeps every digit at the column's scale.
            string server = IsAbsent(expected) ? "null" : expected!.ToString() ?? "?";
            if (IsAbsent(expected) || value.IsNull)
            {
                return IsAbsent(expected) == value.IsNull ? null : "evaluated " + value + ", the server " + server + ".";
            }

            string type = column.DataTypeName!;
            switch (value.Type)
            {
                case QueryexType.QxNumeric:
                    // A whole column is that width at scale zero; a decimal column has its own facets.
                    QueryexNumericType declared = type switch
                    {
                        "tinyint" => new(3, 0),
                        "smallint" => new(5, 0),
                        "int" => new(10, 0),
                        "bigint" => new(19, 0),
                        _ => new((byte)column.NumericPrecision!.Value, (byte)column.NumericScale!.Value),
                    };

                    SqlDecimal number = value.Number;
                    SqlDecimal theirs = expected switch
                    {
                        SqlDecimal exact => exact,
                        SqlInt64 wide => new SqlDecimal(wide.Value),
                        SqlInt32 narrow => new SqlDecimal(narrow.Value),
                        SqlInt16 shorter => new SqlDecimal(shorter.Value),
                        SqlByte tiny => new SqlDecimal(tiny.Value),
                        _ => SqlDecimal.Null,
                    };
                    return resultType == declared && number.Precision == declared.Precision && number.Scale == declared.Scale
                        && number.CompareTo(theirs) == 0
                        ? null
                        : "evaluated " + number + " (" + number.Precision + "," + number.Scale + "), the server "
                            + server + " as " + type + "(" + declared.Precision + "," + declared.Scale + ").";

                case QueryexType.QxBool:
                    // Realised as a bit, or as a number where the expression's shape makes it one.
                    object raw = Clr(expected!);
                    bool truth = raw is bool flag ? flag : Convert.ToInt64(raw, CultureInfo.InvariantCulture) != 0;
                    return truth == value.Boolean ? null : "evaluated " + value + ", the server " + server + ".";

                case QueryexType.QxDate:
                    return DateOnly.FromDateTime((DateTime)Clr(expected!)) == value.Date ? null : "evaluated " + value + ", the server " + server + ".";

                case QueryexType.QxString:
                    // Text ties are equal ignoring case, and the server keeps whichever it met.
                    return StringComparer.InvariantCultureIgnoreCase.Equals(value.Text, (string)Clr(expected!))
                        ? null
                        : "evaluated " + value + ", the server " + server + ".";

                case QueryexType.QxDateTime:
                case QueryexType.QxDateTimeOffset:
                case QueryexType.QxGuid:
                case QueryexType.QxHierarchyId:
                case QueryexType.QxGeography:
                default:
                    return "a measure of type " + value.Type + " is not generated.";
            }
        }

        /// <summary>The evaluation code the server's error corresponds to.</summary>
        /// <param name="error">The server's error number.</param>
        /// <returns>The code, or null for an error that corresponds to none.</returns>
        private static string? Code(int error)
        {
            return error switch
            {
                DivideByZeroError => QueryexEvaluationException.DivisionByZero,
                OverflowError => QueryexEvaluationException.ArithmeticOverflow,
                _ => null,
            };
        }

        /// <summary>Runs something that may fail evaluation.</summary>
        /// <param name="action">What to run.</param>
        /// <returns>The failure's code, or null when it succeeded.</returns>
        private static string? Capture(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (QueryexEvaluationException exception)
            {
                return exception.Code;
            }
        }

        /// <summary>The run's decimal type for one partial's column.</summary>
        /// <param name="column">The column.</param>
        /// <returns>Its precision and scale when it is a decimal; otherwise null.</returns>
        private static QueryexNumericType? DecimalType(DbColumn column)
        {
            return column.DataTypeName == "decimal"
                ? new QueryexNumericType((byte)column.NumericPrecision!.Value, (byte)column.NumericScale!.Value)
                : null;
        }

        /// <summary>Whether a value the server returned is absent.</summary>
        /// <param name="value">The value.</param>
        /// <returns>True when it is.</returns>
        private static bool IsAbsent(object? value)
        {
            return value is null or DBNull || (value is INullable nullable && nullable.IsNull);
        }

        /// <summary>The runtime value of one the server returned as its own type.</summary>
        /// <param name="value">The value, present.</param>
        /// <returns>The runtime value.</returns>
        private static object Clr(object value)
        {
            return value switch
            {
                SqlDecimal number => number.Value,
                SqlInt64 number => number.Value,
                SqlInt32 number => number.Value,
                SqlInt16 number => number.Value,
                SqlByte number => number.Value,
                SqlBoolean flag => flag.Value,
                SqlString text => text.Value,
                SqlDateTime moment => moment.Value,
                _ => value,
            };
        }

        /// <summary>What checking one measure found.</summary>
        /// <param name="Failure">The first identity that did not hold, or null when all of them held.</param>
        /// <param name="ComparedValues">Whether the server succeeded at every level, so values were compared.</param>
        private sealed record Outcome(string? Failure, bool ComparedValues);

        /// <summary>One row the server returned.</summary>
        /// <param name="Values">Every column as a reader's buffer holds it, absent ones null.</param>
        /// <param name="Measure">The measure's column as the server's own value, when the statement has one.</param>
        private sealed record Row(object?[] Values, object? Measure);

        /// <summary>What one statement returned.</summary>
        /// <param name="Schema">The columns.</param>
        /// <param name="Rows">The rows.</param>
        /// <param name="Error">The server's error number, when it failed.</param>
        private sealed record Result(IReadOnlyList<DbColumn> Schema, IReadOnlyList<Row> Rows, int? Error)
        {
            /// <summary>The position of the measure's column, or -1 when the statement has none.</summary>
            public int MeasureColumn { get; init; }
        }
    }
}
