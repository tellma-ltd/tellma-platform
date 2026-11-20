// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading.RateLimiting;
using Tellma.Core.AspNetCore.Api;
using Tellma.Core.AspNetCore.Problems;
using Tellma.Core.AspNetCore.Routing;

namespace Tellma.Core.AspNetCore.Limits
{
    // Spec 0016 §8.2 and §7.5: every refusal is 429 rate-limited with Retry-After, counted on
    // tellma.api.requests.rejected{reason = rate_limited} with the lane on the report policy, and
    // written by the problem writer, so its body and headers are those of every other refusal. The
    // limiter runs before the tenant is resolved, so a refusal reads the same for a tenant that
    // does not exist and one the caller is not a member of.

    /// <summary>Answers a request a rate or concurrency policy refused.</summary>
    internal static class RateRejection
    {
        /// <summary>Writes the refusal: the problem, its <c>Retry-After</c>, the count and the log line.</summary>
        /// <param name="rejected">The refused request and the lease that refused it.</param>
        /// <returns>A task that completes when the refusal is written.</returns>
        public static ValueTask OnRejectedAsync(OnRejectedContext rejected)
        {
            ArgumentNullException.ThrowIfNull(rejected);
            HttpContext context = rejected.HttpContext;
            var refusal = rejected.Lease as RefusedLease;

            // --- The wait: a window's own, or a moment for a concurrency limit, which names none ---
            TimeSpan retryAfter = RetryAfterOf(rejected.Lease);

            // --- Counted by reason and lane, logged by route and policy ---
            IServiceProvider services = context.RequestServices;
            services.GetService<ApiMetrics>()?.RecordRejection(ApiMetrics.RateLimitedReason, refusal?.Lane);
            if (services.GetService<ILoggerFactory>()?.CreateLogger(ApiLog.Category) is ILogger logger)
            {
                Endpoint? endpoint = context.GetEndpoint();
                ApiLog.RateLimited(
                    logger,
                    refusal?.Policy ?? "unknown",
                    refusal?.Lane ?? "none",
                    endpoint is RouteEndpoint route ? TellmaEndpointAudit.Describe(route) : endpoint?.DisplayName ?? "(unnamed endpoint)");
            }

            return new ValueTask(ProblemWriter.WriteAsync(
                context,
                ProblemMapper.Named(StatusCodes.Status429TooManyRequests, ProblemMapper.RateLimitedCode) with { RetryAfter = retryAfter }));
        }

        /// <summary>The wait a refusal names: its lease's, at least a second, else a second.</summary>
        /// <param name="lease">The failed lease.</param>
        /// <returns>The wait.</returns>
        internal static TimeSpan RetryAfterOf(RateLimitLease lease)
        {
            ArgumentNullException.ThrowIfNull(lease);
            return lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan wait) && wait > ProblemMapper.DefaultRetryAfter
                ? wait
                : ProblemMapper.DefaultRetryAfter;
        }
    }
}
