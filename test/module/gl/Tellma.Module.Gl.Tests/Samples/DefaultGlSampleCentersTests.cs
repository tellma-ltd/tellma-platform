// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Module.Gl.Tests.Samples
{
    /// <summary>
    ///     The default sample tree: one root, every parent code naming another center of the list and
    ///     the tree acyclic, codes unique, children only under grouping centers, and every center
    ///     named in English first and in Arabic.
    /// </summary>
    public class DefaultGlSampleCentersTests
    {
        private static readonly IReadOnlyList<SampleCenter> Centers = new DefaultGlSampleCenters().Centers;

        [Fact]
        public void The_tree_is_version_one_and_the_step_accepts_it()
        {
            Assert.Equal(1, new DefaultGlSampleCenters().Version);
            Assert.Equal(8, Centers.Count);
            _ = GlSampleCentersStep<Center>.Check(Centers, typeof(DefaultGlSampleCenters));
        }

        [Fact]
        public void Codes_are_unique_without_regard_to_case()
        {
            Assert.Equal(Centers.Count, Centers.Select(static center => center.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        [Fact]
        public void The_tree_has_one_root_and_every_chain_reaches_it_without_a_cycle()
        {
            var byCode = Centers.ToDictionary(static center => center.Code, StringComparer.Ordinal);
            SampleCenter root = Assert.Single(Centers, static center => center.ParentCode is null);
            Assert.Equal("HQ", root.Code);

            Assert.All(Centers, center =>
            {
                // --- Walk up: every chain ends at the root within as many steps as there are centers ---
                HashSet<string> seen = [center.Code];
                SampleCenter current = center;
                while (current.ParentCode is string parentCode)
                {
                    current = byCode[parentCode];
                    Assert.True(seen.Add(current.Code), $"The chain of '{center.Code}' returns to '{current.Code}'.");
                }

                Assert.Same(root, current);
            });
        }

        [Fact]
        public void Only_grouping_centers_have_children()
        {
            var byCode = Centers.ToDictionary(static center => center.Code, StringComparer.Ordinal);

            Assert.All(
                Centers.Where(static center => center.ParentCode is not null),
                center => Assert.True(CenterTypes.IsGrouping(byCode[center.ParentCode!].CenterType), $"'{center.ParentCode}' does not group."));
        }

        [Fact]
        public void Every_center_is_named_in_english_first_and_in_arabic()
        {
            Assert.All(Centers, static center =>
            {
                Assert.Equal(["en", "ar"], center.Names.Select(static name => name.Language));
                Assert.All(center.Names, static name => Assert.False(string.IsNullOrWhiteSpace(name.Text)));

                // The Arabic twin is Arabic script, not a copy of the English name.
                string arabic = center.Names[1].Text;
                Assert.Contains(arabic, static c => c is >= '؀' and <= 'ۿ');
            });
        }
    }
}
