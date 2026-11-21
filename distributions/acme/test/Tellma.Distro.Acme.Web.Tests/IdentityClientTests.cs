// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.Composition;
using Tellma.Core.Identity;
using Tellma.Distro.Acme.Web.Tests.Infrastructure;

namespace Tellma.Distro.Acme.Web.Tests
{
    /// <summary>
    ///     The distribution's identity-server client, as both hosts compose it from the tracked
    ///     configuration: the web host in Development reaches its in-proc engine as <c>acme-svc</c> with
    ///     the secret it generates for the boot, the web host in Production its standalone identity
    ///     server with the configured secret, and the migrator resolves it too.
    /// </summary>
    public sealed class IdentityClientTests
    {
        [Fact]
        public async Task The_web_host_in_development_reaches_its_in_proc_engine_with_the_boot_s_secret()
        {
            await using WebApplication app = AcmeWebApplication.Build("Development");

            Assert.IsType<IdentityServerClient>(app.Services.GetRequiredService<IIdentityServerClient>());
            IdentityServerClientOptions options = app.Services.GetRequiredService<IOptions<IdentityServerClientOptions>>().Value;
            Assert.Equal(
                (app.Configuration["Tellma:PublicOrigin"] + "/id", "acme-svc"),
                (options.Authority?.AbsoluteUri, options.ClientId));
            Assert.False(string.IsNullOrWhiteSpace(options.ServiceClientSecret));
        }

        [Fact]
        public async Task The_web_host_in_production_reaches_the_standalone_identity_server_with_the_configured_secret()
        {
            await using WebApplication app = AcmeWebApplication.Build("Production", AcmeWebApplication.ProductionSettings);

            IdentityServerClientOptions options = app.Services.GetRequiredService<IOptions<IdentityServerClientOptions>>().Value;

            Assert.Equal(
                ("https://identity.tellma.com/", "acme-svc", "test-service-client-secret"),
                (options.Authority?.AbsoluteUri, options.ClientId, options.ServiceClientSecret));
        }

        [Fact]
        public void The_migrator_s_composition_resolves_the_client_against_the_in_proc_issuer_in_development()
        {
            // --- The migrator's host, over the tracked configuration files it links ---
            HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
            {
                EnvironmentName = Environments.Development,
                ContentRootPath = AcmePaths.Migrator,
            });
            _ = builder.Configuration
                .AddJsonFile(Path.Combine(AcmePaths.Web, "appsettings.json"), optional: false)
                .AddJsonFile(Path.Combine(AcmePaths.Web, "appsettings.Development.json"), optional: false);
            _ = builder.Services.AddTellma(AcmeComposition.Slug, builder.Configuration, builder.Environment, AcmeComposition.Compose);
            using IHost host = builder.Build();

            // The options validate, although the migrator holds no service secret: only the web host
            // holds the one it generates for its boot.
            Assert.IsType<IdentityServerClient>(host.Services.GetRequiredService<IIdentityServerClient>());
            IdentityServerClientOptions options = host.Services.GetRequiredService<IOptions<IdentityServerClientOptions>>().Value;
            Assert.Equal(
                (builder.Configuration["Tellma:PublicOrigin"] + "/id", "acme-svc", null),
                (options.Authority?.AbsoluteUri, options.ClientId, options.ServiceClientSecret));
        }
    }
}
