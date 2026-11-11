// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex;

namespace Tellma.Queryex.Testing.Corpus
{
    /// <summary>
    ///     Every constant statement the conformance suites check.
    /// </summary>
    /// <remarks>
    ///     Held to the same standard as the whole queries: each case that compiles has its SQL, its
    ///     parameter table and its result columns written to a file beside the tests, and is answered
    ///     on a server and by the second reading; each case that does not says exactly what is
    ///     reported, and where.
    /// </remarks>
    public static class ConstantCorpus
    {
        /// <summary>Every case, in a stable order.</summary>
        public static ImmutableArray<ConstantCase> All { get; } = [.. Statements().Concat(Refusals())];

        /// <summary>The statements that compile.</summary>
        /// <returns>The cases.</returns>
        private static IEnumerable<ConstantCase> Statements()
        {
            // The values a report run defaults its parameters to: one item that depends on the day
            // and one on who is asking, namespaced by the statement's place in the batch.
            yield return new ConstantCase
            {
                Id = "constant-context",
                Items =
                [
                    new ConstantItem("startOfMonth(today())", QueryexType.QxDate),
                    new ConstantItem("me()", QueryexType.QxNumeric),
                ],
                BatchOrdinal = 1,
            };

            // One item of every type a literal can be written at. A string literal is read at the
            // item's type, and an absent value takes it.
            yield return new ConstantCase
            {
                Id = "constant-literals",
                Items =
                [
                    new ConstantItem("1 + 2 * 3", QueryexType.QxNumeric),
                    new ConstantItem("'abc' || 'def'", QueryexType.QxString),
                    new ConstantItem("not false", QueryexType.QxBool),
                    new ConstantItem("'2024-03-01'", QueryexType.QxDate),
                    new ConstantItem("'2024-03-01T10:30:00'", QueryexType.QxDateTime),
                    new ConstantItem("'2024-03-01T10:30:00+03:00'", QueryexType.QxDateTimeOffset),
                    new ConstantItem("'6f9619ff-8b86-d011-b42d-00c04fc964ff'", QueryexType.QxGuid),
                    new ConstantItem("null", QueryexType.QxDate),
                    new ConstantItem(" ( 'spaced' ) ", QueryexType.QxString),
                ],
            };

            // Without a signed-in user the current-user function is the absent value: a comparison
            // against it is settled while compiling, and a bare use reads a slot the host leaves
            // empty.
            yield return new ConstantCase
            {
                Id = "constant-without-user",
                Items =
                [
                    new ConstantItem("me()", QueryexType.QxNumeric),
                    new ConstantItem("coalesce(me(), -1)", QueryexType.QxNumeric),
                    new ConstantItem("me() = 2", QueryexType.QxBool),
                ],
                HasUser = false,
            };

            // Operands a guard would repeat: each is computed, and might be absent, so a comparison
            // has to ask both whether it is there and what it is. With no row source there is
            // nowhere to give one a name, so each is written in the form that states it once. The
            // conditions read the day so that nothing here is settled while compiling.
            yield return new ConstantCase
            {
                Id = "constant-guards",
                Items =
                [
                    new ConstantItem(
                        "if(day(today()) > 40, 1, null) = if(month(today()) > 12, 1, null)",
                        QueryexType.QxBool),
                    new ConstantItem(
                        "if(day(today()) > 0, me(), null) != if(month(today()) > 12, 1, null)",
                        QueryexType.QxBool),
                    new ConstantItem(
                        "if(day(today()) > 0, me() + 1, null) < if(month(today()) > 0, 5, null)",
                        QueryexType.QxBool),
                    new ConstantItem(
                        "if(day(today()) > 0, 7, null) in (if(month(today()) > 12, 1, null), me() + 5, 7)",
                        QueryexType.QxBool),
                    new ConstantItem(
                        "if(day(today()) > 40, 7, null) in (if(month(today()) > 12, 1, null), 3)",
                        QueryexType.QxBool),
                    new ConstantItem("if(addDays(today(), 1) > today(), 'later', 'never')", QueryexType.QxString),
                    new ConstantItem("10 / (me() + 2)", QueryexType.QxNumeric),
                ],
            };

            // The calendar and the instant functions, which read nothing but the execution.
            yield return new ConstantCase
            {
                Id = "constant-calendar",
                Items =
                [
                    new ConstantItem("addMonths(startOfYear(today()), 6)", QueryexType.QxDate),
                    new ConstantItem("year(today()) * 100 + month(today())", QueryexType.QxNumeric),
                    new ConstantItem("now()", QueryexType.QxDateTimeOffset),
                    new ConstantItem("local(now())", QueryexType.QxDateTime),
                    new ConstantItem("diffDays('2024-01-01', today())", QueryexType.QxNumeric),
                ],
                BatchOrdinal = 3,
            };
        }

        /// <summary>The statements that are refused, each for one reason.</summary>
        /// <returns>The cases.</returns>
        private static IEnumerable<ConstantCase> Refusals()
        {
            // There is no row to read a path from. Said once per item, about the first path, and
            // each item is its own input: the second item here is the one at fault.
            yield return new ConstantCase
            {
                Id = "constant-refuse-path",
                Items =
                [
                    new ConstantItem("1", QueryexType.QxNumeric),
                    new ConstantItem("1 + [Customer] . Name + Amount", QueryexType.QxNumeric),
                ],
                Diagnostics = ["QX4009"],
                Sites = [Site("QX4009", "Constant[1]", 4, 17, ("name", "Customer.Name"))],
            };

            // The path is named by its segments, not by how they were quoted or spaced.
            yield return new ConstantCase
            {
                Id = "constant-refuse-bracketed-path",
                Items = [new ConstantItem("[Posting Date]", QueryexType.QxDate)],
                Diagnostics = ["QX4009"],
                Sites = [Site("QX4009", "Constant[0]", 0, 14, ("name", "Posting Date"))],
            };

            // Nor is there a group for an aggregation to range over.
            yield return new ConstantCase
            {
                Id = "constant-refuse-aggregation",
                Items = [new ConstantItem("count()", QueryexType.QxNumeric)],
                Diagnostics = ["QX4002"],
                Sites = [Site("QX4002", "Constant[0]", 0, 7)],
            };

            // A constant statement declares no parameters, so every one is undeclared.
            yield return new ConstantCase
            {
                Id = "constant-refuse-parameter",
                Items =
                [
                    new ConstantItem("today()", QueryexType.QxDate),
                    new ConstantItem("today()", QueryexType.QxDate),
                    new ConstantItem("addDays(@AsOf, 1)", QueryexType.QxDate),
                ],
                Diagnostics = ["QX3007"],
                Sites = [Site("QX3007", "Constant[2]", 8, 5, ("name", "AsOf"))],
            };

            // One item is one expression. Reported against the second, as in a predicate.
            yield return new ConstantCase
            {
                Id = "constant-refuse-list",
                Items = [new ConstantItem("1, 2", QueryexType.QxNumeric)],
                Diagnostics = ["QX2009"],
                Sites = [Site("QX2009", "Constant[0]", 3, 1)],
            };

            yield return new ConstantCase
            {
                Id = "constant-refuse-direction",
                Items = [new ConstantItem("1 desc", QueryexType.QxNumeric)],
                Diagnostics = ["QX2007"],
                Sites = [Site("QX2007", "Constant[0]", 2, 4)],
            };

            // An expression of another type is not converted; only a written string literal is.
            yield return new ConstantCase
            {
                Id = "constant-refuse-type",
                Items =
                [
                    new ConstantItem("today()", QueryexType.QxNumeric),
                    new ConstantItem("'abc'", QueryexType.QxNumeric),
                    new ConstantItem("'2024-13-99'", QueryexType.QxDate),
                    new ConstantItem("null", QueryexType.QxBool),
                ],
                Diagnostics = ["QX3200"],
                Sites =
                [
                    Site("QX3200", "Constant[0]", 0, 7, ("type", "Date")),
                    Site("QX3200", "Constant[1]", 0, 5, ("type", "String")),
                    Site("QX3200", "Constant[2]", 0, 12, ("type", "String")),
                    Site("QX3200", "Constant[3]", 0, 4, ("type", "null")),
                ],
            };

            // Text that does not parse is reported against the item it is.
            yield return new ConstantCase
            {
                Id = "constant-refuse-syntax",
                Items =
                [
                    new ConstantItem("today()", QueryexType.QxDate),
                    new ConstantItem("(1 + 2", QueryexType.QxNumeric),
                ],
                Diagnostics = ["QX2002"],
                Sites = [Site("QX2002", "Constant[1]", 6, 0)],
            };

            yield return new ConstantCase
            {
                Id = "constant-refuse-empty",
                Items = [new ConstantItem(string.Empty, QueryexType.QxNumeric)],
                Diagnostics = ["QX2003"],
            };

            // Everything wrong with every item is reported at once, each against its own item.
            yield return new ConstantCase
            {
                Id = "constant-refuse-several",
                Items =
                [
                    new ConstantItem("sum(Amount)", QueryexType.QxNumeric),
                    new ConstantItem("@From, Code", QueryexType.QxString),
                ],
                Diagnostics = ["QX4009", "QX4002", "QX3007", "QX2009"],
                Sites =
                [
                    Site("QX4009", "Constant[0]", 4, 6, ("name", "Amount")),
                    Site("QX4002", "Constant[0]", 0, 11),
                    Site("QX4009", "Constant[1]", 7, 4, ("name", "Code")),
                    Site("QX3007", "Constant[1]", 0, 5, ("name", "From")),
                    Site("QX2009", "Constant[1]", 7, 4),
                ],
            };

            // The statement's ceilings are the call site's, like a query's.
            yield return new ConstantCase
            {
                Id = "constant-refuse-parameters-over-ceiling",
                Items =
                [
                    new ConstantItem("today()", QueryexType.QxDate),
                    new ConstantItem("me()", QueryexType.QxNumeric),
                ],
                Limits = QueryexLimits.Default with { MaxParameters = 1 },
                Diagnostics = ["QX5007"],
                Sites = [Site("QX5007", "Constant[1]", 0, 4, ("limit", "1"))],
            };
        }

        /// <summary>One pinned diagnostic.</summary>
        /// <param name="code">The code.</param>
        /// <param name="location">The input it is attributed to.</param>
        /// <param name="start">Where its range starts within that input.</param>
        /// <param name="length">How long its range is.</param>
        /// <param name="arguments">Every argument it carries.</param>
        /// <returns>The expectation.</returns>
        private static DiagnosticSite Site(
            string code,
            string location,
            int start,
            int length,
            params (string Name, string Value)[] arguments)
        {
            return new DiagnosticSite(
                code,
                location,
                new QueryexSpan(start, length),
                arguments.ToDictionary(argument => argument.Name, argument => argument.Value, StringComparer.Ordinal));
        }
    }
}
