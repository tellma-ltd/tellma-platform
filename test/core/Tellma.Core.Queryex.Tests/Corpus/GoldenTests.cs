// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Tellma.Queryex.Testing.Corpus;
using Tellma.Queryex.Testing.Schema;

namespace Tellma.Core.Queryex.Tests.Corpus
{
    /// <summary>
    ///     Compiles every query the corpus declares and compares the result against a stored file.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The files hold the SQL, the parameter table, and the result columns, because all
    ///         three are part of what a host is promised and any of them can change without the
    ///         others. A change in emission then arrives as a diff somebody reads rather than as a
    ///         test that quietly re-records whatever it was given.
    ///     </para>
    ///     <para>
    ///         Setting the update variable rewrites the files and then fails on purpose, so that a
    ///         variable left set in an environment can never be mistaken for a passing run.
    ///     </para>
    /// </remarks>
    public sealed class GoldenTests
    {
        /// <summary>The variable that turns a run into a rewrite.</summary>
        private const string UpdateVariable = "TELLMA_UPDATE_GOLDENS";

        /// <summary>The engine under test.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>Whether this run rewrites the files instead of checking them.</summary>
        private static bool Updating =>
            Environment.GetEnvironmentVariable(UpdateVariable) is "1" or "true";

        /// <summary>Every case, as theory arguments.</summary>
        /// <returns>The cases.</returns>
        public static TheoryData<string> Cases()
        {
            TheoryData<string> data = [];
            foreach (QueryCase entry in QueryCorpus.All)
            {
                data.Add(entry.Id);
            }

            return data;
        }

        /// <summary>Each case compiles to exactly what its file says it does.</summary>
        /// <param name="id">The case's name.</param>
        [Theory]
        [MemberData(nameof(Cases))]
        public void Case_MatchesItsSnapshot(string id)
        {
            QueryCase entry = QueryCorpus.All.Single(c => c.Id == id);
            QueryexResult<CompiledQuery> result = Engine.CompileQuery(
                entry.Spec,
                new QueryCompilationOptions
                {
                    Schema = LedgerFixture.Schema,
                    Parameters = entry.Parameters,
                    HasUser = entry.HasUser,
                    Limits = entry.Limits,
                });

            if (entry.Diagnostics.Count > 0)
            {
                Assert.False(result.Succeeded, "Expected " + string.Join(", ", entry.Diagnostics));
                Assert.Equal(
                    [.. entry.Diagnostics.Order(StringComparer.Ordinal)],
                    [.. result.Diagnostics.Select(d => d.Code).Distinct().Order(StringComparer.Ordinal)]);

                return;
            }

            Assert.True(
                result.Succeeded,
                string.Join(", ", result.Diagnostics.Select(d => d.Code + "@" + d.Location)));

            string rendered = Render(result.Value);
            string path = Path.Combine(GoldenDirectory(), id + ".sql");

            if (Updating)
            {
                Directory.CreateDirectory(GoldenDirectory());
                File.WriteAllText(path, rendered, new UTF8Encoding(false));
                Assert.Fail("Snapshots were rewritten; run again without " + UpdateVariable + " set.");
            }

            Assert.True(File.Exists(path), "No snapshot for " + id + ". Set " + UpdateVariable + " to record one.");
            Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), rendered);
        }

        /// <summary>Every stored file belongs to a case, and every case that compiles has one.</summary>
        [Fact]
        public void Snapshots_AndCases_Agree()
        {
            if (Updating)
            {
                return;
            }

            HashSet<string> expected = [.. QueryCorpus.All
                .Where(c => c.Diagnostics.Count == 0)
                .Select(c => c.Id)];

            HashSet<string> stored = Directory.Exists(GoldenDirectory())
                ? [.. Directory.EnumerateFiles(GoldenDirectory(), "*.sql")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(name => name is not null)
                    .Select(name => name!)]
                : [];

            Assert.Empty(expected.Except(stored, StringComparer.Ordinal));
            Assert.Empty(stored.Except(expected, StringComparer.Ordinal));
        }

        /// <summary>Renders a compiled query into the form a file holds.</summary>
        /// <param name="query">The compiled query.</param>
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
        private static string GoldenDirectory([CallerFilePath] string here = "")
        {
            return Path.Combine(Path.GetDirectoryName(here)!, "..", "Goldens");
        }
    }
}
