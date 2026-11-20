// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Net.Http.Headers;
using Tellma.Core.AspNetCore.Routing;

namespace Tellma.Core.AspNetCore.Problems
{
    // Spec 0016 §7.5: a verb the route does not accept is 405 and an unsupported media type 415.
    // The SPA's fallback claims every path and verb, so routing never produces either on its own:
    // the endpoints its path matches are rejected by verb or media type and the fallback wins.
    // Without this, the SPA would read the miss as route-not-found, its sign of an outdated client.
    /// <summary>
    ///     Tells why a request the fallback answered matched no endpoint: a route that serves its
    ///     path under other verbs, one that serves its verb but not its media type, or none at all.
    /// </summary>
    /// <param name="endpoints">The application's mapped endpoints, read once on the first miss.</param>
    /// <param name="policies">The factory that builds the routes' parameter constraints.</param>
    internal sealed class RouteMisses(MappedEndpoints endpoints, ParameterPolicyFactory policies)
    {
        /// <summary>The routes a miss is matched against, indexed by path shape, built on the first miss.</summary>
        private CandidateIndex? _candidates;

        /// <summary>
        ///     The problem of a request no endpoint took: 405 with its <c>Allow</c> header when a route
        ///     serves its path under other verbs, 415 when one serves its verb but not its body's
        ///     media type, null when no route serves its path.
        /// </summary>
        /// <param name="http">The request, whose response has not started.</param>
        /// <returns>The problem, or null for a route miss.</returns>
        public ProblemDescription? Classify(HttpContext http)
        {
            ArgumentNullException.ThrowIfNull(http);

            // --- The routes whose pattern and constraints match the path, whatever their verbs ---
            // Only the routes of the path's shape are tried: an anonymous miss never scans the table.
            List<string> allowed = [];
            bool unsupportedMedia = false;
            RouteValueDictionary values = [];
            foreach (Candidate candidate in Candidates().Of(http.Request.Path))
            {
                values.Clear();
                if (!candidate.Matcher.TryMatch(http.Request.Path, values) || !candidate.Admits(http, values))
                {
                    continue;
                }

                if (!candidate.Methods.Contains(http.Request.Method, StringComparer.OrdinalIgnoreCase))
                {
                    allowed.AddRange(candidate.Methods);
                    continue;
                }

                // The verb is the route's: only the body's media type can have turned it away.
                unsupportedMedia |= !candidate.AcceptsBody(http.Request);
            }

            // --- As routing decides, the verb first: a route that takes the verb but not the body is a 415 ---
            if (unsupportedMedia)
            {
                return ProblemMapper.ForStatus(StatusCodes.Status415UnsupportedMediaType, routeMiss: false);
            }

            if (allowed.Count > 0)
            {
                http.Response.Headers[HeaderNames.Allow] = string.Join(", ", allowed.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal));
                return ProblemMapper.ForStatus(StatusCodes.Status405MethodNotAllowed, routeMiss: false);
            }

            return null;
        }

        /// <summary>The routes stating their verbs, the fallback aside, indexed once.</summary>
        /// <returns>The index.</returns>
        private CandidateIndex Candidates()
        {
            CandidateIndex? built = Volatile.Read(ref _candidates);
            if (built is not null)
            {
                return built;
            }

            // A fallback runs last; every other route that names its verbs is one a miss may have meant.
            built = new CandidateIndex([.. endpoints.Endpoints()
                .OfType<RouteEndpoint>()
                .Where(static endpoint => endpoint.Order != int.MaxValue)
                .Select(endpoint => (Endpoint: endpoint, Methods: endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods))
                .Where(static entry => entry.Methods is { Count: > 0 })
                .Select(entry => (entry.Endpoint.RoutePattern, Candidate.Of(entry.Endpoint, entry.Methods!, policies)))]);
            _ = Interlocked.CompareExchange(ref _candidates, built, null);
            return Volatile.Read(ref _candidates)!;
        }

        /// <summary>
        ///     The candidates by path shape: a fixed-length pattern under each segment count it spans
        ///     and the literal its last segment is at that count (or under any last segment, when that
        ///     segment holds a parameter); a catch-all pattern among those tried for every longer path.
        /// </summary>
        private sealed class CandidateIndex
        {
            /// <summary>The key of a last segment that holds a parameter.</summary>
            private const string AnySegment = "{}";

            /// <summary>The fixed-length candidates, by segment count and last literal.</summary>
            private readonly Dictionary<(int Count, string Last), List<Candidate>> _fixed = [];

            /// <summary>The catch-all candidates, each with the fewest segments it matches.</summary>
            private readonly List<(int Minimum, Candidate Candidate)> _catchAll = [];

            /// <summary>Indexes the candidates.</summary>
            /// <param name="candidates">Each candidate with its route's pattern.</param>
            public CandidateIndex(IReadOnlyList<(RoutePattern Pattern, Candidate Candidate)> candidates)
            {
                foreach ((RoutePattern pattern, Candidate candidate) in candidates)
                {
                    IReadOnlyList<RoutePatternPathSegment> segments = pattern.PathSegments;
                    int minimum = segments.Count;
                    while (minimum > 0 && IsOmittable(pattern, segments[minimum - 1]))
                    {
                        minimum--;
                    }

                    // --- A catch-all takes any number of segments past its fixed ones ---
                    if (segments.Any(static segment => segment.Parts.Any(static part => part is RoutePatternParameterPart { IsCatchAll: true })))
                    {
                        _catchAll.Add((minimum, candidate));
                        continue;
                    }

                    // --- An omittable trailing segment makes the pattern match several counts, each ending elsewhere ---
                    for (int count = minimum; count <= segments.Count; count++)
                    {
                        string last = count == 0 ? AnySegment : Literal(segments[count - 1]) ?? AnySegment;
                        if (!_fixed.TryGetValue((count, last), out List<Candidate>? bucket))
                        {
                            bucket = [];
                            _fixed[(count, last)] = bucket;
                        }

                        bucket.Add(candidate);
                    }
                }
            }

            /// <summary>The candidates a path may match: those of its segment count and last segment, then the catch-alls.</summary>
            /// <param name="path">The request's path.</param>
            /// <returns>The candidates, each still to be matched in full.</returns>
            public IEnumerable<Candidate> Of(PathString path)
            {
                string[] segments = (path.Value ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
                string last = segments.Length == 0 ? AnySegment : segments[^1].ToUpperInvariant();
                foreach (string key in (string[])[last, AnySegment])
                {
                    if (_fixed.TryGetValue((segments.Length, key), out List<Candidate>? bucket))
                    {
                        foreach (Candidate candidate in bucket)
                        {
                            yield return candidate;
                        }
                    }
                }

                foreach ((int minimum, Candidate candidate) in _catchAll)
                {
                    if (segments.Length >= minimum)
                    {
                        yield return candidate;
                    }
                }
            }

            /// <summary>Whether a trailing segment may be left out: one optional, defaulted or catch-all parameter alone.</summary>
            /// <param name="pattern">The pattern.</param>
            /// <param name="segment">The segment.</param>
            /// <returns>True when a path may end before it.</returns>
            private static bool IsOmittable(RoutePattern pattern, RoutePatternPathSegment segment)
            {
                return segment.Parts is [RoutePatternParameterPart parameter]
                    && (parameter.IsOptional || parameter.IsCatchAll || pattern.Defaults.ContainsKey(parameter.Name));
            }

            /// <summary>A segment's text when it is one literal, in the case the index compares.</summary>
            /// <param name="segment">The segment.</param>
            /// <returns>The upper-cased literal, or null when the segment holds a parameter.</returns>
            private static string? Literal(RoutePatternPathSegment segment)
            {
                return segment.Parts is [RoutePatternLiteralPart literal] ? literal.Content.ToUpperInvariant() : null;
            }
        }

        /// <summary>One route a miss is matched against.</summary>
        /// <param name="Matcher">Its pattern's matcher.</param>
        /// <param name="Constraints">Its parameters' constraints.</param>
        /// <param name="Methods">Its verbs.</param>
        /// <param name="Accepts">The media types its body may carry; null when it reads no body.</param>
        private sealed record Candidate(
            TemplateMatcher Matcher,
            IReadOnlyList<KeyValuePair<string, IRouteConstraint>> Constraints,
            IReadOnlyList<string> Methods,
            IAcceptsMetadata? Accepts)
        {
            /// <summary>Builds a route's candidate.</summary>
            /// <param name="endpoint">The route.</param>
            /// <param name="methods">Its verbs.</param>
            /// <param name="policies">The factory that builds its constraints.</param>
            /// <returns>The candidate.</returns>
            public static Candidate Of(RouteEndpoint endpoint, IReadOnlyList<string> methods, ParameterPolicyFactory policies)
            {
                RoutePattern pattern = endpoint.RoutePattern;
                List<KeyValuePair<string, IRouteConstraint>> constraints = [];
                foreach ((string name, IReadOnlyList<RoutePatternParameterPolicyReference> references) in pattern.ParameterPolicies)
                {
                    RoutePatternParameterPart? parameter = pattern.GetParameter(name);
                    foreach (RoutePatternParameterPolicyReference reference in references)
                    {
                        if (policies.Create(parameter, reference) is IRouteConstraint constraint)
                        {
                            constraints.Add(new KeyValuePair<string, IRouteConstraint>(name, constraint));
                        }
                    }
                }

                return new Candidate(
                    new TemplateMatcher(new RouteTemplate(pattern), new RouteValueDictionary(pattern.Defaults)),
                    constraints,
                    methods,
                    endpoint.Metadata.GetMetadata<IAcceptsMetadata>());
            }

            /// <summary>Whether the route's constraints admit the values its pattern matched.</summary>
            /// <param name="http">The request.</param>
            /// <param name="values">The matched values.</param>
            /// <returns>True when every constraint does.</returns>
            public bool Admits(HttpContext http, RouteValueDictionary values)
            {
                foreach ((string name, IRouteConstraint constraint) in Constraints)
                {
                    // An optional parameter left out is not checked, as routing leaves it.
                    if (values.ContainsKey(name) && !constraint.Match(http, route: null, name, values, RouteDirection.IncomingRequest))
                    {
                        return false;
                    }
                }

                return true;
            }

            /// <summary>Whether the route takes the request's body media type, as routing's content-type check decides.</summary>
            /// <param name="request">The request.</param>
            /// <returns>True when the route reads no body, or reads this one's type, or a missing one when its body is optional.</returns>
            public bool AcceptsBody(HttpRequest request)
            {
                // A route without a body takes any; a body without a type is taken only where the body is optional.
                return Accepts is not { ContentTypes.Count: > 0 } accepts
                    || (MediaTypeHeaderValue.TryParse(request.ContentType, out MediaTypeHeaderValue? sent)
                        ? accepts.ContentTypes.Any(type => MediaTypeHeaderValue.TryParse(type, out MediaTypeHeaderValue? accepted) && sent.IsSubsetOf(accepted))
                        : accepts.IsOptional);
            }
        }
    }
}
