// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Caching;
using Tellma.Core.Queryex.Diagnostics;
using Tellma.Core.Queryex.Lexing;
using Tellma.Core.Queryex.Nullity;
using Tellma.Core.Queryex.Syntax;

namespace Tellma.Core.Queryex.Pipeline
{
    /// <summary>
    ///     One expression text, taken from characters to an annotated bound tree.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The stages run in order and never reach back: scanning knows about characters,
    ///         parsing about the grammar, binding about the schema and the type system, and the
    ///         nullity pass about the answers binding produced. Each stops the next if it could not
    ///         finish, so no stage ever has to guess what a previous one meant.
    ///     </para>
    ///     <para>
    ///         Results are remembered between the two stages that are worth remembering, and only
    ///         when they succeeded. A run that reported something is not cached: its diagnostics
    ///         would have to be replayed to be worth anything, and a mistake in replaying them is a
    ///         mistake nobody would notice.
    ///     </para>
    /// </remarks>
    internal sealed class ExpressionCompiler
    {
        /// <summary>The parses remembered so far.</summary>
        private readonly BoundedCache<ParseKey, ExpressionListSyntax> _parses;

        /// <summary>The bound trees remembered so far.</summary>
        private readonly BoundedCache<BindKey, BoundExpression> _bindings;

        /// <summary>Initializes a compiler.</summary>
        /// <param name="options">The engine-wide options.</param>
        internal ExpressionCompiler(QueryexEngineOptions options)
        {
            _parses = new BoundedCache<ParseKey, ExpressionListSyntax>(options.MaxCachedSyntaxTrees);
            _bindings = new BoundedCache<BindKey, BoundExpression>(options.MaxCachedBoundExpressions);
        }

        /// <summary>Scans and parses one expression list.</summary>
        /// <param name="text">The expression text.</param>
        /// <param name="limits">The ceilings for this call site.</param>
        /// <param name="sink">Where to record problems.</param>
        /// <param name="location">Which input this text is.</param>
        /// <param name="syntax">The parse tree, when the text parsed.</param>
        /// <returns>True when the text scanned and parsed without a problem.</returns>
        /// <remarks>
        ///     Separate from binding because compiling a whole query has to read the shape of its
        ///     select list before it can know what to bind that list against: whether the query
        ///     groups by anything is derived from the list, and that fact changes what binding it
        ///     concludes.
        /// </remarks>
        internal bool TryParse(
            string text,
            QueryexLimits limits,
            DiagnosticSink sink,
            DiagnosticLocation location,
            out ExpressionListSyntax? syntax)
        {
            ParseKey key = new(text, limits);
            if (_parses.TryGet(key, out ExpressionListSyntax? cached) && cached is not null)
            {
                syntax = cached;
                return true;
            }

            syntax = null;
            DiagnosticScope scope = sink.Scope(location);
            ParseBudget budget = new(limits);

            if (!Lexer.TryTokenize(text, budget, scope, out Token[] tokens))
            {
                return false;
            }

            bool parsed = Parser.TryParse(text, tokens, budget, scope, out ExpressionListSyntax list);
            syntax = list;
            if (parsed)
            {
                _parses.Set(key, list);
            }

            return parsed;
        }

        /// <summary>Binds a parsed expression list and annotates it.</summary>
        /// <param name="text">The text the tree was parsed from, for the cache key.</param>
        /// <param name="syntax">The parse tree.</param>
        /// <param name="context">What the expression depends on besides its own text.</param>
        /// <param name="directions">Whether direction suffixes are accepted here.</param>
        /// <param name="limits">The ceilings for this call site.</param>
        /// <param name="sink">Where to record problems.</param>
        /// <param name="location">Which input this text is.</param>
        /// <returns>The bound expression.</returns>
        internal BoundExpression Bind(
            string text,
            ExpressionListSyntax syntax,
            BindingContext context,
            bool directions,
            QueryexLimits limits,
            DiagnosticSink sink,
            DiagnosticLocation location)
        {
            BindKey? key = KeyFor(text, syntax, context, directions, limits);
            if (key is not null
                && _bindings.TryGet(key, out BoundExpression? cached)
                && cached is not null)
            {
                return cached;
            }

            int before = sink.Count;
            DiagnosticScope scope = sink.Scope(location);
            Binder binder = new(context, scope);
            ImmutableArray<BoundItem> items = binder.BindList(syntax, directions);

            NullityAnalyzer analyzer = new(context.HasUser, context.HasGroupingKeys);
            NullityMap nullity = analyzer.Analyze(items);

            int nodes = 0;
            foreach (BoundItem item in items)
            {
                nodes += item.Expression.NodeCount;
            }

            BoundExpression bound = new(items, nullity, nodes, []);
            if (key is not null && sink.Count == before)
            {
                _bindings.Set(key, bound);
            }

            return bound;
        }

        /// <summary>Scans, parses, binds, and annotates one expression list.</summary>
        /// <param name="text">The expression text.</param>
        /// <param name="context">What the expression depends on besides its own text.</param>
        /// <param name="directions">Whether direction suffixes are accepted here.</param>
        /// <param name="limits">The ceilings for this call site.</param>
        /// <param name="sink">Where to record problems.</param>
        /// <param name="location">Which input this text is.</param>
        /// <param name="syntax">The parse tree, when the text parsed.</param>
        /// <param name="bound">The bound expression, when it bound.</param>
        /// <returns>True when every stage finished without a problem.</returns>
        internal bool TryCompile(
            string text,
            BindingContext context,
            bool directions,
            QueryexLimits limits,
            DiagnosticSink sink,
            DiagnosticLocation location,
            out ExpressionListSyntax? syntax,
            out BoundExpression? bound)
        {
            bound = null;

            // Success is measured against what this text added to the sink rather than against
            // whether the sink is empty, because several clauses of one query share one.
            int before = sink.Count;
            if (!TryParse(text, limits, sink, location, out syntax))
            {
                return false;
            }

            bound = Bind(text, syntax!, context, directions, limits, sink, location);
            return sink.Count == before;
        }

        /// <summary>What a bound tree would be remembered under, when it can be.</summary>
        /// <param name="text">The expression text.</param>
        /// <param name="syntax">The parse tree.</param>
        /// <param name="context">What the expression depends on besides its own text.</param>
        /// <param name="directions">Whether direction suffixes are accepted here.</param>
        /// <param name="limits">The ceilings for this call site.</param>
        /// <returns>The key, or null when this binding is not worth remembering.</returns>
        /// <remarks>
        ///     A binding done without a schema resolves nothing, and one done while inferring
        ///     parameter types writes its findings into a run that belongs to a single authoring
        ///     session; neither is the same thing twice, so neither is cached.
        /// </remarks>
        private static BindKey? KeyFor(
            string text,
            ExpressionListSyntax syntax,
            BindingContext context,
            bool directions,
            QueryexLimits limits)
        {
            return context.Inference is not null || context.Schema is null || context.Root is null
                ? null
                : new BindKey(
                    text,
                    context.Schema,
                    context.Root,
                    context.Mode,
                    context.HasUser,
                    context.HasGroupingKeys,
                    directions,
                    limits,
                    BindKey.Project(syntax, context.Parameters));
        }
    }
}
