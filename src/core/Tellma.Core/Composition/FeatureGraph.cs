// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Composition
{
    /// <summary>
    ///     The dependency graph of a composition's features, by index: node <c>i</c> depends on each
    ///     node in <c>dependencies[i]</c>. Finds cycles and computes the realisation order.
    /// </summary>
    /// <param name="dependencies">Each node's dependencies.</param>
    internal sealed class FeatureGraph(IReadOnlyList<IReadOnlyList<int>> dependencies)
    {
        /// <summary>
        ///     One concrete cycle per strongly connected component that has one, as the path of node
        ///     indices from the component's lowest index back to itself (<c>[a, b, a]</c>; a feature
        ///     that requires itself is <c>[a, a]</c>). Components are ordered by their lowest index.
        /// </summary>
        /// <returns>The cycles; empty for an acyclic graph.</returns>
        public IReadOnlyList<IReadOnlyList<int>> FindCycles()
        {
            List<IReadOnlyList<int>> cycles = [];
            foreach (List<int> component in StronglyConnectedComponents().OrderBy(static component => component.Min()))
            {
                int start = component.Min();
                bool selfLoop = dependencies[start].Contains(start);
                if (component.Count > 1 || selfLoop)
                {
                    cycles.Add(selfLoop ? [start, start] : PathBackTo(start, [.. component]));
                }
            }

            return cycles;
        }

        /// <summary>
        ///     The realisation order: every node after its dependencies and, among the nodes ready at
        ///     each step, the lowest index first, so the order is deterministic and follows the order
        ///     features were added wherever the edges leave it free.
        /// </summary>
        /// <returns>The node indices in order.</returns>
        /// <exception cref="InvalidOperationException">The graph has a cycle; the graph gate reports that before ordering.</exception>
        public IReadOnlyList<int> Order()
        {
            int count = dependencies.Count;
            bool[] emitted = new bool[count];
            List<int> order = new(count);

            // Quadratic, which is nothing at the dozens of features a composition holds, and it keeps
            // the tie-break rule visible.
            while (order.Count < count)
            {
                int next = -1;
                for (int node = 0; node < count; node++)
                {
                    if (!emitted[node] && dependencies[node].All(dependency => emitted[dependency]))
                    {
                        next = node;
                        break;
                    }
                }

                if (next < 0)
                {
                    throw new InvalidOperationException("The feature graph has a cycle and cannot be ordered.");
                }

                emitted[next] = true;
                order.Add(next);
            }

            return order;
        }

        /// <summary>The graph's strongly connected components, by Tarjan's algorithm.</summary>
        /// <returns>The components; a node outside every cycle is a component of its own.</returns>
        private List<List<int>> StronglyConnectedComponents()
        {
            // Tarjan's algorithm, iterative so a deep dependency chain cannot exhaust the stack.
            int count = dependencies.Count;
            int[] index = [.. Enumerable.Repeat(-1, count)];
            int[] lowLink = new int[count];
            bool[] onStack = new bool[count];
            Stack<int> stack = new();
            List<List<int>> components = [];
            int nextIndex = 0;

            for (int root = 0; root < count; root++)
            {
                if (index[root] >= 0)
                {
                    continue;
                }

                // Each frame is a node and the position of the next dependency to visit.
                Stack<(int Node, int Next)> frames = new();
                frames.Push((root, 0));
                index[root] = lowLink[root] = nextIndex++;
                stack.Push(root);
                onStack[root] = true;

                while (frames.Count > 0)
                {
                    (int node, int next) = frames.Pop();
                    if (next < dependencies[node].Count)
                    {
                        frames.Push((node, next + 1));
                        int dependency = dependencies[node][next];
                        if (index[dependency] < 0)
                        {
                            index[dependency] = lowLink[dependency] = nextIndex++;
                            stack.Push(dependency);
                            onStack[dependency] = true;
                            frames.Push((dependency, 0));
                        }
                        else if (onStack[dependency])
                        {
                            lowLink[node] = Math.Min(lowLink[node], index[dependency]);
                        }

                        continue;
                    }

                    // Every dependency of the node is visited: close its component if it roots one,
                    // then fold its low link into the caller's.
                    if (lowLink[node] == index[node])
                    {
                        List<int> component = [];
                        int member;
                        do
                        {
                            member = stack.Pop();
                            onStack[member] = false;
                            component.Add(member);
                        }
                        while (member != node);

                        components.Add(component);
                    }

                    if (frames.Count > 0)
                    {
                        int caller = frames.Peek().Node;
                        lowLink[caller] = Math.Min(lowLink[caller], lowLink[node]);
                    }
                }
            }

            return components;
        }

        /// <summary>A shortest path from <paramref name="start" /> back to itself inside its component.</summary>
        /// <param name="start">The node the cycle starts and ends at.</param>
        /// <param name="component">The members of its strongly connected component.</param>
        /// <returns>The path, both ends included.</returns>
        private List<int> PathBackTo(int start, HashSet<int> component)
        {
            // Breadth-first within the component, so the reported cycle is a shortest one through
            // the start node; the component is strongly connected, so the start is always reached.
            Dictionary<int, int> previous = [];
            Queue<int> queue = new();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                int node = queue.Dequeue();
                foreach (int dependency in dependencies[node].Where(component.Contains).OrderBy(static d => d))
                {
                    if (dependency == start)
                    {
                        List<int> path = [start];
                        for (int step = node; step != start; step = previous[step])
                        {
                            path.Insert(1, step);
                        }

                        path.Add(start);
                        return path;
                    }

                    if (previous.TryAdd(dependency, node))
                    {
                        queue.Enqueue(dependency);
                    }
                }
            }

            throw new InvalidOperationException("A strongly connected component must lead back to its start.");
        }
    }
}
