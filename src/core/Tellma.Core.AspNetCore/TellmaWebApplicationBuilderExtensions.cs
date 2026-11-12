// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Net;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.AspNetCore.Authentication;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Observability;
using Tellma.Core.AspNetCore.Problems;
using Tellma.Core.AspNetCore.Routing;
using Tellma.Core.AspNetCore.Surfaces;
using Tellma.Core.Composition;

namespace Tellma.Core.AspNetCore
{
    /// <summary>Composes a distribution's web host.</summary>
    public static class TellmaWebApplicationBuilderExtensions
    {
        /// <summary>The owner the host's options are recorded under in the options check.</summary>
        internal const string OptionsOwner = "host";

        /// <summary>The host's shutdown timeout, long enough for background work to drain.</summary>
        internal static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        ///     Composes the distribution's features and the web host's services: <paramref name="compose" />
        ///     and then <paramref name="composeWeb" /> on one builder, realised and gated once, then the
        ///     schemes and policies, Data Protection, forwarded headers, the endpoint audit, the health
        ///     checks, logging and telemetry.
        /// </summary>
        /// <param name="builder">The web application builder.</param>
        /// <param name="slug">The distribution's slug.</param>
        /// <param name="compose">Composes the distribution's features; the migrator runs the same delegate.</param>
        /// <param name="composeWeb">
        ///     Web-only composition, such as service registrations that need the web host; never a
        ///     feature or an entity, which the migrator would not see.
        /// </param>
        /// <returns>The builder, for chaining.</returns>
        /// <exception cref="TellmaCompositionException">The composition is invalid.</exception>
        public static WebApplicationBuilder AddTellma(
            this WebApplicationBuilder builder, string slug, Action<TellmaBuilder> compose, Action<TellmaBuilder>? composeWeb = null)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(slug);
            ArgumentNullException.ThrowIfNull(compose);

            // The distribution's version is read from the assembly that declares its composition, not
            // from the composer this method wraps it in.
            builder.Services.TryAddSingleton(DeploymentVersionsReader.Read(compose));
            _ = builder.Services.AddTellma(slug, builder.Configuration, builder.Environment, tellma =>
            {
                compose(tellma);
                composeWeb?.Invoke(tellma);
            });

            AddTellmaAspNetCore(builder.Services, slug, builder.Configuration, builder.Environment);
            return builder;
        }

        /// <summary>Registers the web host's services over a realised composition.</summary>
        /// <param name="services">The services.</param>
        /// <param name="slug">The distribution's slug.</param>
        /// <param name="configuration">The application's configuration.</param>
        /// <param name="environment">The host environment.</param>
        internal static void AddTellmaAspNetCore(IServiceCollection services, string slug, IConfiguration configuration, IHostEnvironment environment)
        {
            // --- The host's options, validated by the startup gate's options check ---
            _ = HostOptionsBinding.Bind<DistributionOptions>(services, DistributionOptions.SectionPath, OptionsOwner);
            _ = HostOptionsBinding.Bind<IdentityHostOptions>(services, IdentityHostOptions.SectionPath, OptionsOwner);
            _ = HostOptionsBinding.Bind<TellmaSessionOptions>(services, TellmaSessionOptions.SectionPath, OptionsOwner);
            _ = HostOptionsBinding.Bind<DataProtectionHostOptions>(services, DataProtectionHostOptions.SectionPath, OptionsOwner);
            _ = HostOptionsBinding.Bind<ForwardedHeadersHostOptions>(services, ForwardedHeadersHostOptions.SectionPath, OptionsOwner);
            _ = HostOptionsBinding.Bind<AdminSurfaceOptions>(services, AdminSurfaceOptions.SectionPath, OptionsOwner);
            _ = HostOptionsBinding.Bind<SupportOptions>(services, SupportOptions.SectionPath, OptionsOwner);
            _ = HostOptionsBinding.Bind<ClientTelemetryOptions>(services, ClientTelemetryOptions.SectionPath, OptionsOwner);
            _ = HostOptionsBinding.Bind<DistributionLinksOptions>(services, DistributionLinksOptions.SectionPath, OptionsOwner);
            _ = HostOptionsBinding.Bind<ApiOptions>(services, ApiOptions.SectionPath, OptionsOwner);
            _ = services.AddSingleton<IValidateOptions<IdentityHostOptions>, IdentityHostOptionsValidator>();
            _ = services.AddSingleton<IValidateOptions<DistributionLinksOptions>, DistributionLinksOptionsValidator>();
            _ = services.AddSingleton<IValidateOptions<ForwardedHeadersHostOptions>, ForwardedHeadersHostOptionsValidator>();

            // --- Hosting: the shutdown drain ---
            _ = services.Configure<HostOptions>(static options => options.ShutdownTimeout = ShutdownTimeout);

            // --- Problems and the host's instruments ---
            _ = services.AddSingleton<HostMetrics>();
            _ = services.AddExceptionHandler<TellmaProblemExceptionHandler>();

            // --- Routing: the audit over every mapped endpoint, the public API's version constraint ---
            _ = services.AddSingleton<MappedEndpoints>();
            _ = services.AddSingleton<IStartupCheck, TellmaEndpointAudit>();
            _ = services.PostConfigure<RouteOptions>(static options =>
            {
                // After every configuration, so an API versioning library's own constraint wins.
                _ = options.ConstraintMap.TryAdd(ApiVersionRouteConstraint.Name, typeof(ApiVersionRouteConstraint));
            });

            // --- Authentication and authorization ---
            TellmaAuthenticationComposition.Register(services);

            // --- Forwarded headers: the configured proxies alone, and only the client address and scheme ---
            _ = services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<ForwardedHeadersHostOptions>>(ConfigureForwardedHeaders);

            // --- Data Protection ---
            DataProtectionComposition.Register(services, slug, HostSettings.Read<DataProtectionHostOptions>(configuration, DataProtectionHostOptions.SectionPath));

            // --- Logs, traces and metrics ---
            DeploymentVersions versions = services
                .Select(static descriptor => descriptor.ServiceType == typeof(DeploymentVersions) ? descriptor.ImplementationInstance : null)
                .OfType<DeploymentVersions>()
                .First();
            ObservabilityComposition.Register(services, configuration, new DeploymentIdentity(slug, environment.EnvironmentName), versions);

            // --- Health: liveness has no checks; readiness has these ---
            _ = services.AddHealthChecks()
                .AddCheck<CatalogHealthCheck>(HealthCheckNames.Catalog, tags: [HealthCheckNames.ReadyTag])
                .AddCheck<DataProtectionHealthCheck>(HealthCheckNames.DataProtection, tags: [HealthCheckNames.ReadyTag])
                .AddCheck<AuthorityHealthCheck>(HealthCheckNames.Authority, tags: [HealthCheckNames.ReadyTag]);

            // --- The SPA's published assets ---
            _ = services.AddOptions<SpaHostingOptions>();
        }

        /// <summary>Configures the forwarded headers middleware from the host's options.</summary>
        /// <param name="options">The middleware's options.</param>
        /// <param name="host">The host's options.</param>
        private static void ConfigureForwardedHeaders(ForwardedHeadersOptions options, IOptions<ForwardedHeadersHostOptions> host)
        {
            // The host is never taken from a header: every derived URI comes from the public origin.
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // The framework's loopback defaults are replaced by the configured proxies alone.
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (string proxy in host.Value.KnownProxies)
            {
                if (IPAddress.TryParse(proxy, out IPAddress? address))
                {
                    options.KnownProxies.Add(address);
                }
            }

            foreach (string network in host.Value.KnownNetworks)
            {
                if (System.Net.IPNetwork.TryParse(network, out System.Net.IPNetwork parsed))
                {
                    options.KnownIPNetworks.Add(parsed);
                }
            }
        }
    }
}
