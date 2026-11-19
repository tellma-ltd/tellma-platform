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
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.AspNetCore.Api;
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

            // --- The tenantless routes a host bundle contributes, such as the webhook endpoint ---
            foreach (IPlatformEndpointSource source in app.Services.GetServices<IPlatformEndpointSource>())
            {
                foreach (IEndpointConventionBuilder endpoint in source.Map(app))
                {
                    Platform(endpoint, source.Reason);
                }
            }

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

            // --- The web API: every route the model derives from the stack registry, on the Web group ---
            IReadOnlyList<CompositionProblem> projection = WebApiProjection.Map(app.Services, groups.Web);

            // --- The SPA, whose fallback answers every request nothing else matched ---
            SpaHosting.Map(app);

            // --- The audit of what the platform mapped ---
            IAuthorizationPolicyProvider policies = app.Services.GetRequiredService<IAuthorizationPolicyProvider>();
            IReadOnlyList<CompositionProblem> problems =
            [
                .. projection,
                .. TellmaEndpointAudit.AuditAsync(mapped.Endpoints(), policies, app.Services.GetService<IStackRouteSource>()?.Routes)
                    .GetAwaiter()
                    .GetResult(),
            ];
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
            // --- The group's policy, and API answers: a refused call is 401 or 403, never a redirect ---
            RouteGroupBuilder builder = app.MapGroup(TenantPrefix + suffix);
            _ = builder.RequireAuthorization(policy).WithMetadata(new TellmaGroupMetadata(group)).DisableCookieRedirect();

            // --- The filter chain, outermost first: tenant, telemetry, the web surface's request filter, then the witness ---
            // Each refuses before the next runs; none evaluates a securable, the handler's service does.
            // The telemetry stage wraps each endpoint's delegate below, between the guard and the witness.
            if (guarded)
            {
                _ = builder.AddEndpointFilter<TenantAccessFilter>();
            }

            if (group == TellmaGroup.Web)
            {
                _ = builder.AddEndpointFilter<WebRequestFilter>();
            }

            if (guarded)
            {
                // Last, so it sees the handler's result before anything is written.
                _ = builder.AddEndpointFilter<AccessWitnessFilter>();
            }

            // Runs after each endpoint's own conventions, so WithMutation's stamp is folded in.
            IEndpointConventionBuilder conventions = builder;
            conventions.Finally(endpoint => TenantGroupConventions.Complete(endpoint, surface));
            if (group == TellmaGroup.Web)
            {
                // A JSON body's ceiling, enforced before the body is read; a binary or limited endpoint states its own.
                long maxBytes = app.Services.GetRequiredService<IOptions<ApiOptions>>().Value.MaxJsonBodyBytes;
                conventions.Finally(endpoint => JsonBodySizeMetadata.Apply(endpoint, maxBytes));
            }

            if (guarded)
            {
                // Endpoint filters reach route handlers alone; these reach every endpoint of the group.
                // The witness wraps first and the guard last, so the guard's wrapper, which the audit
                // checks, stays outermost; the web surface's telemetry runs between the two.
                conventions.Finally(AccessWitnessFilter.Wrap);
                if (group == TellmaGroup.Web)
                {
                    conventions.Finally(ApiTelemetryFilter.Wrap);
                }

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
