// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex.Diagnostics;
using Tellma.Core.Queryex.Emit;
using Tellma.Core.Queryex.Lowering;
using Tellma.Core.Queryex.Nullity;
using Tellma.Core.Queryex.Syntax;

namespace Tellma.Core.Queryex.Pipeline
{
    /// <summary>
    ///     Compiles a whole query: every clause against one shared context, and then one statement.
    /// </summary>
    /// <remarks>
    ///     The clauses cannot be compiled apart and stitched together afterwards. They share a join
    ///     tree, an alias sequence, and a parameter table, and the select list decides facts the
    ///     other clauses are compiled against — so the order things happen in here is part of what
    ///     the output means, not an implementation detail.
    /// </remarks>
    internal static class QueryCompiler
    {
        /// <summary>Compiles one query.</summary>
        /// <param name="compiler">The stage runner, which remembers what it has already done.</param>
        /// <param name="spec">The query.</param>
        /// <param name="options">The compilation options.</param>
        /// <returns>The compiled query, or the diagnostics that stopped it.</returns>
        internal static QueryexResult<CompiledQuery> Compile(
            ExpressionCompiler compiler,
            QuerySpec spec,
            QueryCompilationOptions options)
        {
            DiagnosticSink sink = new();
            CompilationBudget budget = new(options.Limits);
            NullityMap nullity = new();
            QueryClauses clauses = new(compiler, spec, options, sink, budget, nullity);

            if (!clauses.TryBind())
            {
                return new QueryexResult<CompiledQuery>(sink.Drain());
            }

            RelationalPlan plan = clauses.Lower();
            return sink.Count > 0
                ? new QueryexResult<CompiledQuery>(sink.Drain())
                : new QueryexResult<CompiledQuery>(new CompiledQuery
                {
                    Sql = StatementEmitter.Write(plan),
                    Parameters = StatementEmitter.Parameters(plan.Parameters),
                    Columns = [.. plan.Select.Select(static item => item.Column)],
                });
        }

        /// <summary>
        ///     Whether a parsed select list would derive at least one grouping key.
        /// </summary>
        /// <param name="list">The parsed select list.</param>
        /// <returns>True when at least one item would become a key.</returns>
        /// <remarks>
        ///     Read off the parse rather than the bound tree, because binding needs the answer: an
        ///     aggregation over a query that groups by nothing can yield no value at all, and an
        ///     aggregation over one that does yields one per group. Nothing here needs a schema —
        ///     whether a name is a function call or a path is settled by the grammar, and which
        ///     functions aggregate is fixed by the registry.
        /// </remarks>
        internal static bool DerivesGroupingKeys(ExpressionListSyntax list)
        {
            foreach (ExpressionItemSyntax item in list.Items)
            {
                if (!ContainsAggregation(item.Expression) && ContainsPath(item.Expression))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether a parsed expression calls an aggregating function anywhere.</summary>
        /// <param name="node">The parsed expression.</param>
        /// <returns>True when it does.</returns>
        private static bool ContainsAggregation(SyntaxNode node)
        {
            if (node is CallSyntax call && Functions.FunctionRegistry.IsAggregate(call.Name))
            {
                return true;
            }

            foreach (SyntaxNode child in node.Children)
            {
                if (ContainsAggregation(child))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether a parsed expression names a path anywhere.</summary>
        /// <param name="node">The parsed expression.</param>
        /// <returns>True when it does.</returns>
        private static bool ContainsPath(SyntaxNode node)
        {
            if (node is PathSyntax)
            {
                return true;
            }

            foreach (SyntaxNode child in node.Children)
            {
                if (ContainsPath(child))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
