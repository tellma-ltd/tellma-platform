// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;
using Tellma.Identity.Hosting;
using Tellma.Identity.Infrastructure;

namespace Tellma.Identity.Web
{
    /// <summary>
    ///     Entry point for the standalone Tellma Identity Server host. This project is composition
    ///     and configuration only; all identity behavior lives in the <c>Tellma.Identity</c>
    ///     engine, registered through its single <c>AddTellmaIdentity</c> path.
    /// </summary>
    public static class Program
    {
        /// <summary>Builds and runs the standalone identity host.</summary>
        /// <param name="args">Command-line arguments forwarded to the host builder.</param>
        public static void Main(string[] args)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            // Structured logging; sinks and levels come from the "Serilog" configuration section.
            builder.Host.UseSerilog(static (context, services, logger) => logger
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services));

            builder.Services.AddTellmaIdentity(builder.Configuration.GetSection("TellmaIdentity"));

            // OpenTelemetry: W3C Trace Context is the .NET default propagator, so a trace begun in a
            // distribution and passed through its BFF joins here automatically. SqlClient
            // instrumentation is what measures time spent in SQL Server I/O. Azure Monitor exports
            // only when a connection string is configured (on-prem runs without it).
            builder.Services.AddOpenTelemetry()
                .WithMetrics(metrics => metrics
                    .AddMeter(IdentityMetrics.MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation())
                .WithTracing(tracing => tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSqlClientInstrumentation());

            if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
            {
                builder.Services.AddOpenTelemetry().UseAzureMonitor();
            }

            WebApplication app = builder.Build();

            app.UseSerilogRequestLogging();

            if (!app.Environment.IsDevelopment())
            {
                app.UseHsts();
                app.UseHttpsRedirection();
            }

            // Routing must run before authentication so OpenIddict's pass-through middleware can
            // match its protocol endpoints ahead of the engine's controllers.
            app.UseRouting();

            app.UseTellmaIdentity();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapStaticAssets();
            app.MapTellmaIdentity();

            app.Run();
        }
    }
}
