// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Queryex.Testing.Schema;

namespace Tellma.Core.Queryex.Tests.Emit
{
    /// <summary>
    ///     A calendar function nested in itself grows the statement by about the same amount with
    ///     each level, in every position where a value cannot be named ahead of the statement.
    /// </summary>
    /// <remarks>
    ///     The patterns of the arithmetic calendars write their date operand many times over. Were
    ///     the operand copied at each mention, the copies would multiply with each level and a short
    ///     expression could compile to a statement too large to send; written once and read by name,
    ///     each level adds only its own pattern.
    /// </remarks>
    public sealed class NestingGrowthTests
    {
        /// <summary>The engine under test.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>The deepest nesting measured.</summary>
        private const int Deepest = 4;

        /// <summary>
        ///     How much more one level may add than the first nested level added, as a factor.
        /// </summary>
        /// <remarks>
        ///     Slack for what legitimately varies between levels: a parameter's number gaining a
        ///     digit, a second lookup that only some levels need. Copying the operand at each mention
        ///     instead multiplies each level by more than ten, so the bound still separates the two.
        /// </remarks>
        private const double Slack = 1.5;

        /// <summary>The functions that take a date and answer a date, so that they nest.</summary>
        /// <returns>Each function's pattern over its operand, by calendar, as theory arguments.</returns>
        public static TheoryData<string, string> Functions()
        {
            TheoryData<string, string> data = [];
            foreach (string calendar in new[] { "gc", "et", "uq" })
            {
                foreach (string function in new[]
                {
                    "addMonths({0}, 1, '{1}')",
                    "addYears({0}, 1, '{1}')",
                    "startOfYear({0}, '{1}')",
                    "startOfQuarter({0}, '{1}')",
                    "startOfMonth({0}, '{1}')",
                })
                {
                    data.Add(calendar, function);
                }
            }

            return data;
        }

        /// <summary>In a statement with no source, where nothing can be named ahead of the select list.</summary>
        /// <param name="calendar">The calendar's code.</param>
        /// <param name="function">The function's pattern over its operand.</param>
        [Theory]
        [MemberData(nameof(Functions))]
        public void InAConstantStatement_EachLevelAddsAboutTheSame(string calendar, string function)
        {
            AssertLinear(depth =>
            {
                ConstantItem[] items =
                [
                    new ConstantItem(Nest(function, calendar, "cast('2020-01-01', 'date')", depth), QueryexType.QxDate),
                ];

                QueryexResult<CompiledQuery> result = Engine.EvaluateConstant(
                    items,
                    new ConstantCompilationOptions
                    {
                        Schema = LedgerFixture.Schema,
                        QueryexVersion = QueryexLanguage.Version,
                    });

                return Succeeded(result).Sql.Length;
            });
        }

        /// <summary>In the group filter, over an aggregation, where nothing can be named per row.</summary>
        /// <param name="calendar">The calendar's code.</param>
        /// <param name="function">The function's pattern over its operand.</param>
        [Theory]
        [MemberData(nameof(Functions))]
        public void InTheGroupFilter_EachLevelAddsAboutTheSame(string calendar, string function)
        {
            AssertLinear(depth =>
            {
                QuerySpec spec = new()
                {
                    Root = LedgerFixture.Invoice,
                    Aggregate = true,
                    Select = "Centre.Name, count()",
                    Having = FilterTree.Leaf(Nest(function, calendar, "max(PostingDate)", depth) + " > min(PostingDate)"),
                };

                QueryexResult<CompiledQuery> result = Engine.CompileQuery(
                    spec,
                    new QueryCompilationOptions
                    {
                        Schema = LedgerFixture.Schema,
                        QueryexVersion = QueryexLanguage.Version,
                    });

                return Succeeded(result).Sql.Length;
            });
        }

        /// <summary>Applies a function to an operand a number of times, each around the last.</summary>
        /// <param name="function">The function's pattern over its operand.</param>
        /// <param name="calendar">The calendar's code.</param>
        /// <param name="operand">The innermost operand.</param>
        /// <param name="depth">How many times to apply it.</param>
        /// <returns>The expression.</returns>
        private static string Nest(string function, string calendar, string operand, int depth)
        {
            string expression = operand;
            for (int level = 0; level < depth; level++)
            {
                expression = function.Replace("{0}", expression, StringComparison.Ordinal)
                    .Replace("{1}", calendar, StringComparison.Ordinal);
            }

            return expression;
        }

        /// <summary>Measures the statement at each depth and checks that no level adds much more than the first.</summary>
        /// <param name="length">The statement's length at a depth.</param>
        private static void AssertLinear(Func<int, int> length)
        {
            int[] lengths = [.. Enumerable.Range(1, Deepest).Select(length)];

            // What the first nested level added is the yardstick; each deeper level must add
            // about as much, and never nothing, since each level writes its own pattern.
            int first = lengths[1] - lengths[0];
            Assert.True(first > 0, "Nesting added nothing: " + string.Join(", ", lengths));
            for (int depth = 2; depth < Deepest; depth++)
            {
                int added = lengths[depth] - lengths[depth - 1];
                Assert.True(
                    added <= first * Slack,
                    "Level " + (depth + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + " added " + added.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + " characters against " + first.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + " for the first nested level: " + string.Join(", ", lengths));
            }
        }

        /// <summary>The compiled statement, after checking that there is one.</summary>
        /// <param name="result">The compilation's result.</param>
        /// <returns>The statement.</returns>
        private static CompiledQuery Succeeded(QueryexResult<CompiledQuery> result)
        {
            Assert.True(
                result.Succeeded,
                string.Join(", ", result.Diagnostics.Select(d => d.Code + "@" + d.Location)));
            return result.Value;
        }
    }
}
