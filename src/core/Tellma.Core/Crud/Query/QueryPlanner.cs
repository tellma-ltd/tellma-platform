// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.Query
{
    // The composition is spec 0015 §5.2 steps 3 to 5; the search is §5.2.1; the arguments are §4.3.
    /// <summary>
    ///     Composes an admitted query into the spec, the arguments and the options of the batch's one
    ///     row read, and compiles it under the pipeline's ceilings so every diagnostic is reported
    ///     before the round trip, located in the clause the caller wrote. A singleton: it holds no
    ///     caller state.
    /// </summary>
    /// <param name="engine">The engine.</param>
    /// <param name="discoveries">The discovery cache.</param>
    internal sealed class QueryPlanner(QueryexEngine engine, QueryDiscoveries discoveries)
    {
        /// <summary>The active-only conjunct of an activatable surface.</summary>
        public const string ActiveOnly = "IsActive = true";

        /// <summary>Composes and compiles a query.</summary>
        /// <param name="surface">The surface it runs on.</param>
        /// <param name="shape">The admitted request.</param>
        /// <param name="schema">The tenant's schema.</param>
        /// <param name="root">The root, of that schema.</param>
        /// <param name="accessFilter">The caller's read filter on the root; null when unrestricted.</param>
        /// <param name="customSearch">The service's branch replacing the searchable columns'; null for the platform's.</param>
        /// <param name="hasUser">Whether the batch runs for a user, so the engine admits <c>me</c>.</param>
        /// <returns>The plan.</returns>
        /// <exception cref="BadRequestException">An argument does not convert, or a parameter takes a name the search binds.</exception>
        /// <exception cref="InvalidQueryException">The composed query does not compile.</exception>
        /// <exception cref="InvalidOperationException">The platform's own text does not compile.</exception>
        public PlannedQuery Plan(QuerySurface surface, QueryShape shape, QueryexSchema schema, EntityDescriptor root, FilterTree? accessFilter, FilterTree? customSearch, bool hasUser)
        {
            ArgumentNullException.ThrowIfNull(surface);
            ArgumentNullException.ThrowIfNull(shape);
            ArgumentNullException.ThrowIfNull(schema);
            ArgumentNullException.ThrowIfNull(root);

            // --- The caller's parameters: discovered once per clause texts, bound from the JSON ---
            QueryDiscovery discovery = discoveries.Discover(schema, root, shape);
            QueryArguments arguments = QueryArgumentValues.Bind(discovery.Declarations, shape.Arguments);

            // --- The filter: the caller's leaves, the named filters, then the platform's conjuncts ---
            List<FilterTree> conjuncts = [];
            List<Conjunct> origins = [];
            foreach ((int index, string text) in shape.Filters)
            {
                conjuncts.Add(FilterTree.Leaf(text));
                origins.Add(new Conjunct(ConjunctKind.UserFilter, index));
            }

            foreach ((int index, _, FilterTree tree) in shape.NamedFilters)
            {
                conjuncts.Add(tree);
                origins.Add(new Conjunct(ConjunctKind.NamedFilter, index));
            }

            if (surface.IsActivatable && !shape.IncludeInactive)
            {
                conjuncts.Add(FilterTree.Leaf(ActiveOnly));
                origins.Add(new Conjunct(ConjunctKind.Platform, -1));
            }

            if (shape.Search is string search)
            {
                SearchFilter lowered = QuerySearch.Lower(surface, root, search, customSearch);
                Bind(arguments, discovery.Result, lowered);
                conjuncts.Add(lowered.Tree);
                origins.Add(new Conjunct(ConjunctKind.Platform, -1));
            }

            if (accessFilter is not null)
            {
                conjuncts.Add(accessFilter);
                origins.Add(new Conjunct(ConjunctKind.Platform, -1));
            }

            // --- The select and the ordering: the caller's, else the surface's ---
            // An aggregate has no Id to order by, so its select orders its groups: paging needs an
            // ordering, and the select is the only list every grouped query can order by.
            string select = shape.Select ?? surface.DefaultSelectFor(schema, root);
            (string orderBy, OrderingSource ordering) = shape.OrderBy is string sent
                ? (sent, OrderingSource.Sent)
                : shape.Aggregate ? (select, OrderingSource.Select) : (surface.DefaultOrderBy, OrderingSource.Default);
            QuerySpec spec = new()
            {
                Root = root,
                Select = select,
                Aggregate = shape.Aggregate,
                Filter = conjuncts.Count == 0 ? null : FilterTree.And(conjuncts),
                Having = shape.Having is string having ? FilterTree.Leaf(having) : null,
                OrderBy = orderBy,
                Skip = shape.Skip,
                Take = shape.Take,
            };

            // --- The compilation: under the pipeline's ceilings, every diagnostic before the round trip ---
            QueryexResult<CompiledQuery> compiled = engine.CompileQuery(spec, new QueryCompilationOptions
            {
                Schema = schema,
                QueryexVersion = QueryexLanguage.Version,
                Parameters = arguments.Declarations,
                OmittedParameters = arguments.Omitted,
                HasUser = hasUser,
                Limits = QueryexProfiles.PipelineLimits,
            });
            if (compiled.Value is not CompiledQuery query)
            {
                CompositionMap map = new(origins, shape.Select is not null, ordering);
                throw new InvalidQueryException(QueryDiagnostics.Locate(compiled.Diagnostics, map, surface.Resource));
            }

            // --- The options: the count under its cap, the ancestors through the access filter alone ---
            RowQueryOptions options = new()
            {
                CountCap = shape.IncludeCount ? surface.Limits.CountCap : null,
                IncludeAncestors = shape.IncludeAncestors,
                AncestorsFilter = shape.IncludeAncestors ? accessFilter : null,
                CaptureKeys = shape.IncludeAncestors,
            };
            return new PlannedQuery(spec, arguments, options, discovery.Result, query, shape.Select is not null);
        }

        /// <summary>
        ///     Refuses a select item the wire cannot carry: a geography value has no JSON encoding,
        ///     so selecting one is refused, while filters, orderings and having may use it.
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="resource">The root's resource, for a defect's message.</param>
        /// <exception cref="InvalidQueryException">The caller's select has a geography item: one diagnostic per such item.</exception>
        /// <exception cref="InvalidOperationException">The surface's default select has a geography item.</exception>
        public static void RequireSelectable(PlannedQuery plan, string resource)
        {
            ArgumentNullException.ThrowIfNull(plan);
            List<QueryexDiagnostic>? refused = null;
            int from = 0;
            foreach (QueryexColumn column in plan.Compiled.Columns)
            {
                // Each item's text is a slice of the select, in order, so its span is found after the previous item.
                int start = plan.Spec.Select.IndexOf(column.Text, from, StringComparison.Ordinal);
                if (start >= 0)
                {
                    from = start + column.Text.Length;
                }

                if (column.Type != QueryexType.QxGeography)
                {
                    continue;
                }

                if (!plan.SelectSent)
                {
                    throw new InvalidOperationException(
                        string.Create(CultureInfo.InvariantCulture, $"The {resource} default select reads the geography value '{column.Text}', which no page can carry."));
                }

                (refused ??= []).Add(new QueryexDiagnostic(
                    ValidationCodes.QueryGeographyNotSelectable,
                    new QueryexSpan(Math.Max(start, 0), column.Text.Length),
                    QueryDiagnostics.SelectItem(column.Ordinal),
                    [new KeyValuePair<string, string>("item", column.Text)]));
            }

            if (refused is not null)
            {
                throw new InvalidQueryException(refused);
            }
        }

        /// <summary>
        ///     Binds a search's parameters beside the caller's; a caller's parameter that takes one of
        ///     their names is refused while a search applies, since one name cannot hold two values.
        /// </summary>
        /// <param name="arguments">The caller's bound arguments.</param>
        /// <param name="discovery">The discovery of the caller's clauses.</param>
        /// <param name="search">The lowered search.</param>
        /// <exception cref="BadRequestException">A caller's parameter takes a name the search binds.</exception>
        private static void Bind(QueryArguments arguments, DiscoveryResult discovery, SearchFilter search)
        {
            foreach ((QueryexParameterDeclaration declaration, object? value) in search.Parameters)
            {
                if (discovery.Parameters.Any(parameter => string.Equals(parameter.Name, declaration.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new BadRequestException(
                        $"The parameter '@{declaration.Name}' is the search's own while a search applies; rename it in the query.",
                        new Dictionary<string, object?>(StringComparer.Ordinal) { ["parameter"] = declaration.Name });
                }

                _ = arguments.Add(declaration, value);
            }
        }
    }

    /// <summary>A composed, compiled query: what the batch's row read appends, and what the rules after compilation read.</summary>
    /// <param name="Spec">The spec.</param>
    /// <param name="Arguments">Every parameter's declaration and value: the caller's and the search's.</param>
    /// <param name="Options">The count, the ancestors and the key capture.</param>
    /// <param name="Discovery">The discovery of the caller's own clauses, which the traversal rule checks.</param>
    /// <param name="Compiled">The compilation, whose columns the select-type rule checks.</param>
    /// <param name="SelectSent">Whether the caller sent the select, rather than the surface defaulting it.</param>
    internal sealed record PlannedQuery(QuerySpec Spec, QueryArguments Arguments, RowQueryOptions Options, DiscoveryResult Discovery, CompiledQuery Compiled, bool SelectSent);
}
