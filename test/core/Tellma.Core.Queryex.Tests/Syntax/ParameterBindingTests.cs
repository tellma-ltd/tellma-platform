// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Queryex.Testing.Probe;

namespace Tellma.Core.Queryex.Tests.Syntax
{
    /// <summary>
    ///     Printing a text with its arguments written in as literals and its omitted parameters
    ///     folded away.
    /// </summary>
    /// <remarks>
    ///     The answer is handed to something that runs it on its own, so what is pinned here is the
    ///     exact text: each literal's spelling, what folding leaves, and what a caller is refused.
    ///     That the text means what the original meant with its arguments is the property suite's
    ///     and the differential check's to show.
    /// </remarks>
    public sealed class ParameterBindingTests
    {
        /// <summary>The engine under test, shared because it is stateless apart from its caches.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>The parameters every test here declares, one or more of each type that has a value.</summary>
        private static readonly QueryexParameterDeclaration[] Declared =
        [
            new QueryexParameterDeclaration("Amount", QueryexType.QxNumeric, IsNotNull: false),
            new QueryexParameterDeclaration("Customer", QueryexType.QxNumeric, IsNotNull: false),
            new QueryexParameterDeclaration("Main", QueryexType.QxNumeric, IsNotNull: false),
            new QueryexParameterDeclaration("Other", QueryexType.QxNumeric, IsNotNull: false),
            new QueryexParameterDeclaration("Name", QueryexType.QxString, IsNotNull: false),
            new QueryexParameterDeclaration("Posted", QueryexType.QxBool, IsNotNull: false),
            new QueryexParameterDeclaration("Settled", QueryexType.QxBool, IsNotNull: true),
            new QueryexParameterDeclaration("From", QueryexType.QxDate, IsNotNull: false),
            new QueryexParameterDeclaration("To", QueryexType.QxDate, IsNotNull: false),
            new QueryexParameterDeclaration("AsOf", QueryexType.QxDate, IsNotNull: false),
            new QueryexParameterDeclaration("Since", QueryexType.QxDateTime, IsNotNull: false),
            new QueryexParameterDeclaration("At", QueryexType.QxDateTimeOffset, IsNotNull: false),
            new QueryexParameterDeclaration("Key", QueryexType.QxGuid, IsNotNull: false),
        ];

        /// <summary>Each type's value prints as the literal an author would write for it.</summary>
        /// <param name="name">The parameter given the value.</param>
        /// <param name="kind">Which value it is given, by a name the table below reads.</param>
        /// <param name="expected">The literal it prints as.</param>
        [Theory]

        // Numbers: invariant digits, the scale the value carries, a unary minus for a negative.
        [InlineData("Amount", "decimal", "1250.75")]
        [InlineData("Amount", "decimal-scaled", "12.500")]
        [InlineData("Amount", "decimal-negative", "-12.5")]
        [InlineData("Amount", "decimal-fraction", "0.05")]
        [InlineData("Amount", "decimal-negative-zero", "0.0")]
        [InlineData("Amount", "int", "1012")]
        [InlineData("Amount", "int-negative", "-7")]
        [InlineData("Amount", "long", "9007199254740993")]
        [InlineData("Amount", "decimal-max", "79228162514264337593543950335")]

        // Text quoted with its quotes doubled, whatever else it holds.
        [InlineData("Name", "string", "'O''Brien'")]
        [InlineData("Name", "string-empty", "''")]
        [InlineData("Name", "string-marker", "'@Name and '' or 1 = 1'")]

        // Truth values as keywords.
        [InlineData("Posted", "true", "true")]
        [InlineData("Posted", "false", "false")]

        // The types without a literal of their own, as a conversion of their canonical text.
        [InlineData("From", "date", "cast('2026-01-01', 'date')")]
        [InlineData("From", "date-early", "cast('0099-03-04', 'date')")]
        [InlineData("Since", "datetime", "cast('2026-01-01T10:30:00', 'datetime')")]
        [InlineData("Since", "datetime-fraction", "cast('2026-01-01T10:30:00.12', 'datetime')")]
        [InlineData("Since", "datetime-ticks", "cast('2026-01-01T10:30:00.0000001', 'datetime')")]
        [InlineData("At", "datetimeoffset", "cast('2026-01-01T10:30:00+03:00', 'datetimeoffset')")]
        [InlineData("At", "datetimeoffset-west", "cast('2026-01-01T10:30:00.5-05:30', 'datetimeoffset')")]
        [InlineData("At", "datetimeoffset-utc", "cast('2026-01-01T10:30:00+00:00', 'datetimeoffset')")]
        [InlineData("Key", "guid", "cast('0f8fad5b-d9cb-469f-a165-70867728950e', 'guid')")]

        // The absent value keeps the declared type.
        [InlineData("Amount", "null", "cast(null, 'numeric')")]
        [InlineData("Name", "null", "cast(null, 'string')")]
        [InlineData("Posted", "null", "cast(null, 'bool')")]
        [InlineData("From", "null", "cast(null, 'date')")]
        [InlineData("Since", "null", "cast(null, 'datetime')")]
        [InlineData("At", "null", "cast(null, 'datetimeoffset')")]
        [InlineData("Key", "null", "cast(null, 'guid')")]
        public void AValue_PrintsAsItsLiteral(string name, string kind, string expected)
        {
            QueryexResult<string> result = Engine.BindParameters("@" + name, Options(QueryexMode.Value, values: new() { [name] = Value(kind) }));

            Assert.True(result.Succeeded);
            Assert.Equal(expected, result.Value);
        }

        /// <summary>
        ///     Literals print the same whatever culture the host runs under: no other calendar's
        ///     years, no other digits, no decimal comma.
        /// </summary>
        /// <param name="culture">The culture the host runs under.</param>
        [Theory]
        [InlineData("th-TH")]
        [InlineData("ar-SA")]
        [InlineData("de-DE")]
        public void ALiteral_PrintsAlikeUnderEveryCulture(string culture)
        {
            (string Name, string Kind, string Expected)[] vectors =
            [
                ("Amount", "decimal", "1250.75"),
                ("Amount", "decimal-negative", "-12.5"),
                ("From", "date", "cast('2026-01-01', 'date')"),
                ("Since", "datetime-fraction", "cast('2026-01-01T10:30:00.12', 'datetime')"),
                ("At", "datetimeoffset-west", "cast('2026-01-01T10:30:00.5-05:30', 'datetimeoffset')"),
                ("Key", "guid", "cast('0f8fad5b-d9cb-469f-a165-70867728950e', 'guid')"),
            ];

            CultureInfo culturePrevious = CultureInfo.CurrentCulture;
            CultureInfo uiCulturePrevious = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(culture);

                // The culture has to bite for the check to mean anything: each of these writes a
                // date or a number differently from the invariant culture.
                Assert.NotEqual(
                    string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd} {1}", new DateTime(2026, 1, 1), 1250.75m),
                    string.Format(CultureInfo.CurrentCulture, "{0:yyyy-MM-dd} {1}", new DateTime(2026, 1, 1), 1250.75m));

                foreach ((string name, string kind, string expected) in vectors)
                {
                    QueryexResult<string> result = Engine.BindParameters(
                        "@" + name,
                        Options(QueryexMode.Value, values: new() { [name] = Value(kind) }));

                    Assert.True(result.Succeeded, kind);
                    Assert.Equal(expected, result.Value);
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = culturePrevious;
                CultureInfo.CurrentUICulture = uiCulturePrevious;
            }
        }

        /// <summary>
        ///     A literal in its new place is grouped as precedence requires, and a negative number
        ///     keeps its sign under every operator around it.
        /// </summary>
        /// <param name="text">The text as written.</param>
        /// <param name="expected">What it prints as with <c>@Amount</c> given -12.5.</param>
        [Theory]
        [InlineData("Amount - @Amount", "Amount - -12.5")]
        [InlineData("-@Amount", "--12.5")]
        [InlineData("@Amount * 2", "-12.5 * 2")]
        [InlineData("2 * (@Amount + 1)", "2 * (-12.5 + 1)")]
        [InlineData("Amount in (@Amount, 3)", "Amount in (-12.5, 3)")]
        [InlineData("abs(@Amount)", "abs(-12.5)")]
        public void ALiteral_IsGroupedAsPrecedenceRequires(string text, string expected)
        {
            QueryexResult<string> result = Engine.BindParameters(
                text,
                Options(QueryexMode.Value, values: new() { ["Amount"] = -12.5m }));

            Assert.True(result.Succeeded);
            Assert.Equal(expected, result.Value);
        }

        /// <summary>
        ///     Omitted parameters fold exactly as a compilation folds them, valued ones become
        ///     literals beside them, and a text that folds whole prints as a truth value.
        /// </summary>
        /// <param name="text">The predicate as written.</param>
        /// <param name="expected">What it prints as with <c>@Customer</c> and <c>@Other</c> omitted.</param>
        [Theory]

        // The folding rule's own cases.
        [InlineData("PostingDate <= @AsOf and CustomerId = @Customer", "PostingDate <= cast('2026-06-30', 'date')")]
        [InlineData("not (CustomerId = @Customer)", "true")]
        [InlineData("Region = 'East' or CustomerId = @Customer", "true")]
        [InlineData("CustomerId in (@Main, @Other)", "CustomerId in (5)")]
        [InlineData("sum(Amount, CustomerId = @Customer) > @Main", "sum(Amount, true) > 5")]

        // Each polarity lets every row through, so a whole text folds only to true; and a
        // parameter in neither map stays as written.
        [InlineData("not (CustomerId = @Customer or Amount > @Main)", "not Amount > 5")]
        [InlineData("CustomerId = @Customer and not (CustomerId = @Other)", "true")]
        [InlineData("not (CustomerId = @Customer and CustomerId = @Other)", "true")]
        [InlineData("Name = @Name and CustomerId = @Customer", "Name = @Name")]

        // A truth value the author wrote is the author's, and stays.
        [InlineData("false and CustomerId = @Customer", "false")]
        [InlineData("not true or CustomerId = @Customer", "true")]

        // A valued parameter inside an atom folding removes goes with it.
        [InlineData("Amount > @Main and (CustomerId = @Customer or Amount < @Main)", "Amount > 5")]
        public void APredicate_FoldsAndTakesItsLiterals(string text, string expected)
        {
            QueryexResult<string> result = Engine.BindParameters(
                text,
                Options(
                    QueryexMode.Filter,
                    values: new() { ["AsOf"] = new DateOnly(2026, 6, 30), ["Main"] = 5 },
                    omitted: ["Customer", "Other"]));

            Assert.True(result.Succeeded);
            Assert.Equal(expected, result.Value);
        }

        /// <summary>
        ///     Outside a predicate an omitted parameter is the absent value of its type, except in
        ///     an aggregation's condition, which folds in every position.
        /// </summary>
        /// <param name="mode">The position, by name.</param>
        /// <param name="text">The text as written.</param>
        /// <param name="expected">What it prints as with <c>@Customer</c> omitted.</param>
        [Theory]
        [InlineData("Value", "CustomerId = @Customer, Amount", "CustomerId = cast(null, 'numeric'), Amount")]
        [InlineData("Value", "if(CustomerId = @Customer, 1, 0) desc", "if(CustomerId = cast(null, 'numeric'), 1, 0) desc")]
        [InlineData("Aggregate", "sum(Amount, CustomerId = @Customer and IsPosted)", "sum(Amount, IsPosted)")]
        [InlineData("Aggregate", "count(Id, CustomerId = @Customer)", "count(Id, true)")]
        [InlineData("AggregateFilter", "count(Id, CustomerId = @Customer) > 2 and max(Amount) > @Customer", "count(Id, true) > 2")]
        public void AValuePosition_PrintsTheAbsentValue(string mode, string text, string expected)
        {
            QueryexMode position = mode switch
            {
                "Value" => QueryexMode.Value,
                "Aggregate" => QueryexMode.Aggregate,
                _ => QueryexMode.AggregateFilter,
            };

            QueryexResult<string> result = Engine.BindParameters(text, Options(position, omitted: ["Customer"]));

            Assert.True(result.Succeeded);
            Assert.Equal(expected, result.Value);
        }

        /// <summary>The worked example: one bound, one omitted and absorbed.</summary>
        [Fact]
        public void TheExample_BindsOneAndFoldsTheOther()
        {
            QueryexResult<string> result = Engine.BindParameters(
                "Invoice.PostingDate >= @From and Invoice.PostingDate <= @To",
                Options(QueryexMode.Filter, values: new() { ["From"] = new DateOnly(2026, 1, 1) }, omitted: ["To"]));

            Assert.True(result.Succeeded);
            Assert.Equal("Invoice.PostingDate >= cast('2026-01-01', 'date')", result.Value);
        }

        /// <summary>
        ///     Names are matched ignoring case, in the text and in both maps, and the text otherwise
        ///     comes out canonical: keywords lowercase, uniform spacing, redundant grouping gone.
        /// </summary>
        [Fact]
        public void Names_AreMatchedIgnoringCase_AndTheTextIsCanonical()
        {
            QueryexResult<string> result = Engine.BindParameters(
                "((code = @NAME))   AND  [Amount] IN (@main, @OTHER)",
                Options(QueryexMode.Filter, values: new() { ["name"] = "x", ["MAIN"] = 1 }, omitted: ["other"]));

            Assert.True(result.Succeeded);
            Assert.Equal("code = 'x' and Amount in (1)", result.Value);
        }

        /// <summary>A parameter marker inside a string is text, and is never replaced.</summary>
        [Fact]
        public void AMarkerInsideAString_IsLeftAlone()
        {
            QueryexResult<string> result = Engine.BindParameters(
                "Memo = '@Name' and Code = @Name",
                Options(QueryexMode.Filter, values: new() { ["Name"] = "it's" }));

            Assert.True(result.Succeeded);
            Assert.Equal("Memo = '@Name' and Code = 'it''s'", result.Value);
        }

        /// <summary>Nothing is checked against a schema: names that resolve nowhere print as written.</summary>
        [Fact]
        public void NoSchemaIsConsulted()
        {
            QueryexResult<string> result = Engine.BindParameters(
                "Nonesuch.Nothing = @Main and nosuchfunction(@Main)",
                Options(QueryexMode.Filter, values: new() { ["Main"] = 1L }));

            Assert.True(result.Succeeded);
            Assert.Equal("Nonesuch.Nothing = 1 and nosuchfunction(1)", result.Value);
        }

        /// <summary>Lexical and syntax errors come back as their diagnostics.</summary>
        /// <param name="text">The text.</param>
        /// <param name="code">The diagnostic expected.</param>
        [Theory]
        [InlineData("Amount > 'unterminated", "QX1001")]
        [InlineData("Amount > ", "QX2001")]
        public void AnErrorInTheText_ComesBackAsADiagnostic(string text, string code)
        {
            QueryexResult<string> result = Engine.BindParameters(text, Options(QueryexMode.Filter));

            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
        }

        /// <summary>What a caller passes wrong is refused at the call, whatever the text holds.</summary>
        /// <param name="problem">Which mistake, by a name the switch below reads.</param>
        [Theory]
        [InlineData("valued-undeclared")]
        [InlineData("omitted-undeclared")]
        [InlineData("both")]
        [InlineData("twice")]
        [InlineData("numeric-as-double")]
        [InlineData("date-as-datetime")]
        [InlineData("datetime-as-offset")]
        [InlineData("guid-as-string")]
        [InlineData("string-as-number")]
        [InlineData("bool-as-number")]
        [InlineData("null-for-never-absent")]
        [InlineData("undeclarable")]
        public void ACallerMistake_IsRefused(string problem)
        {
            ParameterBindingOptions options = problem switch
            {
                "valued-undeclared" => Options(QueryexMode.Filter, values: new() { ["Nobody"] = 1 }),
                "omitted-undeclared" => Options(QueryexMode.Filter, omitted: ["Nobody"]),
                "both" => Options(QueryexMode.Filter, values: new() { ["Main"] = 1 }, omitted: ["MAIN"]),
                "twice" => Options(
                    QueryexMode.Filter,
                    values: new(StringComparer.Ordinal) { ["Main"] = 1, ["main"] = 2 }),
                "numeric-as-double" => Options(QueryexMode.Filter, values: new() { ["Main"] = 1.5d }),
                "date-as-datetime" => Options(QueryexMode.Filter, values: new() { ["From"] = new DateTime(2026, 1, 1) }),
                "datetime-as-offset" => Options(
                    QueryexMode.Filter,
                    values: new() { ["Since"] = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) }),
                "guid-as-string" => Options(
                    QueryexMode.Filter,
                    values: new() { ["Key"] = "0f8fad5b-d9cb-469f-a165-70867728950e" }),
                "string-as-number" => Options(QueryexMode.Filter, values: new() { ["Name"] = 5 }),
                "bool-as-number" => Options(QueryexMode.Filter, values: new() { ["Posted"] = 1 }),
                "null-for-never-absent" => Options(QueryexMode.Filter, values: new() { ["Settled"] = null }),
                _ => new ParameterBindingOptions
                {
                    Mode = QueryexMode.Filter,
                    QueryexVersion = QueryexLanguage.Version,
                    Parameters = [new QueryexParameterDeclaration("Node", QueryexType.QxHierarchyId, IsNotNull: false)],
                },
            };

            Assert.Throws<ArgumentException>(() => Engine.BindParameters("Amount > ", options));
            Assert.Throws<ArgumentException>(() => Engine.MeasureCondition("sum(Amount)", options));
        }

        /// <summary>
        ///     A text within its ceilings whose literals take it beyond them is refused at binding,
        ///     each problem placed over the whole of the text the caller gave.
        /// </summary>
        /// <param name="ceiling">Which ceiling, by a name the switch below reads.</param>
        /// <param name="code">The diagnostic it reports.</param>
        [Theory]
        [InlineData("length", "QX5001")]
        [InlineData("tokens", "QX5002")]
        public void ATextThatOutgrowsItsCeilings_IsReported(string ceiling, string code)
        {
            const string Text = "ExternalId = @Key";
            ParameterBindingOptions options = Options(
                QueryexMode.Filter,
                values: new() { ["Key"] = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e") }) with
            {
                Limits = ceiling == "length"
                    ? new QueryexLimits { MaxInputLength = 40 }
                    : new QueryexLimits { MaxTokens = 6 },
            };

            QueryexResult<string> result = Engine.BindParameters(Text, options);

            Assert.False(result.Succeeded);
            QueryexDiagnostic problem = Assert.Single(result.Diagnostics);
            Assert.Equal((code, new QueryexSpan(0, Text.Length)), (problem.Code, problem.Span));

            // The same text with its parameter left as written is within them.
            Assert.True(Engine.BindParameters(Text, options with { Values = new Dictionary<string, object?>() }).Succeeded);
        }

        /// <summary>A measure whose printed condition outgrows its ceilings is refused the same way.</summary>
        [Fact]
        public void AMeasureWhoseConditionOutgrowsItsCeilings_IsReported()
        {
            const string Measure = "sum(Amount, ExternalId = @Key)";
            ParameterBindingOptions options = Options(
                QueryexMode.Aggregate,
                values: new() { ["Key"] = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e") }) with
            {
                Limits = new QueryexLimits { MaxInputLength = 40 },
            };

            QueryexResult<MeasureRows> result = Engine.MeasureCondition(Measure, options);

            Assert.False(result.Succeeded);
            QueryexDiagnostic problem = Assert.Single(result.Diagnostics);
            Assert.Equal(("QX5001", new QueryexSpan(0, Measure.Length)), (problem.Code, problem.Span));
        }

        /// <summary>Null arguments and an unsupported version are refused too.</summary>
        [Fact]
        public void NullsAndUnsupportedVersions_AreRefused()
        {
            Assert.Throws<ArgumentNullException>(() => Engine.BindParameters(null!, Options(QueryexMode.Filter)));
            Assert.Throws<ArgumentNullException>(() => Engine.BindParameters("true", null!));
            Assert.Throws<ArgumentNullException>(() => Options(QueryexMode.Filter) with { Values = null! });
            Assert.Throws<ArgumentNullException>(() => Options(QueryexMode.Filter) with { Omitted = null! });
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ParameterBindingOptions { Mode = QueryexMode.Filter, QueryexVersion = QueryexLanguage.Version + 1 });
        }

        /// <summary>
        ///     Printing the result and parsing it again gives back the very tree that was printed:
        ///     every literal reads back as itself, wherever it landed.
        /// </summary>
        /// <param name="text">The text as written.</param>
        [Theory]
        [InlineData("Amount - @Amount * -@Amount")]
        [InlineData("not (Code = @Name or Amount in (@Amount, @Main)) and PostingDate >= @From")]
        [InlineData("PostedAt >= @At and DueOn < @Since and ExternalId = @Key and IsPosted = @Posted")]
        [InlineData("sum(Amount, CustomerId = @Customer and Amount > -@Amount) / count(Id, PostingDate < @From)")]
        [InlineData("@Amount, -(@Amount), (@Amount) * (@Amount), @Name || @Name")]
        public void ThePrintedText_ReadsBackAsTheBoundTree(string text)
        {
            foreach (QueryexMode mode in new[] { QueryexMode.Value, QueryexMode.Filter, QueryexMode.Aggregate })
            {
                ParameterBindingOptions options = Options(
                    mode,
                    values: new()
                    {
                        ["Amount"] = -1.250m,
                        ["Main"] = 0,
                        ["Name"] = "a'b",
                        ["From"] = new DateOnly(2024, 2, 29),
                        ["Since"] = new DateTime(2024, 2, 29, 23, 59, 59).AddTicks(1234),
                        ["At"] = new DateTimeOffset(2024, 2, 29, 23, 59, 59, TimeSpan.FromMinutes(-150)),
                        ["Key"] = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
                        ["Posted"] = null,
                    },
                    omitted: ["Customer"]);

                Assert.True(ProbeParameterBinding.RoundTrips(text, options), text);
            }
        }

        /// <summary>Builds options over the declared parameters.</summary>
        /// <param name="mode">The position the text is written for.</param>
        /// <param name="values">The arguments.</param>
        /// <param name="omitted">The parameters left out.</param>
        /// <returns>The options.</returns>
        private static ParameterBindingOptions Options(
            QueryexMode mode,
            Dictionary<string, object?>? values = null,
            string[]? omitted = null)
        {
            return new ParameterBindingOptions
            {
                Mode = mode,
                QueryexVersion = QueryexLanguage.Version,
                Parameters = Declared,
                Values = values ?? [],
                Omitted = omitted ?? [],
            };
        }

        /// <summary>The value a vector names.</summary>
        /// <param name="kind">The name.</param>
        /// <returns>The value.</returns>
        private static object? Value(string kind)
        {
            return kind switch
            {
                "decimal" => 1250.75m,
                "decimal-scaled" => 12.500m,
                "decimal-negative" => -12.5m,
                "decimal-fraction" => 0.05m,
                "decimal-negative-zero" => -0.0m,
                "int" => 1012,
                "int-negative" => -7,
                "long" => 9007199254740993L,
                "decimal-max" => decimal.MaxValue,
                "string" => "O'Brien",
                "string-empty" => string.Empty,
                "string-marker" => "@Name and ' or 1 = 1",
                "true" => true,
                "false" => false,
                "date" => new DateOnly(2026, 1, 1),
                "date-early" => new DateOnly(99, 3, 4),
                "datetime" => new DateTime(2026, 1, 1, 10, 30, 0, DateTimeKind.Utc),
                "datetime-fraction" => new DateTime(2026, 1, 1, 10, 30, 0).AddTicks(1_200_000),
                "datetime-ticks" => new DateTime(2026, 1, 1, 10, 30, 0).AddTicks(1),
                "datetimeoffset" => new DateTimeOffset(2026, 1, 1, 10, 30, 0, TimeSpan.FromHours(3)),
                "datetimeoffset-west" => new DateTimeOffset(2026, 1, 1, 10, 30, 0, TimeSpan.FromMinutes(-330)).AddMilliseconds(500),
                "datetimeoffset-utc" => new DateTimeOffset(2026, 1, 1, 10, 30, 0, TimeSpan.Zero),
                "guid" => Guid.Parse("0F8FAD5B-D9CB-469F-A165-70867728950E"),
                _ => null,
            };
        }
    }
}
