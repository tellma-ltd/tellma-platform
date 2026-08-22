// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Queryex.Testing.Corpus;
using Tellma.Queryex.Testing.Schema;

namespace Tellma.Core.Queryex.Tests.Corpus
{
    /// <summary>Runs every expression the corpus declares and checks what came out.</summary>
    public sealed class ExpressionCorpusTests
    {
        /// <summary>The engine under test.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>Every case, as theory arguments.</summary>
        /// <returns>The cases.</returns>
        public static TheoryData<string> Cases()
        {
            TheoryData<string> data = [];
            foreach (ExpressionCase entry in ExpressionCorpus.All)
            {
                data.Add(entry.Id);
            }

            return data;
        }

        /// <summary>Each case produces exactly what it says it produces.</summary>
        /// <param name="id">The case's name.</param>
        [Theory]
        [MemberData(nameof(Cases))]
        public void Case_ProducesWhatItDeclares(string id)
        {
            ExpressionCase entry = ExpressionCorpus.All.Single(c => c.Id == id);
            QueryexResult<ValidatedExpression> result = Engine.Validate(
                entry.Text,
                new ValidationOptions
                {
                    Schema = LedgerFixture.Schema,
                    Root = LedgerFixture.Schema.FindEntity(entry.Root)!,
                    Mode = entry.Mode,
                    Directions = entry.Directions,
                    HasUser = entry.HasUser,
                    HasGroupingKeys = entry.HasGroupingKeys,
                    Parameters = entry.Parameters,
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
                string.Join(", ", result.Diagnostics.Select(d => d.Code + "@" + d.Span.Start)));

            if (entry.Items is int count)
            {
                Assert.Equal(count, result.Value.Items.Count);
            }

            if (entry.Type is QueryexType type)
            {
                Assert.Equal(type, result.Value.Items[0].Type);
            }

            if (entry.Nullity is QueryexNullity nullity)
            {
                Assert.Equal(nullity, result.Value.Items[0].Nullity);
            }
        }

        /// <summary>No two cases share a name, which is what makes a snapshot belong to one case.</summary>
        [Fact]
        public void Cases_AreNamedUniquely()
        {
            string[] duplicates = [.. ExpressionCorpus.All
                .GroupBy(c => c.Id, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)];

            Assert.Empty(duplicates);
        }
    }
}
