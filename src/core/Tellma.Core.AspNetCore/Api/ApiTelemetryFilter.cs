// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System.Diagnostics;
using System.Globalization;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Hosting;

namespace Tellma.Core.AspNetCore.Api
{
    // Spec 0016 §5.1, §6.2 and §10.1: the stage between the tenant guard and the handler on the Web
    // group. It runs only once the guard admitted the request, so an early refusal (CSRF, a stale
    // contract, an absent tenant) carries none of its headers. Endpoint filters reach route handlers
    // alone, so it wraps the request delegate and reaches every endpoint of the group.

    /// <summary>
    ///     The web surface's telemetry stage: adds <c>Client</c>, <c>Resource</c> and
    ///     <c>Operation</c> to the request's logger scope and tags its activity with them, times
    ///     the operation on <c>tellma.api.operation.duration</c>, and stamps the response's
    ///     <c>Content-Language</c>, <c>Tellma-Calendar</c>, <c>Tellma-Version-Tags</c> and, when
    ///     enabled, <c>Server-Timing</c> headers as it starts, success or problem.
    /// </summary>
    internal static class ApiTelemetryFilter
    {
        /// <summary>The <c>Server-Timing</c> header's name.</summary>
        public const string ServerTimingHeader = "Server-Timing";

        /// <summary>
        ///     Wraps an endpoint's request delegate in the telemetry stage. Applied after the witness's
        ///     wrapper and before the tenant guard's, so the guard runs first and the witness last.
        /// </summary>
        /// <param name="endpoint">The endpoint being built, after its request delegate is set.</param>
        public static void Wrap(EndpointBuilder endpoint)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            RequestDelegate? inner = endpoint.RequestDelegate;
            if (inner is null)
            {
                return;
            }

            // The route model's entry, read once at build time: the tags' closed sets come from it.
            ProjectedRouteMetadata? projected = endpoint.Metadata.OfType<ProjectedRouteMetadata>().LastOrDefault();
            endpoint.RequestDelegate = context => RunAsync(context, projected, inner);
        }

        /// <summary>Runs an admitted request under the stage.</summary>
        /// <param name="http">The request.</param>
        /// <param name="projected">The route model's entry the endpoint serves; null for an endpoint the model does not describe.</param>
        /// <param name="inner">The endpoint's delegate.</param>
        /// <returns>A task that completes when the request is answered.</returns>
        private static async Task RunAsync(HttpContext http, ProjectedRouteMetadata? projected, RequestDelegate inner)
        {
            IServiceProvider services = http.RequestServices;
            RequestContext request = services.GetRequiredService<IRequestContextAccessor>().Current;

            // --- The scope's members, kept on the request for the lines written after it closes ---
            string? resource = projected?.TagResource;
            string? operation = projected?.Route.Operation;
            ApiLogScope scope = new(request.Client, resource, operation);
            ApiLogScope.Keep(http, scope);

            // --- The request's activity: the same closed sets ---
            if (http.Features.Get<IHttpActivityFeature>()?.Activity is Activity activity)
            {
                _ = activity.SetTag(ApiTelemetryNames.ClientTag, request.Client);
                if (projected is not null)
                {
                    _ = activity
                        .SetTag(ApiTelemetryNames.ResourceTag, resource)
                        .SetTag(ApiTelemetryNames.OperationTag, operation);
                }
            }

            // --- The headers, as the response starts: after the handler's last batch, a problem's included ---
            http.Response.OnStarting(
                static state =>
                {
                    Stamp((HttpContext)state);
                    return Task.CompletedTask;
                },
                http);

            // --- The operation, timed whatever its outcome ---
            TimeProvider time = services.GetService<TimeProvider>() ?? TimeProvider.System;
            ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(ApiLog.Category);
            long started = time.GetTimestamp();
            try
            {
                using (logger.BeginScope(scope))
                {
                    await inner(http).ConfigureAwait(false);
                }
            }
            finally
            {
                if (projected is not null)
                {
                    services.GetRequiredService<ApiMetrics>().RecordOperation(resource!, operation!, request.Client, time.GetElapsedTime(started));
                }
            }
        }

        /// <summary>Stamps the stage's response headers from the request's completed context and its last batch.</summary>
        /// <param name="http">The request, as its response starts.</param>
        private static void Stamp(HttpContext http)
        {
            // --- An absent tenant's answer: nothing of the admitted request, which would tell the
            // refusal of a member whose access ended mid-request from a tenant that does not exist ---
            if (Problems.ProblemWriter.AnsweredTenantNotFound(http))
            {
                return;
            }

            IServiceProvider services = http.RequestServices;
            IHeaderDictionary headers = http.Response.Headers;
            RequestContext request = services.GetRequiredService<IRequestContextAccessor>().Current;

            // --- The resolved message language and the effective calendar ---
            headers[HeaderNames.ContentLanguage] = request.Language;
            headers[TellmaHeaders.Calendar] = request.Calendar;

            // --- The wire tags after the request's last batch, the caller's own only when that batch read them ---
            DataAccessScope? access = services.GetService<DataAccessScope>();
            if (access?.LastOutcome is BatchOutcome outcome && request.Tenant is TenantDescriptor tenant && outcome.VersionTags.TenantId == tenant.Id)
            {
                UserVersionTagSnapshot? user = outcome.UserVersionTags is { } tags && tags.TenantId == tenant.Id && tags.UserId == request.UserId
                    ? tags
                    : null;
                headers[TellmaHeaders.VersionTags] = VersionTagWire.HeaderValue(outcome.VersionTags, user);
            }

            // --- The database's share of the request, when the deployment shows it ---
            if (access is not null && services.GetService<IOptions<ApiOptions>>()?.Value.ServerTiming == true)
            {
                headers[ServerTimingHeader] = ServerTiming(access.DatabaseTime, access.RoundTrips);
            }
        }

        /// <summary>The <c>Server-Timing</c> value of the database's share: <c>db;dur=&lt;ms&gt;;desc="&lt;n&gt; calls"</c>.</summary>
        /// <param name="elapsed">The time the request's batch attempts took.</param>
        /// <param name="roundTrips">The round trips they took.</param>
        /// <returns>The header value.</returns>
        internal static string ServerTiming(TimeSpan elapsed, int roundTrips)
        {
            return string.Create(CultureInfo.InvariantCulture, $"db;dur={elapsed.TotalMilliseconds:0.#};desc=\"{roundTrips} calls\"");
        }
    }
}
