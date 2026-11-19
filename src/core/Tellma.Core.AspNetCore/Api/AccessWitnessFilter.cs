// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Access;
using Tellma.Core.AspNetCore.Problems;
using Tellma.Core.AspNetCore.Routing;

namespace Tellma.Core.AspNetCore.Api
{
    // Spec 0014 §4.6's layer 4 and spec 0016 §5.1: the last filter of a guarded tenant group. The
    // handler's service evaluates the securable; this filter only asks, before a successful result
    // is written, whether it did, and evaluates nothing itself. Endpoint filters reach route handlers
    // alone, so Wrap also holds every endpoint of a guarded group to the witness at the delegate: a
    // plain RequestDelegate endpoint answers no success without one either.

    /// <summary>
    ///     The authorization witness: before a successful result is written, confirms that the
    ///     endpoint's declared securable was evaluated during the request, or for a member endpoint
    ///     that the caller connected as a member. A result without its witness is replaced with
    ///     <c>500 internal</c>, so data never leaves through a handler that did not ask.
    /// </summary>
    internal sealed class AccessWitnessFilter : IEndpointFilter
    {
        /// <inheritdoc />
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(next);
            object? result = await next(context).ConfigureAwait(false);
            HttpContext http = context.HttpContext;

            // A value the framework serializes answers 200; a result states its status, or keeps the response's.
            int status = result is IStatusCodeHttpResult { StatusCode: int stated } ? stated : http.Response.StatusCode;
            if (!Succeeds(status) || Confirm(http))
            {
                return result;
            }

            // --- No witness: the result never leaves; a handler that wrote already loses its connection ---
            if (http.Response.HasStarted)
            {
                http.Abort();
                return null;
            }

            return new ProblemResult(StatusCodes.Status500InternalServerError, "internal");
        }

        /// <summary>
        ///     Wraps a guarded endpoint's request delegate so its answer is held to the witness whatever
        ///     built the endpoint: endpoint filters reach only route handlers, and a plain request
        ///     delegate would otherwise answer a success without one.
        /// </summary>
        /// <remarks>
        ///     The wrapper runs inside the tenant guard's, which wraps last, so the delegate the guard's
        ///     marker names still runs this one.
        /// </remarks>
        /// <param name="endpoint">The endpoint being built, after its request delegate is set.</param>
        public static void Wrap(EndpointBuilder endpoint)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            RequestDelegate? inner = endpoint.RequestDelegate;
            if (inner is null)
            {
                return;
            }

            async Task WitnessedAsync(HttpContext context)
            {
                Watch(context);
                await inner(context).ConfigureAwait(false);
                await SettleAsync(context).ConfigureAwait(false);
            }

            endpoint.RequestDelegate = WitnessedAsync;
        }

        /// <summary>
        ///     Holds a response to its witness at the moment it starts: a success that starts without
        ///     its witness loses its connection before its status line or a byte of its body leaves.
        /// </summary>
        /// <param name="http">The request, before its endpoint's delegate runs.</param>
        private static void Watch(HttpContext http)
        {
            http.Response.OnStarting(
                static state =>
                {
                    var request = (HttpContext)state;
                    if (Succeeds(request.Response.StatusCode) && !Confirm(request))
                    {
                        request.Abort();
                    }

                    return Task.CompletedTask;
                },
                http);
        }

        /// <summary>
        ///     Settles an answer after its delegate returns: a success that has not started and has no
        ///     witness is answered with <c>500 internal</c> instead.
        /// </summary>
        /// <param name="http">The request, after its endpoint's delegate returned.</param>
        /// <returns>A task that completes when the answer is settled.</returns>
        private static Task SettleAsync(HttpContext http)
        {
            // A started response was held to its witness when it started; a refusal needs none.
            if (http.Response.HasStarted || !Succeeds(http.Response.StatusCode) || Confirm(http))
            {
                return Task.CompletedTask;
            }

            // The success never leaves: whatever the delegate set on the response is dropped.
            http.Response.Clear();
            return ProblemWriter.WriteAsync(http, StatusCodes.Status500InternalServerError, "internal");
        }

        /// <summary>Whether a status is a success, which needs its witness.</summary>
        /// <param name="status">The status.</param>
        /// <returns>True for a 2xx status.</returns>
        private static bool Succeeds(int status)
        {
            return status is >= StatusCodes.Status200OK and < StatusCodes.Status300MultipleChoices;
        }

        /// <summary>
        ///     Asks the request's witness whether the endpoint's declared securable, or for a member
        ///     endpoint the member connection, was witnessed; a missing witness is counted and logged.
        /// </summary>
        /// <param name="http">The request.</param>
        /// <returns>True when the witness is there.</returns>
        private static bool Confirm(HttpContext http)
        {
            // --- The declared securable, or none for a member endpoint (the audit requires one of the two) ---
            Endpoint? endpoint = http.GetEndpoint();
            SecurableEndpointMetadata? securable = endpoint?.Metadata.GetMetadata<SecurableEndpointMetadata>();
            string name = endpoint is RouteEndpoint route ? TellmaEndpointAudit.Describe(route) : endpoint?.DisplayName ?? "(unnamed endpoint)";
            return http.RequestServices.GetRequiredService<AccessWitness>().Confirm(securable, name);
        }
    }
}
