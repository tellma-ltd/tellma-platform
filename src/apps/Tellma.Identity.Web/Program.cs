// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.HttpOverrides;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;
using System.Net;
using Tellma.Connector.AcsEmail.Adapter;
using Tellma.Connector.SendGrid.Adapter;
using Tellma.Connector.Smtp.Adapter;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Email;
using Tellma.Core.Webhooks;
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

            // Email. The engine asks for the platform's IEmailSender and knows nothing else; this is
            // where a deployment says what actually carries the mail. All three transports are
            // registered but only the one Email:Provider names is ever constructed, so switching
            // between them — or to the development log sink — is configuration, not a rebuild. The
            // adapters take the root configuration and find their own subsections under it.
            builder.Services.AddTellmaEmail();
            builder.Services.AddSmtpEmail(builder.Configuration);
            builder.Services.AddSendGridEmail(builder.Configuration);
            builder.Services.AddAcsEmail(builder.Configuration);

            // The inbound half of the same pipeline: providers report what became of each message
            // to /api/webhooks/{key}, and the engine's handler writes it onto the single-use code
            // the message carried. Only the hosted providers report at all — an SMTP relay says
            // nothing back, which is why a message records whether events are even expected of it.
            builder.Services.AddTellmaWebhooks();

            // The identity server has no tenants, so no message of its can be a sandbox tenant's and
            // none is ever withheld. The pipeline requires the decision to be stated rather than
            // assumed, and refuses to start without it.
            builder.Services.AddSingleton(SandboxContext.Never);

            // Names this deployment in email telemetry and in the sender's own logs, so mail from
            // the identity server is distinguishable from a distribution's at the provider.
            builder.Services.AddSingleton(new DeploymentIdentity("identity", builder.Environment.EnvironmentName));

            // OpenTelemetry: W3C Trace Context is the .NET default propagator, so a trace begun in a
            // distribution and passed through its BFF joins here automatically. SqlClient
            // instrumentation is what measures time spent in SQL Server I/O. Azure Monitor exports
            // only when a connection string is configured (on-prem runs without it).
            builder.Services.AddOpenTelemetry()
                .WithMetrics(metrics => metrics
                    .AddMeter(IdentityMetrics.MeterName)
                    // Delivery counts and latencies now come from the shared pipeline; without its
                    // meter here the instruments it records would be collected by nothing.
                    .AddMeter(EmailTelemetryNames.MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation())
                .WithTracing(tracing => tracing
                    .AddSource(EmailTelemetryNames.ActivitySourceName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSqlClientInstrumentation());

            if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
            {
                builder.Services.AddOpenTelemetry().UseAzureMonitor();
            }

            // Behind a reverse proxy (Azure App Service, nginx), the real client IP arrives in
            // X-Forwarded-For. Per-IP rate limiting and audit forensics depend on it, so the proxy
            // headers are honored when ForwardedHeaders:Enabled is set — but only from the proxies
            // listed in KnownProxies (addresses) or KnownNetworks (CIDR ranges). The middleware
            // treats an EMPTY proxy set as "skip the check and trust every caller", which would
            // make the client IP spoofable by anyone who sends the header, so startup refuses the
            // enabled-but-empty combination outright. (In-proc hosts that mount the engine behind
            // a proxy must configure the same middleware themselves.)
            // ASP.NET Core has its own activation switch for this middleware — the
            // ForwardedHeaders_Enabled configuration key, set by ASPNETCORE_FORWARDEDHEADERS_ENABLED
            // and recommended by some App Service guidance — which registers it with BOTH known
            // lists empty, i.e. the trust-everyone state. Refuse that path outright so there is one
            // way in and it is the checked one.
            // Compared the way the framework itself does — an ordinal-ignore-case match on
            // "true" — so this refuses exactly what it would have enabled, and a value it ignores
            // (say "1") does not become a startup failure.
            if (string.Equals(builder.Configuration["ForwardedHeaders_Enabled"], "true", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "ASPNETCORE_FORWARDEDHEADERS_ENABLED (the ForwardedHeaders_Enabled configuration key) is not "
                    + "supported: it trusts X-Forwarded-For from every caller, making the client IP spoofable. "
                    + "Unset it and configure the ForwardedHeaders section with the deployment's known proxies "
                    + "or networks instead.");
            }

            bool useForwardedHeaders = builder.Configuration.GetValue("ForwardedHeaders:Enabled", false);
            if (useForwardedHeaders)
            {
                // Note: this is a presence check, so an operator can still choose a range as wide
                // as "0.0.0.0/0" — that is an explicit decision to trust every caller, whereas an
                // empty set is the silent default that reads as restrictive and is not.
                string[] proxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
                string[] networks = builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];
                if (proxies.Length == 0 && networks.Length == 0)
                {
                    throw new InvalidOperationException(
                        "ForwardedHeaders:Enabled requires at least one ForwardedHeaders:KnownProxies address or "
                        + "ForwardedHeaders:KnownNetworks CIDR range: with both empty, X-Forwarded-For would be "
                        + "accepted from any direct caller and the client IP would be spoofable.");
                }

                builder.Services.Configure<ForwardedHeadersOptions>(headers =>
                {
                    headers.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

                    // Replace the loopback-only defaults with the deployment's actual proxies.
                    headers.KnownIPNetworks.Clear();
                    headers.KnownProxies.Clear();
                    foreach (string proxy in proxies)
                    {
                        headers.KnownProxies.Add(IPAddress.Parse(proxy));
                    }

                    foreach (string network in networks)
                    {
                        headers.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
                    }
                });
            }

            WebApplication app = builder.Build();

            // Forwarded headers must be the first middleware so every later component (rate limiter,
            // audit, HTTPS redirect) observes the real client IP and scheme.
            if (useForwardedHeaders)
            {
                app.UseForwardedHeaders();
            }

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

            // Unauthenticated by design: a provider cannot hold a token. Each receiver verifies the
            // payload signature over the raw bytes before anything is trusted, which is the
            // authentication for this route.
            app.MapTellmaWebhooks();

            app.Run();
        }
    }
}
