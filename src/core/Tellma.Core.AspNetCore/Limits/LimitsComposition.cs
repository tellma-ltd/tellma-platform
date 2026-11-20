// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.IO.Compression;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Problems;

namespace Tellma.Core.AspNetCore.Limits
{
    // Spec 0016 §8.2-§8.5 and spec 0011 §5.1: the services of the rate limiter, the request
    // timeouts, response compression and output caching, and their places in the fixed pipeline.
    // Each policy is registered here, before its middleware is installed, so an endpoint's policy
    // metadata is never inert.

    /// <summary>Composes the web host's limits, timeouts, compression and output caching.</summary>
    internal static class LimitsComposition
    {
        /// <summary>Registers the services, every policy with its configured limits.</summary>
        /// <param name="services">The host's services.</param>
        public static void Register(IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            // --- Rate and concurrency: one global router over every policy's partitions ---
            services.TryAddSingleton<RatePolicyRouter>();
            _ = services.AddRateLimiter(static _ => { });
            _ = services.AddOptions<RateLimiterOptions>().Configure<RatePolicyRouter>(static (options, router) =>
            {
                options.GlobalLimiter = router;
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                // The refusal is written whole, whatever the request's token, as every problem is.
                options.OnRejected = static (rejected, _) => RateRejection.OnRejectedAsync(rejected);
            });

            // --- Timeouts: the web, long and agent tool policies, each answering 504 timeout ---
            _ = services.AddRequestTimeouts();
            _ = services.AddOptions<RequestTimeoutOptions>().Configure<IOptions<ApiOptions>, IOptions<McpLimitOptions>>(static (options, api, mcp) =>
            {
                options.AddPolicy(TellmaRequestTimeouts.Web, TimeoutPolicy(api.Value.RequestTimeout));
                options.AddPolicy(TellmaRequestTimeouts.Long, TimeoutPolicy(api.Value.LongRequestTimeout));
                options.AddPolicy(TellmaRequestTimeouts.Mcp, TimeoutPolicy(mcp.Value.ToolTimeout));
            });

            // --- Compression: Brotli then Gzip, the fastest level, JSON alone ---
            _ = services.AddResponseCompression(static options =>
            {
                options.EnableForHttps = true;
                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();
                options.MimeTypes = JsonResponseCompressionProvider.MimeTypes;
            });
            _ = services.Configure<BrotliCompressionProviderOptions>(static options => options.Level = CompressionLevel.Fastest);
            _ = services.Configure<GzipCompressionProviderOptions>(static options => options.Level = CompressionLevel.Fastest);
            _ = services.Replace(ServiceDescriptor.Singleton<IResponseCompressionProvider, JsonResponseCompressionProvider>());

            // --- Output caching: the store; each public document names its own policy ---
            _ = services.AddOutputCache();
        }

        /// <summary>
        ///     Installs response compression. The provider reads whether the configuration turns it off
        ///     per response, so the options are never read before the startup gate has judged them.
        /// </summary>
        /// <param name="app">The application.</param>
        public static void UseCompression(WebApplication app)
        {
            ArgumentNullException.ThrowIfNull(app);
            _ = app.UseResponseCompression();
        }

        /// <summary>
        ///     Installs, in order, the rate limiter (after authentication, so a partition is the
        ///     principal's), the request timeouts with the abort of a response they cut short, and
        ///     output caching.
        /// </summary>
        /// <param name="app">The application.</param>
        public static void UseLimits(WebApplication app)
        {
            ArgumentNullException.ThrowIfNull(app);
            _ = app.UseRateLimiter();
            _ = app.UseRequestTimeouts();
            _ = app.UseMiddleware<TimeoutAbortMiddleware>();
            _ = app.UseOutputCache();
        }

        /// <summary>A timeout policy answering 504 <c>timeout</c> through the problem writer.</summary>
        /// <param name="timeout">How long a request may run.</param>
        /// <returns>The policy.</returns>
        internal static RequestTimeoutPolicy TimeoutPolicy(TimeSpan timeout)
        {
            return new RequestTimeoutPolicy
            {
                Timeout = timeout,
                TimeoutStatusCode = StatusCodes.Status504GatewayTimeout,
                WriteTimeoutResponse = static context => ProblemWriter.WriteAsync(context, StatusCodes.Status504GatewayTimeout, ProblemMapper.TimeoutCode),
            };
        }
    }
}
