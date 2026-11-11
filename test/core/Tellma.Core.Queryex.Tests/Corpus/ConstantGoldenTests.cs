// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Queryex.Testing.Corpus;
using Tellma.Queryex.Testing.Schema;

namespace Tellma.Core.Queryex.Tests.Corpus
{
    /// <summary>
    ///     Compiles every constant statement the corpus declares and compares the result against a
    ///     stored file, or against the diagnostics the case expects.
    /// </summary>
    public sealed class ConstantGoldenTests
    {
        /// <summary>The engine under test.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>Every case, as theory arguments.</summary>
        /// <returns>The cases.</returns>
        public static TheoryData<string> Cases()
        {
            TheoryData<string> data = [];
            foreach (ConstantCase entry in ConstantCorpus.All)
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
            ConstantCase entry = ConstantCorpus.All.Single(c => c.Id == id);
            QueryexResult<CompiledQuery> result = Engine.EvaluateConstant(
                entry.Items,
                entry.Options(LedgerFixture.Schema));

            if (entry.Diagnostics.Count > 0)
            {
                Assert.False(result.Succeeded, "Expected " + string.Join(", ", entry.Diagnostics));
                Snapshots.CheckDiagnostics(entry.Diagnostics, entry.Sites, result.Diagnostics);

                // Every problem names the item it is about, and nothing else.
                Assert.All(
                    result.Diagnostics,
                    diagnostic => Assert.Matches("^Constant\\[[0-9]+\\]$", diagnostic.Location));
                return;
            }

            Assert.True(
                result.Succeeded,
                string.Join(", ", result.Diagnostics.Select(d => d.Code + "@" + d.Location)));

            // A select list and nothing else: no declarations ahead of it, no source, one statement.
            CompiledQuery statement = result.Value;
            Assert.Equal(string.Empty, statement.Prologue);
            Assert.False(statement.Recompile);
            Assert.Equal(statement.Sql, statement.Body + ";\n");
            Assert.StartsWith("SELECT\n", statement.Body, StringComparison.Ordinal);
            Assert.EndsWith("\n", statement.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(";", statement.Body, StringComparison.Ordinal);

            // A clause of the statement's own starts a line; a lookup in a table calendar is a
            // scalar subquery inside an item, with a source of its own, and is no source of the
            // statement's.
            Assert.DoesNotMatch("(?m)^(FROM|OUTER APPLY|CROSS APPLY|WHERE|GROUP BY|HAVING)\\b", statement.Body);
            Assert.DoesNotContain("OUTER APPLY", statement.Body, StringComparison.Ordinal);

            // One column per item, in order, carrying the item's own text and the type asked of it.
            Assert.Equal(entry.Items.Count, statement.Columns.Count);
            for (int index = 0; index < entry.Items.Count; index++)
            {
                QueryexColumn column = statement.Columns[index];
                Assert.Equal(index, column.Ordinal);
                Assert.Equal(entry.Items[index].Text, column.Text);
                Assert.Equal(entry.Items[index].Type, column.Type);
                Assert.Null(column.Path);
                Assert.False(column.IsGroupingKey);
            }

            Snapshots.Check(Snapshots.ConstantDirectory, id, statement);
        }

        /// <summary>Every stored file belongs to a case, and every case that compiles has one.</summary>
        [Fact]
        public void Snapshots_AndCases_Agree()
        {
            if (Snapshots.Updating)
            {
                return;
            }

            HashSet<string> expected = [.. ConstantCorpus.All
                .Where(c => c.Diagnostics.Count == 0)
                .Select(c => c.Id)];

            HashSet<string> stored = Snapshots.Stored(Snapshots.ConstantDirectory);

            Assert.Empty(expected.Except(stored, StringComparer.Ordinal));
            Assert.Empty(stored.Except(expected, StringComparer.Ordinal));
        }
    }
}
