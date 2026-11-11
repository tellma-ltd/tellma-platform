// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Diagnostics;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.AspNetCore.Csrf;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Observability;
using Tellma.Core.AspNetCore.Problems;
using Tellma.Core.AspNetCore.Tenancy;

namespace Tellma.Core.AspNetCore
{
    /// <summary>Installs the web host's fixed request pipeline.</summary>
    public static class TellmaApplicationBuilderExtensions
    {
        /// <summary>The application property that records that the pipeline is installed.</summary>
        internal const string PipelineInstalledKey = "Tellma.PipelineInstalled";

        /// <summary>
        ///     Installs the request pipeline in its fixed order and nothing else: the header stamp,
        ///     forwarded headers, request logging, the problem-details exception handler, HSTS and HTTPS
        ///     redirection outside Development, routing, authentication and authorization, the CSRF
        ///     control and the tenant middleware. A distribution's own middleware goes between this call
        ///     and <c>MapTellma</c>.
        /// </summary>
        /// <param name="app">The application.</param>
        /// <returns>The application, for chaining.</returns>
        /// <exception cref="InvalidOperationException">The pipeline is already installed.</exception>
        public static WebApplication UseTellma(this WebApplication app)
        {
            ArgumentNullException.ThrowIfNull(app);
            IDictionary<string, object?> properties = ((IApplicationBuilder)app).Properties;
            if (properties.ContainsKey(PipelineInstalledKey))
            {
                throw new InvalidOperationException("UseTellma installs the request pipeline once; it was called again.");
            }

            properties[PipelineInstalledKey] = true;
            DeploymentIdentity deployment = app.Services.GetRequiredService<DeploymentIdentity>();
            DeploymentVersions versions = app.Services.GetRequiredService<DeploymentVersions>();

            // --- 1. The header stamp: every response, early refusals included, names its trace and build ---
            _ = app.Use((context, next) =>
            {
                string traceId = TraceIdentity.Of(context);
                _ = Activity.Current?.SetTag(ObservabilityComposition.DeploymentIdTag, deployment.DeploymentId);
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers[TellmaHeaders.TraceId] = traceId;
                    context.Response.Headers[TellmaHeaders.Build] = versions.DistributionVersion;
                    return Task.CompletedTask;
                });
                return next(context);
            });

            // --- 2. Forwarded headers, from the configured proxies alone ---
            if (HostSettings.Read<ForwardedHeadersHostOptions>(app.Configuration, ForwardedHeadersHostOptions.SectionPath).Enabled)
            {
                _ = app.UseForwardedHeaders();
            }

            // --- 3. Request logging, through the host's own logger rather than the static one ---
            _ = app.UseSerilogRequestLogging(options => options.Logger = app.Services.GetRequiredService<ILogger>());

            // --- 4. Exceptions as problem details ---
            _ = app.UseExceptionHandler(new ExceptionHandlerOptions
            {
                // The registered handler answers every exception; this only satisfies the middleware.
                ExceptionHandler = static context => ProblemWriter.WriteAsync(context, StatusCodes.Status500InternalServerError, "internal"),

                // A mapped refusal is an expected answer, not an error to log; everything else is.
                SuppressDiagnosticsCallback = static context => TellmaProblemExceptionHandler.IsMapped(context.Exception)
                    || (context.Exception is OperationCanceledException && context.HttpContext.RequestAborted.IsCancellationRequested),
            });

            // --- 5. Transport security, outside Development ---
            if (!app.Environment.IsDevelopment())
            {
                _ = app.UseHsts();
                _ = app.UseHttpsRedirection();
            }

            // --- 6. Response compression: the web API's, when composed ---

            // --- 7. Routing ---
            _ = app.UseRouting();

            // --- 8. The in-proc identity engine's middleware, when composed ---

            // --- 9. Authentication, then authorization ---
            _ = app.UseAuthentication();
            _ = app.UseAuthorization();

            // --- 10-12. The rate limiter, request timeouts and output caching: the web API's, when composed ---

            // --- 13. CSRF, ahead of every tenant and every endpoint filter ---
            _ = app.UseMiddleware<TellmaCsrfMiddleware>();

            // --- 14. The contract middleware: the web API's, when composed ---

            // --- 15. The tenant ---
            _ = app.UseMiddleware<TenantMiddleware>();
            return app;
        }
    }
}
