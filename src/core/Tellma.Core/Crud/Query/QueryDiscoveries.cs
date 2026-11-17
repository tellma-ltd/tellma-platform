// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Caching.Memory;
using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.Query
{
    // The discovery and its cache are spec 0015 §4.3; the instrument is §16.1.
    /// <summary>
    ///     The argument discovery of a query's own clauses: what they refer to and the type of every
    ///     parameter they mention, cached per tenant schema and clause texts so a warm query never
    ///     discovers again. A singleton.
    /// </summary>
    /// <remarks>
    ///     Only what the caller wrote is discovered: the defaults, the named filters, the active-only
    ///     conjunct, the search and the access filter are the platform's own text. A discovery that
    ///     reports a problem is not cached; the compilation that follows reports it with its
    ///     location.
    /// </remarks>
    /// <param name="engine">The engine.</param>
    /// <param name="metrics">The instruments.</param>
    /// <param name="time">The clock.</param>
    internal sealed class QueryDiscoveries(QueryexEngine engine, CrudMetrics metrics, TimeProvider time) : IDisposable
    {
        /// <summary>The discoveries kept, the most a process needs for its distinct query shapes.</summary>
        public const int Capacity = 1024;

        /// <summary>The discoveries, by schema and clause texts.</summary>
        private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = Capacity });

        /// <summary>Discovers a query's own clauses, from the cache when it holds them.</summary>
        /// <param name="schema">The tenant's schema.</param>
        /// <param name="root">The root, of that schema.</param>
        /// <param name="shape">The admitted request.</param>
        /// <returns>The discovery.</returns>
        public QueryDiscovery Discover(QueryexSchema schema, EntityDescriptor root, QueryShape shape)
        {
            ArgumentNullException.ThrowIfNull(schema);
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(shape);

            // --- Nothing written, nothing to discover ---
            if (shape.Select is null && shape.Filters.Count == 0 && shape.Having is null && shape.OrderBy is null)
            {
                return QueryDiscovery.Empty;
            }

            DiscoveryKey key = new(schema, root.Name, Clauses(shape));
            if (_cache.TryGetValue(key, out QueryDiscovery? cached) && cached is not null)
            {
                return cached;
            }

            // --- A miss: one inference run across every clause the caller wrote ---
            long started = time.GetTimestamp();
            QueryDiscoverySpec spec = new()
            {
                Select = shape.Select,
                Aggregate = shape.Aggregate,
                Filter = UserFilter(shape),
                Having = shape.Having is string having ? FilterTree.Leaf(having) : null,
                OrderBy = shape.OrderBy,
            };
            DiscoveryResult result = engine.DiscoverQuery(spec, new DiscoveryOptions
            {
                Schema = schema,
                Root = root,
                QueryexVersion = QueryexLanguage.Version,
                Limits = QueryexProfiles.PipelineLimits,
            });
            metrics.RecordDiscovery(time.GetElapsedTime(started).TotalSeconds);

            QueryDiscovery discovery = new(result, Declarations(result));
            if (result.Diagnostics.Count == 0)
            {
                _ = _cache.Set(key, discovery, new MemoryCacheEntryOptions { Size = 1 });
            }

            return discovery;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _cache.Dispose();
        }

        /// <summary>
        ///     The caller's filters as the composed filter carries them: one leaf each, conjoined, in
        ///     the order the composition puts them first.
        /// </summary>
        /// <param name="shape">The admitted request.</param>
        /// <returns>The conjunction; null when the caller sent no filter.</returns>
        internal static FilterTree? UserFilter(QueryShape shape)
        {
            return shape.Filters.Count == 0 ? null : FilterTree.And([.. shape.Filters.Select(static filter => FilterTree.Leaf(filter.Text))]);
        }

        /// <summary>
        ///     The declaration of every parameter whose type the clauses settle, nullable: a
        ///     parameter nothing constrains, or whose uses conflict, stays undeclared, and the
        ///     compilation reports it where it is written.
        /// </summary>
        /// <param name="result">The discovery.</param>
        /// <returns>The declarations.</returns>
        private static List<QueryexParameterDeclaration> Declarations(DiscoveryResult result)
        {
            return [.. result.Parameters
                .Where(static parameter => parameter.InferredType is not null && parameter.Conflicts.Count == 0)
                .Select(static parameter => new QueryexParameterDeclaration(parameter.Name, parameter.InferredType!.Value, IsNotNull: false))];
        }

        /// <summary>The clause texts as one key: each length-prefixed, so no two different sets of texts meet.</summary>
        /// <param name="shape">The admitted request.</param>
        /// <returns>The key text.</returns>
        private static string Clauses(QueryShape shape)
        {
            StringBuilder key = new();
            _ = key.Append(shape.Aggregate ? 'a' : 'r');
            Append(key, shape.Select);
            Append(key, shape.Having);
            Append(key, shape.OrderBy);
            foreach ((_, string filter) in shape.Filters)
            {
                Append(key, filter);
            }

            return key.ToString();

            static void Append(StringBuilder key, string? text)
            {
                _ = text is null
                    ? key.Append("|-")
                    : key.Append('|').Append(text.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(text);
            }
        }

        /// <summary>A discovery's cache key.</summary>
        /// <param name="Schema">The tenant's schema, which stands for the model and the shape; compared by instance.</param>
        /// <param name="Root">The root's name.</param>
        /// <param name="Clauses">The clause texts.</param>
        private readonly record struct DiscoveryKey(QueryexSchema Schema, string Root, string Clauses);
    }

    /// <summary>What a query's own clauses refer to, and the declarations of the parameters they mention.</summary>
    /// <param name="Result">The discovery.</param>
    /// <param name="Declarations">One nullable declaration per parameter whose type the clauses settle.</param>
    internal sealed record QueryDiscovery(DiscoveryResult Result, IReadOnlyList<QueryexParameterDeclaration> Declarations)
    {
        /// <summary>The discovery of a query whose caller wrote no clause.</summary>
        public static QueryDiscovery Empty { get; } = new(new DiscoveryResult([], [], [], false, []), []);
    }
}
