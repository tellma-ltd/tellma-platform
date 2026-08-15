// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Tellma.Core.Abstractions.Webhooks;

namespace Tellma.Core.Webhooks
{
    /// <summary>
    ///     The request handler behind <c>/api/webhooks/{key}</c>: find the receiver, buffer the body
    ///     under the cap, hand it over, and turn the receiver's outcome into a status code.
    /// </summary>
    internal static class WebhookEndpoint
    {
        /// <summary>The route the fronting maps. Operators configure provider dashboards against it.</summary>
        internal const string RoutePattern = "/api/webhooks/{key}";

        /// <summary>The route value carrying the receiver key.</summary>
        internal const string KeyRouteValue = "key";

        /// <summary>Handles one inbound webhook call.</summary>
        /// <param name="context">The request context.</param>
        /// <returns>A task that completes when the response has been written.</returns>
        [SuppressMessage(
            "Design",
            "CA1031:Do not catch general exception types",
            Justification = "A receiver is third-party-facing code whose failure must become a 5xx so the provider redelivers, never an unhandled exception that takes the response with it.")]
        internal static async Task HandleAsync(HttpContext context)
        {
            IServiceProvider services = context.RequestServices;
            WebhookMetrics metrics = services.GetRequiredService<WebhookMetrics>();
            ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(WebhookEndpoint).FullName!);
            long startedAt = Stopwatch.GetTimestamp();

            string requestedKey = context.Request.RouteValues.TryGetValue(KeyRouteValue, out object? routeValue)
                ? routeValue as string ?? string.Empty
                : string.Empty;

            // Until a receiver claims the key, the metric tag is a literal: the route accepts any
            // segment, and scanners would otherwise make this dimension unbounded.
            string metricKey = WebhookTelemetryNames.UnknownKeyTagValue;
            string outcome = WebhookTelemetryNames.UnknownKeyOutcome;

            try
            {
                IWebhookReceiver? receiver = FindReceiver(services, requestedKey);
                if (receiver is null)
                {
                    WebhookLog.UnknownWebhookKey(logger, requestedKey);
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                metricKey = receiver.Key;

                // The key is now known, so "unknown_key" would be a contradiction on every path out
                // of here. Anything that escapes before an outcome is assigned — a caller that
                // dropped the connection mid-body, most often — is metered as aborted instead.
                outcome = WebhookTelemetryNames.AbortedOutcome;

                WebhookOptions options = services.GetRequiredService<IOptions<WebhookOptions>>().Value;
                ReadOnlyMemory<byte>? body;
                try
                {
                    body = await WebhookRequestReader
                        .TryReadBodyAsync(context.Request, options.MaxRequestBodyBytes, context.RequestAborted)
                        .ConfigureAwait(false);
                }
                catch (BadHttpRequestException)
                {
                    // Kestrel's own request-size limit tripped first; answer identically so the
                    // caller sees one behaviour whichever limit was reached.
                    body = null;
                }

                if (body is null)
                {
                    outcome = WebhookTelemetryNames.TooLargeOutcome;
                    WebhookLog.WebhookBodyTooLarge(logger, receiver.Key, options.MaxRequestBodyBytes);
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return;
                }

                WebhookRequest request = new(
                    context.Request.Method.ToUpperInvariant(),
                    WebhookRequestReader.ToDictionary(context.Request.Headers, context.Request.Headers.Count),
                    WebhookRequestReader.ToDictionary(context.Request.Query, context.Request.Query.Count),
                    body.Value);

                WebhookResult result;
                try
                {
                    result = await receiver.HandleAsync(request, context.RequestAborted).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    outcome = WebhookTelemetryNames.ErrorOutcome;
                    WebhookLog.WebhookReceiverThrew(logger, exception, receiver.Key);
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    return;
                }

                outcome = ToOutcomeTag(result.Outcome);
                await WriteResultAsync(context, logger, receiver.Key, result).ConfigureAwait(false);
            }
            finally
            {
                metrics.RecordRequest(metricKey, outcome, Stopwatch.GetElapsedTime(startedAt).TotalSeconds);
            }
        }

        private static IWebhookReceiver? FindReceiver(IServiceProvider services, string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            // A linear scan over one to three receivers, resolved per request because a receiver may
            // legitimately be scoped.
            foreach (IWebhookReceiver receiver in services.GetServices<IWebhookReceiver>())
            {
                if (string.Equals(receiver.Key, key, StringComparison.Ordinal))
                {
                    return receiver;
                }
            }

            return null;
        }

        private static async Task WriteResultAsync(
            HttpContext context, ILogger logger, string key, WebhookResult result)
        {
            context.Response.StatusCode = result.Outcome switch
            {
                WebhookOutcome.Accepted => StatusCodes.Status200OK,
                WebhookOutcome.Unauthorized => StatusCodes.Status401Unauthorized,
                WebhookOutcome.Invalid => StatusCodes.Status400BadRequest,
                WebhookOutcome.TransientFailure => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status500InternalServerError,
            };

            if (result.Outcome != WebhookOutcome.Accepted)
            {
                // The receiver's detail is for our logs; the external system gets a bare status code.
                WebhookLog.WebhookRejected(logger, key, ToOutcomeTag(result.Outcome), result.Detail);
                return;
            }

            if (result.ResponseBody is not ReadOnlyMemory<byte> responseBody)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(result.ResponseContentType))
            {
                // Providers are strict about the content type of a challenge echo; an unlabeled body
                // would fail their verification in a way that is hard to diagnose from their side.
                WebhookLog.WebhookResponseBodyWithoutContentType(logger, key);
                return;
            }

            context.Response.ContentType = result.ResponseContentType;
            context.Response.ContentLength = responseBody.Length;
            await context.Response.Body.WriteAsync(responseBody, context.RequestAborted).ConfigureAwait(false);
        }

        private static string ToOutcomeTag(WebhookOutcome outcome)
        {
            return outcome switch
            {
                WebhookOutcome.Accepted => WebhookTelemetryNames.AcceptedOutcome,
                WebhookOutcome.Unauthorized => WebhookTelemetryNames.UnauthorizedOutcome,
                WebhookOutcome.Invalid => WebhookTelemetryNames.InvalidOutcome,
                WebhookOutcome.TransientFailure => WebhookTelemetryNames.TransientFailureOutcome,
                _ => WebhookTelemetryNames.ErrorOutcome,
            };
        }
    }
}
