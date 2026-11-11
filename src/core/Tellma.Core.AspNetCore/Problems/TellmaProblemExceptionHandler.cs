// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;
using System.Globalization;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Observability;

namespace Tellma.Core.AspNetCore.Problems
{
    /// <summary>
    ///     Maps the tenancy and assurance exceptions to problem details, and everything else to the
    ///     <c>500 internal</c> backstop: the host's mapping until the web API's full mapping replaces it.
    /// </summary>
    /// <param name="metrics">The host's instruments, which count step-up challenges.</param>
    /// <param name="environment">The host environment; Development adds the exception to the backstop.</param>
    internal sealed class TellmaProblemExceptionHandler(HostMetrics metrics, IHostEnvironment environment) : IExceptionHandler
    {
        /// <summary>The retry-after of an unavailable tenant that names none.</summary>
        internal static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(30);

        /// <summary>Whether the handler maps an exception to anything but the backstop.</summary>
        /// <param name="exception">The exception.</param>
        /// <returns>True for the exceptions the mapping names.</returns>
        public static bool IsMapped(Exception exception)
        {
            return exception is StepUpRequiredException
                or IndividualRequiredException
                or TenantNotFoundException
                or TenantUnavailableException
                or TenantStateException;
        }

        /// <inheritdoc />
        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            ArgumentNullException.ThrowIfNull(exception);

            // --- A caller that went away gets no answer ---
            if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
            {
                return true;
            }

            switch (exception)
            {
                // --- The assurance bar: the caller must step up ---
                case StepUpRequiredException stepUp:
                    metrics.RecordStepUpChallenge();
                    int? maxAge = stepUp.MaxAge is { } age ? (int)Math.Ceiling(age.TotalSeconds) : null;
                    StepUpChallenge.Write(httpContext.Response, stepUp.Acr, maxAge);
                    await ProblemWriter.WriteAsync(httpContext, StatusCodes.Status401Unauthorized, stepUp.Code).ConfigureAwait(false);
                    return true;

                case IndividualRequiredException individual:
                    await ProblemWriter.WriteAsync(httpContext, StatusCodes.Status403Forbidden, individual.Code).ConfigureAwait(false);
                    return true;

                // --- The tenant: absent (or the caller is no member), refused by its state, or unreachable ---
                case TenantNotFoundException notFound:
                    await ProblemWriter.WriteAsync(httpContext, StatusCodes.Status404NotFound, notFound.Code).ConfigureAwait(false);
                    return true;

                case TenantUnavailableException unavailable:
                    await WriteUnavailableAsync(httpContext, unavailable).ConfigureAwait(false);
                    return true;

                // --- A catalog write the catalog refused ---
                case TenantStateException refused:
                    await ProblemWriter.WriteAsync(httpContext, StatusCodes.Status409Conflict, refused.Code, refused.Detail).ConfigureAwait(false);
                    return true;

                // --- The backstop: nothing but the trace id outside Development ---
                default:
                    IReadOnlyDictionary<string, object?>? arguments = environment.IsDevelopment()
                        ? new Dictionary<string, object?> { ["exception"] = $"{exception.GetType().FullName}: {exception.Message}" }
                        : null;
                    await ProblemWriter.WriteAsync(httpContext, StatusCodes.Status500InternalServerError, "internal", arguments: arguments).ConfigureAwait(false);
                    return true;
            }
        }

        /// <summary>Writes an unavailable tenant's problem, whose status depends on its code.</summary>
        /// <param name="context">The request.</param>
        /// <param name="unavailable">The exception.</param>
        /// <returns>A task that completes when the body is written.</returns>
        private static Task WriteUnavailableAsync(HttpContext context, TenantUnavailableException unavailable)
        {
            switch (unavailable.Code)
            {
                case TenantProblemCodes.NotFound:
                    return ProblemWriter.WriteAsync(context, StatusCodes.Status404NotFound, unavailable.Code);

                case TenantProblemCodes.CatalogUnavailable:
                case TenantProblemCodes.SchemaBehind:
                    // Waiting changes these: the catalog comes back, the upgrade finishes.
                    TimeSpan wait = unavailable.RetryAfter ?? DefaultRetryAfter;
                    context.Response.Headers[HeaderNames.RetryAfter] =
                        ((long)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                    return ProblemWriter.WriteAsync(context, StatusCodes.Status503ServiceUnavailable, unavailable.Code);

                default:
                    // Suspended, read-only and frozen: an operator's state, which waiting does not change.
                    return ProblemWriter.WriteAsync(context, StatusCodes.Status403Forbidden, unavailable.Code);
            }
        }
    }
}
