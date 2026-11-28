// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Diagnostics;
using Tellma.Core.Queryex.Nullity;
using Tellma.Core.Queryex.Pipeline;

namespace Tellma.Queryex.Testing.Probe
{
    /// <summary>One text bound, and the Queryex version it was bound under.</summary>
    /// <param name="Text">The expression text.</param>
    /// <param name="QueryexVersion">The version the binding ran under.</param>
    public sealed record ProbeLeafVersion(string Text, int QueryexVersion);

    /// <summary>
    ///     Looks at which Queryex version each leaf of a filter tree is bound under.
    /// </summary>
    /// <remarks>
    ///     While one version exists, a leaf binds to the same tree under any version, so nothing the
    ///     public surface returns shows whether a stamped leaf bound under its own stamp. That rule
    ///     starts to matter on the day a second version ships, and this is what keeps it from
    ///     regressing silently before then: it builds leaves stamped with versions this engine does
    ///     not have, which the public factory refuses, and records what each binding actually ran
    ///     under.
    /// </remarks>
    public static class ProbeLeafBinding
    {
        /// <summary>
        ///     Builds a leaf stamped with any version, including one this engine does not compile.
        /// </summary>
        /// <param name="text">The expression text.</param>
        /// <param name="queryexVersion">The stamp.</param>
        /// <returns>The leaf.</returns>
        public static FilterTree StampedLeaf(string text, int queryexVersion)
        {
            ArgumentNullException.ThrowIfNull(text);

            return new FilterTree.LeafNode(text, queryexVersion);
        }

        /// <summary>The versions a filter tree's leaves bind under when a query is compiled.</summary>
        /// <param name="tree">The tree.</param>
        /// <param name="compilationVersion">The version the compilation names.</param>
        /// <param name="schema">The schema.</param>
        /// <param name="root">The entity paths resolve from.</param>
        /// <returns>Every binding request, in the order the leaves were bound.</returns>
        public static IReadOnlyList<ProbeLeafVersion> Compiled(
            FilterTree tree,
            int compilationVersion,
            QueryexSchema schema,
            EntityDescriptor root)
        {
            ArgumentNullException.ThrowIfNull(tree);
            ArgumentNullException.ThrowIfNull(schema);
            ArgumentNullException.ThrowIfNull(root);

            List<ProbeLeafVersion> seen = [];
            ExpressionCompiler compiler = Observing(seen);

            FilterBinder.Bind(
                compiler,
                tree,
                new BindingContext
                {
                    Schema = schema,
                    Root = root,
                    Mode = QueryexMode.Filter,
                    QueryexVersion = compilationVersion,
                },
                QueryexLimits.Default,
                new DiagnosticSink(),
                DiagnosticLocation.Clause(QueryexClause.Filter),
                new CompilationBudget(QueryexLimits.Default),
                new NullityMap());

            return seen;
        }

        /// <summary>The versions a filter tree's leaves bind under when a query is discovered.</summary>
        /// <param name="tree">The tree.</param>
        /// <param name="schema">The schema.</param>
        /// <param name="root">The entity paths resolve from.</param>
        /// <returns>Every binding request, in the order the leaves were bound.</returns>
        /// <remarks>Discovery names the current version, which is its default.</remarks>
        public static IReadOnlyList<ProbeLeafVersion> Discovered(
            FilterTree tree,
            QueryexSchema schema,
            EntityDescriptor root)
        {
            ArgumentNullException.ThrowIfNull(tree);
            ArgumentNullException.ThrowIfNull(schema);
            ArgumentNullException.ThrowIfNull(root);

            List<ProbeLeafVersion> seen = [];
            Discoverer.DiscoverQuery(
                Observing(seen),
                new QueryDiscoverySpec { Filter = tree },
                new DiscoveryOptions { Schema = schema, Root = root });

            return seen;
        }

        /// <summary>A fresh compiler that records every binding request.</summary>
        /// <param name="seen">Where the requests are recorded.</param>
        /// <returns>The compiler.</returns>
        private static ExpressionCompiler Observing(List<ProbeLeafVersion> seen)
        {
            return new ExpressionCompiler(new QueryexEngineOptions())
            {
                BindObserver = (text, context) => seen.Add(new ProbeLeafVersion(text, context.QueryexVersion)),
            };
        }
    }
}
