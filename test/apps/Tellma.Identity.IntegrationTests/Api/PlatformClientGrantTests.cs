// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Tellma.Identity.IntegrationTests.Infrastructure;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tellma.Identity.IntegrationTests.Api
{
    /// <summary>
    ///     Provisioning a distribution grants its audience to the platform clients that may name
    ///     distribution APIs. Every other suite provisions into a registry holding no such client,
    ///     which skips the grant entirely — so this is the only place the write is reached.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class PlatformClientGrantTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task Provisioning_grants_the_new_audience_to_a_seeded_cli_client()
        {
            // A seeded CLI client is the ordinary deployed shape, and the one that makes the grant
            // do work rather than fall through.
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(
                fixture,
                "idplatgrant",
                new Dictionary<string, string?>
                {
                    ["TellmaIdentity:Seed:Clients:0:ClientId"] = "tellma-cli",
                    ["TellmaIdentity:Seed:Clients:0:DisplayName"] = "Tellma CLI",
                    ["TellmaIdentity:Seed:Clients:0:Kind"] = "Cli",
                    ["TellmaIdentity:Seed:Clients:0:RedirectUris:0"] = "http://127.0.0.1/callback",
                });

            await TestData.ProvisionDistributionAsync(factory, "acme");

            using IServiceScope scope = factory.Services.CreateScope();
            IOpenIddictApplicationManager applications =
                scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

            object cli = await applications.FindByClientIdAsync("tellma-cli", TestContext.Current.CancellationToken)
                ?? throw new InvalidOperationException("The CLI client was not seeded.");

            OpenIddictApplicationDescriptor descriptor = new();
            await applications.PopulateAsync(descriptor, cli, TestContext.Current.CancellationToken);

            Assert.Contains(
                Permissions.Prefixes.Resource + "https://acme.app.tellma.com",
                descriptor.Permissions);
        }

        [Fact]
        public async Task Provisioning_a_second_distribution_accumulates_audiences()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(
                fixture,
                "idplatgrant2",
                new Dictionary<string, string?>
                {
                    ["TellmaIdentity:Seed:Clients:0:ClientId"] = "tellma-cli",
                    ["TellmaIdentity:Seed:Clients:0:DisplayName"] = "Tellma CLI",
                    ["TellmaIdentity:Seed:Clients:0:Kind"] = "Cli",
                    ["TellmaIdentity:Seed:Clients:0:RedirectUris:0"] = "http://127.0.0.1/callback",
                });

            await TestData.ProvisionDistributionAsync(factory, "acme");
            await TestData.ProvisionDistributionAsync(factory, "globex");

            using IServiceScope scope = factory.Services.CreateScope();
            IOpenIddictApplicationManager applications =
                scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

            object cli = await applications.FindByClientIdAsync("tellma-cli", TestContext.Current.CancellationToken)
                ?? throw new InvalidOperationException("The CLI client was not seeded.");

            OpenIddictApplicationDescriptor descriptor = new();
            await applications.PopulateAsync(descriptor, cli, TestContext.Current.CancellationToken);

            Assert.Contains(Permissions.Prefixes.Resource + "https://acme.app.tellma.com", descriptor.Permissions);
            Assert.Contains(Permissions.Prefixes.Resource + "https://globex.app.tellma.com", descriptor.Permissions);
        }
    }
}
