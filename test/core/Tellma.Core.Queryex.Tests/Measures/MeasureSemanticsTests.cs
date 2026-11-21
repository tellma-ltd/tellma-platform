// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using static Tellma.Core.Queryex.Tests.Measures.MeasureHarness;

namespace Tellma.Core.Queryex.Tests.Measures
{
    /// <summary>What a measure means over its partials: absence, choice, errors, and what a caller may pass.</summary>
    public sealed class MeasureSemanticsTests
    {
        /// <summary>A choice runs only the branch it takes, and a guard written with it holds.</summary>
        [Fact]
        public void Choices_AreLazy()
        {
            MeasureHarness harness = Compile(
                "if(sum(Cost) = 0, null, sum(Amount) / sum(Cost)), sum(Amount) / sum(Cost),"
                + " if(sum(Cost) != 0 and sum(Amount) / sum(Cost) > 1, 'High', 'Low'),"
                + " sum(Cost) = 0 or sum(Amount) / sum(Cost) > 1,"
                + " coalesce(sum(Amount), sum(Amount) / sum(Cost))");
            // The first measure reads the cost before the amount, so that is the partials' order.
            object?[][] rows = [[0.0000m], [1.0000m]];

            Assert.True(harness.EvaluateOne(0, rows).IsNull);
            Assert.Equal(
                QueryexEvaluationException.DivisionByZero,
                Assert.Throws<QueryexEvaluationException>(() => harness.EvaluateOne(1, rows)).Code);
            Assert.Equal("Low", harness.EvaluateOne(2, rows).Text);
            Assert.True(harness.EvaluateOne(3, rows).Boolean);
            Assert.Equal("1.0000 (38,4)", Typed(harness.EvaluateOne(4, rows).Number));

            // The absent branch takes no part in the choice's type.
            Assert.Equal(new QueryexNumericType(38, 6), harness.Prepare().ResultTypes[0]);
        }

        /// <summary>Arithmetic propagates absence, and an absent dividend over zero is absent rather than an error.</summary>
        [Fact]
        public void Absence_Propagates()
        {
            MeasureValue[] values = Compile("sum(Cost) + 1, sum(Cost) / count(Cost), -sum(Cost), round(sum(Cost), 2)").Evaluate();
            Assert.All(values, value => Assert.True(value.IsNull));
            Assert.All(values, value => Assert.Equal(QueryexType.QxNumeric, value.Type));
        }

        /// <summary>Comparisons are total: absence equals absence, and no ordering holds against it.</summary>
        [Fact]
        public void Comparisons_AreTotal()
        {
            MeasureValue[] values = Compile(
                "max(Due) = min(Due), max(Due) != min(Day), max(Qty) > min(Qty), max(Qty) <= min(Qty),"
                + " max(Qty) in (1, 2), max(Qty) is null, not (max(Qty) is not null)")
                .Evaluate([], [], [new DateOnly(2024, 1, 1)], [], []);

            Assert.Equal(["true", "true", "false", "false", "false", "true", "true"], values.Select(value => value.ToString()));
        }

        /// <summary>Values of every comparable type compare by value, and text and dates arrive from literals.</summary>
        [Fact]
        public void Comparisons_CompareByValue()
        {
            MeasureValue[] values = Compile(
                "if(max(Day) > '2024-01-01', 'Late', 'Early'), max(Qty) in (1, 2, 3), sum(Amount) = 2.50,"
                + " max(Flag) = true, max(Instant) < now(), min(Stamp) >= min(Stamp), if(sum(Amount) > 0, 1, 0.25)")
                .Evaluate(
                    [new DateOnly(2024, 2, 29)],
                    [2],
                    [2.5000m],
                    [true],
                    [new DateTimeOffset(2024, 6, 15, 7, 0, 0, TimeSpan.Zero)],
                    [new DateTime(2024, 1, 1)]);

            Assert.Equal("Late", values[0].Text);
            Assert.Equal(["true", "true", "true", "false", "true"], values[1..6].Select(value => value.ToString()));
            Assert.Equal("1.00 (3,2)", Typed(values[6].Number));
        }

        /// <summary>A bare measure is its partial; a measure's description is its text, type and nullity.</summary>
        [Fact]
        public void Items_DescribeTheMeasures()
        {
            MeasureHarness harness = Compile("sum(Amount), max(Day) , count() * 2");
            Assert.Equal(
                [
                    new CompiledMeasure("sum(Amount)", QueryexType.QxNumeric, QueryexNullity.NotNull),
                    new CompiledMeasure("max(Day)", QueryexType.QxDate, QueryexNullity.NotNull),
                    new CompiledMeasure("count() * 2", QueryexType.QxNumeric, QueryexNullity.NotNull),
                ],
                harness.Measures.Items);

            Assert.Equal(
                [
                    new MeasurePartial(1, MeasurePartialKind.Sum, QueryexType.QxNumeric),
                    new MeasurePartial(2, MeasurePartialKind.Max, QueryexType.QxDate),
                    new MeasurePartial(3, MeasurePartialKind.Count, QueryexType.QxNumeric),
                ],
                harness.Measures.Partials);
        }

        /// <summary>A run that does not describe its partials as the measures need them is refused.</summary>
        [Fact]
        public void Prepare_RefusesAMismatchedRun()
        {
            MeasureHarness harness = Compile(
                "sum(Amount) + @P + count()",
                new QueryexParameterDeclaration("P", QueryexType.QxNumeric, IsNotNull: false));
            harness.Arguments["P"] = 1;
            _ = harness.Prepare();

            Assert.Throws<ArgumentException>(() => harness.Measures.Prepare(new MeasureContext(
                [new QueryexNumericType(38, 4)], harness.Arguments, default, default, null)));

            harness.DecimalTypes[1] = new QueryexNumericType(19, 0);
            Assert.Throws<ArgumentException>(harness.Prepare);
            harness.DecimalTypes[1] = null;

            harness.DecimalTypes[0] = null;
            Assert.Throws<ArgumentException>(harness.Prepare);
            harness.DecimalTypes[0] = new QueryexNumericType(4, 5);
            Assert.Throws<ArgumentException>(harness.Prepare);
            harness.DecimalTypes[0] = new QueryexNumericType(38, 4);

            harness.Arguments["P"] = "one";
            Assert.Throws<ArgumentException>(harness.Prepare);
            harness.Arguments.Remove("P");
            Assert.Throws<ArgumentException>(harness.Prepare);

            // Names are matched ignoring case, so two that differ only in case are one name twice.
            Dictionary<string, object?> twice = new(StringComparer.Ordinal) { ["P"] = 1, ["p"] = 2 };
            Assert.Throws<ArgumentException>(() => harness.Measures.Prepare(new MeasureContext(
                harness.DecimalTypes, twice, default, default, null)));
        }

        /// <summary>A table serves only the run that created it, and only its own cells and partials.</summary>
        [Fact]
        public void Tables_RefuseWhatIsNotTheirs()
        {
            MeasureHarness harness = Compile("sum(Amount), count()");
            MeasureEvaluator evaluator = harness.Prepare();
            PartialTable table = evaluator.CreateTable(1);
            int cell = table.AddCell();

            Assert.Throws<ArgumentException>(() => harness.Prepare().Evaluate(table, cell, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.Evaluate(table, 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.Evaluate(table, cell, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.CreateTable(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => table.Add(cell, 2, Buffer(1m), 0));
            Assert.Throws<ArgumentException>(() => table.Add(cell, 0, Buffer(1L), 0));
            Assert.Throws<ArgumentException>(() => table.Merge(cell, cell));

            // A count is read from the reader's wide integers only.
            Assert.Throws<ArgumentException>(() => table.Add(cell, 1, Buffer(1), 0));
        }

        /// <summary>A value read as a type it is not, or read while absent, is a mistake rather than a default.</summary>
        [Fact]
        public void Values_RefuseTheWrongAccessor()
        {
            MeasureValue[] values = Compile("max(Day), sum(Cost)").Evaluate([new DateOnly(2024, 1, 1)]);

            Assert.Equal(new DateOnly(2024, 1, 1), values[0].Date);
            Assert.Throws<InvalidOperationException>(() => values[0].Number);
            Assert.Throws<InvalidOperationException>(() => values[1].Number);
            Assert.Equal("null", values[1].ToString());
            Assert.NotEqual(values[0], values[1]);
            Assert.Equal(values[0], Compile("max(Day)").First(new DateOnly(2024, 1, 1)));
        }
    }
}
