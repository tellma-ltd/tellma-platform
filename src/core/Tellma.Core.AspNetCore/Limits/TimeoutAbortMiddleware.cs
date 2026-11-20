// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Tellma.Core.AspNetCore.Api;
using Tellma.Core.AspNetCore.Routing;

namespace Tellma.Core.AspNetCore.Limits
{
    // Spec 0016 §8.3: a request that runs past its timeout answers 504 timeout when nothing was
    // written; otherwise its connection is aborted. The framework's timeout stage writes the 504; a
    // response already started cannot become one, so this stage, just inside it, aborts the
    // connection rather than letting the cancellation surface as an unhandled exception.

    /// <summary>Aborts the connection of a request that ran past its timeout after its response had started.</summary>
    /// <param name="next">The rest of the pipeline.</param>
    /// <param name="loggerFactory">Creates the web API's logger.</param>
    internal sealed class TimeoutAbortMiddleware(RequestDelegate next, ILoggerFactory loggerFactory)
    {
        /// <summary>The web API's logger.</summary>
        private readonly ILogger _logger = (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger(ApiLog.Category);

        /// <summary>Runs the request, aborting its connection when its timeout cancels it mid-response.</summary>
        /// <param name="context">The request.</param>
        /// <returns>A task that completes when the request is done.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            try
            {
                await next(context).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (context.Response.HasStarted && TimedOut(context))
            {
                // A partial body must not read as a whole one: the client sees the connection fail.
                Endpoint? endpoint = context.GetEndpoint();
                ApiLog.ResponseAbortedOnTimeout(_logger, endpoint is RouteEndpoint route ? TellmaEndpointAudit.Describe(route) : endpoint?.DisplayName ?? "(unnamed endpoint)");
                context.Abort();
            }
        }

        /// <summary>Whether the request's own timeout, rather than its client, cancelled it.</summary>
        /// <param name="context">The request.</param>
        /// <returns>True when its timeout fired.</returns>
        private static bool TimedOut(HttpContext context)
        {
            return context.Features.Get<IHttpRequestTimeoutFeature>() is { RequestTimeoutToken.IsCancellationRequested: true };
        }
    }
}
