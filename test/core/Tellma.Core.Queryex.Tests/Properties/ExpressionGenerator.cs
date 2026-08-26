// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;

namespace Tellma.Core.Queryex.Tests.Properties
{
    /// <summary>
    ///     Makes expressions up, the same ones every time.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Written out rather than taken from a library, because what these checks need is not
    ///         sophistication but repeatability: the same seed produces the same expressions on
    ///         every machine, so a failure can be reproduced by quoting a number.
    ///     </para>
    ///     <para>
    ///         Two shapes are generated. Well-formed expressions over the fixture's own columns,
    ///         which should compile and survive a round trip through the printer; and rubbish, which
    ///         should come back as diagnostics rather than as an exception or a hang.
    ///     </para>
    /// </remarks>
    internal sealed class ExpressionGenerator
    {
        /// <summary>The columns the generator draws on, by type.</summary>
        private static readonly string[] Numbers = ["Amount", "Rate", "Count", "CreatedById"];

        /// <summary>The text columns the generator draws on.</summary>
        private static readonly string[] Texts = ["Memo", "Notes", "Code", "Customer.Name"];

        /// <summary>The date columns the generator draws on.</summary>
        private static readonly string[] Dates = ["PostingDate", "DueDate"];

        /// <summary>The truth-valued columns the generator draws on.</summary>
        private static readonly string[] Flags = ["IsPosted", "IsApproved", "[not]"];

        /// <summary>The characters rubbish is made of.</summary>
        private const string Alphabet = "abcXY_.,()[]'\"+-*/%<>=!&|@ 09\t";

        /// <summary>The source of the choices, seeded so a run repeats.</summary>
        private readonly Random _random;

        /// <summary>Initializes a generator.</summary>
        /// <param name="seed">The seed.</param>
        internal ExpressionGenerator(int seed)
        {
            _random = new Random(seed);
        }

        /// <summary>An expression that should compile.</summary>
        /// <param name="depth">How deeply it may nest.</param>
        /// <returns>The expression text.</returns>
        internal string Predicate(int depth)
        {
            return depth <= 0 ? SimplePredicate() : CompoundPredicate(depth);
        }

        /// <summary>A predicate with nothing nested inside it.</summary>
        /// <returns>The expression text.</returns>
        private string SimplePredicate()
        {
            return _random.Next(3) switch
            {
                0 => Pick(Flags),
                1 => Number(0) + Operator() + Number(0),
                _ => Text(0) + " = " + Text(0),
            };
        }

        /// <summary>A predicate with something nested inside it.</summary>
        /// <param name="depth">How deeply it may nest.</param>
        /// <returns>The expression text.</returns>
        private string CompoundPredicate(int depth)
        {
            return _random.Next(6) switch
            {
                0 => "(" + Predicate(depth - 1) + " and " + Predicate(depth - 1) + ")",
                1 => "(" + Predicate(depth - 1) + " or " + Predicate(depth - 1) + ")",
                2 => "not " + Predicate(depth - 1),
                3 => Number(depth - 1) + Operator() + Number(depth - 1),
                4 => Pick(Dates) + " is null",
                _ => "contains(" + Text(depth - 1) + ", " + Text(depth - 1) + ")",
            };
        }

        /// <summary>An expression that should compile to a number.</summary>
        /// <param name="depth">How deeply it may nest.</param>
        /// <returns>The expression text.</returns>
        internal string Number(int depth)
        {
            return depth <= 0 ? SimpleNumber() : CompoundNumber(depth);
        }

        /// <summary>A number with nothing nested inside it.</summary>
        /// <returns>The expression text.</returns>
        private string SimpleNumber()
        {
            return _random.Next(2) == 0
                ? Pick(Numbers)
                : _random.Next(1000).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>A number with something nested inside it.</summary>
        /// <param name="depth">How deeply it may nest.</param>
        /// <returns>The expression text.</returns>
        private string CompoundNumber(int depth)
        {
            return _random.Next(6) switch
            {
                0 => "(" + Number(depth - 1) + " + " + Number(depth - 1) + ")",
                1 => "(" + Number(depth - 1) + " * " + Number(depth - 1) + ")",
                2 => "(" + Number(depth - 1) + " / " + Number(depth - 1) + ")",
                3 => "abs(" + Number(depth - 1) + ")",
                4 => "coalesce(" + Number(depth - 1) + ", " + Number(depth - 1) + ")",
                _ => "year(" + Pick(Dates) + ")",
            };
        }

        /// <summary>An expression that should compile to text.</summary>
        /// <param name="depth">How deeply it may nest.</param>
        /// <returns>The expression text.</returns>
        internal string Text(int depth)
        {
            return depth <= 0 ? SimpleText() : CompoundText(depth);
        }

        /// <summary>Text with nothing nested inside it.</summary>
        /// <returns>The expression text.</returns>
        private string SimpleText()
        {
            return _random.Next(2) == 0 ? Pick(Texts) : "'" + Letters(_random.Next(4)) + "'";
        }

        /// <summary>Text with something nested inside it.</summary>
        /// <param name="depth">How deeply it may nest.</param>
        /// <returns>The expression text.</returns>
        private string CompoundText(int depth)
        {
            return _random.Next(4) switch
            {
                0 => "(" + Text(depth - 1) + " || " + Text(depth - 1) + ")",
                1 => "upper(" + Text(depth - 1) + ")",
                2 => "trim(" + Text(depth - 1) + ")",
                _ => "coalesce(" + Text(depth - 1) + ", " + Text(depth - 1) + ")",
            };
        }

        /// <summary>Rubbish, which the engine has to survive rather than accept.</summary>
        /// <param name="length">How long it should be.</param>
        /// <returns>The text.</returns>
        internal string Rubbish(int length)
        {
            StringBuilder text = new(length);
            for (int index = 0; index < length; index++)
            {
                text.Append(Alphabet[_random.Next(Alphabet.Length)]);
            }

            return text.ToString();
        }

        /// <summary>One of a set of choices.</summary>
        /// <param name="choices">The choices.</param>
        /// <returns>The chosen one.</returns>
        private string Pick(string[] choices)
        {
            return choices[_random.Next(choices.Length)];
        }

        /// <summary>A comparison operator.</summary>
        /// <returns>The operator, with spaces around it.</returns>
        private string Operator()
        {
            return _random.Next(6) switch
            {
                0 => " = ",
                1 => " != ",
                2 => " < ",
                3 => " <= ",
                4 => " > ",
                _ => " >= ",
            };
        }

        /// <summary>A short run of letters.</summary>
        /// <param name="length">How many.</param>
        /// <returns>The letters.</returns>
        private string Letters(int length)
        {
            StringBuilder text = new(length);
            for (int index = 0; index < length; index++)
            {
                text.Append((char)('a' + _random.Next(26)));
            }

            return text.ToString();
        }
    }
}
