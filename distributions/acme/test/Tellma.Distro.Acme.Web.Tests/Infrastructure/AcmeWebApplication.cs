// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Tellma.Core.AspNetCore;

namespace Tellma.Distro.Acme.Web.Tests.Infrastructure
{
    /// <summary>
    ///     Acme's web host, built as its <c>Program.cs</c> builds it (a suite pins that file to
    ///     exactly these calls) over the web project's own configuration, and never started, so no
    ///     database is needed.
    /// </summary>
    internal static class AcmeWebApplication
    {
        /// <summary>The settings of a production host: what a deployment supplies beyond the tracked files.</summary>
        public static IReadOnlyDictionary<string, string?> ProductionSettings { get; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Tellma:PublicOrigin"] = "https://acme.app.tellma.com",
            ["Tellma:Catalog:ConnectionString"] = "Server=tcp:acme-sql.database.windows.net;Database=tellma-acme-catalog;Authentication=Active Directory Managed Identity;Encrypt=True",
            ["Tellma:Sql:Profiles:default"] = "Authentication=Active Directory Managed Identity;Encrypt=True",
            ["Tellma:Sql:AllowedServers:0"] = ".database.windows.net",
            ["Tellma:Identity:Authority"] = "https://identity.tellma.com",
            ["Tellma:Identity:ServiceClientSecret"] = "test-service-client-secret",
        };

        /// <summary>Builds the host and maps its routes, which runs the endpoint audit.</summary>
        /// <param name="environment">The host environment.</param>
        /// <param name="settings">Settings over the web project's configuration files, or null.</param>
        /// <returns>The built, unstarted application.</returns>
        public static WebApplication Build(string environment, IReadOnlyDictionary<string, string?>? settings = null)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = environment,
                ApplicationName = typeof(AcmeWebHostMarker).Assembly.GetName().Name,
                ContentRootPath = AcmePaths.Web,
            });
            if (settings is not null)
            {
                _ = builder.Configuration.AddInMemoryCollection(settings);
            }

            // --- Program.cs, call for call ---
            _ = builder.AddTellma(AcmeComposition.Slug, AcmeComposition.Compose, AcmeWebComposition.Compose);
            WebApplication app = builder.Build();
            _ = app.UseTellma();
            _ = app.MapTellma();
            return app;
        }
    }
}
