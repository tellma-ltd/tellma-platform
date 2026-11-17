// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Calendars;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.Query
{
    // The ceilings are spec 0015 §2.5 and §5.2 step 2; the named filters and the search strip are
    // §5.2 step 3 and §5.2.1.
    /// <summary>
    ///     A query request admitted against its surface's ceilings, read once: the page, the search
    ///     text without its bidi controls, the clauses the caller sent and the named filters
    ///     resolved. Admission performs no I/O, so it refuses before any round trip.
    /// </summary>
    internal sealed class QueryShape
    {
        /// <summary>The page size of a request that sends none.</summary>
        public const int DefaultTake = 50;

        /// <summary>Creates a shape; through <see cref="Admit" /> only.</summary>
        private QueryShape()
        {
        }

        /// <summary>The caller's select; null when the caller sent none.</summary>
        public string? Select { get; private init; }

        /// <summary>The caller's filters that carry text, each with its index in the request.</summary>
        public IReadOnlyList<(int Index, string Text)> Filters { get; private init; } = [];

        /// <summary>The named filters, each with its index in the request and its tree.</summary>
        public IReadOnlyList<(int Index, string Name, FilterTree Tree)> NamedFilters { get; private init; } = [];

        /// <summary>The caller's having clause; null when the caller sent none.</summary>
        public string? Having { get; private init; }

        /// <summary>The caller's ordering; null when the caller sent none.</summary>
        public string? OrderBy { get; private init; }

        /// <summary>The rows to skip.</summary>
        public int Skip { get; private init; }

        /// <summary>The page size.</summary>
        public int Take { get; private init; }

        /// <summary>The search text without its bidi controls; null when there is no search.</summary>
        public string? Search { get; private init; }

        /// <summary>The caller's arguments, by parameter name ignoring case.</summary>
        public IReadOnlyDictionary<string, JsonElement> Arguments { get; private init; } = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Whether the page carries the capped count.</summary>
        public bool IncludeCount { get; private init; }

        /// <summary>Whether the page carries its matches' ancestors: asked for, on a tree stack, of a page of rows.</summary>
        public bool IncludeAncestors { get; private init; }

        /// <summary>Whether inactive rows are included.</summary>
        public bool IncludeInactive { get; private init; }

        /// <summary>Whether the select is an aggregation.</summary>
        public bool Aggregate { get; private init; }

        /// <summary>Admits a request against its surface.</summary>
        /// <param name="request">The request.</param>
        /// <param name="surface">The surface it runs on.</param>
        /// <returns>The shape.</returns>
        /// <exception cref="BadRequestException">A negative skip, a page size below one, a having clause without an aggregation, or two arguments whose names differ only in case.</exception>
        /// <exception cref="LimitExceededException">The page size, the skip window or the search length exceeds its ceiling.</exception>
        /// <exception cref="InvalidQueryException">A named filter the surface does not declare.</exception>
        public static QueryShape Admit(QueryRequest request, QuerySurface surface)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(surface);

            // --- The page: absent is 50; nothing is ever clamped ---
            int skip = request.Skip;
            int take = request.Take ?? DefaultTake;
            if (skip < 0)
            {
                throw new BadRequestException("The rows to skip cannot be negative.", Argument("skip", skip));
            }

            if (take < 1)
            {
                throw new BadRequestException("The page size must be at least one row.", Argument("take", take));
            }

            if (take > surface.Limits.MaxTake)
            {
                throw new LimitExceededException(LimitNames.MaxTake, take, surface.Limits.MaxTake);
            }

            long window = (long)skip + take;
            if (window > surface.Limits.MaxSkipWindow)
            {
                throw new LimitExceededException(LimitNames.SkipWindow, (int)Math.Min(window, int.MaxValue), surface.Limits.MaxSkipWindow);
            }

            // --- The search, once its bidi controls are stripped: empty is no search ---
            string? search = Strip(request.Search);
            if (search is not null && search.Length > surface.Limits.MaxSearchLength)
            {
                throw new LimitExceededException(LimitNames.MaxSearchLength, search.Length, surface.Limits.MaxSearchLength);
            }

            // --- The clauses: blank text is a clause not sent ---
            string? having = Text(request.Having);
            if (having is not null && !request.Aggregate)
            {
                throw new BadRequestException("A having clause applies to an aggregate query only.");
            }

            List<(int, string)> filters = [];
            for (int index = 0; index < (request.Filters?.Count ?? 0); index++)
            {
                if (Text(request.Filters![index]) is string filter)
                {
                    filters.Add((index, filter));
                }
            }

            return new QueryShape
            {
                Select = Text(request.Select),
                Filters = filters,
                NamedFilters = Named(request.NamedFilters, surface),
                Having = having,
                OrderBy = Text(request.OrderBy),
                Skip = skip,
                Take = take,
                Search = search,
                Arguments = ByName(request.Arguments),
                IncludeCount = request.IncludeCount,
                IncludeAncestors = request.IncludeAncestors && surface.IsTree && !request.Aggregate,
                IncludeInactive = request.IncludeInactive,
                Aggregate = request.Aggregate,
            };
        }

        /// <summary>A search text without its bidi controls (ZWJ and ZWNJ kept); null when nothing is left.</summary>
        /// <param name="text">The text as sent.</param>
        /// <returns>The stripped text, or null.</returns>
        internal static string? Strip(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            // Most texts carry no control, so they are returned as they are.
            if (!text.Any(CalendarText.IsBidiControl))
            {
                return text;
            }

            StringBuilder stripped = new(text.Length);
            foreach (char c in text.Where(static c => !CalendarText.IsBidiControl(c)))
            {
                _ = stripped.Append(c);
            }

            return stripped.Length == 0 ? null : stripped.ToString();
        }

        /// <summary>A clause's text, or null when it is absent or blank.</summary>
        /// <param name="text">The text as sent.</param>
        /// <returns>The text, or null.</returns>
        private static string? Text(string? text)
        {
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        /// <summary>
        ///     Resolves the named filters in request order; every name the surface does not declare is
        ///     one diagnostic at its index, all reported together.
        /// </summary>
        /// <param name="names">The names as sent.</param>
        /// <param name="surface">The surface.</param>
        /// <returns>The filters.</returns>
        private static List<(int, string, FilterTree)> Named(IReadOnlyList<string>? names, QuerySurface surface)
        {
            List<(int, string, FilterTree)> named = [];
            List<QueryexDiagnostic>? unknown = null;
            for (int index = 0; index < (names?.Count ?? 0); index++)
            {
                string name = names![index] ?? string.Empty;
                if (surface.NamedFilters.TryGetValue(name, out FilterTree? tree))
                {
                    named.Add((index, name, tree));
                    continue;
                }

                (unknown ??= []).Add(new QueryexDiagnostic(
                    ValidationCodes.QueryUnknownNamedFilter,
                    new QueryexSpan(0, name.Length),
                    QueryDiagnostics.NamedFilter(index),
                    [new KeyValuePair<string, string>("name", name)]));
            }

            return unknown is null ? named : throw new InvalidQueryException(unknown);
        }

        /// <summary>The arguments by name ignoring case; two names that differ only in case are refused.</summary>
        /// <param name="arguments">The arguments as sent.</param>
        /// <returns>The arguments.</returns>
        private static Dictionary<string, JsonElement> ByName(IReadOnlyDictionary<string, JsonElement>? arguments)
        {
            Dictionary<string, JsonElement> byName = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string name, JsonElement value) in arguments ?? new Dictionary<string, JsonElement>())
            {
                if (!byName.TryAdd(name, value))
                {
                    throw new BadRequestException(
                        string.Create(CultureInfo.InvariantCulture, $"The arguments name the parameter '{name}' twice, in two cases."),
                        Argument("parameter", name));
                }
            }

            return byName;
        }

        /// <summary>One argument of a refusal.</summary>
        /// <param name="name">The argument's name.</param>
        /// <param name="value">Its value.</param>
        /// <returns>The arguments.</returns>
        private static Dictionary<string, object?> Argument(string name, object? value)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal) { [name] = value };
        }
    }
}
