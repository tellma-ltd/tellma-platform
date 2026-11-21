// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex.IntegrationTests
{
    /// <summary>
    ///     Writes random measures over the fact fixture from the constructs a host can evaluate
    ///     itself, from a fixed seed so every run checks the same ones.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The operands of <c>and</c> and <c>or</c> are comparisons of bare partials and literals,
    ///         which can never fail. The evaluator stops at the first operand that decides, while the
    ///         backend promises no order, so an operand that can fail would fail on one side only —
    ///         a difference the language documents rather than one this check should find.
    ///     </para>
    ///     <para>
    ///         A divisor is never the literal zero: that measure fails everywhere, and a generator that
    ///         wrote many of them would be checking the error path five hundred times over.
    ///     </para>
    /// </remarks>
    /// <param name="seed">The seed.</param>
    internal sealed class MeasureGenerator(int seed)
    {
        /// <summary>The deepest nesting a generated measure reaches.</summary>
        internal const int MaxDepth = 4;

        /// <summary>The numeric partials, of every kind and width.</summary>
        private static readonly string[] NumericPartials =
        [
            "sum(Amount)", "sum(Cost)", "sum(Rate)", "sum(Fine)", "sum(Qty)", "sum(Tiny)", "sum(Small)", "sum(Big)",
            "sum(Whole)", "sum(Amount - Cost)", "sum(Amount, Flag)", "sum(Qty * 2)",
            "count()", "count(Cost)", "count(Rate)", "count(Label, not Flag)",
            "avg(Amount)", "avg(Rate)", "avg(Qty)", "avg(Fine)", "avg(Tiny)",
            "min(Tiny)", "max(Tiny)", "min(Small)", "max(Small)", "min(Qty)", "max(Qty)", "min(Big)", "max(Big)",
            "min(Whole)", "max(Whole)", "min(Amount)", "max(Amount)", "min(Rate)", "max(Rate)", "min(Fine)",
            "max(Fine)", "min(Cost)", "max(Cost)",
        ];

        /// <summary>The numbers that come from dates.</summary>
        private static readonly string[] DateNumbers =
        [
            "year(max(Day))", "quarter(min(Day))", "month(max(Due), 'gc')", "day(min(Due))",
            "diffDays(min(Day), max(Day))", "diffHours(min(Due), max(Day))", "diffMinutes(min(Day), max(Due))",
            "diffSeconds(min(Day), max(Day))", "diffDays(max(Day), today())",
        ];

        /// <summary>The numeric literals, at several written scales.</summary>
        private static readonly string[] Literals = ["0", "1", "2", "3", "7", "100", "0.5", "1.25", "1.0000", "0.001", "12345.678"];

        /// <summary>The dates a measure can produce.</summary>
        private static readonly string[] Dates = ["max(Day)", "min(Day)", "max(Due)", "min(Due)", "today()"];

        /// <summary>The comparison operators.</summary>
        private static readonly string[] Comparisons = ["=", "!=", "<", "<=", ">", ">="];

        /// <summary>The generator.</summary>
        private readonly Random _random = new(seed);

        /// <summary>Writes one measure.</summary>
        /// <returns>The measure's text.</returns>
        internal string Next()
        {
            string measure;
            do
            {
                // Mostly numbers, which is where the type algebra lives; the rest are the other
                // types a measure can produce.
                measure = _random.Next(10) switch
                {
                    0 => Truth(MaxDepth - 1, safe: false),
                    1 => Date(MaxDepth - 1),
                    2 => Text(MaxDepth - 1),
                    _ => Number(MaxDepth),
                };
            }
            while (!Aggregates(measure));

            return measure;
        }

        /// <summary>Whether a measure aggregates anything, which a measure has to.</summary>
        /// <param name="measure">The measure.</param>
        /// <returns>True when it calls an aggregation.</returns>
        private static bool Aggregates(string measure)
        {
            return measure.Contains("sum(", StringComparison.Ordinal)
                || measure.Contains("count(", StringComparison.Ordinal)
                || measure.Contains("avg(", StringComparison.Ordinal)
                || measure.Contains("min(", StringComparison.Ordinal)
                || measure.Contains("max(", StringComparison.Ordinal);
        }

        /// <summary>A numeric expression.</summary>
        /// <param name="depth">How much deeper it may nest.</param>
        /// <returns>The text.</returns>
        private string Number(int depth)
        {
            if (depth <= 1 || _random.Next(4) == 0)
            {
                return NumberLeaf();
            }

            int below = depth - 1;
            return _random.Next(13) switch
            {
                0 or 1 => "(" + Number(below) + " + " + Number(below) + ")",
                2 => "(" + Number(below) + " - " + Number(below) + ")",
                3 or 4 => "(" + Number(below) + " * " + Number(below) + ")",
                5 or 6 => "(" + Number(below) + " / " + Divisor(below) + ")",
                7 => "-(" + Number(below) + ")",
                8 => Pick("abs", "floor", "ceiling") + "(" + Number(below) + ")",
                9 => "round(" + Number(below) + ", " + Pick("0", "1", "2", "4") + ")",
                10 => "if(" + Truth(below, safe: false) + ", " + Number(below) + ", " + Number(below) + ")",
                11 => "coalesce(" + Number(below) + ", " + Number(below) + ")",
                _ => "if(" + Number(below) + " = 0, null, " + Number(below) + ")",
            };
        }

        /// <summary>A divisor: any numeric expression but the literal zero.</summary>
        /// <param name="depth">How much deeper it may nest.</param>
        /// <returns>The text.</returns>
        private string Divisor(int depth)
        {
            string divisor;
            do
            {
                divisor = Number(depth);
            }
            while (divisor == "0");

            return divisor;
        }

        /// <summary>A numeric leaf: a partial, a number from dates, a literal, a parameter or the user.</summary>
        /// <returns>The text.</returns>
        private string NumberLeaf()
        {
            return _random.Next(20) switch
            {
                < 12 => Pick(NumericPartials),
                < 14 => Pick(DateNumbers),
                < 18 => Pick(Literals),
                18 => "@P",
                _ => "me()",
            };
        }

        /// <summary>A truth value.</summary>
        /// <param name="depth">How much deeper it may nest.</param>
        /// <param name="safe">Whether it must be unable to fail, as an operand of a connective.</param>
        /// <returns>The text.</returns>
        private string Truth(int depth, bool safe)
        {
            if (depth <= 1 || _random.Next(3) == 0)
            {
                return TruthLeaf(safe);
            }

            int below = depth - 1;
            return _random.Next(5) switch
            {
                0 => "(" + Truth(below, safe: true) + " and " + Truth(below, safe: true) + ")",
                1 => "(" + Truth(below, safe: true) + " or " + Truth(below, safe: true) + ")",
                2 => "not (" + Truth(below, safe) + ")",
                3 when !safe => "(" + Number(below) + " " + Pick(Comparisons) + " " + Number(below) + ")",
                _ => TruthLeaf(safe),
            };
        }

        /// <summary>A truth value with no connective in it.</summary>
        /// <param name="safe">Whether it must be unable to fail.</param>
        /// <returns>The text.</returns>
        private string TruthLeaf(bool safe)
        {
            string number = safe ? Pick(NumericPartials) : NumberLeaf();
            return _random.Next(8) switch
            {
                0 => Pick(NumericPartials) + " is " + Pick("null", "not null"),
                1 => number + " in (" + Pick(Literals) + ", " + Pick(Literals) + ")",
                2 => Pick(Dates) + " " + Pick(Comparisons) + " " + Pick(Dates),
                3 => "max(Flag) = " + Pick("true", "false", "min(Flag)"),
                4 => "max(Instant) " + Pick(Comparisons) + " " + Pick("min(Instant)", "now()"),
                5 => "min(Stamp) " + Pick(Comparisons) + " max(Stamp)",
                6 => Pick("max(Day)", "min(Due)") + " " + Pick(Comparisons) + " '2024-02-29'",
                _ => number + " " + Pick(Comparisons) + " " + (safe ? Pick(Literals) : NumberLeaf()),
            };
        }

        /// <summary>A date.</summary>
        /// <param name="depth">How much deeper it may nest.</param>
        /// <returns>The text.</returns>
        private string Date(int depth)
        {
            return depth <= 1 || _random.Next(3) == 0
                ? Pick(Dates)
                : _random.Next(2) == 0
                    ? "if(" + Truth(depth - 1, safe: false) + ", " + Date(depth - 1) + ", " + Date(depth - 1) + ")"
                    : "coalesce(" + Date(depth - 1) + ", " + Date(depth - 1) + ")";
        }

        /// <summary>Text: a least or greatest plain text, or a choice of words.</summary>
        /// <param name="depth">How much deeper it may nest.</param>
        /// <returns>The text.</returns>
        private string Text(int depth)
        {
            return depth <= 1 || _random.Next(2) == 0
                ? Pick("min(Label)", "max(Label)")
                : "if(" + Truth(depth - 1, safe: false) + ", " + Pick("'High'", "max(Label)") + ", " + Pick("'Low'", "min(Label)") + ")";
        }

        /// <summary>One of several choices.</summary>
        /// <param name="choices">The choices.</param>
        /// <returns>The one picked.</returns>
        private string Pick(params string[] choices)
        {
            return choices[_random.Next(choices.Length)];
        }
    }
}
