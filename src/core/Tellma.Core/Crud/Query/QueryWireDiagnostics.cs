// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Api;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.Query
{
    // The wire's clause names are spec 0016 §3.4-§3.5's closed list; the location within a clause is
    // QueryDiagnostic's own rule: the item of a list clause, the path below a filter's root, and
    // nothing for a single expression.
    /// <summary>
    ///     Carries a located query diagnostic, or a located discovery site, to the wire: the request
    ///     clause it concerns (<c>select</c>, <c>filters[i]</c>, <c>namedFilters[i]</c>,
    ///     <c>orderBy</c>, <c>having</c>, <c>measures</c>) and the item within it, with the engine's
    ///     code, span and arguments verbatim.
    /// </summary>
    internal static class QueryWireDiagnostics
    {
        /// <summary>The wire's name of the select clause.</summary>
        public const string SelectClause = "select";

        /// <summary>The wire's name of the ordering clause.</summary>
        public const string OrderByClause = "orderBy";

        /// <summary>The wire's name of the having clause.</summary>
        public const string HavingClause = "having";

        /// <summary>The wire's name of the measures clause.</summary>
        public const string MeasuresClause = "measures";

        /// <summary>The located prefix of a filter the request sent.</summary>
        private const string FiltersPrefix = "filters[";

        /// <summary>The located prefix of a named filter the request asked for.</summary>
        private const string NamedFiltersPrefix = "namedFilters[";

        /// <summary>The engine's location of the select.</summary>
        private const string Select = "Select";

        /// <summary>The engine's location of the ordering.</summary>
        private const string OrderBy = "OrderBy";

        /// <summary>The engine's location of the having clause.</summary>
        private const string Having = "Having";

        /// <summary>The engine's location of the measures.</summary>
        private const string Measures = "Measures";

        /// <summary>One located diagnostic as the wire carries it.</summary>
        /// <param name="diagnostic">The diagnostic, located in the caller's clauses.</param>
        /// <param name="map">Where the composed query's parts came from, which places a diagnostic the engine located nowhere in particular.</param>
        /// <returns>The wire's diagnostic.</returns>
        public static QueryDiagnostic ToWire(QueryexDiagnostic diagnostic, CompositionMap map)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            ArgumentNullException.ThrowIfNull(map);
            (string clause, string? location) = Place(diagnostic.Location, map);

            // The arguments verbatim, the first of a repeated name kept.
            Dictionary<string, object?> arguments = new(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> argument in diagnostic.Arguments)
            {
                _ = arguments.TryAdd(argument.Key, argument.Value);
            }

            return new QueryDiagnostic(clause, diagnostic.Code, diagnostic.Span.Start, diagnostic.Span.Length, location, arguments);
        }

        /// <summary>One located discovery site as the wire carries it.</summary>
        /// <param name="location">The site's location, located in the caller's clauses.</param>
        /// <param name="span">The site's span within its clause's text.</param>
        /// <param name="map">Where the composed query's parts came from.</param>
        /// <returns>The wire's site.</returns>
        public static QueryexSite ToSite(string? location, QueryexSpan span, CompositionMap map)
        {
            ArgumentNullException.ThrowIfNull(map);
            (string clause, string? within) = Place(location, map);
            return new QueryexSite(clause, within, span.Start, span.Length);
        }

        /// <summary>
        ///     The request clause a located location names, and the item within it: a filter's path
        ///     below its root (<c>Or[1]</c>), a list clause's item as the engine names it
        ///     (<c>Select[2]</c>), and null for the clause itself or a single expression. A location
        ///     the engine gave no clause, such as a ceiling the whole query exceeds, is placed at the
        ///     clause a ceiling the platform's text reached is placed at.
        /// </summary>
        /// <param name="location">The located location; null when the engine gave none.</param>
        /// <param name="map">Where the composed query's parts came from.</param>
        /// <returns>The clause and the item within it.</returns>
        public static (string Clause, string? Location) Place(string? location, CompositionMap map)
        {
            ArgumentNullException.ThrowIfNull(map);
            return Placed(location) ?? Placed(QueryDiagnostics.CallerFilterClause(map)) ?? (SelectClause, null);
        }

        /// <summary>The clause and item a located location names, when it names a clause.</summary>
        /// <param name="location">The located location.</param>
        /// <returns>The clause and the item within it; null when the location names no clause of the request.</returns>
        private static (string Clause, string? Location)? Placed(string? location)
        {
            // --- A filter or a named filter, at its index in the request, and the path below its root ---
            if (location is null)
            {
                return null;
            }

            if (location.StartsWith(FiltersPrefix, StringComparison.Ordinal) || location.StartsWith(NamedFiltersPrefix, StringComparison.Ordinal))
            {
                int close = location.IndexOf(']', StringComparison.Ordinal);
                if (close < 0)
                {
                    return null;
                }

                string rest = location[(close + 1)..];
                return (location[..(close + 1)], rest.Length == 0 ? null : rest.TrimStart('.'));
            }

            // --- The list clauses keep the engine's item; the having clause, a filter tree, its path below the root ---
            return Item(location, Select, SelectClause)
                ?? Item(location, OrderBy, OrderByClause)
                ?? Item(location, Measures, MeasuresClause)
                ?? (location == Having ? (HavingClause, null)
                    : location.StartsWith(Having + ".", StringComparison.Ordinal) ? (HavingClause, location[(Having.Length + 1)..])
                    : null);
        }

        /// <summary>The clause and item of a location in a list clause.</summary>
        /// <param name="location">The location.</param>
        /// <param name="engine">The engine's name of the clause (<c>Select</c>).</param>
        /// <param name="wire">The wire's name of the clause (<c>select</c>).</param>
        /// <returns>The clause and the item, null for the whole clause; null when the location is not in the clause.</returns>
        private static (string Clause, string? Location)? Item(string location, string engine, string wire)
        {
            return location == engine ? (wire, null)
                : location.StartsWith(engine + "[", StringComparison.Ordinal) ? (wire, location)
                : null;
        }
    }
}
