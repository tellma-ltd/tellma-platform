// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex;

namespace Tellma.Queryex.Testing.Corpus
{
    /// <summary>
    ///     Every expression the conformance suites check, and what each is supposed to produce.
    /// </summary>
    /// <remarks>
    ///     Written in code rather than read from a file on purpose: the cases name library types,
    ///     modes, and declarations directly, and a file format for them would be a second parser
    ///     nobody tests.
    /// </remarks>
    public static class ExpressionCorpus
    {
        /// <summary>Every case, in a stable order.</summary>
        public static ImmutableArray<ExpressionCase> All { get; } = Build();

        /// <summary>Builds the corpus.</summary>
        /// <returns>The cases.</returns>
        private static ImmutableArray<ExpressionCase> Build()
        {
            IEnumerable<ExpressionCase> all = Lexical()
                .Concat(Grammar())
                .Concat(Typing())
                .Concat(Coercion())
                .Concat(Calendars())
                .Concat(Nullities())
                .Concat(Modes())
                .Concat(Hierarchy())
                .Concat(Inference())
                .Concat(Ceilings())
                .Concat(Functions());

            return [.. all];
        }

        /// <summary>
        ///     Every function in the library, applied to real columns.
        /// </summary>
        /// <returns>The cases.</returns>
        /// <remarks>
        ///     These exist to be executed. What a function's template renders is pinned by its
        ///     snapshot, but a snapshot only says the compiler still produces what it produced
        ///     yesterday — a template naming the opposite case conversion, or the wrong rounding, or
        ///     its arguments the other way round, agrees with its own snapshot perfectly. Running
        ///     each one over rows and comparing against the second reading is what says the two
        ///     agree on what the function means. The arguments are columns rather than literals so
        ///     the answer varies by row, and each reaches both an absent value and a present one.
        /// </remarks>
        private static IEnumerable<ExpressionCase> Functions()
        {
            // Numbers.
            yield return Runs("fn-abs", "abs(Rate)");
            yield return Runs("fn-ceiling", "ceiling(Rate)");
            yield return Runs("fn-floor", "floor(Rate)");
            yield return Runs("fn-round", "round(Amount, 2)");
            yield return Runs("fn-round-zero", "round(Rate, 0)");

            // Text. Trailing spaces and fixed-width storage are why several of these read Ref.
            yield return Runs("fn-upper", "upper(Memo)");
            yield return Runs("fn-lower", "lower(Code)");
            yield return Runs("fn-trim", "trim(Ref)");
            yield return Runs("fn-length", "length(Ref)");
            yield return Runs("fn-length-nvarchar", "length(Memo)");
            yield return Runs("fn-left", "left(Code, 3)");
            yield return Runs("fn-right", "right(Code, 3)");
            yield return Runs("fn-substring-2", "substring(Code, 2)");
            yield return Runs("fn-substring-3", "substring(Code, 2, 3)");
            yield return Runs("fn-replace", "replace(Code, 'A', 'Z')");
            yield return Runs("fn-contains", "contains(Memo, 'a')");
            yield return Runs("fn-starts-with", "startsWith(Code, 'IN')");
            yield return Runs("fn-ends-with", "endsWith(Code, '1')");
            yield return Runs("fn-contains-empty", "contains(Code, '')");
            yield return Runs("fn-ends-with-space", "endsWith(Ref, ' ')");

            // Dates and instants, read at both the field level and the truncation level.
            yield return Runs("fn-year", "year(PostingDate)");
            yield return Runs("fn-quarter", "quarter(PostingDate)");
            yield return Runs("fn-month", "month(PostingDate)");
            yield return Runs("fn-week", "week(PostingDate)");
            yield return Runs("fn-weekday", "weekday(PostingDate)");
            yield return Runs("fn-day", "day(PostingDate)");
            yield return Runs("fn-hour", "hour(PostedOn)");
            yield return Runs("fn-minute", "minute(PostedOn)");
            yield return Runs("fn-second", "second(PostedOn)");
            yield return Runs("fn-start-of-year", "startOfYear(PostingDate)");
            yield return Runs("fn-start-of-month", "startOfMonth(PostingDate)");
            yield return Runs("fn-start-of-week", "startOfWeek(PostingDate)");
            yield return Runs("fn-start-of-day", "startOfDay(PostedOn)");
            yield return Runs("fn-add-days", "addDays(PostingDate, 45)");
            yield return Runs("fn-add-days-negative", "addDays(PostingDate, -45)");
            yield return Runs("fn-add-months", "addMonths(PostingDate, 13)");
            yield return Runs("fn-add-years", "addYears(PostingDate, 2)");
            yield return Runs("fn-add-hours", "addHours(PostedOn, 30)");
            yield return Runs("fn-add-minutes", "addMinutes(PostedOn, 90)");
            yield return Runs("fn-add-seconds", "addSeconds(PostedOn, 3700)");
            yield return Runs("fn-diff-days", "diffDays(PostingDate, DueDate)");
            yield return Runs("fn-diff-months", "diffMonths(PostingDate, DueDate)");
            yield return Runs("fn-diff-years", "diffYears(PostingDate, DueDate)");
            yield return Runs("fn-diff-hours", "diffHours(PostedOn, DueOn)");
            yield return Runs("fn-diff-minutes", "diffMinutes(PostedOn, DueOn)");
            yield return Runs("fn-diff-seconds", "diffSeconds(PostedOn, DueOn)");

            // Choice and absence.
            yield return Runs("fn-if", "if(IsPosted, Amount, Rate)");
            yield return Runs("fn-coalesce-two", "coalesce(Rate, Amount)");
            yield return Runs("fn-coalesce-three", "coalesce(Rate, Rate, Amount)");
            yield return Runs("fn-coalesce-text", "coalesce(Memo, Code)");

            // Conversions, one per direction the matrix allows from a column.
            yield return Runs("fn-cast-numeric-to-string", "cast(Amount, 'string')");
            // Round-tripped rather than read straight off a text column: an explicit conversion of
            // text that is not a number is a runtime failure by design, on the server and in the
            // second reading alike, and that is a different case from this one.
            yield return Runs("fn-cast-string-to-numeric", "cast(cast(Amount, 'string'), 'numeric')");
            yield return Runs("fn-cast-date-to-datetime", "cast(PostingDate, 'datetime')");
            yield return Runs("fn-cast-datetime-to-date", "cast(PostedOn, 'date')");
            yield return Runs("fn-cast-identity", "cast(Amount, 'numeric')");

            // Operators, which have templates of their own.
            yield return Runs("op-add", "Amount + Count");
            yield return Runs("op-subtract", "Amount - Count");
            yield return Runs("op-multiply", "Amount * Count");
            yield return Runs("op-divide", "Amount / Count");
            yield return Runs("op-divide-integers", "Count / Count");
            yield return Runs("op-remainder", "Count % 3");
            yield return Runs("op-negate", "-Amount");
            yield return Runs("op-concat", "Code || Memo");
            yield return Runs("op-not", "not IsPosted");
            yield return Runs("op-and", "IsPosted and IsApproved");
            yield return Runs("op-or", "IsPosted or IsApproved");

            // Comparison, which is where absence has to behave and where the guards live.
            yield return Runs("op-equal-absentable", "Rate = Amount");
            yield return Runs("op-not-equal-absentable", "Rate != Amount");
            yield return Runs("op-less", "Rate < Amount");
            yield return Runs("op-less-or-equal", "Rate <= Amount");
            yield return Runs("op-greater", "Rate > Amount");
            yield return Runs("op-greater-or-equal", "Rate >= Amount");
            yield return Runs("op-equal-text", "Memo = Code");
            yield return Runs("op-is-null", "Rate is null");
            yield return Runs("op-is-not-null", "Rate is not null");
            yield return Runs("op-in", "Count in (1, 2, 3)");
            yield return Runs("op-in-absentable", "Rate in (1, Amount)");
            yield return Runs("op-equal-both-absent", "Rate = Rate");
            yield return Runs("op-equal-dates", "DueDate = PostingDate");
            yield return Runs("op-equal-guid", "ExternalId = BatchId");
        }

        /// <summary>What the scanner accepts and refuses.</summary>
        /// <returns>The cases.</returns>
        private static IEnumerable<ExpressionCase> Lexical()
        {
            yield return Bad("lex-unterminated-string", "'abc", "QX1001");
            yield return Bad("lex-unterminated-identifier", "[abc", "QX1002");
            yield return Bad("lex-leading-point", ".5", "QX1003");
            yield return Bad("lex-trailing-point", "5.", "QX1003");
            yield return Bad("lex-exponent", "1e3", "QX1003");
            yield return Bad("lex-hex", "0x0A", "QX1003");
            yield return Bad("lex-digit-separator", "1_000", "QX1003");
            yield return Bad("lex-precision", new string('9', 39), "QX1004");
            yield return Bad("lex-unexpected-character", "Amount # 1", "QX1005");
            yield return Good("lex-escaped-quote", "'it''s'", QueryexType.QxString);
            yield return Good("lex-scaled-number", "0.500", QueryexType.QxNumeric);
        }

        /// <summary>What the grammar accepts and refuses.</summary>
        /// <returns>The cases.</returns>
        private static IEnumerable<ExpressionCase> Grammar()
        {
            yield return Bad("syntax-unexpected-token", "Amount..Rate", "QX2001");
            yield return Bad("syntax-unbalanced", "(Amount", "QX2002");
            yield return Bad("syntax-empty-item", "Amount, , Rate", "QX2003");
            yield return Bad("syntax-empty-argument", "round(Amount,)", "QX2004");
            yield return Bad("syntax-empty-parentheses", "()", "QX2005");
            yield return Bad("syntax-chained-comparison", "1 = 1 = 1", "QX2006");
            yield return Bad("syntax-direction-not-permitted", "Amount asc", "QX2007");
            yield return Bad("syntax-direction-not-at-end", "(Amount asc) + 1", "QX2008") with
            {
                Directions = true,
            };

            yield return new ExpressionCase
            {
                Id = "syntax-predicate-list",
                Text = "IsPosted, IsPosted",
                Mode = QueryexMode.Filter,
                Diagnostics = ["QX2009"],
            };

            yield return Good("syntax-keyword-property", "[not]", QueryexType.QxBool);
            yield return Good("syntax-function-named-property", "Count", QueryexType.QxNumeric);
            yield return Good("syntax-call-over-property-name", "count()", QueryexType.QxNumeric) with
            {
                Mode = QueryexMode.Aggregate,
                HasGroupingKeys = true,
            };

            yield return Good("syntax-list", "Amount, Rate, Code", QueryexType.QxNumeric) with
            {
                Items = 3,
            };
        }

        /// <summary>What the type system concludes and refuses.</summary>
        /// <returns>The cases.</returns>
        private static IEnumerable<ExpressionCase> Typing()
        {
            yield return Bad("type-unknown-property", "Nonesuch", "QX3001");
            yield return Bad("type-path-ends-at-navigation", "Customer", "QX3002");
            yield return Bad("type-unknown-function", "nosuchfunction(1)", "QX3003");
            yield return Bad("type-wrong-arity", "year()", "QX3004");
            yield return Bad("type-no-overload", "year(Memo)", "QX3005");
            yield return Bad("type-undeclared-parameter", "Amount > @x", "QX3007");
            yield return Bad("type-literal-required", "round(Amount, Count)", "QX3100");
            yield return Bad("type-value-not-accepted", "year(PostingDate, 'zz')", "QX3101");
            yield return Bad("type-unsupported-cast", "cast(Location, 'string')", "QX3102");
            yield return Bad("type-incompatible-operands", "Memo = PostingDate", "QX3200");
            yield return Bad("type-no-date-widening", "now() >= PostingDate", "QX3200");
            yield return Bad("type-operand-not-ordered", "ExternalId > ExternalId", "QX3201");
            yield return Bad("type-concat-of-number", "1 || 'a'", "QX3201");
            yield return Bad("type-geography-not-equatable", "Territory = Territory", "QX3201");
            yield return Bad("type-undeterminable", "null", "QX3202");

            yield return Good("type-equality-of-bools", "IsPosted = [not]", QueryexType.QxBool);
            yield return Good("type-guid-equality", "BatchId = ExternalId", QueryexType.QxBool);
            yield return Good("type-cast-null", "cast(null, 'numeric')", QueryexType.QxNumeric) with
            {
                Nullity = QueryexNullity.Null,
            };

            yield return Good("type-identity-cast", "cast(Amount, 'numeric')", QueryexType.QxNumeric);
            yield return Good("type-concat", "Memo || Notes", QueryexType.QxString);
            yield return Good("type-remainder", "Count % 2", QueryexType.QxNumeric);
            yield return Good("type-negation", "-Amount", QueryexType.QxNumeric);
            yield return Good("type-membership", "Count in (1, 2, 3)", QueryexType.QxBool);
            yield return Good("type-is-null", "Rate is null", QueryexType.QxBool);
            yield return Good("type-not", "not IsPosted", QueryexType.QxBool);
        }

        /// <summary>How written values are read at the type their surroundings want.</summary>
        /// <returns>The cases.</returns>
        private static IEnumerable<ExpressionCase> Coercion()
        {
            yield return Good("coerce-date", "'2024-01-01' = PostingDate", QueryexType.QxBool);
            yield return Good("coerce-datetime", "PostedOn = '2024-01-01T10:30:00'", QueryexType.QxBool);
            yield return Good(
                "coerce-offset",
                "PostedAt = '2024-01-01T10:30:00+03:00'",
                QueryexType.QxBool);

            yield return Good(
                "coerce-guid",
                "ExternalId = 'a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11'",
                QueryexType.QxBool);

            yield return Good("coerce-through-cast", "cast(Notes, 'date') = PostingDate", QueryexType.QxBool);
            yield return Good("coerce-into-branches", "if(IsPosted, PostingDate, '2024-01-01')", QueryexType.QxDate);
            yield return Good("coerce-into-coalesce", "coalesce(DueDate, '2024-01-01')", QueryexType.QxDate) with
            {
                Nullity = QueryexNullity.NotNull,
            };

            yield return Bad("coerce-malformed-date", "PostingDate = '2024-13-99'", "QX3200");
        }

        /// <summary>What the calendar operations demand of their input.</summary>
        /// <returns>The cases.</returns>
        private static IEnumerable<ExpressionCase> Calendars()
        {
            yield return Bad("calendar-needs-zone", "year(PostedAt)", "QX3103");
            yield return Bad("calendar-add-months-needs-zone", "addMonths(PostedAt, 1)", "QX3103");
            yield return Good("calendar-zoned", "year(local(PostedAt))", QueryexType.QxNumeric);
            yield return Good("calendar-date-is-zoned", "year(PostingDate)", QueryexType.QxNumeric);
            yield return Good("calendar-fixed-unit", "addDays(PostedAt, 1)", QueryexType.QxDateTimeOffset);
            yield return Good("calendar-explicit-zone", "local(PostedAt, 'Africa/Nairobi')", QueryexType.QxDateTime);
            yield return Bad("calendar-unknown-zone", "local(PostedAt, 'Mars/Olympus')", "QX3101");
            yield return Good("calendar-gregorian", "year(PostingDate, 'gc')", QueryexType.QxNumeric);
            yield return Bad("calendar-unimplemented", "year(PostingDate, 'et')", "QX3101");
            yield return Good("calendar-week", "week(PostingDate)", QueryexType.QxNumeric);
            yield return Good("calendar-weekday", "weekday(PostingDate)", QueryexType.QxNumeric);
            yield return Good("calendar-start-of-week", "startOfWeek(PostingDate)", QueryexType.QxDate);
            yield return Good("calendar-difference", "diffDays(PostingDate, DueDate)", QueryexType.QxNumeric);
            yield return Bad("calendar-difference-mixed", "diffDays(PostingDate, PostedAt)", "QX3005");
        }

        /// <summary>What the nullity analysis concludes.</summary>
        /// <returns>The cases.</returns>
        private static IEnumerable<ExpressionCase> Nullities()
        {
            yield return Nullity("nullity-present-column", "Amount", QueryexNullity.NotNull);
            yield return Nullity("nullity-absentable-column", "Rate", QueryexNullity.Nullable);
            yield return Nullity("nullity-through-navigation", "Customer.Name", QueryexNullity.Nullable);
            yield return Nullity("nullity-through-mandatory-navigation", "Centre.Name", QueryexNullity.NotNull);
            yield return Nullity("nullity-arithmetic", "Amount + Rate", QueryexNullity.Nullable);
            yield return Nullity("nullity-comparison", "Rate > 1", QueryexNullity.NotNull);
            yield return Nullity("nullity-coalesce", "coalesce(Rate, 0)", QueryexNullity.NotNull);
            yield return Nullity("nullity-conditional", "if(IsPosted, 1, Rate)", QueryexNullity.Nullable);
            yield return Nullity("nullity-conditional-folded", "if(true, 1, Rate)", QueryexNullity.NotNull);
            yield return Nullity("nullity-concat", "Memo || 'x'", QueryexNullity.Nullable);

            yield return Nullity("nullity-sum-grouped", "sum(Amount)", QueryexNullity.NotNull) with
            {
                Mode = QueryexMode.Aggregate,
                HasGroupingKeys = true,
            };

            yield return Nullity("nullity-sum-conditional", "sum(Amount, IsPosted)", QueryexNullity.Nullable) with
            {
                Mode = QueryexMode.Aggregate,
                HasGroupingKeys = true,
            };

            yield return Nullity("nullity-count-conditional", "count(Amount, IsPosted)", QueryexNullity.NotNull) with
            {
                Mode = QueryexMode.Aggregate,
                HasGroupingKeys = true,
            };

            yield return Nullity("nullity-sum-ungrouped", "sum(Amount)", QueryexNullity.Nullable) with
            {
                Mode = QueryexMode.Aggregate,
            };

            yield return Nullity("nullity-user", "me()", QueryexNullity.NotNull);
            yield return Nullity("nullity-user-absent", "me()", QueryexNullity.Null) with
            {
                HasUser = false,
            };

            yield return Nullity("nullity-declared-present", "@a", QueryexNullity.NotNull) with
            {
                Parameters = [new QueryexParameterDeclaration("a", QueryexType.QxNumeric, IsNotNull: true)],
            };

            yield return Nullity("nullity-declared-absentable", "@a", QueryexNullity.Nullable) with
            {
                Parameters = [new QueryexParameterDeclaration("a", QueryexType.QxNumeric, IsNotNull: false)],
            };
        }

        /// <summary>What each position demands of what is written in it.</summary>
        /// <returns>The cases.</returns>
        private static IEnumerable<ExpressionCase> Modes()
        {
            yield return new ExpressionCase
            {
                Id = "mode-predicate-required",
                Text = "Amount",
                Mode = QueryexMode.Filter,
                Diagnostics = ["QX4001"],
            };

            yield return new ExpressionCase
            {
                Id = "mode-aggregation-forbidden",
                Text = "sum(Amount)",
                Diagnostics = ["QX4002"],
            };

            yield return new ExpressionCase
            {
                Id = "mode-nested-aggregation",
                Text = "sum(sum(Amount))",
                Mode = QueryexMode.Aggregate,
                Diagnostics = ["QX4003"],
            };

            yield return new ExpressionCase
            {
                Id = "mode-path-outside-aggregation",
                Text = "Amount > 1",
                Mode = QueryexMode.AggregateFilter,
                Diagnostics = ["QX4004"],
            };

            yield return new ExpressionCase
            {
                Id = "mode-mixed-paths",
                Text = "Amount * sum(Amount)",
                Mode = QueryexMode.Aggregate,
                Diagnostics = ["QX4007"],
            };

            yield return new ExpressionCase
            {
                Id = "mode-key-must-be-comparable",
                Text = "Location",
                Mode = QueryexMode.Aggregate,
                Diagnostics = ["QX3201"],
            };

            yield return new ExpressionCase
            {
                Id = "mode-direction-needs-order",
                Text = "Location desc",
                Directions = true,
                Diagnostics = ["QX3201"],
            };

            yield return Good("mode-measure", "sum(Amount)", QueryexType.QxNumeric) with
            {
                Mode = QueryexMode.Aggregate,
                HasGroupingKeys = true,
            };

            yield return new ExpressionCase
            {
                Id = "mode-aggregate-filter",
                Text = "sum(Amount) > 100",
                Mode = QueryexMode.AggregateFilter,
                HasGroupingKeys = true,
            };
        }

        /// <summary>What the hierarchy predicates demand of their key.</summary>
        /// <returns>The cases.</returns>
        private static IEnumerable<ExpressionCase> Hierarchy()
        {
            yield return new ExpressionCase
            {
                Id = "hierarchy-descendant",
                Text = "descendantOf(Account.Concept, 'Assets')",
                Mode = QueryexMode.Filter,
            };

            yield return new ExpressionCase
            {
                Id = "hierarchy-ancestor-many-keys",
                Text = "ancestorOf(Account.Concept, 'Assets', 'Equity')",
                Mode = QueryexMode.Filter,
            };

            yield return new ExpressionCase
            {
                Id = "hierarchy-key-must-be-path",
                Text = "descendantOf('Assets', 'Assets')",
                Mode = QueryexMode.Filter,
                Diagnostics = ["QX3300"],
            };

            yield return new ExpressionCase
            {
                Id = "hierarchy-entity-not-hierarchical",
                Text = "descendantOf(Customer.Name, 'x')",
                Mode = QueryexMode.Filter,
                Diagnostics = ["QX3301"],
            };

            yield return new ExpressionCase
            {
                Id = "hierarchy-key-not-unique",
                Text = "descendantOf(Account.Label, 'x')",
                Mode = QueryexMode.Filter,
                Diagnostics = ["QX3302"],
            };

            yield return new ExpressionCase
            {
                Id = "hierarchy-key-reads-a-column",
                Text = "descendantOf(Account.Concept, Code)",
                Mode = QueryexMode.Filter,
                Diagnostics = ["QX3303"],
            };
        }

        /// <summary>What inference concludes about undeclared parameters.</summary>
        /// <returns>The cases.</returns>
        private static IEnumerable<ExpressionCase> Inference()
        {
            yield return new ExpressionCase
            {
                Id = "inference-conflict",
                Text = "PostingDate >= @x and Amount > @x",
                Mode = QueryexMode.Filter,
                Diagnostics = ["QX3007"],
            };
        }

        /// <summary>What happens at each ceiling.</summary>
        /// <returns>The cases.</returns>
        private static IEnumerable<ExpressionCase> Ceilings()
        {
            yield return new ExpressionCase
            {
                Id = "limit-input-length",
                Text = new string(' ', 40) + "Amount",
                Limits = QueryexLimits.Default with { MaxInputLength = 8 },
                Diagnostics = ["QX5001"],
            };

            yield return new ExpressionCase
            {
                Id = "limit-tokens",
                Text = "Amount + Amount + Amount + Amount",
                Limits = QueryexLimits.Default with { MaxTokens = 4 },
                Diagnostics = ["QX5002"],
            };

            yield return new ExpressionCase
            {
                Id = "limit-depth",
                Text = "((((((Amount))))))",
                Limits = QueryexLimits.Default with { MaxSyntaxDepth = 3 },
                Diagnostics = ["QX5003"],
            };

            yield return new ExpressionCase
            {
                Id = "limit-nodes",
                Text = "Amount + Amount + Amount + Amount + Amount",
                Limits = QueryexLimits.Default with { MaxTypedNodes = 4 },
                Diagnostics = ["QX5004"],
            };

            yield return new ExpressionCase
            {
                Id = "limit-list-items",
                Text = "Amount, Amount, Amount, Amount",
                Limits = QueryexLimits.Default with { MaxListItems = 2 },
                Diagnostics = ["QX5005"],
            };
        }

        /// <summary>A case that is expected to compile to a given type.</summary>
        /// <param name="id">The case's name.</param>
        /// <param name="text">The expression text.</param>
        /// <param name="type">The type it should have.</param>
        /// <returns>The case.</returns>
        private static ExpressionCase Good(string id, string text, QueryexType type)
        {
            return new ExpressionCase { Id = id, Text = text, Type = type };
        }

        /// <summary>A case that is expected to compile to a given nullity.</summary>
        /// <param name="id">The case's name.</param>
        /// <param name="text">The expression text.</param>
        /// <param name="nullity">The nullity it should have.</param>
        /// <returns>The case.</returns>
        private static ExpressionCase Nullity(string id, string text, QueryexNullity nullity)
        {
            return new ExpressionCase { Id = id, Text = text, Nullity = nullity };
        }

        /// <summary>A case that only has to compile, because its point is what it computes.</summary>
        /// <param name="id">The case's name.</param>
        /// <param name="text">The expression text.</param>
        /// <returns>The case.</returns>
        private static ExpressionCase Runs(string id, string text)
        {
            return new ExpressionCase { Id = id, Text = text };
        }

        /// <summary>A case that is expected to report a given diagnostic.</summary>
        /// <param name="id">The case's name.</param>
        /// <param name="text">The expression text.</param>
        /// <param name="code">The code it should report.</param>
        /// <returns>The case.</returns>
        private static ExpressionCase Bad(string id, string text, string code)
        {
            return new ExpressionCase { Id = id, Text = text, Diagnostics = [code] };
        }
    }
}
