// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using static Tellma.Core.Queryex.Tests.Measures.MeasureHarness;

namespace Tellma.Core.Queryex.Tests.Measures
{
    /// <summary>How partials combine: row by row into a cell, and cell by cell into a coarser one.</summary>
    public sealed class MeasureAccumulationTests
    {
        /// <summary>Text alike ignoring case, for rows whose order must not matter.</summary>
        private static readonly string[] Words = ["Sales", "SALES", "Beta", "alpha"];

        /// <summary>Every partial kind and type combines across cells the way its aggregation does.</summary>
        [Fact]
        public void EveryPartial_CombinesAcrossCells()
        {
            MeasureHarness harness = Compile(
                "sum(Amount), count(), count(Cost), min(Tiny), max(Small), min(Qty), max(Big), max(Whole), min(Rate),"
                + " max(Flag), min(Flag), max(Day), min(Stamp), max(Instant), max(Label), min(Node), max(Node)");

            object?[][] first =
            [
                [1.2500m], [3L], [2L], [(byte)9], [(short)-4], [7], [10L], [12m], [0.5m],
                [false], [false], [new DateOnly(2024, 2, 29)], [new DateTime(2024, 1, 2, 3, 4, 5)],
                [new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.FromHours(3))], ["Beta"], ["/1/10/"], ["/1/10/"],
            ];

            object?[][] second =
            [
                [-0.2500m], [4L], [0L], [(byte)3], [(short)-2], [-7], [-10L], [11m], [0.25m],
                [true], [true], [new DateOnly(2023, 12, 31)], [new DateTime(2024, 1, 2, 3, 4, 4)],
                [new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero)], ["alpha"], ["/1/2/"], ["/2/"],
            ];

            MeasureEvaluator evaluator = harness.Prepare();
            PartialTable table = evaluator.CreateTable(0);
            int one = table.AddCell();
            int two = table.AddCell();
            int both = table.AddCell();
            for (int partial = 0; partial < first.Length; partial++)
            {
                Load(table, one, partial, first[partial]);
                Load(table, two, partial, second[partial]);
            }

            table.Merge(both, one);
            table.Merge(both, two);

            string[] combined = [.. Enumerable.Range(0, harness.Measures.Items.Count).Select(m => evaluator.Evaluate(table, both, m).ToString())];
            Assert.Equal(
                [
                    "1.0000", "7", "2", "3", "-2", "-7", "10", "12", "0.250000",
                    "true", "false", "2024-02-29", "2024-01-02T03:04:04.0000000", "2024-01-01T08:00:00.0000000+00:00",
                    "Beta", "/1/2/", "/2/",
                ],
                combined);

            // Merging leaves the source as it was.
            Assert.Equal("1.2500", evaluator.Evaluate(table, one, 0).ToString());
            Assert.Equal(3, table.CellCount);
        }

        /// <summary>An empty cell's sums and extremes are absent, and its counts are zero.</summary>
        [Fact]
        public void EmptyCell_IsAbsentButCountsZero()
        {
            MeasureValue[] values = Compile("sum(Cost), count(Cost), max(Day), avg(Cost), count() + 1").Evaluate();

            Assert.True(values[0].IsNull);
            Assert.Equal("0", values[1].ToString());
            Assert.True(values[2].IsNull);
            Assert.Equal(QueryexType.QxDate, values[2].Type);

            // A null over a zero count is absent, not a division by zero.
            Assert.True(values[3].IsNull);
            Assert.Equal("1", values[4].ToString());
        }

        /// <summary>Text compares ignoring case, and of values equal so, the ordinally smallest is kept.</summary>
        [Fact]
        public void Text_ComparesIgnoringCase_AndKeepsTheOrdinallySmallestTie()
        {
            MeasureHarness harness = Compile("max(Label), min(Label)");
            string[] words = ["sales", "Alpha", "Sales", "alpha", "SALES", "Beta"];

            foreach (string[] order in new[] { words, [.. Enumerable.Reverse(words)], ["SALES", "Sales", "sales", "alpha", "Alpha", "Beta"] })
            {
                MeasureValue[] values = harness.Evaluate([.. order], [.. order]);
                Assert.Equal("SALES", values[0].Text);
                Assert.Equal("Alpha", values[1].Text);
            }
        }

        /// <summary>Hierarchy paths compare level by level as integers, an ancestor before its descendants.</summary>
        [Theory]
        [InlineData("/1/", "/1/2/")]
        [InlineData("/1/2/", "/1/10/")]
        [InlineData("/1/10/", "/2/")]
        [InlineData("/", "/1/")]
        [InlineData("/-1/", "/1/")]
        [InlineData("/2/1/", "/10/")]
        public void Paths_CompareLevelByLevel(string lower, string higher)
        {
            MeasureHarness harness = Compile("min(Node), max(Node)");
            foreach (string[] order in new[] { new[] { lower, higher }, [higher, lower] })
            {
                MeasureValue[] values = harness.Evaluate(order, order);
                Assert.Equal(lower, values[0].Text);
                Assert.Equal(higher, values[1].Text);
            }
        }

        /// <summary>Instants compare by the instant, then by the offset; truth values put false first.</summary>
        [Fact]
        public void InstantsAndTruthValues_HaveATotalOrder()
        {
            DateTimeOffset east = new(2024, 1, 1, 10, 0, 0, TimeSpan.FromHours(3));
            DateTimeOffset west = new(2024, 1, 1, 7, 0, 0, TimeSpan.Zero);

            MeasureValue[] values = Compile("max(Instant), min(Instant), max(Flag), min(Flag)")
                .Evaluate([west, east], [east, west], [true, false], [true, false]);

            Assert.True(values[0].DateTimeOffset.EqualsExact(east));
            Assert.True(values[1].DateTimeOffset.EqualsExact(west));
            Assert.True(values[2].Boolean);
            Assert.False(values[3].Boolean);
        }

        /// <summary>A sum holds exactly thirty-eight digits, and one unit more overflows, by row or by cell.</summary>
        [Fact]
        public void Sums_OverflowPastThirtyEightDigits()
        {
            MeasureHarness harness = Compile("sum(Fine)");
            harness.DecimalTypes[0] = new QueryexNumericType(38, 28);
            MeasureEvaluator evaluator = harness.Prepare();
            PartialTable table = evaluator.CreateTable(2);

            int full = table.AddCell();
            Load(table, full, 0, [9_999_999_999.999999999999999999m, 0.0000000000000000009999999999m]);
            Assert.Equal("9999999999.9999999999999999999999999999 (38,28)", Typed(evaluator.Evaluate(table, full, 0).Number));
            Assert.Equal(
                QueryexEvaluationException.ArithmeticOverflow,
                Assert.Throws<QueryexEvaluationException>(() => table.Add(full, 0, Buffer(0.0000000000000000000000000001m), 0)).Code);

            // Two near-maximum partials.
            int left = table.AddCell();
            int right = table.AddCell();
            Load(table, left, 0, [6_000_000_000m]);
            Load(table, right, 0, [6_000_000_000m]);
            Assert.Equal(
                QueryexEvaluationException.ArithmeticOverflow,
                Assert.Throws<QueryexEvaluationException>(() => table.Merge(left, right)).Code);
        }

        /// <summary>The same rows give the same values however they are ordered and split into cells.</summary>
        [Fact]
        public void ShuffledRows_GiveIdenticalResults()
        {
            MeasureHarness harness = Compile(
                "sum(Amount) / count(), avg(Rate), max(Label), min(Day), max(Qty) - min(Qty), min(Instant)");

            Random random = new(7);
            List<object?[]> rows = [];
            for (int index = 0; index < 200; index++)
            {
                rows.Add(
                [
                    Math.Round((decimal)((random.NextDouble() * 2000) - 500), 4),
                    1L,
                    random.Next(4) == 0 ? null : Math.Round((decimal)random.NextDouble(), 6),
                    random.Next(4) == 0 ? null : 1L,
                    Words[random.Next(Words.Length)],
                    new DateOnly(2024, 1, 1).AddDays(random.Next(400)),
                    random.Next(-1000, 1000),
                    random.Next(-1000, 1000),
                    new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.FromHours(random.Next(-3, 4))),
                ]);
            }

            string Run(IEnumerable<object?[]> ordered, int cells)
            {
                MeasureEvaluator evaluator = harness.Prepare();
                PartialTable table = evaluator.CreateTable(cells + 1);
                int total = table.AddCell();
                int[] parts = [.. Enumerable.Range(0, cells).Select(_ => table.AddCell())];
                int position = 0;
                foreach (object?[] row in ordered)
                {
                    int cell = parts[position++ % cells];
                    for (int partial = 0; partial < row.Length; partial++)
                    {
                        Load(table, cell, partial, [row[partial]]);
                    }
                }

                foreach (int part in parts)
                {
                    table.Merge(total, part);
                }

                return string.Join(
                    " | ",
                    Enumerable.Range(0, harness.Measures.Items.Count).Select(m =>
                    {
                        MeasureValue value = evaluator.Evaluate(table, total, m);
                        return value.Type == QueryexType.QxNumeric && !value.IsNull ? Typed(value.Number) : value.ToString();
                    }));
            }

            string expected = Run(rows, 1);
            Assert.Equal(expected, Run([.. rows.OrderBy(_ => random.Next())], 7));
            Assert.Equal(expected, Run([.. Enumerable.Reverse(rows)], 3));
        }

        /// <summary>
        ///     A subtotal is the measure over the combined partials, never a combination of its
        ///     children's measures.
        /// </summary>
        [Fact]
        public void Rollup_EvaluatesOverTheCombinedPartials()
        {
            MeasureHarness harness = Compile("sum(Amount), sum(Amount - Cost) / sum(Amount), avg(Rate)");
            harness.DecimalTypes[harness.Partial("sum(Rate)")] = new QueryexNumericType(38, 4);
            MeasureEvaluator evaluator = harness.Prepare();
            PartialTable table = evaluator.CreateTable(4);

            // Head office's own facts, then its two branches.
            (decimal Sales, decimal Margin, decimal Discount, long Count)[] leaves =
            [
                (2_000.0000m, 500.0000m, 0.0500m, 1),
                (30_000.0000m, 9_000.0000m, 0.3000m, 4),
                (20_000.0000m, 4_000.0000m, 0.1000m, 2),
            ];

            int head = table.AddCell();
            string[][] expected =
            [
                ["2000.0000 (38,4)", "0.250000 (38,6)", "0.050000 (38,6)"],
                ["30000.0000 (38,4)", "0.300000 (38,6)", "0.075000 (38,6)"],
                ["20000.0000 (38,4)", "0.200000 (38,6)", "0.050000 (38,6)"],
            ];

            for (int index = 0; index < leaves.Length; index++)
            {
                int leaf = table.AddCell();
                Load(table, leaf, 0, [leaves[index].Sales]);
                Load(table, leaf, 1, [leaves[index].Margin]);
                Load(table, leaf, 2, [leaves[index].Discount]);
                Load(table, leaf, 3, [leaves[index].Count]);
                Assert.Equal(expected[index], Values(evaluator, table, leaf));
                table.Merge(head, leaf);
            }

            Assert.Equal(["52000.0000 (38,4)", "0.259615 (38,6)", "0.064285 (38,6)"], Values(evaluator, table, head));
        }

        /// <summary>Every measure's value over one cell, with its type.</summary>
        /// <param name="evaluator">The evaluator.</param>
        /// <param name="table">The table.</param>
        /// <param name="cell">The cell.</param>
        /// <returns>The values.</returns>
        private static string[] Values(MeasureEvaluator evaluator, PartialTable table, int cell)
        {
            return [.. Enumerable.Range(0, evaluator.ResultTypes.Count).Select(m => Typed(evaluator.Evaluate(table, cell, m).Number))];
        }
    }
}
