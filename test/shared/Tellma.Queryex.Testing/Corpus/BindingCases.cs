// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data.SqlTypes;
using System.Globalization;
using Tellma.Core.Queryex;
using Tellma.Queryex.Testing.Schema;
using Tellma.Queryex.Testing.Semantics;

namespace Tellma.Queryex.Testing.Corpus
{
    /// <summary>
    ///     One invoice filter with parameters of every type, and what a caller gives each of them:
    ///     a value, the absent value, or nothing at all.
    /// </summary>
    /// <param name="Id">A name for the case, which says which seed and attempt made it.</param>
    /// <param name="Filter">The filter text, over invoices.</param>
    /// <param name="Values">The arguments, as the CLR values a host passes, null for the absent value.</param>
    /// <param name="Omitted">The parameters left out.</param>
    public sealed record BindingCase(
        string Id,
        string Filter,
        IReadOnlyDictionary<string, object?> Values,
        IReadOnlyList<string> Omitted)
    {
        /// <summary>The options that bind this case's arguments into its text.</summary>
        public ParameterBindingOptions Options => new()
        {
            Mode = QueryexMode.Filter,
            QueryexVersion = QueryexLanguage.Version,
            Parameters = BindingCases.Parameters,
            Values = Values,
            Omitted = Omitted,
        };

        /// <summary>The query over this case's filter, with its parameters still in it.</summary>
        public QuerySpec Spec => Query(Filter);

        /// <summary>
        ///     The context that answers the query with these arguments: each one as a language value,
        ///     and the omitted parameters left out — and given a value all the same, so a reading that
        ///     used one answers differently.
        /// </summary>
        /// <param name="basis">The context to start from.</param>
        /// <returns>The context.</returns>
        public InterpreterContext Context(InterpreterContext basis)
        {
            ArgumentNullException.ThrowIfNull(basis);

            Dictionary<string, QxValue> parameters = new(StringComparer.OrdinalIgnoreCase);
            foreach (QueryexParameterDeclaration declaration in BindingCases.Parameters)
            {
                parameters[declaration.Name] = Values.TryGetValue(declaration.Name, out object? value)
                    ? BindingCases.ToValue(declaration.Type, value)
                    : BindingCases.Stand(declaration.Type);
            }

            return basis with
            {
                Parameters = parameters,
                Omitted = Omitted.ToHashSet(StringComparer.OrdinalIgnoreCase),
            };
        }

        /// <summary>The query over a filter: every invoice it admits, by key, in key order.</summary>
        /// <param name="filter">The filter text.</param>
        /// <returns>The query.</returns>
        public static QuerySpec Query(string filter)
        {
            ArgumentNullException.ThrowIfNull(filter);

            return new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf(filter),
                OrderBy = "Id",
            };
        }
    }

    /// <summary>
    ///     Makes up invoice filters with parameters, and arguments for them, the same ones every time.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         What binding arguments into a text has to preserve is meaning: the text it prints,
    ///         run with no arguments, admits exactly the rows the original admits with them. So the
    ///         atoms compare the fixture's columns of every type with parameters of that type, and
    ///         the values are drawn mostly from the fixture's own rows, so that comparisons fall on
    ///         both sides of the boundary rather than all on one.
    ///     </para>
    ///     <para>
    ///         Each parameter is given a value, the absent value, or left out, and the atoms are
    ///         combined under every connective, so folding meets each polarity and negative numbers
    ///         land under each operator that can sit around them.
    ///     </para>
    /// </remarks>
    public static class BindingCases
    {
        /// <summary>The parameters every case declares, at least one of each type that has a value.</summary>
        public static IReadOnlyList<QueryexParameterDeclaration> Parameters { get; } =
        [
            new QueryexParameterDeclaration("Number", QueryexType.QxNumeric, IsNotNull: false),
            new QueryexParameterDeclaration("Other", QueryexType.QxNumeric, IsNotNull: false),
            new QueryexParameterDeclaration("Text", QueryexType.QxString, IsNotNull: false),
            new QueryexParameterDeclaration("Flag", QueryexType.QxBool, IsNotNull: false),
            new QueryexParameterDeclaration("Day", QueryexType.QxDate, IsNotNull: false),
            new QueryexParameterDeclaration("Moment", QueryexType.QxDateTime, IsNotNull: false),
            new QueryexParameterDeclaration("Instant", QueryexType.QxDateTimeOffset, IsNotNull: false),
            new QueryexParameterDeclaration("Key", QueryexType.QxGuid, IsNotNull: false),
        ];

        /// <summary>
        ///     Measures of one aggregation each, over invoices, with the same aggregation written
        ///     without its condition.
        /// </summary>
        /// <remarks>
        ///     What a measure aggregates over every row has to equal what the bare aggregation
        ///     aggregates over the rows the measure's condition admits. Every aggregation is here,
        ///     conditions of every parameter type, one with no condition at all and one that admits
        ///     no row.
        /// </remarks>
        public static IReadOnlyList<(string Measure, string Unconditioned)> Measures { get; } =
        [
            ("sum(Amount, CustomerId = @Number)", "sum(Amount)"),
            ("count(Id, PostingDate >= @Day and IsPosted)", "count(Id)"),
            ("avg(Amount, Code in (@Text, 'INV-3') or Amount < @Other)", "avg(Amount)"),
            ("min(PostingDate, ApprovedAt < @Instant)", "min(PostingDate)"),
            ("max(Amount, not (BatchId = @Key))", "max(Amount)"),
            ("sum(Amount * 2, PostedOn > @Moment or -@Number > Amount)", "sum(Amount * 2)"),
            ("count(CustomerId, IsApproved != @Flag)", "count(CustomerId)"),
            ("max(Code, not (Amount > @Number and CustomerId = @Other))", "max(Code)"),
            ("sum(Amount)", "sum(Amount)"),
            ("count(Id, false)", "count(Id)"),
        ];

        /// <summary>The comparison operators an atom draws from.</summary>
        private static readonly string[] Comparisons = ["=", "!=", "<", "<=", ">", ">="];

        /// <summary>The atoms, each comparing a column with one parameter; <c>{op}</c> is any comparison.</summary>
        private static readonly string[] Atoms =
        [
            "Amount {op} @Number",
            "@Number {op} Count",
            "CustomerId in (@Number, @Other)",
            "Amount * 2 > @Number",
            "-@Number < Amount",
            "Count - @Other >= 0",
            "CustomerId = @Other",
            "Code = @Text",
            "Memo != @Text",
            "Code in (@Text, 'INV-2')",
            "@Text = Memo",
            "IsPosted = @Flag",
            "IsApproved != @Flag",
            "PostingDate {op} @Day",
            "DueDate < @Day",
            "PostedOn {op} @Moment",
            "DueOn >= @Moment",
            "ApprovedAt {op} @Instant",
            "PostedAt < @Instant",
            "BatchId = @Key",
            "ExternalId != @Key",
            "ExternalId in (@Key)",
        ];

        /// <summary>Generates cases.</summary>
        /// <param name="seed">Which run of cases to make.</param>
        /// <param name="count">How many.</param>
        /// <returns>The cases.</returns>
        public static IReadOnlyList<BindingCase> Generate(int seed, int count)
        {
            Random random = new(seed);
            IReadOnlyList<LedgerRow> rows = LedgerData.Rows(LedgerFixture.Invoice);

            List<BindingCase> cases = [];
            for (int attempt = 0; attempt < count; attempt++)
            {
                string filter = Predicate(random, depth: 1 + (attempt % 3));

                // A value, the absent value or nothing, for each parameter whether or not the
                // filter mentions it: the ones it does not mention must change nothing.
                Dictionary<string, object?> values = new(StringComparer.OrdinalIgnoreCase);
                List<string> omitted = [];
                foreach (QueryexParameterDeclaration declaration in Parameters)
                {
                    int roll = random.Next(10);
                    if (roll < 2)
                    {
                        omitted.Add(declaration.Name);
                    }
                    else
                    {
                        values[declaration.Name] = roll < 3 ? null : Draw(random, declaration.Type, rows);
                    }
                }

                cases.Add(new BindingCase(
                    seed.ToString(CultureInfo.InvariantCulture) + "/" + attempt.ToString(CultureInfo.InvariantCulture),
                    filter,
                    values,
                    omitted));
            }

            return cases;
        }

        /// <summary>A parameter's argument as the language value the second reading is given.</summary>
        /// <param name="type">The declared type.</param>
        /// <param name="value">The CLR value, or null for the absent value.</param>
        /// <returns>The value.</returns>
        public static QxValue ToValue(QueryexType type, object? value)
        {
            return value switch
            {
                null => QxValue.Absent(type),
                decimal number => QxValue.Number(new SqlDecimal(number)),
                int number => QxValue.Number(number),
                long number => QxValue.Number(number),
                _ => QxValue.Of(type, value),
            };
        }

        /// <summary>The value an omitted parameter is given anyway, which no reading may use.</summary>
        /// <param name="type">The declared type.</param>
        /// <returns>A value of that type that some fixture rows match and others do not.</returns>
        public static QxValue Stand(QueryexType type)
        {
            return type switch
            {
                QueryexType.QxNumeric => QxValue.Number(2),
                QueryexType.QxString => QxValue.Text("INV-3"),
                QueryexType.QxBool => QxValue.Flag(true),
                QueryexType.QxDate => QxValue.Date(new DateOnly(2024, 5, 1)),
                QueryexType.QxDateTime => QxValue.Moment(new DateTime(2024, 5, 1, 12, 0, 0)),
                QueryexType.QxDateTimeOffset => QxValue.Instant(new DateTimeOffset(2024, 5, 1, 12, 0, 0, TimeSpan.Zero)),
                QueryexType.QxGuid => QxValue.Identifier(Guid.Empty),
                QueryexType.QxHierarchyId or QueryexType.QxGeography => QxValue.Absent(type),
                _ => QxValue.Absent(type),
            };
        }

        /// <summary>Makes up a predicate.</summary>
        /// <param name="random">The source of choices.</param>
        /// <param name="depth">How many connectives deep it may go.</param>
        /// <returns>The predicate's text.</returns>
        private static string Predicate(Random random, int depth)
        {
            if (depth == 0 || random.Next(4) == 0)
            {
                string atom = Atoms[random.Next(Atoms.Length)];
                return atom.Replace("{op}", Comparisons[random.Next(Comparisons.Length)], StringComparison.Ordinal);
            }

            return random.Next(5) switch
            {
                0 => "not (" + Predicate(random, depth - 1) + ")",
                1 or 2 => "(" + Predicate(random, depth - 1) + ") and (" + Predicate(random, depth - 1) + ")",
                _ => "(" + Predicate(random, depth - 1) + ") or (" + Predicate(random, depth - 1) + ")",
            };
        }

        /// <summary>Draws a value of a type, mostly from the fixture's rows and a step either side.</summary>
        /// <param name="random">The source of choices.</param>
        /// <param name="type">The type.</param>
        /// <param name="rows">The invoices.</param>
        /// <returns>The CLR value a host would pass.</returns>
        private static object Draw(Random random, QueryexType type, IReadOnlyList<LedgerRow> rows)
        {
            LedgerRow row = rows[random.Next(rows.Count)];
            int step = random.Next(3) - 1;

            return type switch
            {
                // Every CLR type a number arrives as, negative values and trailing zeros included.
                QueryexType.QxNumeric => random.Next(4) switch
                {
                    0 => row["Amount"].AsNumber.Value + step,
                    1 => (int)row["Count"].AsNumber.Value + step,
                    2 => (long)row["CreatedById"].AsNumber.Value,
                    _ => new[] { -0.5m, 0m, 125.2500m, -250.25m, 2.0m }[random.Next(5)],
                },
                QueryexType.QxString => new[] { "INV-1", "INV-4", "alpha memo", "ALPHA MEMO", "O'Brien", string.Empty }[random.Next(6)],
                QueryexType.QxBool => random.Next(2) == 0,
                QueryexType.QxDate => row["PostingDate"].AsDate.AddDays(step),
                QueryexType.QxDateTime => row["PostedOn"].AsMoment.AddTicks(step * 1_234_567),
                QueryexType.QxDateTimeOffset => random.Next(3) switch
                {
                    0 => row["ApprovedAt"].AsInstant.AddTicks(step),
                    1 => row["ApprovedAt"].AsInstant.ToOffset(TimeSpan.FromMinutes(-330)),
                    _ => row["ApprovedAt"].AsInstant.ToOffset(TimeSpan.Zero).AddMinutes(step * 30),
                },
                // The first invoice carries every optional column, its external key included.
                QueryexType.QxGuid => random.Next(3) switch
                {
                    0 => Guid.Empty,
                    1 => rows[0]["ExternalId"].AsIdentifier,
                    _ => row["BatchId"].AsIdentifier,
                },
                QueryexType.QxHierarchyId or QueryexType.QxGeography => throw new ArgumentOutOfRangeException(nameof(type)),
                _ => throw new ArgumentOutOfRangeException(nameof(type)),
            };
        }
    }
}
