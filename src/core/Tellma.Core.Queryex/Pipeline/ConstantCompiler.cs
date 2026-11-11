// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Diagnostics;
using Tellma.Core.Queryex.Emit;
using Tellma.Core.Queryex.Lowering;
using Tellma.Core.Queryex.Nullity;
using Tellma.Core.Queryex.Syntax;

namespace Tellma.Core.Queryex.Pipeline
{
    /// <summary>
    ///     Compiles a constant statement: expressions that read no row, answered as one row.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The statement is a select list and nothing else. With no row source there is nothing
    ///         for a path to read, nothing for an aggregation to range over, and nowhere to give a
    ///         repeated operand a name — so the first two are refused while binding, and the third
    ///         is written in the form that states the operand once.
    ///     </para>
    ///     <para>
    ///         Complete in itself, like a compiled query: it shares no join, alias or parameter with
    ///         any other statement, and its slots are namespaced by its own batch ordinal.
    ///     </para>
    /// </remarks>
    internal static class ConstantCompiler
    {
        /// <summary>Compiles one constant statement.</summary>
        /// <param name="compiler">The stage runner, which remembers what it has already parsed.</param>
        /// <param name="items">The items, in order; at least one.</param>
        /// <param name="options">The compilation options.</param>
        /// <returns>The compiled statement, or the diagnostics that stopped it.</returns>
        internal static QueryexResult<CompiledQuery> Compile(
            ExpressionCompiler compiler,
            ImmutableArray<ConstantItem> items,
            ConstantCompilationOptions options)
        {
            DiagnosticSink sink = new();
            CompilationBudget budget = new(options.Limits);
            NullityMap nullity = new();

            BindingContext context = Context(options.Schema, options.QueryexVersion, options.HasUser);

            // Every item is bound, whatever the earlier ones reported, so a caller fixing several
            // stored expressions sees everything wrong with them at once.
            ImmutableArray<TypedExpr>.Builder bound = ImmutableArray.CreateBuilder<TypedExpr>(items.Length);
            for (int index = 0; index < items.Length; index++)
            {
                var location = DiagnosticLocation.Constant(index);
                BoundExpression? expression = Bind(compiler, items[index], context, options.Limits, sink, location);
                if (expression is null)
                {
                    continue;
                }

                TypedExpr value = expression.Items[0].Expression;
                budget.TryConsumeTypedNodes(expression.NodeCount, value.Span, sink.Scope(location));
                nullity.Absorb(expression.Nullity);
                bound.Add(value);
            }

            if (sink.Count > 0)
            {
                return new QueryexResult<CompiledQuery>(sink.Drain());
            }

            RelationalPlan plan = Lower(items, bound.MoveToImmutable(), nullity, options.Schema, options.BatchOrdinal, budget, sink);
            if (sink.Count > 0)
            {
                return new QueryexResult<CompiledQuery>(sink.Drain());
            }

            // Assembled from the two parts, as a query is, so the same identity holds: the batch is
            // the declarations, the statement, and the terminator. Nothing here hoists, so the first
            // part is empty.
            (string prologue, string body) = StatementEmitter.Write(plan);
            return new QueryexResult<CompiledQuery>(new CompiledQuery
            {
                Sql = prologue + body + StatementEmitter.Terminator,
                Prologue = prologue,
                Body = body,
                Parameters = StatementEmitter.Parameters(plan.Parameters),
                Columns = [.. plan.Select.Select(static item => item.Column)],
            });
        }

        /// <summary>What an item of a constant statement is bound against.</summary>
        /// <param name="schema">The schema the statement's result is used with.</param>
        /// <param name="queryexVersion">The Queryex version the items are compiled under.</param>
        /// <param name="hasUser">Whether execution will have a signed-in user.</param>
        /// <returns>The context: value-shaped, with no root and no declared parameter.</returns>
        internal static BindingContext Context(QueryexSchema schema, int queryexVersion, bool hasUser)
        {
            return new BindingContext
            {
                Schema = schema,
                Root = null,
                Mode = QueryexMode.Value,
                QueryexVersion = queryexVersion,
                HasUser = hasUser,
            };
        }

        /// <summary>Parses, binds and annotates one item.</summary>
        /// <param name="compiler">The stage runner.</param>
        /// <param name="item">The item.</param>
        /// <param name="context">What the item is bound against.</param>
        /// <param name="limits">The ceilings for this call site.</param>
        /// <param name="sink">Where to record problems.</param>
        /// <param name="location">Which input the item is.</param>
        /// <returns>
        ///     The item bound as a single expression of its type, or null when something was
        ///     reported about it.
        /// </returns>
        internal static BoundExpression? Bind(
            ExpressionCompiler compiler,
            ConstantItem item,
            BindingContext context,
            QueryexLimits limits,
            DiagnosticSink sink,
            DiagnosticLocation location)
        {
            int before = sink.Count;
            if (!compiler.TryParse(item.Text, limits, sink, location, out ExpressionListSyntax? syntax)
                || syntax is null)
            {
                return null;
            }

            DiagnosticScope scope = sink.Scope(location);

            // Said once, about the first path: there is no row here to read any of them from, and
            // one message per path would say the same thing as many times. A binding with no root
            // resolves no path and stays quiet about it, so this is the only word on the subject.
            if (FirstPath(syntax) is PathSyntax path)
            {
                scope.Report(
                    DiagnosticCodes.PathNotPermitted,
                    path.Span,
                    DiagnosticArgumentNames.Name,
                    string.Join('.', path.Segments.Select(static segment => segment.Name)));
            }

            Binder binder = new(context, scope);
            TypedExpr bound = binder.BindSingle(syntax, BoundTypes.FromPublic(item.Type));
            if (sink.Count != before || bound.Type == BoundType.Error)
            {
                return null;
            }

            ImmutableArray<BoundItem> items = [new BoundItem(bound, bound.Span, QueryexDirection.None)];
            NullityAnalyzer analyzer = new(context.HasUser, hasGroupingKeys: false);
            return new BoundExpression(items, analyzer.Analyze(items), bound.NodeCount, []);
        }

        /// <summary>Lowers every bound item into one plan with no row source.</summary>
        /// <param name="items">The items, in order.</param>
        /// <param name="bound">Each item's bound expression, in the same order.</param>
        /// <param name="nullity">Every item's nullity answers, merged.</param>
        /// <param name="schema">The schema, which names the month tables of its table calendars.</param>
        /// <param name="batchOrdinal">The statement's position among the ones executed together.</param>
        /// <param name="budget">The ceilings shared across the compilation.</param>
        /// <param name="sink">Where to record problems.</param>
        /// <returns>The plan.</returns>
        private static RelationalPlan Lower(
            ImmutableArray<ConstantItem> items,
            ImmutableArray<TypedExpr> bound,
            NullityMap nullity,
            QueryexSchema? schema,
            int batchOrdinal,
            CompilationBudget budget,
            DiagnosticSink sink)
        {
            Lowerer lowerer = new(null, schema, batchOrdinal, budget, sink);
            ImmutableArray<PlanSelectItem>.Builder select = ImmutableArray.CreateBuilder<PlanSelectItem>(items.Length);
            List<PlanNode> fragments = new(items.Length);

            for (int index = 0; index < items.Length; index++)
            {
                // No value may be given a name: a name is bound once per row of a source, and this
                // statement has none. An operand a guard would repeat is written in the form that
                // states it once instead.
                lowerer.Begin(nullity, DiagnosticLocation.Constant(index), bindingsAllowed: false);
                PlanValue value = lowerer.LowerValue(bound[index]);
                fragments.Add(value);

                // The item's own text and the type asked of it; no path, and no key, since nothing
                // here is read from an entity or grouped.
                select.Add(new PlanSelectItem(
                    value,
                    StatementEmitter.ColumnName(index),
                    new QueryexColumn(index, items[index].Text, items[index].Type, nullity[bound[index]], null, false)));
            }

            lowerer.Complete(fragments, []);
            return new RelationalPlan
            {
                Root = null,
                Joins = [],
                Variables = [.. lowerer.Variables],
                Laterals = [.. lowerer.Laterals],
                Parameters = [.. lowerer.Slots],
                Select = select.MoveToImmutable(),
                Where = null,
                GroupBy = [],
                Having = null,
                OrderBy = [],
                Skip = null,
                Take = null,
            };
        }

        /// <summary>The first path a parsed text names, in the order it is written.</summary>
        /// <param name="node">The parsed text, or a part of it.</param>
        /// <returns>The path, or null when the text names none.</returns>
        /// <remarks>
        ///     Recursive, which the parser's depth ceiling keeps bounded.
        /// </remarks>
        private static PathSyntax? FirstPath(SyntaxNode node)
        {
            if (node is PathSyntax path)
            {
                return path;
            }

            foreach (SyntaxNode child in node.Children)
            {
                if (FirstPath(child) is PathSyntax found)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
