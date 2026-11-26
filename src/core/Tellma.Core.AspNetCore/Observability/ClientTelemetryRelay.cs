// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.AspNetCore.Authentication;
using Tellma.Core.AspNetCore.Csrf;
using Tellma.Core.AspNetCore.Problems;
using Tellma.Core.AspNetCore.Routing;

namespace Tellma.Core.AspNetCore.Observability
{
    /// <summary>
    ///     The relay of the SPA's telemetry, <c>POST /api/client-telemetry</c>: it checks a signed-in
    ///     browser's batch and writes its items into the host's logs and counters. It serves the
    ///     <c>web</c> client alone, refusing any other with <c>403 csrf-rejected</c>. It answers 204
    ///     whenever the batch parses, however many items it dropped, and 204 at once when the relay is
    ///     turned off.
    /// </summary>
    internal static class ClientTelemetryRelay
    {
        /// <summary>The relay's route.</summary>
        public const string Route = ObservabilityComposition.ClientTelemetryPath;

        /// <summary>Why the relay serves no tenant: a tab's items name the tenant only as the client's claim.</summary>
        public const string TenantlessReason = "client-telemetry";

        /// <summary>The one client the relay serves: the SPA, by the name its <c>Tellma-Client</c> header states.</summary>
        public const string ClientName = "web";

        /// <summary>Maps the relay; its caller states its authorization, rate policy and body limit.</summary>
        /// <param name="routes">The route builder.</param>
        /// <returns>The endpoint.</returns>
        public static IEndpointConventionBuilder Map(IEndpointRouteBuilder routes)
        {
            ArgumentNullException.ThrowIfNull(routes);
            return routes.MapPost(Route, HandleAsync);
        }

        /// <summary>Answers one batch.</summary>
        /// <param name="context">The request, past the CSRF control and the rate policy.</param>
        /// <returns>A task that completes when the answer is written.</returns>
        internal static async Task HandleAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            IServiceProvider services = context.RequestServices;

            // --- Another configured client: refused as the CSRF control refuses a header it does not accept ---
            // The control admits every name the API options list; the relay is the SPA's alone.
            if (!string.Equals(HeaderName(context.Request), ClientName, StringComparison.Ordinal))
            {
                await RefuseClientAsync(context).ConfigureAwait(false);
                return;
            }

            // --- Turned off: discarded unread ---
            if (!services.GetRequiredService<IOptions<ClientTelemetryOptions>>().Value.Enabled)
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            // --- The body, then its envelope; a broken one is refused whole ---
            DateTimeOffset receivedAt = services.GetRequiredService<TimeProvider>().GetUtcNow();
            ClientTelemetryBatch? batch = await ReadAsync(context).ConfigureAwait(false);
            if (ClientTelemetryRules.Refusal(batch, HeaderVersion(context.Request)) is string refusal)
            {
                await ProblemWriter.WriteAsync(context, StatusCodes.Status400BadRequest, ProblemMapper.BadRequestCode, refusal).ConfigureAwait(false);
                return;
            }

            // --- The items, each kept or dropped on its own ---
            ClientTelemetryCaller caller = new(
                context.User.FindFirst(TellmaClaimTypes.Subject)?.Value,
                context.Request.Headers.UserAgent.ToString());
            services.GetRequiredService<ClientTelemetryWriter>().Write(batch!, caller, receivedAt);
            context.Response.StatusCode = StatusCodes.Status204NoContent;
        }

        /// <summary>
        ///     Reads the body, at most <see cref="ClientTelemetryRules.MaxBodyBytes" />, whatever the
        ///     server enforces, and parses it as a batch.
        /// </summary>
        /// <param name="context">The request.</param>
        /// <returns>The batch; null when the body is the JSON <c>null</c>.</returns>
        /// <exception cref="BadHttpRequestException">The body is too large (413), or is not a batch's JSON (400).</exception>
        private static async Task<ClientTelemetryBatch?> ReadAsync(HttpContext context)
        {
            // --- A body that states its length past the limit is refused unread ---
            const int Max = ClientTelemetryRules.MaxBodyBytes;
            HttpRequest request = context.Request;
            if (request.ContentLength > Max)
            {
                throw TooLarge();
            }

            // --- Read up to one byte past the limit, which proves the body too large ---
            byte[] buffer = ArrayPool<byte>.Shared.Rent(Max + 1);
            try
            {
                int length = 0;
                while (length <= Max)
                {
                    int read = await request.Body.ReadAsync(buffer.AsMemory(length, Max + 1 - length), context.RequestAborted).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    length += read;
                }

                if (length > Max)
                {
                    throw TooLarge();
                }

                // --- Parsed under the platform's JSON rules: an empty or malformed body is a malformed one ---
                try
                {
                    return JsonSerializer.Deserialize<ClientTelemetryBatch>(buffer.AsSpan(0, length), TellmaJson.Options);
                }
                catch (JsonException exception)
                {
                    throw new BadHttpRequestException("The body is not a telemetry batch's JSON.", StatusCodes.Status400BadRequest, exception);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>The name part of the request's <c>Tellma-Client</c> header, which the CSRF control has checked.</summary>
        /// <param name="request">The request.</param>
        /// <returns>The name; null when the header has none.</returns>
        private static string? HeaderName(HttpRequest request)
        {
            string? header = Header(request);
            int slash = header?.IndexOf('/', StringComparison.Ordinal) ?? -1;
            return slash < 0 ? null : header![..slash];
        }

        /// <summary>The version part of the request's <c>Tellma-Client</c> header, which the CSRF control has checked.</summary>
        /// <param name="request">The request.</param>
        /// <returns>The version; null when the header has none.</returns>
        private static string? HeaderVersion(HttpRequest request)
        {
            string? header = Header(request);
            int slash = header?.IndexOf('/', StringComparison.Ordinal) ?? -1;
            return slash < 0 ? null : header![(slash + 1)..];
        }

        /// <summary>The request's one <c>Tellma-Client</c> header.</summary>
        /// <param name="request">The request.</param>
        /// <returns>The header; null when there is not exactly one.</returns>
        private static string? Header(HttpRequest request)
        {
            return request.Headers[TellmaHeaders.Client].Count == 1 ? request.Headers[TellmaHeaders.Client][0] : null;
        }

        /// <summary>
        ///     Refuses a client other than the SPA exactly as the CSRF control refuses a client header it
        ///     does not accept: counted by rule, logged with the route, answered <c>403 csrf-rejected</c>.
        /// </summary>
        /// <param name="context">The request.</param>
        /// <returns>A task that completes when the refusal is written.</returns>
        private static Task RefuseClientAsync(HttpContext context)
        {
            IServiceProvider services = context.RequestServices;
            services.GetRequiredService<HostMetrics>().RecordCsrfRejection(CsrfRules.Header);
            HostLog.CsrfRejected(
                services.GetRequiredService<ILogger<TellmaCsrfMiddleware>>(),
                CsrfRules.Header,
                context.GetEndpoint() is RouteEndpoint route ? TellmaEndpointAudit.Describe(route) : Route);
            return ProblemWriter.WriteAsync(
                context,
                StatusCodes.Status403Forbidden,
                "csrf-rejected",
                arguments: new Dictionary<string, object?>(StringComparer.Ordinal) { ["rule"] = CsrfRules.Header });
        }

        /// <summary>The refusal of a body past the limit.</summary>
        /// <returns>The exception.</returns>
        private static BadHttpRequestException TooLarge()
        {
            return new BadHttpRequestException("The telemetry batch is larger than the relay reads.", StatusCodes.Status413PayloadTooLarge);
        }
    }
}
