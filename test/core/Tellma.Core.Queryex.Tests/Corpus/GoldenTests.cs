// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Queryex.Testing.Corpus;
using Tellma.Queryex.Testing.Schema;

namespace Tellma.Core.Queryex.Tests.Corpus
{
    /// <summary>
    ///     Compiles every query the corpus declares and compares the result against a stored file.
    /// </summary>
    /// <remarks>
    ///     What a file holds, and how one is rewritten, is the same for every kind of compiled
    ///     statement and lives beside this class.
    /// </remarks>
    public sealed class GoldenTests
    {
        /// <summary>The engine under test.</summary>
        private static readonly QueryexEngine Engine = new();

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
                entry.Options(LedgerFixture.Schema));

            if (entry.Diagnostics.Count > 0)
            {
                Assert.False(result.Succeeded, "Expected " + string.Join(", ", entry.Diagnostics));
                Snapshots.CheckDiagnostics(entry.Diagnostics, entry.Sites, result.Diagnostics);
                return;
            }

            Assert.True(
                result.Succeeded,
                string.Join(", ", result.Diagnostics.Select(d => d.Code + "@" + d.Location)));

            // The parts a host embeds are exactly the batch, cut where a host needs it cut: the
            // declarations and their empty line, then the statement with no terminator, then the
            // statement's one hint when it is planned afresh. The hint is never in the body.
            CompiledQuery query = result.Value;
            Assert.Equal(
                query.Sql,
                query.Prologue + query.Body + (query.Recompile ? "OPTION (RECOMPILE)\n" : string.Empty) + ";\n");
            Assert.DoesNotContain("OPTION", query.Body, StringComparison.Ordinal);
            Assert.True(query.Prologue.Length == 0 || query.Prologue.EndsWith(";\n\n", StringComparison.Ordinal));
            Assert.StartsWith("SELECT\n", query.Body, StringComparison.Ordinal);
            Assert.EndsWith("\n", query.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(";", query.Body, StringComparison.Ordinal);

            Snapshots.Check(Snapshots.QueryDirectory, id, query);
        }

        /// <summary>Every stored file belongs to a case, and every case that compiles has one.</summary>
        [Fact]
        public void Snapshots_AndCases_Agree()
        {
            if (Snapshots.Updating)
            {
                return;
            }

            HashSet<string> expected = [.. QueryCorpus.All
                .Where(c => c.Diagnostics.Count == 0)
                .Select(c => c.Id)];

            HashSet<string> stored = Snapshots.Stored(Snapshots.QueryDirectory);

            Assert.Empty(expected.Except(stored, StringComparer.Ordinal));
            Assert.Empty(stored.Except(expected, StringComparer.Ordinal));
        }
    }
}
