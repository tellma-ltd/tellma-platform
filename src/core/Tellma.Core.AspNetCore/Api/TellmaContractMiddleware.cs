// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.AspNetCore.Problems;
using Tellma.Core.AspNetCore.Routing;

namespace Tellma.Core.AspNetCore.Api
{
    // Spec 0016 §3.10: the check runs after the CSRF control and before the tenant middleware, so a
    // mismatched request is refused before its tenant is resolved or its body read.

    /// <summary>
    ///     Refuses a request to a projected web endpoint whose <c>Tellma-Contract</c> header names
    ///     another fingerprint than the endpoint's, with <c>412 client-outdated</c>, before anything
    ///     reads its body: a stale client would otherwise save the old shape over the new one. A
    ///     request without the header passes, for tests and tooling; an endpoint without contract
    ///     metadata is never checked.
    /// </summary>
    /// <param name="next">The rest of the pipeline.</param>
    /// <param name="metrics">The web API's instruments.</param>
    /// <param name="loggerFactory">Creates the web API's logger.</param>
    internal sealed class TellmaContractMiddleware(RequestDelegate next, ApiMetrics metrics, ILoggerFactory loggerFactory)
    {
        /// <summary>The problem code of a refused request.</summary>
        public const string ClientOutdatedCode = "client-outdated";

        /// <summary>The web API's logger.</summary>
        private readonly ILogger _logger = (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger(ApiLog.Category);

        /// <summary>Checks the request's contract, then runs the rest of the pipeline.</summary>
        /// <param name="context">The request.</param>
        /// <returns>A task that completes when the request is answered.</returns>
        public Task InvokeAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // --- Only a projected endpoint, and only a caller that states a contract, is checked ---
            Endpoint? endpoint = context.GetEndpoint();
            if (endpoint?.Metadata.GetMetadata<ContractMetadata>() is not ContractMetadata contract
                || !context.Request.Headers.TryGetValue(TellmaHeaders.Contract, out StringValues stated)
                || Agrees(stated, contract.Fingerprint))
            {
                return next(context);
            }

            // --- A stated contract that differs: counted, logged by route, refused ---
            metrics.RecordRejection(ApiMetrics.ClientOutdatedReason);
            ApiLog.RequestRejected(
                _logger,
                ApiMetrics.ClientOutdatedReason,
                endpoint is RouteEndpoint route ? TellmaEndpointAudit.Describe(route) : endpoint.DisplayName ?? "(unnamed endpoint)");
            return ProblemWriter.WriteAsync(context, StatusCodes.Status412PreconditionFailed, ClientOutdatedCode);
        }

        /// <summary>Whether the header states exactly the endpoint's fingerprint, once.</summary>
        /// <param name="stated">The header's values.</param>
        /// <param name="fingerprint">The endpoint's fingerprint.</param>
        /// <returns>True when it does.</returns>
        private static bool Agrees(StringValues stated, string fingerprint)
        {
            return stated.Count == 1 && string.Equals(stated[0], fingerprint, StringComparison.Ordinal);
        }
    }
}
