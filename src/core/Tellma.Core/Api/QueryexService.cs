// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Access;
using Tellma.Core.Crud.Query;
using Tellma.Core.Queryex;

namespace Tellma.Core.Api
{
    // The endpoint is spec 0029 §9: authorize, discover, compile with the SQL discarded, traverse,
    // and answer 200 with every diagnostic. The Permission profile is spec 0014 §3.7 step 6's
    // filter rules; the traversal rule is spec 0015 §5.2 step 4.
    /// <summary>
    ///     The Queryex authoring endpoint: checks clauses being written in an editor as a query, or a
    ///     role save, would compile them, and reports every problem, every parameter with its type,
    ///     every path with whether the caller may read through it, and on request the filters'
    ///     top-level comparisons. No SQL runs. A call whose caller a prologue confirmed within the
    ///     fast-deny window makes no round trip; otherwise one prologue-only round trip re-validates
    ///     the caller, reloads an aged set and stamps activity, as any guarded batch does.
    /// </summary>
    /// <param name="engine">The query engine.</param>
    /// <param name="schemas">The query schemas, whose widest shape says which roots exist.</param>
    /// <param name="database">The tenant's database, whose schema the clauses compile against.</param>
    /// <param name="connection">The scope's connection, whose connected user's set decides each path.</param>
    /// <param name="runner">Runs the prologue-only batch a caller the cache cannot vouch for is confirmed by.</param>
    /// <param name="evaluator">Decides a securable from the connected user's set.</param>
    /// <param name="securables">The registered securables.</param>
    /// <param name="traversal">The navigation-traversal rule.</param>
    /// <param name="access">The access options, whose filter length a permission filter is held to.</param>
    /// <param name="time">The clock a decision is made at.</param>
    [ApiRoute("queryex")]
    internal sealed class QueryexService(
        QueryexEngine engine,
        IQueryexSchemaProvider schemas,
        ITenantDatabase database,
        IScopeConnection connection,
        IGuardedBatchRunner runner,
        AccessEvaluator evaluator,
        ISecurableRegistry securables,
        QueryTraversalRule traversal,
        IOptions<AccessOptions> access,
        TimeProvider time)
    {
        /// <summary>The engine's code of a parameter used but never declared, which discovery reports as untyped instead.</summary>
        private const string UndeclaredParameter = "QX3007";

        /// <summary>
        ///     Checks clauses being written: as a query compiles them, under the pipeline's ceilings
        ///     and with each path held to the caller's read access, or as a role save validates a
        ///     permission filter. Every problem is in the answer; none fails the call.
        /// </summary>
        /// <param name="request">The clauses.</param>
        /// <param name="cancellationToken">Cancels the caller's connection, when it is not cached.</param>
        /// <returns>What the clauses refer to, and what a query or a role save would refuse them for.</returns>
        /// <exception cref="NotFoundException">No entity has the root's name.</exception>
        /// <exception cref="BadRequestException">
        ///     No root; measures; a having clause without an aggregation; a confirmed parameter without
        ///     a name, with a type no parameter takes, or named twice; or, for a permission filter,
        ///     more than one filter or any other clause or parameter.
        /// </exception>
        [ApiAction("discover", MemberOnly = true, Idempotent = true, Mutation = false, Mcp = McpExposure.Hidden)]
        public async Task<QueryexDiscoverResult> DiscoverAsync(QueryexDiscoverRequest request, CancellationToken cancellationToken = default)
        {
            // --- Admission, before any I/O: the root, then the clauses the profile admits ---
            if (request is null)
            {
                throw new BadRequestException("The request has no body.");
            }

            QueryexSchema schema = database.Schema;
            EntityDescriptor root = Root(request.Root, schema);
            var shape = DiscoverShape.Admit(request);

            // --- The caller's set: from the connect cache when a prologue confirmed it within the
            // fast-deny window and nothing is due; else the guarded runner's prologue with no statement
            // appended, which refuses a deactivated caller, reloads an aged set and stamps activity ---
            ConnectedUser user = await connection.CurrentAsync(cancellationToken).ConfigureAwait(false);
            if (connection.IsPrologueDue(user))
            {
                user = await runner.RunAsync(
                    BatchPurpose.Read,
                    static (_, _) => ValueTask.CompletedTask,
                    static (confirmed, _) => confirmed,
                    cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            return shape.Profile == QueryexProfile.Permission
                ? Permission(shape, schema, root)
                : Query(shape, schema, root, user);
        }

        /// <summary>
        ///     The root, by name: one the widest schema shape does not have is not found, as is one the
        ///     tenant's own schema lacks.
        /// </summary>
        /// <param name="name">The root's name as sent.</param>
        /// <param name="schema">The tenant's schema.</param>
        /// <returns>The root, of the tenant's schema.</returns>
        /// <exception cref="BadRequestException">No name was sent.</exception>
        /// <exception cref="NotFoundException">No entity has the name.</exception>
        private EntityDescriptor Root(string? name, QueryexSchema schema)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new BadRequestException("The root entity is required.");
            }

            // The widest shape holds every entity any tenant has; the tenant's own shape is the one compiled against.
            return schemas.GetSchema(MultilingualShape.All).FindEntity(name) is not null && schema.FindEntity(name) is EntityDescriptor root
                ? root
                : throw new NotFoundException(name, []);
        }

        /// <summary>
        ///     The query profile: discovery over every clause, the compilation the query would run with
        ///     its SQL discarded, and the traversal rule over every path.
        /// </summary>
        /// <param name="shape">The admitted request.</param>
        /// <param name="schema">The tenant's schema.</param>
        /// <param name="root">The root.</param>
        /// <param name="user">The connected user, whose set decides each path.</param>
        /// <returns>The answer.</returns>
        private QueryexDiscoverResult Query(DiscoverShape shape, QueryexSchema schema, EntityDescriptor root, ConnectedUser user)
        {
            // --- The filter as the query composes the caller's: one leaf per filter, conjoined in request order ---
            FilterTree? filter = shape.Filters.Count == 0 ? null : FilterTree.And([.. shape.Filters.Select(static sent => FilterTree.Leaf(sent.Text))]);
            FilterTree? having = shape.Having is string text ? FilterTree.Leaf(text) : null;
            CompositionMap map = new([.. shape.Filters.Select(static sent => new Conjunct(ConjunctKind.UserFilter, sent.Index))], shape.Select is not null, OrderingSource.Sent);

            // --- Discover: one inference run across every clause ---
            DiscoveryResult discovery = engine.DiscoverQuery(
                new QueryDiscoverySpec
                {
                    Select = shape.Select,
                    Aggregate = shape.Aggregate,
                    Filter = filter,
                    Having = having,
                    OrderBy = shape.OrderBy,
                    Conjuncts = shape.Conjuncts,
                },
                new DiscoveryOptions { Schema = schema, Root = root, QueryexVersion = QueryexLanguage.Version, Limits = QueryexProfiles.PipelineLimits });

            // --- Compile as the query would, with the confirmed types and the inferred ones; the SQL is discarded ---
            // The engine's failures stop at the compilation; a parameter it reports undeclared has no type
            // yet, which the parameters' answer says, so that report is dropped (spec 0029 §9 step 3).
            QueryexResult<CompiledQuery> compiled = engine.CompileQuery(
                new QuerySpec
                {
                    Root = root,
                    Select = shape.Select ?? root.Key.Name,
                    Aggregate = shape.Aggregate,
                    Filter = filter,
                    Having = having,
                    OrderBy = shape.OrderBy,
                },
                new QueryCompilationOptions
                {
                    Schema = schema,
                    QueryexVersion = QueryexLanguage.Version,
                    Parameters = Declarations(discovery, shape.Confirmed),
                    HasUser = database.Context.UserId is not null,
                    Limits = QueryexProfiles.PipelineLimits,
                });
            List<QueryDiagnostic> diagnostics = compiled.Value is not null ? [] : [.. QueryDiagnostics
                .Locate([.. compiled.Diagnostics.Where(static diagnostic => diagnostic.Code != UndeclaredParameter)], map, root.Name)
                .Select(diagnostic => QueryWireDiagnostics.ToWire(diagnostic, map))];

            // --- Traverse: each path the caller's set refuses is a diagnostic at the path ---
            IReadOnlyList<TraversalRefusal> refusals = traversal.Refusals(schema, root.Name, discovery, Decisions(user));
            HashSet<PathUse> refused = new(refusals.Select(static refusal => refusal.Path), ReferenceEqualityComparer.Instance);
            foreach (TraversalRefusal refusal in refusals)
            {
                QueryexSite site = Site(refusal.Path.Site, map);
                diagnostics.Add(new QueryDiagnostic(
                    site.Clause,
                    ValidationCodes.QueryPathNotReachable,
                    site.Start,
                    site.Length,
                    site.Location,
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["entity"] = refusal.Entity, ["path"] = string.Join('.', refusal.Path.Segments) }));
            }

            return Answer(diagnostics, discovery, shape, root, map, path => !refused.Contains(path));
        }

        /// <summary>
        ///     The permission profile: the one filter judged as a role save judges it, with discovery
        ///     beside it for its parameters, paths and comparisons. A stored filter is the platform's
        ///     own text when a query runs, so no path is held to the caller's access.
        /// </summary>
        /// <param name="shape">The admitted request.</param>
        /// <param name="schema">The tenant's schema.</param>
        /// <param name="root">The root.</param>
        /// <returns>The answer.</returns>
        private QueryexDiscoverResult Permission(DiscoverShape shape, QueryexSchema schema, EntityDescriptor root)
        {
            // --- Nothing written, nothing to judge ---
            CompositionMap map = new([new Conjunct(ConjunctKind.UserFilter, 0)], SelectSent: false, OrderingSource.Sent);
            if (shape.Filters.Count == 0)
            {
                return new QueryexDiscoverResult([], [], [], UsesAggregation: false, shape.Conjuncts ? [] : null);
            }

            // --- Discover, under the stored filter's ceilings ---
            string filter = shape.Filters[0].Text;
            DiscoveryResult discovery = engine.DiscoverQuery(
                new QueryDiscoverySpec { Filter = FilterTree.And([FilterTree.Leaf(filter)]), Conjuncts = shape.Conjuncts },
                new DiscoveryOptions { Schema = schema, Root = root, QueryexVersion = QueryexLanguage.Version, Limits = QueryexProfiles.AccessLimits });

            // --- Judge, as the role save does: the length, then the validation of a predicate over the root ---
            // The role save stores the text trimmed, so the trimmed text is judged; every span is moved
            // back past the leading whitespace, so it still indexes the editor's text.
            int maxLength = access.Value.MaxFilterLength;
            string stored = filter.Trim();
            int leading = filter.Length - filter.TrimStart().Length;
            PermissionFilterVerdict verdict = PermissionFilterRules.Check(stored, root, schema, QueryexLanguage.Version, maxLength, engine.Validate);
            string clause = QueryDiagnostics.UserFilter(0);
            List<QueryDiagnostic> diagnostics = [];
            if (verdict.MaxLengthExceeded is int exceeded)
            {
                // The range past the maximum, the part to cut.
                diagnostics.Add(new QueryDiagnostic(
                    clause,
                    AccessCodes.FilterTooLong,
                    leading + exceeded,
                    stored.Length - exceeded,
                    Location: null,
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["maxLength"] = exceeded }));
            }
            else if (!verdict.Valid)
            {
                diagnostics.AddRange(verdict.Diagnostics.Select(diagnostic => QueryWireDiagnostics.ToWire(
                    diagnostic with { Span = new QueryexSpan(diagnostic.Span.Start + leading, diagnostic.Span.Length) },
                    map)));
                if (diagnostics.Count == 0)
                {
                    // Refused with no diagnostic to show, as the role save refuses it.
                    diagnostics.Add(new QueryDiagnostic(clause, AccessCodes.FilterInvalid, 0, 0, Location: null, new Dictionary<string, object?>(StringComparer.Ordinal)));
                }
            }

            return Answer(diagnostics, discovery, shape, root, map, static _ => true);
        }

        /// <summary>The answer: the diagnostics, then discovery's parameters, paths and, when asked for, comparisons, located in the request's clauses.</summary>
        /// <param name="diagnostics">The diagnostics.</param>
        /// <param name="discovery">The discovery.</param>
        /// <param name="shape">The admitted request.</param>
        /// <param name="root">The root.</param>
        /// <param name="map">Where the composed query's parts came from.</param>
        /// <param name="reachable">Whether the caller may read through a path.</param>
        /// <returns>The answer.</returns>
        private static QueryexDiscoverResult Answer(
            List<QueryDiagnostic> diagnostics, DiscoveryResult discovery, DiscoverShape shape, EntityDescriptor root, CompositionMap map, Func<PathUse, bool> reachable)
        {
            // --- The parameters: the confirmed type, else the one the uses agree on ---
            List<QueryexParameterInfo> parameters = [.. discovery.Parameters.Select(parameter => new QueryexParameterInfo(
                parameter.Name,
                [.. parameter.Occurrences.Select(occurrence => Site(occurrence, map))],
                shape.Confirmed.TryGetValue(parameter.Name, out QueryexType confirmed) ? confirmed : Inferred(parameter),
                [.. parameter.Conflicts.Select(static conflict => conflict.Type).Distinct()],
                parameter.ComparedWith,
                Identified(root, parameter.ComparedWith)))];

            // --- The paths: the entity each reads its last segment on ---
            List<QueryexPathInfo> paths = [.. discovery.Paths.Select(path => new QueryexPathInfo(path.Segments, Site(path.Site, map), ReadOn(root, path.Segments), reachable(path)))];

            // --- The comparisons, when asked for: each in its filter, with its literal in the wire's encoding ---
            List<QueryexConjunct>? conjuncts = shape.Conjuncts
                ? [.. (discovery.Conjuncts ?? []).Select(conjunct => new QueryexConjunct(
                    FilterOf(conjunct.Site, map),
                    conjunct.Path is null ? null : string.Join('.', conjunct.Path),
                    conjunct.Expression,
                    Operator(conjunct.Operator),
                    Encode(conjunct.Value),
                    conjunct.Type))]
                : null;

            return new QueryexDiscoverResult(diagnostics, parameters, paths, discovery.UsesAggregation, conjuncts);
        }

        /// <summary>
        ///     The declarations the compilation reads: each discovered parameter's confirmed type, else
        ///     the type its uses agree on; a parameter with neither stays undeclared.
        /// </summary>
        /// <param name="discovery">The discovery.</param>
        /// <param name="confirmed">The confirmed types, by name ignoring case.</param>
        /// <returns>The declarations, each nullable as a query binds an absent argument.</returns>
        private static List<QueryexParameterDeclaration> Declarations(DiscoveryResult discovery, IReadOnlyDictionary<string, QueryexType> confirmed)
        {
            List<QueryexParameterDeclaration> declarations = [];
            foreach (ParameterUse parameter in discovery.Parameters)
            {
                QueryexType? type = confirmed.TryGetValue(parameter.Name, out QueryexType sent) ? sent : Inferred(parameter);
                if (type is QueryexType declared)
                {
                    declarations.Add(new QueryexParameterDeclaration(parameter.Name, declared, IsNotNull: false));
                }
            }

            return declarations;
        }

        /// <summary>The type a parameter's uses agree on; null when nothing constrains it or its uses conflict.</summary>
        /// <param name="parameter">The parameter.</param>
        /// <returns>The type, or null.</returns>
        private static QueryexType? Inferred(ParameterUse parameter)
        {
            return parameter.Conflicts.Count == 0 ? parameter.InferredType : null;
        }

        /// <summary>A discovery site, located in the request's clauses.</summary>
        /// <param name="site">The site, as discovery reported it.</param>
        /// <param name="map">Where the composed query's parts came from.</param>
        /// <returns>The wire's site.</returns>
        private static QueryexSite Site(QueryexTextSite site, CompositionMap map)
        {
            return QueryWireDiagnostics.ToSite(QueryDiagnostics.LocateSite(site.Location, map), site.Span, map);
        }

        /// <summary>The index in the request of the filter a comparison was found in.</summary>
        /// <param name="site">The comparison's site.</param>
        /// <param name="map">Where the composed query's parts came from.</param>
        /// <returns>The filter's index.</returns>
        /// <exception cref="InvalidOperationException">The site is in no filter the request sent.</exception>
        private static int FilterOf(QueryexTextSite site, CompositionMap map)
        {
            // Every comparison is a top-level atom of one of the caller's filters, so its clause is that filter's.
            string clause = Site(site, map).Clause;
            const string Prefix = "filters[";
            return clause.StartsWith(Prefix, StringComparison.Ordinal) && clause.EndsWith(']')
                ? int.Parse(clause.AsSpan(Prefix.Length, clause.Length - Prefix.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture)
                : throw new InvalidOperationException($"Discovery reported a comparison at {site.Location}, outside every filter the request sent.");
        }

        /// <summary>The wire's spelling of a comparison.</summary>
        /// <param name="comparison">The comparison.</param>
        /// <returns>The spelling.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The engine defines no such comparison.</exception>
        private static string Operator(FilterConjunctOperator comparison)
        {
            return comparison switch
            {
                FilterConjunctOperator.Equal => "=",
                FilterConjunctOperator.NotEqual => "!=",
                FilterConjunctOperator.Less => "<",
                FilterConjunctOperator.LessOrEqual => "<=",
                FilterConjunctOperator.Greater => ">",
                FilterConjunctOperator.GreaterOrEqual => ">=",
                FilterConjunctOperator.DescendantOf => "descendantOf",
                _ => throw new ArgumentOutOfRangeException(nameof(comparison), comparison, "The engine defines no such comparison."),
            };
        }

        /// <summary>A literal in the wire's scalar encoding: a decimal as its digits in a string, a date as <c>yyyy-MM-dd</c>, and so on.</summary>
        /// <param name="value">The literal's CLR value; null for a null literal.</param>
        /// <returns>The encoded literal; null for a null one.</returns>
        private static JsonElement? Encode(object? value)
        {
            return value is null ? null : JsonSerializer.SerializeToElement(value, value.GetType(), TellmaJson.Options);
        }

        /// <summary>The entity a path's last segment is read on: the root, or the target of the navigation before it.</summary>
        /// <param name="root">The root.</param>
        /// <param name="segments">The path's segments, from the root.</param>
        /// <returns>The entity's name.</returns>
        private static string ReadOn(EntityDescriptor root, IReadOnlyList<string> segments)
        {
            EntityDescriptor at = root;
            for (int index = 0; index < segments.Count - 1 && SchemaLookup.Navigation(at, segments[index]) is NavigationDescriptor navigation; index++)
            {
                at = navigation.Target;
            }

            return at.Name;
        }

        /// <summary>The entity a path identifies when it ends at a key, or at a foreign key, whose target it then is.</summary>
        /// <param name="root">The root.</param>
        /// <param name="segments">The path's segments, from the root; null for none.</param>
        /// <returns>The entity's name; null when the path ends at neither.</returns>
        private static string? Identified(EntityDescriptor root, IReadOnlyList<string>? segments)
        {
            if (segments is not { Count: > 0 })
            {
                return null;
            }

            // --- The entity the last segment is read on ---
            EntityDescriptor at = root;
            for (int index = 0; index < segments.Count - 1; index++)
            {
                if (SchemaLookup.Navigation(at, segments[index]) is not NavigationDescriptor navigation)
                {
                    return null;
                }

                at = navigation.Target;
            }

            // --- Its key identifies it; a foreign key identifies the navigation's target ---
            string last = segments[^1];
            return string.Equals(at.Key.Name, last, StringComparison.OrdinalIgnoreCase) ? at.Name
                : at.Navigations.FirstOrDefault(navigation => string.Equals(navigation.ForeignKey.Name, last, StringComparison.OrdinalIgnoreCase))?.Target.Name;
        }

        /// <summary>The caller's read decision per entity, each made once from the connected user's set, as a query's composition makes it.</summary>
        /// <param name="user">The connected user.</param>
        /// <returns>The decisions.</returns>
        private Func<string, AccessDecision> Decisions(ConnectedUser user)
        {
            DateTimeOffset now = time.GetUtcNow();
            Dictionary<string, AccessDecision> decided = new(StringComparer.OrdinalIgnoreCase);
            return entity =>
            {
                if (!decided.TryGetValue(entity, out AccessDecision? decision))
                {
                    decision = securables.Find(entity, AccessActions.Read) is SecurableDescriptor securable
                        ? evaluator.DecideFrom(user.Access, securable, now)
                        : evaluator.Unknown(entity, AccessActions.Read);
                    decided[entity] = decision;
                }

                return decision;
            };
        }
    }
}
