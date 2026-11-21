// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.AspNetCore.Api;
using Tellma.Core.AspNetCore.Routing;

namespace Tellma.Core.AspNetCore.Limits
{
    // Spec 0016 §8.3: a request that runs past its timeout answers 504 timeout when nothing was
    // written; otherwise its connection is aborted, and a write's response is discarded with the
    // closed connection. The framework's timeout stage writes the 504, but only for a request that
    // throws its cancellation before anything of its answer exists; this stage, just inside it,
    // aborts every other timed-out request rather than letting it end as a 504, a 500 or a whole 200:
    //  - a response already started cannot become a 504;
    //  - a sent persist runs to its end whatever cancels the request, so its write may have
    //    committed, and a 504 would read as a write that never happened;
    //  - a body writer that began may have advanced bytes the framework's clear cannot drop, which a
    //    length-declared 504 would then contradict.

    /// <summary>
    ///     Aborts the connection of a request that ran past its timeout once its answer can no longer
    ///     be a 504: its response started, a persist was sent, or a body writer began.
    /// </summary>
    /// <param name="next">The rest of the pipeline.</param>
    /// <param name="loggerFactory">Creates the web API's logger.</param>
    internal sealed class TimeoutAbortMiddleware(RequestDelegate next, ILoggerFactory loggerFactory)
    {
        /// <summary>The web API's logger.</summary>
        private readonly ILogger _logger = (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger(ApiLog.Category);

        /// <summary>Runs the request, aborting its connection when its timeout fired after its answer could be a 504.</summary>
        /// <param name="context">The request.</param>
        /// <returns>A task that completes when the request is done.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            try
            {
                await next(context).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (TimedOut(context) && Begun(context, bodyCounts: true) is string reason)
            {
                // Swallowed, so the framework's stage writes no 504 over what was begun or committed.
                Abort(context, reason);
                return;
            }

            // --- A timed-out request that ended normally: a write's answer, or a body the writer cut short ---
            // A writer that swallows the cancellation ends a started body early, as though it were
            // whole, and a committed write's answer must not arrive.
            if (TimedOut(context) && Begun(context, bodyCounts: false) is string ended)
            {
                Abort(context, ended);
            }
        }

        /// <summary>Why a timed-out request's answer can no longer be a 504, if it cannot.</summary>
        /// <param name="context">The request.</param>
        /// <param name="bodyCounts">Whether a body writer that began counts, as it does for a request its cancellation ended.</param>
        /// <returns>The reason the abort is logged with; null when a 504 may still answer.</returns>
        private static string? Begun(HttpContext context, bool bodyCounts)
        {
            return context.Response.HasStarted ? "started"
                : PersistSent(context) ? "persist"
                : bodyCounts && BodyBegun(context) ? "body"
                : null;
        }

        /// <summary>Whether the request's own timeout, rather than its client, cancelled it.</summary>
        /// <param name="context">The request.</param>
        /// <returns>True when its timeout fired.</returns>
        private static bool TimedOut(HttpContext context)
        {
            return context.Features.Get<IHttpRequestTimeoutFeature>() is { RequestTimeoutToken.IsCancellationRequested: true };
        }

        /// <summary>Whether the request sent a persist round trip, whose write may have committed.</summary>
        /// <param name="context">The request.</param>
        /// <returns>True when one was sent.</returns>
        private static bool PersistSent(HttpContext context)
        {
            return context.RequestServices?.GetService<DataAccessScope>() is { PersistSent: true };
        }

        /// <summary>
        ///     Whether a body writer began the response: it declared the body's type or length, and may
        ///     have advanced bytes that are not yet sent.
        /// </summary>
        /// <param name="context">The request.</param>
        /// <returns>True when one began.</returns>
        private static bool BodyBegun(HttpContext context)
        {
            return context.Response.ContentType is not null || context.Response.ContentLength is not null;
        }

        /// <summary>Logs and aborts a timed-out request's connection: its client sees the connection fail.</summary>
        /// <param name="context">The request.</param>
        /// <param name="reason">Why its answer could no longer be a 504.</param>
        private void Abort(HttpContext context, string reason)
        {
            Endpoint? endpoint = context.GetEndpoint();
            ApiLog.ResponseAbortedOnTimeout(_logger, endpoint is RouteEndpoint route ? TellmaEndpointAudit.Describe(route) : endpoint?.DisplayName ?? "(unnamed endpoint)", reason);
            context.Abort();
        }
    }
}
