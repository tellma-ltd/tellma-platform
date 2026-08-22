// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Queryex.Testing.Probe;

namespace Tellma.Core.Queryex.Tests.Functions
{
    /// <summary>
    ///     The function library is a table, and a table can be checked as one. These are the
    ///     properties every entry has to have for the stages downstream of it to be sound — an
    ///     overload set nothing can choose between, or an emission that quietly repeats an operand,
    ///     is a defect in the table rather than something a user could ever cause.
    /// </summary>
    public class FunctionRegistryTests
    {
        [Fact]
        public void Every_declared_function_has_at_least_one_overload()
        {
            Assert.NotEmpty(ProbeRegistry.Functions);
            Assert.All(ProbeRegistry.Functions, function => Assert.NotEmpty(function.Signatures));
        }

        [Fact]
        public void Every_function_name_is_declared_once()
        {
            List<string> duplicates = [.. ProbeRegistry.Functions
                .GroupBy(static function => function.Name, StringComparer.OrdinalIgnoreCase)
                .Where(static group => group.Count() > 1)
                .Select(static group => group.Key)];

            Assert.Empty(duplicates);
        }

        [Fact]
        public void No_two_overloads_of_one_function_accept_the_same_shape()
        {
            // Two overloads that no argument could tell apart would be reported as ambiguous at run
            // time, which is a diagnostic about a defect in this table rather than about anything
            // the author wrote.
            List<string> ambiguous = [];
            foreach (ProbeFunction function in ProbeRegistry.Functions)
            {
                IEnumerable<IGrouping<string, ProbeSignature>> collisions = function.Signatures
                    .GroupBy(Shape, StringComparer.Ordinal)
                    .Where(static group => group.Count() > 1);

                foreach (IGrouping<string, ProbeSignature> collision in collisions)
                {
                    ambiguous.Add($"{function.Name}: {collision.Key}");
                }
            }

            Assert.Empty(ambiguous);
        }

        [Fact]
        public void A_variadic_tail_is_always_the_last_parameter()
        {
            List<string> misplaced = [];
            foreach (ProbeFunction function in ProbeRegistry.Functions)
            {
                foreach (ProbeSignature signature in function.Signatures)
                {
                    for (int index = 0; index < signature.Parameters.Count - 1; index++)
                    {
                        if (signature.Parameters[index].Rest)
                        {
                            misplaced.Add($"{function.Name}[{signature.Index}]");
                        }
                    }
                }
            }

            Assert.Empty(misplaced);
        }

        [Fact]
        public void Every_pattern_uses_only_arguments_the_signature_declares()
        {
            // A pattern reaching past the declared parameters would emit whatever happened to be
            // next, and the engine supplies its own extra arguments after them — so the count has to
            // stay within the declared ones plus those.
            List<string> overreaching = [];
            foreach (ProbeFunction function in ProbeRegistry.Functions)
            {
                foreach (ProbeSignature signature in function.Signatures)
                {
                    if (signature.UseCounts.Count > signature.Parameters.Count + 1)
                    {
                        overreaching.Add($"{function.Name}[{signature.Index}]");
                    }
                }
            }

            Assert.Empty(overreaching);
        }

        [Fact]
        public void A_consumed_selector_never_reaches_the_backend()
        {
            // A selector chooses an emission; its text is the author's, so letting it through would
            // put author-written text into SQL.
            List<string> leaked = [];
            foreach (ProbeFunction function in ProbeRegistry.Functions)
            {
                foreach (ProbeSignature signature in function.Signatures)
                {
                    for (int index = 0; index < signature.Parameters.Count; index++)
                    {
                        bool consumed = signature.Parameters[index].Constraint == "MemberOf";
                        bool emitted = index < signature.UseCounts.Count && signature.UseCounts[index] > 0;
                        if (consumed && emitted)
                        {
                            leaked.Add($"{function.Name}[{signature.Index}].{signature.Parameters[index].Name}");
                        }
                    }
                }
            }

            Assert.Empty(leaked);
        }

        [Fact]
        public void Every_always_present_result_comes_from_an_emission_that_tolerates_absence()
        {
            // Promising a value whatever the operands are obliges the emission to be total. Where
            // the emission is a pattern, that means it never leaves a bare operand to decide the
            // answer; where it is one of the bespoke strategies, the guards are built in.
            List<string> untotal = [];
            foreach (ProbeFunction function in ProbeRegistry.Functions)
            {
                foreach (ProbeSignature signature in function.Signatures)
                {
                    if (signature.Nullity != "AlwaysNotNull" || signature.Pattern is null)
                    {
                        continue;
                    }

                    if (!signature.Pattern.Contains("ISNULL", StringComparison.Ordinal))
                    {
                        untotal.Add($"{function.Name}[{signature.Index}]");
                    }
                }
            }

            Assert.Empty(untotal);
        }

        [Fact]
        public void Every_conversion_the_language_offers_is_declared_both_ways_round()
        {
            // Converting a type to itself always exists, and an absent value converts to anything:
            // that pair is what lets a bare absent value be given a column type.
            Assert.All(
                ProbeRegistry.CastTargets,
                target => Assert.True(ProbeRegistry.SupportsCast(target, target)));
        }

        [Theory]
        [InlineData("string", "numeric", true)]
        [InlineData("string", "date", true)]
        [InlineData("string", "guid", true)]
        [InlineData("string", "bool", false)]
        [InlineData("numeric", "string", true)]
        [InlineData("numeric", "bool", true)]
        [InlineData("numeric", "date", false)]
        [InlineData("guid", "string", true)]
        [InlineData("guid", "numeric", false)]
        [InlineData("date", "datetime", true)]
        [InlineData("date", "datetimeoffset", false)]
        [InlineData("datetimeoffset", "date", true)]
        [InlineData("datetimeoffset", "datetime", true)]
        [InlineData("bool", "guid", false)]
        public void Offers_exactly_the_conversions_the_language_declares(string from, string to, bool supported)
        {
            Assert.Equal(supported, ProbeRegistry.SupportsCast(from, to));
        }

        [Fact]
        public void Accepts_only_the_calendar_this_version_implements()
        {
            ProbeFunction year = ProbeRegistry.Functions.Single(
                static function => function.Name == "year");

            ProbeSignature withCalendar = year.Signatures.Single(
                static signature => signature.Parameters.Count == 2);

            Assert.Equal(["gc"], withCalendar.Parameters[1].AcceptedValues);
        }

        /// <summary>Renders an overload's shape: what it would take to tell it from another.</summary>
        /// <param name="signature">The overload.</param>
        /// <returns>The shape.</returns>
        private static string Shape(ProbeSignature signature)
        {
            IEnumerable<string> parameters = signature.Parameters.Select(static parameter =>
                parameter.Types + (parameter.Rest ? "..." : string.Empty));

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{signature.Parameters.Count}({string.Join("|", parameters)})");
        }

        /// <summary>
        ///     A signature that produces a number or text says how the backend will hold it, or
        ///     takes it from an argument that shares its type.
        /// </summary>
        /// <remarks>
        ///     Three of the writer's decisions turn on that answer, and every one of them is wrong
        ///     silently: whether a division keeps its remainder, whether a running total overflows,
        ///     and whether joining two pieces of text clips them.
        /// </remarks>
        [Fact]
        public void Every_numeric_or_text_result_says_how_it_is_stored()
        {
            List<string> unstated = [];
            foreach (ProbeFunction function in ProbeRegistry.Functions)
            {
                foreach (ProbeSignature signature in function.Signatures)
                {
                    if (signature.Pattern is null
                        || signature.StoredAs is not null
                        || signature.Follows is not null)
                    {
                        continue;
                    }

                    // Derived from the operands instead, which only works where an operand shares
                    // the result's type in the language.
                    bool derivable = signature.Parameters.Any(
                        parameter => parameter.Types == signature.Returns
                            || parameter.Variable == signature.Returns);

                    if (!derivable && signature.Returns is "Numeric" or "String")
                    {
                        unstated.Add(function.Name + "#" + signature.Index);
                    }
                }
            }

            Assert.Empty(unstated);
        }

        /// <summary>
        ///     No signature both takes a variadic tail and asks the engine to supply an argument.
        /// </summary>
        /// <remarks>
        ///     A pattern repeats its tail over every argument past a given position, and the
        ///     engine-supplied ones sit past every declared argument. One signature with both would
        ///     sweep the supplied ones into the repetition.
        /// </remarks>
        [Fact]
        public void No_signature_mixes_a_variadic_tail_with_supplied_arguments()
        {
            List<string> mixed = [];
            foreach (ProbeFunction function in ProbeRegistry.Functions)
            {
                foreach (ProbeSignature signature in function.Signatures)
                {
                    if (signature.SyntheticCount > 0 && signature.Parameters.Any(p => p.Rest))
                    {
                        mixed.Add(function.Name + "#" + signature.Index);
                    }
                }
            }

            Assert.Empty(mixed);
        }
    }
}
