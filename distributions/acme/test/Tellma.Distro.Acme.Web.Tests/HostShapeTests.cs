// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;
using Tellma.Core.Tenancy;
using Tellma.Distro.Acme.Web.Provisioning;
using Tellma.Distro.Acme.Web.Tests.Infrastructure;

namespace Tellma.Distro.Acme.Web.Tests
{
    /// <summary>
    ///     The web host's shape: its Program.cs makes exactly the platform's calls, the routes it maps
    ///     pass the endpoint audit in each identity mode, and its Development additions stay in
    ///     Development.
    /// </summary>
    public sealed partial class HostShapeTests
    {
        [Fact]
        public void The_web_host_s_program_makes_exactly_the_three_platform_calls()
        {
            Assert.Equal(
                [
                    "WebApplicationBuilder builder = WebApplication.CreateBuilder(args)",
                    "_ = builder.AddTellma(AcmeComposition.Slug, AcmeComposition.Compose, AcmeWebComposition.Compose)",
                    "WebApplication app = builder.Build()",
                    "_ = app.UseTellma()",
                    "_ = app.MapTellma()",
                    "app.Run()",
                ],
                Statements(Path.Combine(AcmePaths.Web, "Program.cs")));
        }

        [Fact]
        public void The_migrator_s_program_makes_exactly_the_one_platform_call()
        {
            Assert.Equal(
                ["return await TellmaMigrator.RunAsync(args, AcmeComposition.Slug, AcmeComposition.Compose, TellmaIdentityStore.MigrateAsync).ConfigureAwait(false)"],
                Statements(Path.Combine(AcmePaths.Migrator, "Program.cs")));
        }

        [Theory]
        [InlineData("Development")]
        [InlineData("Production")]
        public async Task Every_endpoint_the_host_maps_passes_the_endpoint_audit(string environment)
        {
            // MapTellma audits what it mapped and throws on any problem, so building is the assertion;
            // the webhook route is checked to be among what was audited.
            await using WebApplication app = AcmeWebApplication.Build(
                environment, environment == "Production" ? AcmeWebApplication.ProductionSettings : null);

            RouteEndpoint[] webhooks = [.. ((IEndpointRouteBuilder)app).DataSources
                .SelectMany(static source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .Where(static endpoint => endpoint.RoutePattern.RawText == "/api/webhooks/{key}")];
            Assert.NotEmpty(webhooks);
            Assert.All(webhooks, static endpoint => Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>()));
        }

        [Fact]
        public async Task Development_provisions_through_the_local_migrator()
        {
            await using WebApplication app = AcmeWebApplication.Build("Development");

            Assert.IsType<LocalMigratorProvisioningTrigger>(app.Services.GetRequiredService<ITenantProvisioningTrigger>());
        }

        [Fact]
        public async Task Production_keeps_the_platform_s_unconfigured_provisioning()
        {
            await using WebApplication app = AcmeWebApplication.Build("Production", AcmeWebApplication.ProductionSettings);

            Assert.IsType<NotConfiguredProvisioningTrigger>(app.Services.GetRequiredService<ITenantProvisioningTrigger>());
        }

        /// <summary>A C# file's statements, without comments, usings and line breaks.</summary>
        /// <param name="file">The file.</param>
        /// <returns>The statements, whitespace collapsed.</returns>
        private static string[] Statements(string file)
        {
            string code = string.Join(
                ' ',
                File.ReadAllLines(file)
                    .Select(static line => line.Trim())
                    .Where(static line => !line.StartsWith("//", StringComparison.Ordinal) && !line.StartsWith("using ", StringComparison.Ordinal)));
            return [.. code.Split(';')
                .Select(static statement => Whitespace().Replace(statement, " ").Trim())
                .Where(static statement => statement.Length > 0)];
        }

        [GeneratedRegex(@"\s+")]
        private static partial Regex Whitespace();
    }
}
