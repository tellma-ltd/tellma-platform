// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data.SqlTypes;
using Tellma.Queryex.Testing.Schema;

namespace Tellma.Core.Queryex.Tests.Measures
{
    /// <summary>
    ///     Compiles measures over the fact fixture and evaluates them over partials the way a host
    ///     loads them: one typed array per column, one row at a time.
    /// </summary>
    internal sealed class MeasureHarness
    {
        /// <summary>The engine under test.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>The decimal type each column's partials take, as the backend derives them.</summary>
        private static readonly (string Column, QueryexNumericType Sum, QueryexNumericType? Extreme)[] ColumnTypes =
        [
            ("Amount", new(38, 4), new(19, 4)),
            ("Cost", new(38, 4), new(19, 4)),
            ("Rate", new(38, 6), new(9, 6)),
            ("Fine", new(38, 10), new(28, 10)),
            ("Whole", new(38, 0), new(10, 0)),
            ("Tiny", new(38, 0), null),
            ("Small", new(38, 0), null),
            ("Qty", new(38, 0), null),
            ("Big", new(38, 0), null),
        ];

        /// <summary>Initializes a harness over compiled measures.</summary>
        /// <param name="query">The compiled query that carries them.</param>
        private MeasureHarness(CompiledQuery query)
        {
            Query = query;
            Measures = query.Measures!;
            DecimalTypes = [.. Measures.Partials.Select(DefaultType)];
        }

        /// <summary>The compiled query.</summary>
        internal CompiledQuery Query { get; }

        /// <summary>The compiled measures.</summary>
        internal CompiledMeasures Measures { get; }

        /// <summary>The decimal types a run reports, parallel to the partials; replace one to vary a run.</summary>
        internal QueryexNumericType?[] DecimalTypes { get; }

        /// <summary>The declared parameters' values.</summary>
        internal Dictionary<string, object?> Arguments { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The value the user slot is bound with.</summary>
        internal object? UserId { get; set; } = 2;

        /// <summary>Compiles measures over the fact fixture, grouped by leaf.</summary>
        /// <param name="measures">The measures.</param>
        /// <param name="parameters">The parameters they may read.</param>
        /// <returns>The harness.</returns>
        internal static MeasureHarness Compile(string measures, params QueryexParameterDeclaration[] parameters)
        {
            QueryexResult<CompiledQuery> result = Engine.CompileQuery(
                new QuerySpec
                {
                    Root = MeasureFixture.Fact,
                    Aggregate = true,
                    Select = "Leaf",
                    Measures = measures,
                },
                new QueryCompilationOptions
                {
                    QueryexVersion = QueryexLanguage.Version,
                    Schema = MeasureFixture.Schema,
                    Parameters = parameters,
                });

            Assert.True(
                result.Succeeded,
                string.Join(", ", result.Diagnostics.Select(d => d.Code + "@" + d.Location)));
            return new MeasureHarness(result.Value);
        }

        /// <summary>Fixes the types for one run.</summary>
        /// <returns>The run's evaluator.</returns>
        internal MeasureEvaluator Prepare()
        {
            return Measures.Prepare(new MeasureContext(
                DecimalTypes,
                Arguments,
                new DateOnly(2024, 6, 15),
                new DateTimeOffset(2024, 6, 15, 9, 30, 0, TimeSpan.FromHours(3)),
                UserId));
        }

        /// <summary>Evaluates every measure over one cell loaded with the given rows.</summary>
        /// <param name="rows">Per partial, the values its rows contribute; an absent value contributes nothing.</param>
        /// <returns>Each measure's value.</returns>
        internal MeasureValue[] Evaluate(params object?[][] rows)
        {
            return [.. Enumerable.Range(0, Measures.Items.Count).Select(measure => EvaluateOne(measure, rows))];
        }

        /// <summary>Evaluates one measure over one cell loaded with the given rows.</summary>
        /// <param name="measure">The measure's position.</param>
        /// <param name="rows">Per partial, the values its rows contribute; an absent value contributes nothing.</param>
        /// <returns>The measure's value.</returns>
        internal MeasureValue EvaluateOne(int measure, params object?[][] rows)
        {
            MeasureEvaluator evaluator = Prepare();
            PartialTable table = evaluator.CreateTable(1);
            int cell = table.AddCell();
            for (int partial = 0; partial < rows.Length; partial++)
            {
                Load(table, cell, partial, rows[partial]);
            }

            return evaluator.Evaluate(table, cell, measure);
        }

        /// <summary>Evaluates the first measure over one cell loaded with one value per partial.</summary>
        /// <param name="values">Per partial, the one value its row contributes, or null for none.</param>
        /// <returns>The first measure's value.</returns>
        internal MeasureValue First(params object?[] values)
        {
            return Evaluate([.. values.Select(value => new[] { value })])[0];
        }

        /// <summary>The ordinal of the partial whose column has the given text.</summary>
        /// <param name="text">The partial's text as its column reports it.</param>
        /// <returns>The partial's position.</returns>
        internal int Partial(string text)
        {
            for (int index = 0; index < Measures.Partials.Count; index++)
            {
                if (Query.Columns[Measures.Partials[index].Ordinal].Text == text)
                {
                    return index;
                }
            }

            throw new InvalidOperationException("No partial is " + text + ".");
        }

        /// <summary>Loads rows of one partial into a cell, each through a one-element reader buffer.</summary>
        /// <param name="table">The table.</param>
        /// <param name="cell">The cell.</param>
        /// <param name="partial">The partial.</param>
        /// <param name="values">The rows' values; an absent one is skipped, as a host skips it.</param>
        internal static void Load(PartialTable table, int cell, int partial, IEnumerable<object?> values)
        {
            foreach (object? value in values)
            {
                if (value is not null)
                {
                    table.Add(cell, partial, Buffer(value), 0);
                }
            }
        }

        /// <summary>A reader buffer holding one value, typed as the reader types it.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The buffer.</returns>
        internal static Array Buffer(object value)
        {
            return value switch
            {
                decimal number => new[] { number },
                long number => new[] { number },
                int number => new[] { number },
                short number => new[] { number },
                byte number => new[] { number },
                bool flag => new[] { flag },
                DateOnly date => new[] { date },
                DateTime moment => new[] { moment },
                DateTimeOffset instant => new[] { instant },
                string text => new string?[] { text },
                _ => throw new ArgumentException("No reader buffer holds " + value.GetType().Name + "."),
            };
        }

        /// <summary>The exact digits and type of a number.</summary>
        /// <param name="number">The number.</param>
        /// <returns>The rendering: the digits at the number's scale, then its precision and scale.</returns>
        internal static string Typed(SqlDecimal number)
        {
            return number + " (" + number.Precision + "," + number.Scale + ")";
        }

        /// <summary>The type the backend gives a partial's column, as the fixture's columns determine it.</summary>
        /// <param name="partial">The partial.</param>
        /// <returns>The decimal type, or null for a partial that is not a decimal.</returns>
        private QueryexNumericType? DefaultType(MeasurePartial partial)
        {
            if (partial.Type != QueryexType.QxNumeric || partial.Kind == MeasurePartialKind.Count)
            {
                return null;
            }

            string text = Query.Columns[partial.Ordinal].Text;
            foreach ((string column, QueryexNumericType sum, QueryexNumericType? extreme) in ColumnTypes)
            {
                if (text.Contains(column, StringComparison.Ordinal))
                {
                    return partial.Kind == MeasurePartialKind.Sum ? sum : extreme;
                }
            }

            throw new InvalidOperationException("The harness knows no type for " + text + ".");
        }
    }
}
