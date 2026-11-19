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
        /// <summary>The routes a miss is matched against, built on the first miss.</summary>
        private IReadOnlyList<Candidate>? _candidates;

        /// <summary>
        ///     The problem of a request no endpoint took: 405 with its <c>Allow</c> header when a route
        ///     serves its path under other verbs, 415 when one serves its verb but not its body's
        ///     media type, null when no route serves its path.
        /// </summary>
        /// <param name="http">The request, whose response has not started.</param>
        /// <param name="tenantPath">Whether the request's path is under a tenant id.</param>
        /// <returns>The problem, or null for a route miss.</returns>
        public ProblemDescription? Classify(HttpContext http, bool tenantPath)
        {
            ArgumentNullException.ThrowIfNull(http);

            // --- The routes whose pattern and constraints match the path, whatever their verbs ---
            List<string> allowed = [];
            bool unsupportedMedia = false;
            foreach (Candidate candidate in Candidates())
            {
                RouteValueDictionary values = [];
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
                return ProblemMapper.ForStatus(StatusCodes.Status415UnsupportedMediaType, tenantPath);
            }

            if (allowed.Count > 0)
            {
                http.Response.Headers[HeaderNames.Allow] = string.Join(", ", allowed.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal));
                return ProblemMapper.ForStatus(StatusCodes.Status405MethodNotAllowed, tenantPath);
            }

            return null;
        }

        /// <summary>The routes stating their verbs, the fallback aside, built once.</summary>
        /// <returns>The candidates.</returns>
        private IReadOnlyList<Candidate> Candidates()
        {
            IReadOnlyList<Candidate>? built = Volatile.Read(ref _candidates);
            if (built is not null)
            {
                return built;
            }

            // A fallback runs last; every other route that names its verbs is one a miss may have meant.
            built = [.. endpoints.Endpoints()
                .OfType<RouteEndpoint>()
                .Where(static endpoint => endpoint.Order != int.MaxValue)
                .Select(endpoint => (Endpoint: endpoint, Methods: endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods))
                .Where(static entry => entry.Methods is { Count: > 0 })
                .Select(entry => Candidate.Of(entry.Endpoint, entry.Methods!, policies))];
            _ = Interlocked.CompareExchange(ref _candidates, built, null);
            return Volatile.Read(ref _candidates)!;
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
