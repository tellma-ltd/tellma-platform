// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.AspNetCore.Bff;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Routing;
using Tellma.Core.AspNetCore.Surfaces;
using Tellma.Core.AspNetCore.Tenancy;
using Tellma.Core.Composition;

namespace Tellma.Core.AspNetCore
{
    /// <summary>Maps the platform's endpoints and route groups.</summary>
    public static class TellmaEndpointRouteBuilderExtensions
    {
        /// <summary>The prefix every tenant route group begins with.</summary>
        internal const string TenantPrefix = "/{tenantId:int:min(1)}";

        /// <summary>
        ///     Maps the platform's tenantless routes (the information document, the health probes, the
        ///     admin surface when enabled, the protected-resource metadata and the SPA) and the tenant
        ///     route groups, then audits what it mapped. A distribution maps its own endpoints onto the
        ///     groups returned, inheriting their policies, filters and metadata; the startup gate audits
        ///     those too.
        /// </summary>
        /// <param name="app">The application, whose pipeline <c>UseTellma</c> installed.</param>
        /// <returns>The route groups.</returns>
        /// <exception cref="InvalidOperationException"><c>UseTellma</c> has not run, or <c>MapTellma</c> already has.</exception>
        /// <exception cref="TellmaCompositionException">A platform endpoint fails the audit.</exception>
        public static TellmaEndpoints MapTellma(this WebApplication app)
        {
            ArgumentNullException.ThrowIfNull(app);
            if (!((IApplicationBuilder)app).Properties.ContainsKey(TellmaApplicationBuilderExtensions.PipelineInstalledKey))
            {
                throw new InvalidOperationException("MapTellma maps onto the pipeline UseTellma installs; call app.UseTellma() first.");
            }

            // The gate's audit walks these sources, endpoints a distribution maps afterwards included.
            IEndpointRouteBuilder routes = app;
            MappedEndpoints mapped = app.Services.GetRequiredService<MappedEndpoints>();
            mapped.Capture(routes.DataSources);

            // --- The tenantless platform routes ---
            Platform(app.MapGet(DistributionInfoEndpoint.Route, DistributionInfoEndpoint.WriteAsync), "distribution-info");
            Platform(app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = static _ => false }), "health");
            Platform(
                app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = static check => check.Tags.Contains(HealthCheckNames.ReadyTag) }),
                "health");
            Platform(app.MapGet(ProtectedResourceEndpoint.Route, ProtectedResourceEndpoint.WriteAsync), "protected-resource-metadata");

            // The admin surface's prefix stays reserved when it is off: the audit refuses a
            // distribution endpoint under it either way.
            IdentityHostOptions identity = HostSettings.Read<IdentityHostOptions>(app.Configuration, IdentityHostOptions.SectionPath);
            if (HostSettings.Read<AdminSurfaceOptions>(app.Configuration, AdminSurfaceOptions.SectionPath).IsEnabled(identity.Mode))
            {
                AdminEndpoints.MapV1(app);
            }

            // --- The BFF: sign-in, sign-out and the back-channel logout are anonymous; the user needs a session ---
            (IEndpointConventionBuilder[] anonymous, IEndpointConventionBuilder[] session) = BffEndpoints.Map(app);
            foreach (IEndpointConventionBuilder endpoint in anonymous)
            {
                Platform(endpoint, BffEndpoints.TenantlessReason);
            }

            foreach (IEndpointConventionBuilder endpoint in session)
            {
                _ = endpoint
                    .RequireAuthorization(TellmaPolicies.Web)
                    .AsTenantlessEndpoint(BffEndpoints.TenantlessReason)
                    .WithMetadata(PlatformEndpointMetadata.Instance);
            }

            // --- The in-proc identity server's controllers and pages, under its path base ---
            if (InProcIdentity.IsInProc(app.Configuration))
            {
                InProcIdentity.Map(app);
            }

            // --- The tenant route groups ---
            TellmaEndpoints groups = new(
                TenantGroup(app, "/api/web", TellmaGroup.Web, TenantSurface.Web, TellmaPolicies.Web, guarded: true),
                TenantGroup(app, "/api/v{version:apiVersion}", TellmaGroup.Api, TenantSurface.Api, TellmaPolicies.Api, guarded: true),
                TenantGroup(app, "/hub", TellmaGroup.Hub, TenantSurface.Hub, TellmaPolicies.Web, guarded: false),
                TenantGroup(app, "/blobs", TellmaGroup.Blobs, TenantSurface.Blobs, TellmaPolicies.Web, guarded: true),
                app.MapGroup("/api").WithMetadata(new TellmaGroupMetadata(TellmaGroup.Tenantless)));

            // --- The SPA, whose fallback answers every request nothing else matched ---
            SpaHosting.Map(app);

            // --- The audit of what the platform mapped ---
            IAuthorizationPolicyProvider policies = app.Services.GetRequiredService<IAuthorizationPolicyProvider>();
            IReadOnlyList<CompositionProblem> problems = TellmaEndpointAudit.AuditAsync(mapped.Endpoints(), policies, app.Services.GetService<IStackRouteSource>()?.Routes)
                .GetAwaiter()
                .GetResult();
            return problems.Count > 0 ? throw new TellmaCompositionException(problems) : groups;
        }

        /// <summary>Maps a tenant route group under its surface's policy, its filter and its final stamp.</summary>
        /// <param name="app">The application.</param>
        /// <param name="suffix">The group's route after the tenant id.</param>
        /// <param name="group">The group.</param>
        /// <param name="surface">The group's surface.</param>
        /// <param name="policy">The group's policy.</param>
        /// <param name="guarded">Whether the access filter guards the group; the hub's front door is the tenant middleware.</param>
        /// <returns>The group.</returns>
        private static RouteGroupBuilder TenantGroup(WebApplication app, string suffix, TellmaGroup group, TenantSurface surface, string policy, bool guarded)
        {
            RouteGroupBuilder builder = app.MapGroup(TenantPrefix + suffix);
            _ = builder.RequireAuthorization(policy).WithMetadata(new TellmaGroupMetadata(group));
            if (guarded)
            {
                _ = builder.AddEndpointFilter<TenantAccessFilter>();
            }

            // Runs after each endpoint's own conventions, so WithMutation's stamp is folded in.
            IEndpointConventionBuilder conventions = builder;
            conventions.Finally(endpoint => TenantGroupConventions.Complete(endpoint, surface));
            if (guarded)
            {
                // Endpoint filters reach route handlers alone; this reaches every endpoint of the group.
                conventions.Finally(TenantAccessFilter.Wrap);
            }

            return builder;
        }

        /// <summary>Stamps a tenantless platform endpoint: anonymous, tenantless, the platform's.</summary>
        /// <param name="builder">The endpoint.</param>
        /// <param name="reason">Why it serves no tenant.</param>
        private static void Platform(IEndpointConventionBuilder builder, string reason)
        {
            _ = builder
                .AllowAnonymous()
                .AsTenantlessEndpoint(reason)
                .WithMetadata(PlatformEndpointMetadata.Instance);
        }
    }
}
