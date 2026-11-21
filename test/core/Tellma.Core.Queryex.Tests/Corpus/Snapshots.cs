// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Tellma.Queryex.Testing.Corpus;

namespace Tellma.Core.Queryex.Tests.Corpus
{
    /// <summary>
    ///     The stored files compiled statements are compared against, and the form they hold.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A file holds the SQL, the parameter table, and the result columns, because all three
    ///         are part of what a host is promised and any of them can change without the others. A
    ///         change in emission then arrives as a diff somebody reads rather than as a test that
    ///         quietly re-records whatever it was given.
    ///     </para>
    ///     <para>
    ///         Setting the update variable rewrites the files and then fails on purpose, so that a
    ///         variable left set in an environment can never be mistaken for a passing run.
    ///     </para>
    /// </remarks>
    internal static class Snapshots
    {
        /// <summary>The variable that turns a run into a rewrite.</summary>
        private const string UpdateVariable = "TELLMA_UPDATE_GOLDENS";

        /// <summary>Whether this run rewrites the files instead of checking them.</summary>
        internal static bool Updating =>
            Environment.GetEnvironmentVariable(UpdateVariable) is "1" or "true";

        /// <summary>Where the stored files of whole queries live.</summary>
        internal static string QueryDirectory => Directory();

        /// <summary>Where the stored files of constant statements live, apart from the queries'.</summary>
        internal static string ConstantDirectory => Path.Combine(Directory(), "Constants");

        /// <summary>Compares one compiled statement against its stored file, or rewrites the file.</summary>
        /// <param name="directory">The directory the file lives in.</param>
        /// <param name="id">The case's name, which is the file's.</param>
        /// <param name="compiled">The compiled statement.</param>
        internal static void Check(string directory, string id, CompiledQuery compiled)
        {
            string rendered = Render(compiled);
            string path = Path.Combine(directory, id + ".sql");

            if (Updating)
            {
                System.IO.Directory.CreateDirectory(directory);
                File.WriteAllText(path, rendered, new UTF8Encoding(false));
                Assert.Fail("Snapshots were rewritten; run again without " + UpdateVariable + " set.");
            }

            Assert.True(File.Exists(path), "No snapshot for " + id + ". Set " + UpdateVariable + " to record one.");
            Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), rendered);
        }

        /// <summary>The names of the files one directory holds.</summary>
        /// <param name="directory">The directory.</param>
        /// <returns>The names, without their extension; none when the directory does not exist.</returns>
        internal static HashSet<string> Stored(string directory)
        {
            return System.IO.Directory.Exists(directory)
                ? [.. System.IO.Directory.EnumerateFiles(directory, "*.sql")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(name => name is not null)
                    .Select(name => name!)]
                : [];
        }

        /// <summary>
        ///     Checks that a compilation reported exactly the codes a case expects, each pinned
        ///     diagnostic among them.
        /// </summary>
        /// <param name="expected">The codes the case expects.</param>
        /// <param name="sites">The diagnostics the case pins exactly.</param>
        /// <param name="reported">What the compilation reported.</param>
        internal static void CheckDiagnostics(
            IReadOnlyList<string> expected,
            IReadOnlyList<DiagnosticSite> sites,
            IReadOnlyList<QueryexDiagnostic> reported)
        {
            Assert.Equal(
                [.. expected.Order(StringComparer.Ordinal)],
                [.. reported.Select(d => d.Code).Distinct().Order(StringComparer.Ordinal)]);

            // Where the case pins a problem down, it is reported exactly there and carrying exactly
            // that: the location a host attributes it to, the span within that input, and the
            // arguments its message needs.
            foreach (DiagnosticSite site in sites)
            {
                Assert.True(
                    reported.Any(site.Matches),
                    "Expected " + site + " among " + string.Join("; ", reported.Select(Describe)));
            }
        }

        /// <summary>Describes a reported diagnostic the way an expectation describes itself.</summary>
        /// <param name="diagnostic">The diagnostic.</param>
        /// <returns>The description.</returns>
        private static string Describe(QueryexDiagnostic diagnostic)
        {
            return new DiagnosticSite(
                diagnostic.Code,
                diagnostic.Location,
                diagnostic.Span,
                diagnostic.Arguments.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))
                .ToString();
        }

        /// <summary>Renders a compiled statement into the form a file holds.</summary>
        /// <param name="query">The compiled statement.</param>
        /// <returns>The rendered form.</returns>
        private static string Render(CompiledQuery query)
        {
            StringBuilder rendered = new();
            rendered.Append(query.Sql.ReplaceLineEndings("\n"));
            rendered.Append("\n-- parameters\n");
            foreach (QueryexParameterSlot slot in query.Parameters)
            {
                rendered.Append("--   ").Append(slot.Name).Append(" : ")
                    .Append(slot.Type).Append(' ')
                    .Append(Describe(slot.StoreType)).Append(' ')
                    .Append(slot.Origin);

                if (slot.DeclaredName is not null)
                {
                    rendered.Append(" <- ").Append(slot.DeclaredName);
                }

                if (slot.Value is not null)
                {
                    rendered.Append(" = ").Append(Literal(slot.Value));
                }

                rendered.Append('\n');
            }

            rendered.Append("-- columns\n");
            foreach (QueryexColumn column in query.Columns)
            {
                rendered.Append("--   ").Append(column.Ordinal.ToString(CultureInfo.InvariantCulture))
                    .Append(" : ").Append(column.Type).Append(' ').Append(column.Nullity);

                if (column.IsGroupingKey)
                {
                    rendered.Append(" key");
                }

                if (column.Path is not null)
                {
                    rendered.Append(" path=").Append(string.Join('.', column.Path));
                }

                rendered.Append(' ').Append(column.Text).Append('\n');
            }

            return rendered.ToString();
        }

        /// <summary>Renders a store type without reading any culture.</summary>
        /// <param name="type">The type.</param>
        /// <returns>The rendered type.</returns>
        private static string Describe(QueryexStoreType type)
        {
            string rendered = type.Family.ToString();
            if (type.Size is int size)
            {
                rendered += "(" + size.ToString(CultureInfo.InvariantCulture);
                rendered += type.Scale is int scale
                    ? ", " + scale.ToString(CultureInfo.InvariantCulture) + ")"
                    : ")";
            }
            else if (type.Family is QueryexStoreFamily.QxVarChar or QueryexStoreFamily.QxNVarChar)
            {
                rendered += "(max)";
            }

            return rendered;
        }

        /// <summary>Renders a bound value without reading any culture.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The rendered value.</returns>
        private static string Literal(object value)
        {
            return value switch
            {
                string text => "'" + text + "'",
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty,
            };
        }

        /// <summary>Where the stored files live.</summary>
        /// <param name="here">This file's path, filled in by the compiler.</param>
        /// <returns>The directory.</returns>
        /// <remarks>
        ///     Resolved against the source tree rather than the build output, because the files are
        ///     written as well as read and a copy in the output directory would be rewritten and then
        ///     thrown away.
        /// </remarks>
        private static string Directory([CallerFilePath] string here = "")
        {
            return Path.Combine(Path.GetDirectoryName(here)!, "..", "Goldens");
        }
    }
}
