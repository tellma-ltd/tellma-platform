// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Queryex;

namespace Tellma.Core.Api
{
    // The admission is spec 0029 §9 step 1; a having clause without an aggregation is refused as
    // the query refuses it (spec 0015 §5.2 step 2), and so are measures without one.
    /// <summary>
    ///     A discovery request admitted against its profile, read once: the clauses that carry text,
    ///     each filter with its index in the request, and the confirmed parameter types by name.
    ///     Admission performs no I/O, so it refuses before any round trip.
    /// </summary>
    internal sealed class DiscoverShape
    {
        /// <summary>Creates a shape; through <see cref="Admit" /> only.</summary>
        private DiscoverShape()
        {
        }

        /// <summary>The profile the text is checked under.</summary>
        public QueryexProfile Profile { get; private init; }

        /// <summary>The select; null when none was sent.</summary>
        public string? Select { get; private init; }

        /// <summary>The filters that carry text, each with its index in the request.</summary>
        public IReadOnlyList<(int Index, string Text)> Filters { get; private init; } = [];

        /// <summary>The having clause; null when none was sent.</summary>
        public string? Having { get; private init; }

        /// <summary>The ordering; null when none was sent.</summary>
        public string? OrderBy { get; private init; }

        /// <summary>The measures of an aggregate query; null when none were sent.</summary>
        public string? Measures { get; private init; }

        /// <summary>Whether the select is an aggregation.</summary>
        public bool Aggregate { get; private init; }

        /// <summary>Whether the answer carries the filters' top-level comparisons.</summary>
        public bool Conjuncts { get; private init; }

        /// <summary>The confirmed parameter types, by name ignoring case.</summary>
        public IReadOnlyDictionary<string, QueryexType> Confirmed { get; private init; } = new Dictionary<string, QueryexType>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Admits a request.</summary>
        /// <param name="request">The request.</param>
        /// <returns>The shape.</returns>
        /// <exception cref="BadRequestException">
        ///     An undefined profile; measures or a having clause without an aggregation; a confirmed
        ///     parameter without a name, with a type no parameter takes, or named twice; or, for a
        ///     permission filter, more than one filter or any other clause or parameter.
        /// </exception>
        public static DiscoverShape Admit(QueryexDiscoverRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!Enum.IsDefined(request.Profile))
            {
                throw new BadRequestException("The profile is not one the endpoint defines.", Argument("profile", (int)request.Profile));
            }

            // --- The clauses: blank text is a clause not sent ---
            string? select = Text(request.Select);
            string? having = Text(request.Having);
            string? orderBy = Text(request.OrderBy);
            string? measures = Text(request.Measures);
            List<(int, string)> filters = [];
            for (int index = 0; index < (request.Filters?.Count ?? 0); index++)
            {
                if (Text(request.Filters![index]) is string filter)
                {
                    filters.Add((index, filter));
                }
            }

            // --- The group clauses: an aggregate's alone ---
            if (measures is not null && !request.Aggregate)
            {
                throw new BadRequestException("Measures apply to an aggregate query only.");
            }

            if (having is not null && !request.Aggregate)
            {
                throw new BadRequestException("A having clause applies to an aggregate query only.");
            }

            // --- A permission filter: one filter and nothing else, as a role save stores it ---
            bool beyondFilter = (request.Filters?.Count ?? 0) > 1 || select is not null || having is not null || orderBy is not null || measures is not null
                || request.Aggregate || request.Parameters is { Count: > 0 };
            return request.Profile == QueryexProfile.Permission && beyondFilter
                ? throw new BadRequestException("A permission filter is one filter, with no other clause, no aggregation and no parameter.")
                : new DiscoverShape
                {
                    Profile = request.Profile,
                    Select = select,
                    Filters = filters,
                    Having = having,
                    OrderBy = orderBy,
                    Measures = measures,
                    Aggregate = request.Aggregate,
                    Conjuncts = request.Conjuncts,
                    Confirmed = ConfirmedTypes(request.Parameters),
                };
        }

        /// <summary>A clause's text, or null when it is absent or blank.</summary>
        /// <param name="text">The text as sent.</param>
        /// <returns>The text, or null.</returns>
        private static string? Text(string? text)
        {
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        /// <summary>The confirmed types by name ignoring case; a missing name, a type no parameter takes and a name sent twice are refused.</summary>
        /// <param name="parameters">The confirmed types as sent.</param>
        /// <returns>The types.</returns>
        private static Dictionary<string, QueryexType> ConfirmedTypes(IReadOnlyList<QueryexParameterInput>? parameters)
        {
            Dictionary<string, QueryexType> confirmed = new(StringComparer.OrdinalIgnoreCase);
            foreach (QueryexParameterInput? parameter in parameters ?? [])
            {
                if (parameter is null || string.IsNullOrWhiteSpace(parameter.Name))
                {
                    throw new BadRequestException("A confirmed parameter needs a name.");
                }

                // A hierarchy position and a geography have no parameter form.
                if (!Enum.IsDefined(parameter.Type) || parameter.Type is QueryexType.QxHierarchyId or QueryexType.QxGeography)
                {
                    throw new BadRequestException(
                        string.Create(CultureInfo.InvariantCulture, $"The parameter '{parameter.Name}' is confirmed as a type no parameter takes."),
                        Argument("parameter", parameter.Name));
                }

                if (!confirmed.TryAdd(parameter.Name, parameter.Type))
                {
                    throw new BadRequestException(
                        string.Create(CultureInfo.InvariantCulture, $"The parameter '{parameter.Name}' is confirmed twice."),
                        Argument("parameter", parameter.Name));
                }
            }

            return confirmed;
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
