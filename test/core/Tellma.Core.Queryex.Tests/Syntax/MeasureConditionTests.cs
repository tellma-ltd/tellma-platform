// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex.Tests.Syntax
{
    /// <summary>
    ///     The row condition a measure aggregates over, as the self-contained predicate a list of
    ///     those rows runs.
    /// </summary>
    /// <remarks>
    ///     Pinned as text. That the rows the answer admits are the rows the measure aggregates is the
    ///     differential check's to show.
    /// </remarks>
    public sealed class MeasureConditionTests
    {
        /// <summary>The engine under test, shared because it is stateless apart from its caches.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>The parameters every test here declares.</summary>
        private static readonly QueryexParameterDeclaration[] Declared =
        [
            new QueryexParameterDeclaration("Customer", QueryexType.QxNumeric, IsNotNull: false),
            new QueryexParameterDeclaration("From", QueryexType.QxDate, IsNotNull: false),
            new QueryexParameterDeclaration("Type", QueryexType.QxString, IsNotNull: false),
        ];

        /// <summary>The measures the answer is stated with.</summary>
        /// <param name="measure">The measure.</param>
        /// <param name="customer">The value given for <c>@Customer</c>, or null to leave it out.</param>
        /// <param name="expected">The condition, or null for every row.</param>
        [Theory]
        [InlineData(
            "sum(Amount, Invoice.Customer.CustomerType = 'Corporate')",
            null,
            "(Invoice.Customer.CustomerType = 'Corporate')")]
        [InlineData("sum(Amount, Invoice.Customer.CustomerType = 'Corporate') / sum(Amount)", null, null)]
        [InlineData(
            "sum(Amount, CustomerId = @Customer) / count(Amount, CustomerId = @Customer)",
            1012,
            "(CustomerId = 1012)")]
        [InlineData("sum(Amount, CustomerId = @Customer) / count(Amount, CustomerId = @Customer)", null, null)]
        public void TheStatedMeasures_AnswerAsStated(string measure, int? customer, string? expected)
        {
            ParameterBindingOptions options = customer is int value
                ? Options(values: new() { ["Customer"] = value })
                : Options(omitted: ["Customer"]);

            QueryexResult<MeasureRows> result = Engine.MeasureCondition(measure, options);

            Assert.True(result.Succeeded);
            Assert.Equal(expected, result.Value.Condition);
        }

        /// <summary>
        ///     The answer is the disjunction of the distinct conditions in the order first met; a
        ///     condition that folds to false drops out, and one that folds to true reads every row.
        /// </summary>
        /// <param name="measure">The measure.</param>
        /// <param name="expected">The condition, or null for every row.</param>
        [Theory]

        // Distinct conditions, each parenthesized, in first-use order.
        [InlineData(
            "sum(Amount, IsPosted) - sum(Amount, Code = @Type or Code = 'X') + max(Amount, IsPosted)",
            "(IsPosted) or (Code = 'INV-1' or Code = 'X')")]

        // Compared as canonical text: two spellings of one condition count once, and a name
        // spelled in another case is another text.
        [InlineData("sum(Amount, (IsPosted)) / count(Id, IsPosted)", "(IsPosted)")]
        [InlineData("sum(Amount, IsPosted) / count(Id, ISPOSTED)", "(IsPosted) or (ISPOSTED)")]
        [InlineData("min(PostingDate, PostingDate >= @From) = max(PostingDate, (PostingDate) >= @FROM)", "(PostingDate >= cast('2024-02-29', 'date'))")]

        // A condition that is false drops; when every one is, no row is read. Folding never
        // makes one false, since an omitted parameter lets every row through under either
        // polarity, so only a condition written as false is.
        [InlineData("sum(Amount, false) + count(Id, Amount > 0)", "(Amount > 0)")]
        [InlineData("sum(Amount, ((false)))", "false")]
        [InlineData("sum(Amount, false) + avg(Amount, false)", "false")]
        [InlineData("sum(Amount, Amount > 0 and false)", "(Amount > 0 and false)")]

        // A condition folding to true, or written as true, reads every row.
        [InlineData("sum(Amount, Amount > 0) + count(Id, CustomerId = @Customer)", null)]
        [InlineData("sum(Amount, not (CustomerId = @Customer)) + count(Id, Amount > 0)", null)]
        [InlineData("sum(Amount, Amount > 0) + count(Id, (true))", null)]
        [InlineData("count(Id, Amount > 0 or CustomerId = @Customer)", null)]

        // An atom that mentions an omitted parameter anywhere folds whole, a call included.
        [InlineData("sum(Amount, IsPosted and if(CustomerId = @Customer, true, IsPosted))", "(IsPosted)")]

        // Only the condition counts: a parameter in the aggregated value is not read.
        [InlineData("sum(Amount * @Customer, IsPosted)", "(IsPosted)")]

        // A text with no aggregation is not a measure, and has no condition to report.
        [InlineData("Amount * 2", null)]

        // An aggregation inside an aggregation's condition is binding's problem; here it is one
        // more aggregation, and this one has no condition.
        [InlineData("sum(Amount, count(Id) > 1)", null)]
        public void TheConditions_CombineByTheRule(string measure, string? expected)
        {
            QueryexResult<MeasureRows> result = Engine.MeasureCondition(
                measure,
                Options(values: new() { ["Type"] = "INV-1", ["From"] = new DateOnly(2024, 2, 29) }, omitted: ["Customer"]));

            Assert.True(result.Succeeded);
            Assert.Equal(expected, result.Value.Condition);
        }

        /// <summary>The mode is not consulted: an aggregation's condition folds in every position.</summary>
        [Fact]
        public void TheMode_IsNotConsulted()
        {
            foreach (QueryexMode mode in new[] { QueryexMode.Filter, QueryexMode.Value, QueryexMode.Aggregate, QueryexMode.AggregateFilter })
            {
                QueryexResult<MeasureRows> result = Engine.MeasureCondition(
                    "sum(Amount, Amount > 0 and CustomerId = @Customer)",
                    Options(omitted: ["Customer"]) with { Mode = mode });

                Assert.Equal("(Amount > 0)", result.Value?.Condition);
            }
        }

        /// <summary>Lexical and syntax errors come back as their diagnostics.</summary>
        [Fact]
        public void AnErrorInTheMeasure_ComesBackAsADiagnostic()
        {
            QueryexResult<MeasureRows> result = Engine.MeasureCondition("sum(Amount, ", Options());

            Assert.False(result.Succeeded);
            Assert.NotEmpty(result.Diagnostics);
            Assert.Throws<ArgumentNullException>(() => Engine.MeasureCondition(null!, Options()));
            Assert.Throws<ArgumentNullException>(() => Engine.MeasureCondition("sum(Amount)", null!));
        }

        /// <summary>Builds options over the declared parameters.</summary>
        /// <param name="values">The arguments.</param>
        /// <param name="omitted">The parameters left out.</param>
        /// <returns>The options.</returns>
        private static ParameterBindingOptions Options(
            Dictionary<string, object?>? values = null,
            string[]? omitted = null)
        {
            return new ParameterBindingOptions
            {
                Mode = QueryexMode.Aggregate,
                QueryexVersion = QueryexLanguage.Version,
                Parameters = Declared,
                Values = values ?? [],
                Omitted = omitted ?? [],
            };
        }
    }
}
