// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using static Tellma.Core.Queryex.Tests.Measures.MeasureHarness;

namespace Tellma.Core.Queryex.Tests.Measures
{
    /// <summary>
    ///     The backend type and digits each kind of node lands on when a measure is evaluated over
    ///     partials, each vector stated with its value and its precision and scale.
    /// </summary>
    public sealed class MeasureTypeTests
    {
        /// <summary>Whole numbers stay in their family, and overflow at its width.</summary>
        [Fact]
        public void WholeFamilies_KeepTheirWidth()
        {
            Assert.Equal("8 (10,0)", Typed(Compile("max(Qty) + min(Qty)").First(5, 3).Number));
            Assert.Equal("8 (11,0)", Typed(Compile("max(Whole) + min(Whole)").First(5m, 3m).Number));
            Assert.Equal("2500000000 (21,0)", Typed(Compile("max(Whole) * min(Whole)").First(50_000m, 50_000m).Number));

            AssertFails(QueryexEvaluationException.ArithmeticOverflow, () => Compile("max(Qty) * min(Qty)").First(50_000, 50_000));
            AssertFails(QueryexEvaluationException.ArithmeticOverflow, () => Compile("max(Tiny) + min(Tiny)").First((byte)200, (byte)100));

            // The wider of two whole types.
            Assert.Equal("7 (19,0)", Typed(Compile("if(count() > 0, max(Qty), max(Big))").First(1L, 7, 9L).Number));
        }

        /// <summary>Negation and the absolute value overflow at the minimum, and negating a byte widens it.</summary>
        [Fact]
        public void Negation_OverflowsAtTheMinimum()
        {
            AssertFails(QueryexEvaluationException.ArithmeticOverflow, () => Compile("-min(Qty)").First(int.MinValue));
            AssertFails(QueryexEvaluationException.ArithmeticOverflow, () => Compile("abs(min(Qty))").First(int.MinValue));
            Assert.Equal("-255 (5,0)", Typed(Compile("-max(Tiny)").First((byte)255).Number));
            Assert.Equal("2147483647 (10,0)", Typed(Compile("abs(min(Qty))").First(-int.MaxValue).Number));
        }

        /// <summary>Thirty-eight digits leave no room for both scales, so the coarser one is kept.</summary>
        [Fact]
        public void ScaleCollapse_KeepsTheCoarserScale()
        {
            Assert.Equal("2 (38,0)", Typed(Compile("sum(Amount) + sum(Qty)").First(1.1234m, 1m).Number));
            Assert.Equal("2 (38,0)", Typed(Compile("sum(Qty) + sum(Fine)").First(1m, 0.5000000000m).Number));
            Assert.Equal("1 (38,0)", Typed(Compile("if(count() > 0, sum(Qty), sum(Amount))").First(1L, 1m, 1.1234m).Number));
            Assert.Equal("1 (38,0)", Typed(Compile("coalesce(sum(Amount), sum(Qty))").First(1.1234m, 7m).Number));
            Assert.Equal("-0.8765 (38,4)", Typed(Compile("sum(Rate) - sum(Amount)").First(0.123456m, 1.0000m).Number));

            // Scaling the coarser operand first keeps the decimals.
            Assert.Equal("2.5000 (38,4)", Typed(Compile("sum(Qty) * 1.0000 + sum(Amount)").First(2m, 0.5000m).Number));

            // Two decimal sums at different scales, as one run's reader reports them.
            MeasureHarness mixed = Compile("sum(Amount) + sum(Cost)");
            mixed.DecimalTypes[mixed.Partial("sum(Amount)")] = new QueryexNumericType(38, 2);
            Assert.Equal("1.37 (38,2)", Typed(mixed.First(1.25m, 0.1234m).Number));
        }

        /// <summary>
        ///     A sum whose scale collapses is rounded half away from zero, as the backend rounds it,
        ///     where the framework's own addition truncates it.
        /// </summary>
        [Fact]
        public void ScaleCollapse_RoundsASum()
        {
            Assert.Equal("1005 (38,0)", Typed(Compile("sum(Qty) + 2.5").First(1002m).Number));
            Assert.Equal("-1003 (38,0)", Typed(Compile("sum(Qty) + 2.5").First(-1005m).Number));
            Assert.Equal("-1005 (38,0)", Typed(Compile("sum(Qty) - 2.5").First(-1002m).Number));
        }

        /// <summary>A literal carries its written digits and scale into the operation.</summary>
        [Fact]
        public void Literals_CarryTheirWrittenScale()
        {
            Assert.Equal("1.50000 (38,5)", Typed(Compile("sum(Amount) * 0.5").First(3.0000m).Number));
            Assert.Equal("3.750000 (38,6)", Typed(Compile("sum(Amount) * 1.25").First(3.0000m).Number));
            Assert.Equal("33.333300 (38,6)", Typed(Compile("(sum(Amount) / sum(Cost)) * 100").First(1.0000m, 3.0000m).Number));
            Assert.Equal("33.333333 (38,6)", Typed(Compile("100 * sum(Amount) / sum(Cost)").First(1.0000m, 3.0000m).Number));
        }

        /// <summary>Division truncates, rounding rounds half away from zero, and a narrowing choice rounds.</summary>
        [Fact]
        public void Rounding_FollowsEachRule()
        {
            Assert.Equal("0.666666 (38,6)", Typed(Compile("sum(Amount) / sum(Cost)").First(2.0000m, 3.0000m).Number));
            Assert.Equal("-2.3500 (38,4)", Typed(Compile("round(sum(Amount), 2)").First(-2.3450m).Number));
            Assert.Equal("2.3500 (38,4)", Typed(Compile("round(sum(Amount), 2)").First(2.3450m).Number));
            Assert.Equal("-16.0000 (38,4)", Typed(Compile("round(sum(Amount), 0)").First(-15.5000m).Number));
            Assert.Equal("0.6667 (38,4)", Typed(Compile("if(count() > 0, sum(Amount) / sum(Cost), sum(Amount))").First(1L, 2.0000m, 3.0000m).Number));

            // Every one of 38 places dropped: a half or more rounds up into a digit the type has no
            // room for, as the backend fails it, and less rounds to zero.
            MeasureHarness whole = Compile("round(sum(Rate), 0)");
            whole.DecimalTypes[whole.Partial("sum(Rate)")] = new QueryexNumericType(38, 38);
            Assert.Equal("0.00000000000000000000000000000000000000 (38,38)", Typed(whole.First(0.4m).Number));
            Assert.Equal(
                QueryexEvaluationException.ArithmeticOverflow,
                Assert.Throws<QueryexEvaluationException>(() => whole.First(0.9m)).Code);
        }

        /// <summary>Each partial is typed as the backend types its column.</summary>
        [Fact]
        public void Partials_TakeTheirColumnsTypes()
        {
            Assert.Equal("5 (19,0)", Typed(Compile("count()").First(5L).Number));
            Assert.Equal("1.5000 (19,4)", Typed(Compile("max(Amount)").First(1.5m).Number));
            Assert.Equal("-7 (5,0)", Typed(Compile("min(Small)").First((short)-7).Number));
            Assert.Equal("255 (3,0)", Typed(Compile("max(Tiny)").First((byte)255).Number));

            // An average is its sum over its count, at the backend's average type.
            Assert.Equal("1.500000 (38,6)", Typed(Compile("avg(Amount)").Evaluate([1.0000m, 2.0000m], [1L, 1L])[0].Number));
            Assert.Equal("0.3333333333 (38,10)", Typed(Compile("avg(Fine)").First(1.0000000000m, 3L).Number));
        }

        /// <summary>A parameter and the user take the type the data provider infers for their value.</summary>
        [Fact]
        public void Parameters_TakeTheirValuesOwnType()
        {
            MeasureHarness harness = Compile(
                "sum(Qty) * @P",
                new QueryexParameterDeclaration("P", QueryexType.QxNumeric, IsNotNull: false));
            harness.Arguments["p"] = 1.50m;
            Assert.Equal("4.50 (38,2)", Typed(harness.First(3m).Number));

            harness.Arguments["P"] = null;
            Assert.True(harness.First(3m).IsNull);

            MeasureHarness user = Compile("count() + me()");
            Assert.Equal("7 (20,0)", Typed(user.First(5L).Number));
        }

        /// <summary>Calendar components are whole numbers, and day counts are the emitter's types.</summary>
        [Fact]
        public void Dates_GiveTheEmittersTypes()
        {
            MeasureValue[] parts = Compile("year(max(Day)), quarter(max(Day)), month(max(Day), 'gc'), day(max(Day))")
                .Evaluate([new DateOnly(2024, 2, 29)]);
            Assert.Equal(["2024 (10,0)", "1 (10,0)", "2 (10,0)", "29 (10,0)"], parts.Select(part => Typed(part.Number)));

            const string Differences = "diffDays(min(Day), max(Day)), diffHours(min(Day), max(Day)),"
                + " diffMinutes(min(Day), max(Day)), diffSeconds(min(Day), max(Day)), diffDays(max(Day), min(Day))";

            // Across a leap day.
            MeasureValue[] leap = Compile(Differences).Evaluate([new DateOnly(2024, 2, 28)], [new DateOnly(2024, 3, 1)]);
            Assert.Equal(
                ["2.000000 (26,6)", "48.000000 (26,6)", "2880.000000 (26,6)", "172800 (19,0)", "-2.000000 (26,6)"],
                leap.Select(part => Typed(part.Number)));

            // Across a year end.
            MeasureValue[] year = Compile(Differences).Evaluate([new DateOnly(2023, 12, 31)], [new DateOnly(2024, 1, 1)]);
            Assert.Equal(
                ["1.000000 (26,6)", "24.000000 (26,6)", "1440.000000 (26,6)", "86400 (19,0)", "-1.000000 (26,6)"],
                year.Select(part => Typed(part.Number)));

            // Against the date the statement was run on.
            Assert.Equal("5.000000 (26,6)", Typed(Compile("diffDays(max(Day), today())").First(new DateOnly(2024, 6, 10)).Number));
        }

        /// <summary>Floor and ceiling drop a decimal's places, and widen a narrow whole number.</summary>
        [Fact]
        public void FloorAndCeiling_DropThePlaces()
        {
            Assert.Equal("-2 (38,0)", Typed(Compile("floor(sum(Amount))").First(-1.5000m).Number));
            Assert.Equal("-1 (38,0)", Typed(Compile("ceiling(sum(Amount))").First(-1.5000m).Number));
            Assert.Equal("2 (19,0)", Typed(Compile("ceiling(max(Amount))").First(1.0001m).Number));
            Assert.Equal("9 (10,0)", Typed(Compile("floor(max(Tiny))").First((byte)9).Number));
            Assert.Equal("9 (19,0)", Typed(Compile("floor(max(Big))").First(9L).Number));
        }

        /// <summary>
        ///     A negative product that rounds to one unit of its last place keeps its sign, where the
        ///     framework's own multiplication drops it.
        /// </summary>
        [Fact]
        public void SignDefect_IsCorrected()
        {
            Assert.Equal("-0.000001 (38,6)", Typed(Compile("sum(Amount) * sum(Cost)").First(-0.0015m, 0.0005m).Number));
            Assert.Equal("-0.000001 (38,6)", Typed(Compile("sum(Fine) * 100").First(-0.0000000060m).Number));
            Assert.Equal("0.000001 (38,6)", Typed(Compile("sum(Amount) * sum(Cost)").First(-0.0015m, -0.0005m).Number));

            // The operations that never had the defect keep the sign as they always did.
            Assert.Equal("-0.0010 (38,4)", Typed(Compile("round(sum(Amount), 3)").First(-0.0005m).Number));
            Assert.Equal("-0.0001 (38,4)", Typed(Compile("if(count() > 0, sum(Fine), sum(Amount))").First(1L, -0.00005m, 0m).Number));
            Assert.Equal("-0.333333 (38,6)", Typed(Compile("sum(Amount) / sum(Cost)").First(-1.0000m, 3.0000m).Number));
            Assert.Equal("-1 (38,0)", Typed(Compile("ceiling(sum(Amount))").First(-1.0001m).Number));
        }

        /// <summary>The reader's decimals arrive at their own scale and are read at the partial's.</summary>
        [Fact]
        public void ReaderShapedInputs_TakeThePartialsType()
        {
            Assert.Equal(
                "0.750000 (38,6)",
                Typed(Compile("sum(Amount) / sum(Cost)").Evaluate([1.5000m], [0m, 2m])[0].Number));
        }

        /// <summary>One compiled list of measures meets a different decimal type in each run.</summary>
        [Fact]
        public void Preparation_IsPerRun()
        {
            MeasureHarness harness = Compile("sum(Amount) * 2, count(), max(Day)");
            QueryexNumericType? first = harness.Prepare().ResultTypes[0];

            harness.DecimalTypes[harness.Partial("sum(Amount)")] = new QueryexNumericType(38, 2);
            MeasureEvaluator second = harness.Prepare();

            Assert.Equal(new QueryexNumericType(38, 4), first);
            Assert.Equal(new QueryexNumericType(38, 2), second.ResultTypes[0]);
            Assert.Equal(new QueryexNumericType(19, 0), second.ResultTypes[1]);
            Assert.Null(second.ResultTypes[2]);
        }

        /// <summary>Asserts that evaluation fails with one code.</summary>
        /// <param name="code">The code.</param>
        /// <param name="evaluate">The evaluation.</param>
        private static void AssertFails(string code, Action evaluate)
        {
            Assert.Equal(code, Assert.Throws<QueryexEvaluationException>(evaluate).Code);
        }
    }
}
