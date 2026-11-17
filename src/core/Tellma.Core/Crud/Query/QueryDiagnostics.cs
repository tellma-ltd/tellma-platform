// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.Query
{
    // The clause names are spec 0015 §5.2 step 3 (filters[i], namedFilters[i]) and §5.2 step 4
    // (Select[i]), and the engine's own (Select, OrderBy, Having, each with the item's span); spec
    // 0016 §3.5 derives the wire's clause from them.
    /// <summary>
    ///     Locates the composed query's diagnostics in the clauses the caller wrote: a filter leaf at
    ///     its index in the request, a named filter at its index, an ordering taken from the select
    ///     at the select's item. A diagnostic in the platform's own text is a defect, except a
    ///     ceiling, which the caller's text may have pushed the whole query past.
    /// </summary>
    internal static class QueryDiagnostics
    {
        /// <summary>The engine's location of the composed filter's conjuncts.</summary>
        private const string ConjunctPrefix = "Filter.And[";

        /// <summary>The engine's code band of its ceilings.</summary>
        private const string CeilingBand = "QX5";

        /// <summary>The location of a filter the request sent, by its index in the request.</summary>
        /// <param name="index">The index.</param>
        /// <returns>The location.</returns>
        public static string UserFilter(int index)
        {
            return string.Create(CultureInfo.InvariantCulture, $"filters[{index}]");
        }

        /// <summary>The location of a named filter, by its index in the request.</summary>
        /// <param name="index">The index.</param>
        /// <returns>The location.</returns>
        public static string NamedFilter(int index)
        {
            return string.Create(CultureInfo.InvariantCulture, $"namedFilters[{index}]");
        }

        /// <summary>The location of a select item, by its ordinal.</summary>
        /// <param name="ordinal">The ordinal.</param>
        /// <returns>The location.</returns>
        public static string SelectItem(int ordinal)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Select[{ordinal}]");
        }

        /// <summary>Locates a failed compilation's diagnostics in the caller's clauses.</summary>
        /// <param name="diagnostics">The engine's diagnostics.</param>
        /// <param name="map">Where the composed query's parts came from.</param>
        /// <param name="resource">The root's resource, for a defect's message.</param>
        /// <returns>The diagnostics the caller can act on, each once.</returns>
        /// <exception cref="InvalidOperationException">A diagnostic other than a ceiling in the platform's own text.</exception>
        public static IReadOnlyList<QueryexDiagnostic> Locate(IReadOnlyList<QueryexDiagnostic> diagnostics, CompositionMap map, string resource)
        {
            ArgumentNullException.ThrowIfNull(diagnostics);
            ArgumentNullException.ThrowIfNull(map);
            List<QueryexDiagnostic> located = [];
            HashSet<(string Code, string? Location, int Start, int Length)> seen = [];
            foreach (QueryexDiagnostic diagnostic in diagnostics)
            {
                QueryexDiagnostic mapped = Locate(diagnostic, map, resource);
                if (seen.Add((mapped.Code, mapped.Location, mapped.Span.Start, mapped.Span.Length)))
                {
                    located.Add(mapped);
                }
            }

            return located;
        }

        /// <summary>Locates one diagnostic.</summary>
        /// <param name="diagnostic">The diagnostic.</param>
        /// <param name="map">Where the composed query's parts came from.</param>
        /// <param name="resource">The root's resource.</param>
        /// <returns>The located diagnostic.</returns>
        private static QueryexDiagnostic Locate(QueryexDiagnostic diagnostic, CompositionMap map, string resource)
        {
            string? location = diagnostic.Location;

            // --- A conjunct of the composed filter: the caller's leaf, a named filter, or the platform's ---
            if (location is not null && location.StartsWith(ConjunctPrefix, StringComparison.Ordinal))
            {
                int close = location.IndexOf(']', ConjunctPrefix.Length);
                int conjunct = int.Parse(location.AsSpan(ConjunctPrefix.Length, close - ConjunctPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture);
                string rest = location[(close + 1)..];
                Conjunct origin = map.Conjuncts[conjunct];
                return origin.Kind switch
                {
                    ConjunctKind.UserFilter => diagnostic with { Location = UserFilter(origin.Index) + rest },
                    ConjunctKind.NamedFilter => diagnostic with { Location = NamedFilter(origin.Index) + rest },
                    ConjunctKind.Platform => Trusted(diagnostic, "Filter", resource),
                    _ => Trusted(diagnostic, "Filter", resource),
                };
            }

            // --- The select and the ordering: the caller's, the select standing in for the ordering, or a default ---
            return location is not null && location.StartsWith("Select", StringComparison.Ordinal) && !map.SelectSent
                ? Trusted(diagnostic, "Select", resource)
                : location is not null && location.StartsWith("OrderBy", StringComparison.Ordinal)
                ? map.Ordering switch
                {
                    OrderingSource.Sent => diagnostic,
                    // The ordering is the select's text, so the span reads the same item there.
                    OrderingSource.Select => diagnostic with { Location = string.Concat("Select", location.AsSpan("OrderBy".Length)) },
                    OrderingSource.Default => Trusted(diagnostic, "OrderBy", resource),
                    _ => Trusted(diagnostic, "OrderBy", resource),
                }
                : diagnostic;
        }

        /// <summary>
        ///     A diagnostic in the platform's own text: a ceiling the whole query exceeds, located at
        ///     the clause with no span the caller could edit; anything else a defect.
        /// </summary>
        /// <param name="diagnostic">The diagnostic.</param>
        /// <param name="clause">The engine's name of the clause it arose in.</param>
        /// <param name="resource">The root's resource.</param>
        /// <returns>The ceiling, located at the clause.</returns>
        /// <exception cref="InvalidOperationException">The diagnostic is not a ceiling.</exception>
        private static QueryexDiagnostic Trusted(QueryexDiagnostic diagnostic, string clause, string resource)
        {
            return diagnostic.Code.StartsWith(CeilingBand, StringComparison.Ordinal)
                ? diagnostic with { Location = clause, Span = new QueryexSpan(0, 0) }
                : throw new InvalidOperationException(
                    $"The {resource} query's own text failed to compile: {diagnostic.Code} at {diagnostic.Location ?? clause}. A default, a named filter, a search branch or an access filter does not fit the tenant's schema.");
        }
    }

    /// <summary>Where the composed query's parts came from.</summary>
    /// <param name="Conjuncts">The origin of each conjunct of the composed filter, in order.</param>
    /// <param name="SelectSent">Whether the caller sent the select.</param>
    /// <param name="Ordering">Where the ordering came from.</param>
    internal sealed record CompositionMap(IReadOnlyList<Conjunct> Conjuncts, bool SelectSent, OrderingSource Ordering);

    /// <summary>The origin of one conjunct of the composed filter.</summary>
    /// <param name="Kind">Who wrote it.</param>
    /// <param name="Index">Its index in the request, for a filter or a named filter.</param>
    internal readonly record struct Conjunct(ConjunctKind Kind, int Index);

    /// <summary>Who wrote a conjunct of the composed filter.</summary>
    internal enum ConjunctKind
    {
        /// <summary>The caller, as one of the request's filters.</summary>
        UserFilter,

        /// <summary>The stack, as a named filter the request asked for.</summary>
        NamedFilter,

        /// <summary>The platform: the active-only conjunct, the search or the access filter.</summary>
        Platform,
    }

    /// <summary>Where the composed query's ordering came from.</summary>
    internal enum OrderingSource
    {
        /// <summary>The caller sent it.</summary>
        Sent,

        /// <summary>The caller's select, standing in for an aggregate's ordering.</summary>
        Select,

        /// <summary>The surface's default.</summary>
        Default,
    }
}
