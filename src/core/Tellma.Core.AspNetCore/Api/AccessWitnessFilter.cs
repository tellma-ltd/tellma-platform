// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

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
    // is written, whether it did, and evaluates nothing itself.

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
            if (!Succeeds(result, http))
            {
                return result;
            }

            // --- The declared securable, or none for a member endpoint (the audit requires one of the two) ---
            Endpoint? endpoint = http.GetEndpoint();
            SecurableEndpointMetadata? securable = endpoint?.Metadata.GetMetadata<SecurableEndpointMetadata>();
            string name = endpoint is RouteEndpoint route ? TellmaEndpointAudit.Describe(route) : endpoint?.DisplayName ?? "(unnamed endpoint)";
            if (http.RequestServices.GetRequiredService<AccessWitness>().Confirm(securable, name))
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

        /// <summary>Whether a handler's result answers with a success status.</summary>
        /// <param name="result">The handler's result.</param>
        /// <param name="http">The request.</param>
        /// <returns>True for a 2xx answer, which needs its witness.</returns>
        private static bool Succeeds(object? result, HttpContext http)
        {
            // A value the framework serializes answers 200; a result states its status, or keeps the response's.
            int status = result is IStatusCodeHttpResult { StatusCode: int stated } ? stated : http.Response.StatusCode;
            return status is >= StatusCodes.Status200OK and < StatusCodes.Status300MultipleChoices;
        }
    }
}
