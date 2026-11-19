// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using System.Globalization;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Api;
using Tellma.Core.AspNetCore.Observability;
using Tellma.Core.AspNetCore.Tenancy;

namespace Tellma.Core.AspNetCore.Problems
{
    /// <summary>
    ///     Writes a problem as the response: an RFC 9457 body rendered in the request's culture, its
    ///     status and its headers (<c>Retry-After</c>, a step-up's <c>WWW-Authenticate</c>), counted
    ///     on <c>tellma.api.problems</c> and logged.
    /// </summary>
    internal static class ProblemWriter
    {
        /// <summary>The problem body's media type.</summary>
        public const string ContentType = "application/problem+json";

        /// <summary>The neutral language every resource falls back to.</summary>
        private static readonly CultureInfo Neutral = CultureInfo.ReadOnly(CultureInfo.GetCultureInfo("en"));

        /// <summary>Writes a problem as the response, replacing its status.</summary>
        /// <param name="context">The request.</param>
        /// <param name="status">The status code.</param>
        /// <param name="code">The kebab-case problem code.</param>
        /// <param name="detail">A sentence on this occurrence, carried verbatim; null for the code's own detail.</param>
        /// <param name="arguments">The problem's raw display data, or null.</param>
        /// <returns>A task that completes when the body is written.</returns>
        public static Task WriteAsync(
            HttpContext context, int status, string code, string? detail = null, IReadOnlyDictionary<string, object?>? arguments = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(code);
            return WriteAsync(context, new ProblemDescription(status, code)
            {
                Arguments = arguments is { Count: > 0 } ? arguments : null,
                Detail = detail is null ? null : new ProblemDetail.Verbatim(detail),
            });
        }

        /// <summary>Writes a problem as the response, replacing its status.</summary>
        /// <param name="context">The request.</param>
        /// <param name="problem">The problem.</param>
        /// <returns>A task that completes when the body is written.</returns>
        public static Task WriteAsync(HttpContext context, ProblemDescription problem)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(problem);
            IServiceProvider services = context.RequestServices;

            // --- The body, rendered here in the request's localization ---
            // Entered and left synchronously in this method: a culture an awaited callee applied never
            // reaches its caller, so the writer applies the request's own rather than trusting the flow's.
            ProblemDocument document;
            using (Localize(context, problem.Code))
            {
                document = ProblemRenderer.Render(problem, services, TraceIdentity.Of(context));
            }

            // --- The status and the headers that go with it ---
            HttpResponse response = context.Response;
            if (problem.StepUp is StepUpDemand stepUp)
            {
                services.GetService<HostMetrics>()?.RecordStepUpChallenge();
                StepUpChallenge.Write(response, stepUp.Acr, stepUp.MaxAgeSeconds);
            }

            if (problem.RetryAfter is TimeSpan wait)
            {
                response.Headers[HeaderNames.RetryAfter] = ((long)Math.Ceiling(Math.Max(0, wait.TotalSeconds))).ToString(CultureInfo.InvariantCulture);
            }

            response.StatusCode = problem.Status;
            response.ContentType = ContentType;

            // --- Counted and logged once per problem written ---
            services.GetService<ApiMetrics>()?.RecordProblem(problem.Status, problem.Code);
            if (services.GetService<ILoggerFactory>()?.CreateLogger(ApiLog.Category) is ILogger logger)
            {
                using (BeginScopes(context, logger))
                {
                    ApiLog.Problem(logger, problem.Status >= StatusCodes.Status500InternalServerError ? LogLevel.Warning : LogLevel.Information, problem.Status, problem.Code);
                }
            }

            return JsonSerializer.SerializeAsync(response.Body, document, TellmaJson.Options, context.RequestAborted);
        }

        /// <summary>
        ///     Opens a request's logger scopes on a logger, for a line written outside them: the tenant
        ///     request's members and the telemetry filter's, whichever the request reached.
        /// </summary>
        /// <param name="context">The request.</param>
        /// <param name="logger">The logger.</param>
        /// <returns>The scopes, disposed together.</returns>
        public static IDisposable BeginScopes(HttpContext context, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(logger);
            IRequestContextAccessor? accessor = context.RequestServices.GetService<IRequestContextAccessor>();
            IDisposable? tenant = accessor?.Current.Tenant is null ? null : logger.BeginScope(new TenantLogScope(accessor));
            IDisposable? api = ApiLogScope.Of(context) is ApiLogScope scope ? logger.BeginScope(scope) : null;
            return new Scopes(tenant, api);
        }

        /// <summary>
        ///     Enters the localization a problem renders in: the request's, except an absent tenant's
        ///     problem, which renders in the neutral language whatever the request asked, so it reads
        ///     the same for a tenant that does not exist and one the caller may not open.
        /// </summary>
        /// <param name="context">The request.</param>
        /// <param name="code">The problem code.</param>
        /// <returns>The scope, or null when the host has no request context.</returns>
        private static IDisposable? Localize(HttpContext context, string code)
        {
            if (context.RequestServices.GetService<IRequestContextAccessor>() is not IRequestContextAccessor accessor)
            {
                return null;
            }

            LocalizationContext localization = accessor.Current.Localization;
            if (string.Equals(code, TenantProblemCodes.NotFound, StringComparison.Ordinal))
            {
                localization = localization with
                {
                    Culture = Neutral,
                    Language = Neutral.Name,
                    CultureTag = Neutral.Name,
                    ContentLanguageIndex = 1,
                    ContentLanguages = [],
                };
            }

            return LocalizationScope.Enter(localization);
        }

        /// <summary>Two logger scopes disposed together, innermost first.</summary>
        /// <param name="outer">The tenant request's scope, or null.</param>
        /// <param name="inner">The telemetry filter's scope, or null.</param>
        private sealed class Scopes(IDisposable? outer, IDisposable? inner) : IDisposable
        {
            /// <inheritdoc />
            public void Dispose()
            {
                inner?.Dispose();
                outer?.Dispose();
            }
        }
    }
}
