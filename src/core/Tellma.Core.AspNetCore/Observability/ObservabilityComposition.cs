// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Instrumentation.SqlClient;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using System.Diagnostics;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.AspNetCore.Observability
{
    /// <summary>
    ///     Wires the host's logs, traces and metrics: Serilog from the <c>Serilog</c> section with the
    ///     version enrichers, and OpenTelemetry over the platform's meters and activity sources and the
    ///     framework's instrumentation. No exporter: a host bundle adds the one its cloud uses.
    /// </summary>
    internal static class ObservabilityComposition
    {
        /// <summary>The OpenTelemetry resource attribute naming the platform's version.</summary>
        public const string PlatformVersionAttribute = "tellma.platform.version";

        /// <summary>The request activity's tag naming the deployment.</summary>
        public const string DeploymentIdTag = "tellma.deployment.id";

        /// <summary>The relay of the SPA's own telemetry, which is never traced itself.</summary>
        public const string ClientTelemetryPath = "/api/client-telemetry";

        /// <summary>The platform's meters, each exported when the feature that owns it is composed.</summary>
        internal static readonly string[] Meters =
        [
            TenancyTelemetryNames.MeterName,
            HostTelemetryNames.MeterName,
            "Tellma.Blobs",
            "Tellma.Email",
            "Tellma.Webhooks",
            "Tellma.Core.Mcp",
            "Tellma.Identity",
        ];

        /// <summary>The platform's activity sources.</summary>
        internal static readonly string[] Sources = ["Tellma.Core", "Tellma.Email"];

        /// <summary>The span tags that carry a SQL batch's text, which reaches logs only through the slow-round-trip event.</summary>
        internal static readonly string[] QueryTextTags = ["db.query.text", "db.query.summary", "db.statement"];

        /// <summary>Registers logging, tracing and metrics.</summary>
        /// <param name="services">The services.</param>
        /// <param name="configuration">The application's configuration.</param>
        /// <param name="deployment">The deployment's identity.</param>
        /// <param name="versions">The deployment's versions.</param>
        public static void Register(
            IServiceCollection services, IConfiguration configuration, DeploymentIdentity deployment, DeploymentVersions versions)
        {
            // --- Logs: Serilog, whose events also reach OpenTelemetry's provider and no other ---
            // Serilog forwards every event to each registered logger provider, so the host's
            // defaults (console, debug, event source, event log) would write each line a second
            // time, past the Logging section's filters; Serilog's own sinks replace them. The host's
            // logger stays its own: the process-wide static logger is left alone, so two hosts in one
            // process (as in tests) never write through, or close, each other's sinks.
            _ = services.RemoveAll<ILoggerProvider>();
            _ = services.AddSerilog(
                (provider, logger) => logger
                    .ReadFrom.Configuration(configuration)
                    .ReadFrom.Services(provider)
                    .Enrich.FromLogContext()
                    .Enrich.WithProperty("PlatformVersion", versions.PlatformVersion)
                    .Enrich.WithProperty("DistributionVersion", versions.DistributionVersion),
                preserveStaticLogger: true,
                writeToProviders: true);

            // --- Trace context: a browser's ids adopted, its flags, state and baggage not ---
            // The framework parents the request activity through the registered propagator; the
            // instrumentation re-extracts through OpenTelemetry's unless that is the plain W3C one.
            services.Replace(ServiceDescriptor.Singleton<DistributedContextPropagator, BrowserTraceContextPropagator>());
            Sdk.SetDefaultTextMapPropagator(new TraceContextPropagator());

            // --- Traces and metrics ---
            Sampler root = new AlwaysOnSampler();
            _ = services.AddOpenTelemetry()
                .ConfigureResource(resource => resource
                    .AddService(serviceName: deployment.Application, serviceVersion: versions.DistributionVersion, autoGenerateServiceInstanceId: true)
                    .AddAttributes(
                    [
                        new KeyValuePair<string, object>(PlatformVersionAttribute, versions.PlatformVersion),
                        new KeyValuePair<string, object>("deployment.environment.name", deployment.EnvironmentName),
                    ]))
                .WithTracing(tracing => tracing
                    // An inbound sampled flag never decides: a remote parent's trace is sampled by the root.
                    .SetSampler(new ParentBasedSampler(root, remoteParentSampled: root, remoteParentNotSampled: root))
                    .AddSource(Sources)
                    .AddAspNetCoreInstrumentation(options => options.Filter = static context => !IsClientTelemetry(context.Request))
                    .AddHttpClientInstrumentation()
                    .AddSqlClientInstrumentation(ConfigureSqlClient))
                .WithMetrics(metrics => metrics
                    .AddMeter(Meters)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation())
                .WithLogging(configureBuilder: null, configureOptions: static options =>
                {
                    // The tenant's logger scope reaches every sink.
                    options.IncludeScopes = true;
                    options.IncludeFormattedMessage = true;
                });
        }

        /// <summary>
        ///     Configures the SqlClient instrumentation: a command's text and its summary are removed
        ///     from its span. The instrumentation sets them when it starts the span and enriches it
        ///     afterwards, so the enrichment is the last word.
        /// </summary>
        /// <param name="options">The instrumentation's options.</param>
        internal static void ConfigureSqlClient(SqlClientTraceInstrumentationOptions options)
        {
            options.EnrichWithSqlCommand = static (activity, _) => RemoveQueryText(activity);
        }

        /// <summary>Removes a SQL span's batch text.</summary>
        /// <param name="activity">The span.</param>
        internal static void RemoveQueryText(Activity activity)
        {
            foreach (string tag in QueryTextTags)
            {
                _ = activity.SetTag(tag, null);
            }
        }

        /// <summary>Whether a request is the client telemetry relay's.</summary>
        /// <param name="request">The request.</param>
        /// <returns>True when it is.</returns>
        private static bool IsClientTelemetry(HttpRequest request)
        {
            return request.Path.StartsWithSegments(ClientTelemetryPath, StringComparison.OrdinalIgnoreCase);
        }
    }
}
