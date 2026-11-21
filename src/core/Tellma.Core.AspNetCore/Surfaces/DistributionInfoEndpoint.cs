// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.AspNetCore.Hosting;

namespace Tellma.Core.AspNetCore.Surfaces
{
    /// <summary>The information document: built per request from the runtime configuration.</summary>
    internal static class DistributionInfoEndpoint
    {
        /// <summary>The document's route.</summary>
        public const string Route = "/api/distribution-info";

        /// <summary>The session's sign-in route.</summary>
        public const string LoginRoute = "/bff/login";

        /// <summary>The web surface's route template.</summary>
        public const string WebSurface = "/{tenantId}/api/web";

        /// <summary>The document's cache lifetime: a minute, so a configuration change reaches every client soon.</summary>
        public const string CacheControl = "public, max-age=60";

        /// <summary>The admin surface's versions this release maps.</summary>
        internal static readonly int[] AdminVersions = [1];

        /// <summary>Answers the document.</summary>
        /// <param name="context">The request.</param>
        /// <returns>A task that completes when the document is written.</returns>
        public static Task WriteAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            DistributionInfo info = Build(context.RequestServices);
            context.Response.Headers[HeaderNames.CacheControl] = CacheControl;
            context.Response.ContentType = "application/json; charset=utf-8";
            return JsonSerializer.SerializeAsync(context.Response.Body, info, TellmaJson.Options, context.RequestAborted);
        }

        /// <summary>Builds the document from the host's configuration.</summary>
        /// <param name="services">The request's services.</param>
        /// <returns>The document.</returns>
        public static DistributionInfo Build(IServiceProvider services)
        {
            ArgumentNullException.ThrowIfNull(services);
            DeploymentIdentity deployment = services.GetRequiredService<DeploymentIdentity>();
            DeploymentVersions versions = services.GetRequiredService<DeploymentVersions>();
            IConfiguration configuration = services.GetRequiredService<IConfiguration>();
            IdentityHostOptions identity = services.GetRequiredService<IOptions<IdentityHostOptions>>().Value;
            DistributionOptions distribution = services.GetRequiredService<IOptions<DistributionOptions>>().Value;
            DistributionLinksOptions links = services.GetRequiredService<IOptions<DistributionLinksOptions>>().Value;
            SupportOptions support = services.GetRequiredService<IOptions<SupportOptions>>().Value;
            ClientTelemetryOptions telemetry = services.GetRequiredService<IOptions<ClientTelemetryOptions>>().Value;
            TellmaSessionOptions session = services.GetRequiredService<IOptions<TellmaSessionOptions>>().Value;
            bool admin = services.GetRequiredService<IOptions<AdminSurfaceOptions>>().Value.IsEnabled(identity.Mode);

            // The startup gate has checked the origin, the authority and the display name.
            string origin = configuration[HostConfigurationKeys.PublicOrigin]
                ?? throw new InvalidOperationException($"{HostConfigurationKeys.PublicOrigin} is not set.");
            string authority = identity.ResolveAuthority(origin);
            return new DistributionInfo(
                deployment.Application,
                distribution.DisplayName ?? throw new InvalidOperationException($"{DistributionOptions.SectionPath}:{nameof(DistributionOptions.DisplayName)} is not set."),
                deployment.DeploymentId,
                versions.PlatformVersion,
                versions.DistributionVersion,
                new IdentityInfo(authority, identity.Mode.ToString(), $"{authority}/account?client_id={Uri.EscapeDataString(deployment.Application)}"),
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    // The MCP and public API surfaces are announced by the features that map them.
                    ["web"] = WebSurface,
                    ["mcp"] = null,
                    ["api"] = null,
                },
                LoginRoute,
                admin ? AdminVersions : [],
                new DistributionLinks(NullIfEmpty(links.Terms), NullIfEmpty(links.Privacy)),
                new SupportInfo(support.Enabled, ProviderAccess: admin),
                new ClientTelemetryInfo(telemetry.Enabled, telemetry.SampleRate),
                new SessionInfo(session.IdleDays));
        }

        /// <summary>A value, or null when it is empty.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The value, or null.</returns>
        private static string? NullIfEmpty(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
