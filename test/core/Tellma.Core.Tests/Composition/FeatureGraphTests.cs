// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Composition;

namespace Tellma.Core.Tests.Composition
{
    /// <summary>The feature graph's ordering and cycle detection, by node index.</summary>
    public class FeatureGraphTests
    {
        [Fact]
        public void The_order_puts_dependencies_first_and_breaks_ties_by_lowest_index()
        {
            // 0 <- 3 <- 1; 2 is free.
            FeatureGraph graph = new([[], [3], [], [0]]);

            int[] expected = [0, 2, 3, 1];
            Assert.Equal(expected, graph.Order());
        }

        [Fact]
        public void An_acyclic_graph_has_no_cycles()
        {
            Assert.Empty(new FeatureGraph([[], [0], [0, 1]]).FindCycles());
        }

        [Fact]
        public void A_cycle_is_reported_as_a_shortest_path_from_its_lowest_index()
        {
            // 1 -> 2 -> 3 -> 1, plus the chord 2 -> 1.
            FeatureGraph graph = new([[], [2], [3, 1], [1]]);

            IReadOnlyList<int> cycle = Assert.Single(graph.FindCycles());
            int[] expected = [1, 2, 1];
            Assert.Equal(expected, cycle);
        }

        [Fact]
        public void A_self_loop_is_a_cycle_of_one()
        {
            IReadOnlyList<int> cycle = Assert.Single(new FeatureGraph([[0]]).FindCycles());

            int[] expected = [0, 0];
            Assert.Equal(expected, cycle);
        }

        [Fact]
        public void A_cyclic_graph_cannot_be_ordered()
        {
            _ = Assert.Throws<InvalidOperationException>(() => new FeatureGraph([[1], [0]]).Order());
        }

        [Fact]
        public void A_deep_chain_is_handled_without_recursion()
        {
            // Each node depends on the next; a recursive walk would exhaust the stack.
            const int Count = 20_000;
            IReadOnlyList<int>[] dependencies = [.. Enumerable.Range(0, Count).Select(static i => (IReadOnlyList<int>)(i == Count - 1 ? [0] : [i + 1]))];

            IReadOnlyList<int> cycle = Assert.Single(new FeatureGraph(dependencies).FindCycles());
            Assert.Equal(Count + 1, cycle.Count);
        }
    }
}
