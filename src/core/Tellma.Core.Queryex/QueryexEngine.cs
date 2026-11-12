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
                QueryexVersion = options.QueryexVersion,
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
        ///     A root was supplied without its schema, a group-level predicate was supplied for a
        ///     query that does not group, or a rebase or semi-join does not fit the schema or sits in
        ///     the group-level predicate.
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
            HostStructure.RequireFilters(spec, options.Schema, options.Root);
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
        ///     negative, the declarations are not usable, an omitted parameter is not declared, a
        ///     key-set restriction does not fit the schema, the undeclared columns are not usable, or
        ///     a rebase or semi-join does not fit the schema or sits in the group-level predicate.
        /// </exception>
        /// <remarks>
        ///     The only door to a query's SQL there is. A fragment compiled on its own could not
        ///     share joins, aliases, or parameters with the query it would eventually be joined to,
        ///     so offering one would be an invitation to paste two fragments together — which is the
        ///     failure structural composition exists to rule out.
        /// </remarks>
        public QueryexResult<CompiledQuery> CompileQuery(QuerySpec spec, QueryCompilationOptions options)
        {
            ArgumentNullException.ThrowIfNull(spec);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(spec.Select);
            RequireEntity(options.Schema, spec.Root);
            RequirePaging(spec, options);

            // Checked on every call, a hit included: host structure that does not fit the schema
            // is refused at the call that supplied it, whatever an earlier call left in the cache.
            ImmutableArray<ResolvedRestriction> restrictions = HostStructure.ResolveRestrictions(spec);
            HostStructure.RequireUndeclaredColumns(spec);
            HostStructure.RequireFilters(spec, options.Schema);

            // Before the cache is consulted, because the resolved set is part of what a compilation
            // is remembered under — and so, like every other check of what the host passed, on
            // every call.
            var omitted = OmittedParameters.Resolve(options, nameof(options));
            var key = QueryKey.Of(spec, options, omitted);
            if (_queries.TryGet(key, out CompiledQuery? cached) && cached is not null)
            {
                return new QueryexResult<CompiledQuery>(Restamped(cached, spec));
            }

            QueryexResult<CompiledQuery> result = QueryCompiler.Compile(
                _compiler,
                spec,
                options,
                restrictions,
                omitted);
            if (result.Succeeded)
            {
                _queries.Set(key, result.Value);
            }

            return result;
        }

        /// <summary>
        ///     Compiles expressions that read no row into one statement that answers them as one row.
        /// </summary>
        /// <param name="items">The expressions, each with the type its value has to have.</param>
        /// <param name="options">The compilation options.</param>
        /// <returns>The compiled statement, or the diagnostics that stopped it.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentException">
        ///     There are no items or more than the ceiling on list items permits, an item or its text
        ///     is null, an item's type is not one the language has, or the batch ordinal is negative.
        /// </exception>
        /// <remarks>
        ///     <para>
        ///         How a host learns the values of expressions that depend on nothing but when and by
        ///         whom they run — the defaults a report run applies, say. The engine compiles the
        ///         statement and the host executes it, exactly as with a query: the values that
        ///         depend on the execution come out as the same named slots, so a host that binds
        ///         them from the values it binds a query's with gets one consistent answer across
        ///         both.
        ///     </para>
        ///     <para>
        ///         The statement has a select list and no source, so it always answers with exactly
        ///         one row, one column per item in the order supplied. An item names no path, no
        ///         aggregation and no parameter, and is a single expression. Nothing is hoisted, so
        ///         the declarations part of the result is empty.
        ///     </para>
        ///     <para>
        ///         Complete in itself, like a compiled query: it shares nothing with another
        ///         statement, and is not a fragment to paste into one.
        ///     </para>
        /// </remarks>
        public QueryexResult<CompiledQuery> EvaluateConstant(
            IReadOnlyList<ConstantItem> items,
            ConstantCompilationOptions options)
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(options);

            // Copied before anything is checked, so what was checked is what is compiled and what
            // the compilation is remembered under, whatever the caller does with the list meanwhile.
            ImmutableArray<ConstantItem> snapshot = [.. items];
            RequireConstants(snapshot, options);

            var key = QueryKey.OfConstant(snapshot, options);
            if (_queries.TryGet(key, out CompiledQuery? cached) && cached is not null)
            {
                return new QueryexResult<CompiledQuery>(cached);
            }

            QueryexResult<CompiledQuery> result = ConstantCompiler.Compile(_compiler, snapshot, options);
            if (result.Succeeded)
            {
                _queries.Set(key, result.Value);
            }

            return result;
        }

        /// <summary>
        ///     Prints a text with its arguments written in as literals and its omitted parameters
        ///     folded away.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="options">The arguments, the omitted parameters and what they are declared as.</param>
        /// <returns>The canonical text, or the diagnostics that stopped it parsing.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentException">
        ///     The declarations are not usable, a valued or omitted name is not declared, a name is
        ///     given more than one value or is both given a value and omitted, or a value is not one
        ///     of its parameter's declared type.
        /// </exception>
        /// <remarks>
        ///     <para>
        ///         What a host hands to something that runs a text on its own, such as a list of the
        ///         rows behind a number: parsing the answer gives the text's own parse with each
        ///         valued parameter replaced by a literal, and with the omitted ones folded exactly as
        ///         a compilation folds them — along the skeleton of a predicate, and in the condition
        ///         of every aggregation. A parameter that is neither stays as written. A text that
        ///         folds away entirely prints as <c>true</c> or <c>false</c>.
        ///     </para>
        ///     <para>
        ///         A number prints as its digits, negative ones behind a unary minus; text quoted;
        ///         a truth value as its keyword; a date, a date and time, an instant and an
        ///         identifier as a conversion of their canonical text; and the absent value, or an
        ///         omitted parameter where a value is read, as the conversion of <c>null</c> to the
        ///         declared type. Syntactic only: no schema is consulted, and only lexical and
        ///         syntax errors are reported.
        ///     </para>
        /// </remarks>
        public QueryexResult<string> BindParameters(string text, ParameterBindingOptions options)
        {
            ArgumentNullException.ThrowIfNull(text);
            ArgumentNullException.ThrowIfNull(options);

            return ParameterBinder.Bind(_compiler, text, options);
        }

        /// <summary>
        ///     Works out which rows a measure aggregates, as a self-contained row-level predicate.
        /// </summary>
        /// <param name="measure">The measure.</param>
        /// <param name="options">
        ///     The arguments, the omitted parameters and what they are declared as; the mode is not
        ///     consulted, since an aggregation's condition folds in every position.
        /// </param>
        /// <returns>The rows the measure aggregates, or the diagnostics that stopped it parsing.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentException">
        ///     Whatever <see cref="BindParameters" /> refuses of the options.
        /// </exception>
        /// <remarks>
        ///     <para>
        ///         What a list opened from a measure's number runs, so that it lists exactly the
        ///         rows the number aggregates. Each aggregation's condition, its second argument, is
        ///         printed as <see cref="BindParameters" /> prints a text. The answer is every row —
        ///         a null condition — when any aggregation has no condition or one that folds to
        ///         true, and when the measure has no aggregation at all. Otherwise it is the distinct
        ///         conditions, compared as the text they print as, each parenthesized and joined
        ///         with <c>or</c> in the order they first appear; a condition that folds to false
        ///         drops out, and when every one does the answer is <c>false</c>.
        ///     </para>
        ///     <para>
        ///         Syntactic only, like <see cref="BindParameters" />: no schema is consulted, and
        ///         only lexical and syntax errors are reported.
        ///     </para>
        /// </remarks>
        public QueryexResult<MeasureRows> MeasureCondition(string measure, ParameterBindingOptions options)
        {
            ArgumentNullException.ThrowIfNull(measure);
            ArgumentNullException.ThrowIfNull(options);

            return ParameterBinder.MeasureCondition(_compiler, measure, options);
        }

        /// <summary>Checks the parts of a constant statement the caller is responsible for.</summary>
        /// <param name="items">The items.</param>
        /// <param name="options">The compilation options.</param>
        /// <exception cref="ArgumentException">One of them is not usable.</exception>
        private static void RequireConstants(ImmutableArray<ConstantItem> items, ConstantCompilationOptions options)
        {
            if (items.Length == 0 || items.Length > options.Limits.MaxListItems)
            {
                // How many items there are is the host's doing, not something a user typed, so it is
                // refused here rather than reported as a diagnostic nobody could act on.
                throw new ArgumentException(
                    "A constant statement takes at least one item and no more than the ceiling on list items.",
                    nameof(items));
            }

            foreach (ConstantItem? item in items)
            {
                if (item is null || item.Text is null)
                {
                    throw new ArgumentException("A constant item and its text cannot be null.", nameof(items));
                }

                if (!Enum.IsDefined(item.Type))
                {
                    throw new ArgumentException(
                        "A constant item's type must be one of the language's types.",
                        nameof(items));
                }
            }

            if (options.BatchOrdinal < 0)
            {
                throw new ArgumentException(
                    "A batch ordinal names a position among the queries executed together.",
                    nameof(options));
            }
        }

        /// <summary>A cached compilation, carrying this call's page.</summary>
        /// <param name="cached">The compilation a call for another page may have left.</param>
        /// <param name="spec">This call's query.</param>
        /// <returns>The compilation, with the paging slots holding this call's values.</returns>
        /// <remarks>
        ///     The key holds whether a query pages, never which page: two pages of one query are one
        ///     statement, so the cached template and the backend's cached plan serve both. The values
        ///     live only in the paging slots, which are always the last one or two parameters, skip
        ///     before take — so each caller still sees its own page.
        /// </remarks>
        private static CompiledQuery Restamped(CompiledQuery cached, QuerySpec spec)
        {
            int paging = (spec.Skip is null ? 0 : 1) + (spec.Take is null ? 0 : 1);
            if (paging == 0)
            {
                return cached;
            }

            QueryexParameterSlot[] parameters = [.. cached.Parameters];
            int index = parameters.Length - paging;
            if (spec.Skip is int skip)
            {
                parameters[index] = parameters[index] with { Value = skip };
                index++;
            }

            if (spec.Take is int take)
            {
                parameters[index] = parameters[index] with { Value = take };
            }

            return cached with { Parameters = parameters };
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
