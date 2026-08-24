// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Caching;
using Tellma.Core.Queryex.Diagnostics;
using Tellma.Core.Queryex.Pipeline;

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     Compiles Queryex text against a schema.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Thread-safe, and meant to be long-lived: an instance remembers what it has already
    ///         parsed, bound, and compiled, all of it bounded so that untrusted text cannot make it
    ///         grow without limit.
    ///     </para>
    ///     <para>
    ///         The engine compiles and never executes. It opens no connection and evaluates nothing;
    ///         the values that depend on when and by whom a query runs — the date, the instant, the
    ///         signed-in user, the tenant's zone — come out as named parameter slots for the host to
    ///         bind at execution.
    ///     </para>
    ///     <para>
    ///         Nothing a user can type produces an exception. Mistakes in expression text come back
    ///         as diagnostics; exceptions are reserved for mistakes in what the host itself passed.
    ///     </para>
    /// </remarks>
    public sealed class QueryexEngine
    {
        /// <summary>The stages, and what they remember.</summary>
        private readonly ExpressionCompiler _compiler;

        /// <summary>The compilations remembered so far.</summary>
        private readonly BoundedCache<QueryKey, CompiledQuery> _queries;

        /// <summary>Initializes an engine with the default options.</summary>
        public QueryexEngine()
            : this(new QueryexEngineOptions())
        {
        }

        /// <summary>Initializes an engine.</summary>
        /// <param name="options">The engine-wide options.</param>
        /// <exception cref="ArgumentNullException"><paramref name="options" /> is null.</exception>
        public QueryexEngine(QueryexEngineOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            _compiler = new ExpressionCompiler(options);
            _queries = new BoundedCache<QueryKey, CompiledQuery>(options.MaxCachedQueryTemplates);
        }

        /// <summary>
        ///     Checks an expression list and reports what each item is, without producing any SQL.
        /// </summary>
        /// <param name="text">The expression text.</param>
        /// <param name="options">What to check it against.</param>
        /// <returns>The validated items, or the diagnostics.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentException">
        ///     The root does not belong to the schema, directions were asked for in a position that
        ///     has no ordering, or the declarations are not usable.
        /// </exception>
        /// <remarks>
        ///     This is what a save-time check calls, so that a stored expression is checked by the
        ///     very binder that will compile it years later rather than by a second implementation
        ///     that drifts from it.
        /// </remarks>
        public QueryexResult<ValidatedExpression> Validate(string text, ValidationOptions options)
        {
            ArgumentNullException.ThrowIfNull(text);
            ArgumentNullException.ThrowIfNull(options);
            RequireEntity(options.Schema, options.Root);

            if (options.Directions && options.Mode.Shape != QueryexShape.Value)
            {
                throw new ArgumentException(
                    "Direction suffixes are only meaningful where the expression produces a value.",
                    nameof(options));
            }

            DiagnosticSink sink = new();
            BindingContext context = new()
            {
                Schema = options.Schema,
                Root = options.Root,
                Mode = options.Mode,
                LanguageVersion = options.LanguageVersion,
                HasUser = options.HasUser,
                HasGroupingKeys = options.HasGroupingKeys,
                Parameters = Symbols.From(options.Parameters),
            };

            bool compiled = _compiler.TryCompile(
                text,
                context,
                options.Directions,
                options.Limits,
                sink,
                DiagnosticLocation.None,
                out _,
                out BoundExpression? bound);

            if (!compiled || bound is null)
            {
                return new QueryexResult<ValidatedExpression>(sink.Drain());
            }

            // Checked here as well as when a whole query is compiled, because a stored expression
            // that a save-time check waved through would fail at run time instead — and by then
            // whoever wrote it is not around to be told.
            CompilationBudget budget = new(options.Limits);
            budget.TryConsumeTypedNodes(bound.NodeCount, default, sink.Scope(DiagnosticLocation.None));
            if (sink.Count > 0)
            {
                return new QueryexResult<ValidatedExpression>(sink.Drain());
            }

            ImmutableArray<ValidatedItem>.Builder items =
                ImmutableArray.CreateBuilder<ValidatedItem>(bound.Items.Length);

            foreach (BoundItem item in bound.Items)
            {
                items.Add(new ValidatedItem(
                    item.Span,
                    BoundTypes.ToPublic(item.Expression.Type),
                    bound.Nullity[item.Expression],
                    item.Direction,
                    item.Expression.ContainsAggregate));
            }

            return new QueryexResult<ValidatedExpression>(new ValidatedExpression(items.ToImmutable()));
        }

        /// <summary>Reports what an expression list refers to, for an authoring tool.</summary>
        /// <param name="text">The expression text.</param>
        /// <param name="options">What to read it against.</param>
        /// <returns>What it refers to, and whatever problems could still be reported.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentException">A root was supplied without its schema, or vice versa.</exception>
        public DiscoveryResult Discover(string text, DiscoveryOptions options)
        {
            ArgumentNullException.ThrowIfNull(text);
            ArgumentNullException.ThrowIfNull(options);
            RequirePairedSchema(options);

            return Discoverer.Discover(_compiler, text, options);
        }

        /// <summary>Reports what a whole query under authoring refers to.</summary>
        /// <param name="spec">The clauses written so far.</param>
        /// <param name="options">What to read them against.</param>
        /// <returns>What they refer to, and whatever problems could still be reported.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentException">
        ///     A root was supplied without its schema, or a group-level predicate was supplied for a
        ///     query that does not group.
        /// </exception>
        /// <remarks>
        ///     One inference run across every clause, which is why this exists beside the
        ///     single-expression form: a parameter equated to another in one clause and given a type
        ///     by a third clause is only solvable while both facts are still in hand.
        /// </remarks>
        public DiscoveryResult DiscoverQuery(QueryDiscoverySpec spec, DiscoveryOptions options)
        {
            ArgumentNullException.ThrowIfNull(spec);
            ArgumentNullException.ThrowIfNull(options);
            RequirePairedSchema(options);

            RequireGrouping(spec.Having, spec.Aggregate, nameof(spec));
            return Discoverer.DiscoverQuery(_compiler, spec, options);
        }

        /// <summary>Compiles a whole query into SQL and the parameters it binds.</summary>
        /// <param name="spec">The query.</param>
        /// <param name="options">The compilation options.</param>
        /// <returns>The compiled query, or the diagnostics that stopped it.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentException">
        ///     The root does not belong to the schema, a group-level predicate was supplied for a
        ///     query that does not group, the paging values are not usable, the batch ordinal is
        ///     negative, or the declarations are not usable.
        /// </exception>
        /// <remarks>
        ///     The only door to SQL there is. A fragment compiled on its own could not share joins,
        ///     aliases, or parameters with the query it would eventually be joined to, so offering
        ///     one would be an invitation to paste two fragments together — which is the failure
        ///     structural composition exists to rule out.
        /// </remarks>
        public QueryexResult<CompiledQuery> CompileQuery(QuerySpec spec, QueryCompilationOptions options)
        {
            ArgumentNullException.ThrowIfNull(spec);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(spec.Select);
            RequireEntity(options.Schema, spec.Root);
            RequirePaging(spec, options);

            var key = QueryKey.Of(spec, options);
            if (_queries.TryGet(key, out CompiledQuery? cached) && cached is not null)
            {
                return new QueryexResult<CompiledQuery>(cached);
            }

            QueryexResult<CompiledQuery> result = QueryCompiler.Compile(_compiler, spec, options);
            if (result.Succeeded)
            {
                _queries.Set(key, result.Value);
            }

            return result;
        }

        /// <summary>Checks that a group-level predicate belongs to a query that groups.</summary>
        /// <param name="having">The group-level predicate, when there is one.</param>
        /// <param name="aggregate">Whether the query groups.</param>
        /// <param name="parameterName">The caller's parameter name, for the exception.</param>
        /// <exception cref="ArgumentException">There is one and the query does not group.</exception>
        private static void RequireGrouping(FilterTree? having, bool aggregate, string parameterName)
        {
            if (having is not null && !aggregate)
            {
                throw new ArgumentException(
                    "A group-level predicate belongs only to a query that groups.",
                    parameterName);
            }
        }

        /// <summary>Checks that an entity belongs to the schema it is being used with.</summary>
        /// <param name="schema">The schema.</param>
        /// <param name="entity">The entity.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentException">The entity belongs to another schema.</exception>
        private static void RequireEntity(QueryexSchema schema, EntityDescriptor entity)
        {
            ArgumentNullException.ThrowIfNull(schema);
            ArgumentNullException.ThrowIfNull(entity);

            if (!schema.Contains(entity))
            {
                throw new ArgumentException(
                    "The root entity must belong to the schema being compiled against.",
                    nameof(entity));
            }
        }

        /// <summary>Checks that a schema and a root were supplied together or not at all.</summary>
        /// <param name="options">The discovery options.</param>
        /// <exception cref="ArgumentException">Only one of the two was supplied.</exception>
        private static void RequirePairedSchema(DiscoveryOptions options)
        {
            if ((options.Schema is null) != (options.Root is null))
            {
                throw new ArgumentException(
                    "A root entity and the schema it belongs to are supplied together or not at all.",
                    nameof(options));
            }

            if (options.Schema is not null && options.Root is not null)
            {
                RequireEntity(options.Schema, options.Root);
            }
        }

        /// <summary>Checks the parts of a query the caller, not the user, is responsible for.</summary>
        /// <param name="spec">The query.</param>
        /// <param name="options">The compilation options.</param>
        /// <exception cref="ArgumentException">One of them is not usable.</exception>
        private static void RequirePaging(QuerySpec spec, QueryCompilationOptions options)
        {
            RequireGrouping(spec.Having, spec.Aggregate, nameof(spec));

            if (spec.Skip is < 0)
            {
                throw new ArgumentException("Rows to skip cannot be negative.", nameof(spec));
            }

            if (spec.Take is <= 0)
            {
                // The backend's paging clause rejects a page of no rows outright, and a caller who
                // wants none of them has no reason to ask for any.
                throw new ArgumentException("Rows to take must be at least one.", nameof(spec));
            }

            if (options.BatchOrdinal < 0)
            {
                throw new ArgumentException(
                    "A batch ordinal names a position among the queries executed together.",
                    nameof(options));
            }
        }
    }
}
