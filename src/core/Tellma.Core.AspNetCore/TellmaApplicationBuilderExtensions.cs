// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;
using Serilog;
using System.Diagnostics;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Api;
using Tellma.Core.AspNetCore.Csrf;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Limits;
using Tellma.Core.AspNetCore.Observability;
using Tellma.Core.AspNetCore.Problems;
using Tellma.Core.AspNetCore.Realtime;
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
        ///     forwarded headers, request logging, the problem-details exception handler and status-code
        ///     pages, HSTS and HTTPS redirection outside Development, response compression, routing,
        ///     the hub's refusal count, authentication and authorization, the rate limiter, the request
        ///     timeouts, output caching, the CSRF control, the web API's contract check and the tenant
        ///     middleware. A distribution's own middleware goes between this call and <c>MapTellma</c>.
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

                    // Tenant JSON is never stored by a cache; the blob surface states its own caching.
                    if (TenantJson.Is(context) && !context.Response.Headers.ContainsKey(HeaderNames.CacheControl))
                    {
                        context.Response.Headers.CacheControl = TenantJson.NoStore;
                    }

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
            // Its completion event is written after the tenant middleware's logger scope has closed,
            // so the tenant request's members are copied onto it from the request's context.
            _ = app.UseSerilogRequestLogging(options =>
            {
                options.Logger = app.Services.GetRequiredService<ILogger>();
                options.EnrichDiagnosticContext = static (diagnostics, context) =>
                {
                    TenantLogScope.Enrich(diagnostics, context.RequestServices.GetService<IRequestContextAccessor>());
                    ApiLogScope.Enrich(diagnostics, context);
                };
            });

            // --- 4. Exceptions as problem details, and the framework's empty refusals as the same bodies ---
            _ = app.UseExceptionHandler(new ExceptionHandlerOptions
            {
                // The registered handler answers every exception; this only satisfies the middleware.
                ExceptionHandler = static context => ProblemWriter.WriteAsync(context, StatusCodes.Status500InternalServerError, ProblemMapper.InternalCode),

                // The handler logs what is an error (the backstop, with its exception) and nothing a
                // mapped refusal or an abandoned request answers, so the middleware logs nothing itself.
                SuppressDiagnosticsCallback = static _ => true,
            });
            _ = app.UseStatusCodePages(FrameworkProblems.HandleStatusAsync);

            // --- 5. Transport security, outside Development ---
            if (!app.Environment.IsDevelopment())
            {
                _ = app.UseHsts();
                _ = app.UseHttpsRedirection();
            }

            // --- 6. Response compression of JSON, unless configured off ---
            LimitsComposition.UseCompression(app);

            // --- 7. Routing ---
            _ = app.UseRouting();

            // --- 8. The in-proc identity engine's middleware, when composed ---
            if (InProcIdentity.IsInProc(app.Configuration))
            {
                InProcIdentity.Use(app);
            }

            // --- 9. Authentication, then authorization ---
            // The hub's refusals by them and by the CSRF control are counted as its front door's are.
            _ = app.UseMiddleware<HubRefusalMiddleware>();
            _ = app.UseAuthentication();
            _ = app.UseAuthorization();

            // --- 10-12. The rate limiter, request timeouts and output caching ---
            // After authentication, so a partition is the principal's; before the tenant, so a
            // refusal reads the same for a tenant that does not exist and one the caller may not open.
            LimitsComposition.UseLimits(app);

            // --- 13. CSRF, ahead of every tenant and every endpoint filter ---
            _ = app.UseMiddleware<TellmaCsrfMiddleware>();

            // --- 14. The contract check, before the tenant is resolved or a body read ---
            // It checks a projected endpoint alone, so a host without stacks passes every request through.
            _ = app.UseMiddleware<TellmaContractMiddleware>();

            // --- 15. The tenant ---
            _ = app.UseMiddleware<TenantMiddleware>();
            return app;
        }
    }
}
