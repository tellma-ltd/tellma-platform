// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Tellma.Identity.IntegrationTests.Infrastructure;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tellma.Identity.IntegrationTests
{
    /// <summary>
    ///     Startup seeding is complete and idempotent: the scope catalog, the platform clients
    ///     from configuration, and the dev admin (Development only) all exist after boot, and a
    ///     second boot against the same store changes nothing.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class SeedingTests(SqlServerFixture fixture)
    {
        private static readonly Dictionary<string, string?> SeedConfiguration = new()
        {
            ["TellmaIdentity:Seed:DevAdmin:Enabled"] = "true",
            ["TellmaIdentity:Seed:Clients:0:ClientId"] = "tellma-cli",
            ["TellmaIdentity:Seed:Clients:0:DisplayName"] = "Tellma CLI",
            ["TellmaIdentity:Seed:Clients:0:Kind"] = "Cli",
            ["TellmaIdentity:Seed:Clients:0:RedirectUris:0"] = "http://127.0.0.1/callback",
            ["TellmaIdentity:Seed:Clients:1:ClientId"] = "tellma-control-plane",
            ["TellmaIdentity:Seed:Clients:1:DisplayName"] = "Control Plane",
            ["TellmaIdentity:Seed:Clients:1:Kind"] = "ControlPlane",
            ["TellmaIdentity:Seed:Clients:1:ClientSecret"] = "control-plane-test-secret-0123456789",
        };

        [Fact]
        public async Task Seeding_creates_scopes_platform_clients_and_dev_admin_idempotently()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(
                fixture, "idseed", SeedConfiguration);

            // First boot seeds everything.
            using (factory.CreateClient())
            {
                await AssertSeededAsync(factory);
            }

            // A second boot against the same store must be a no-op, not a failure.
            using StandaloneFactory secondBoot = new();
            foreach ((string key, string? value) in factory.ConfigurationOverrides)
            {
                secondBoot.ConfigurationOverrides[key] = value;
            }

            using (secondBoot.CreateClient())
            {
                await AssertSeededAsync(secondBoot);
            }
        }

        /// <summary>Asserts the seeded catalog is present and correctly shaped.</summary>
        private static async Task AssertSeededAsync(StandaloneFactory factory)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            IOpenIddictScopeManager scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
            IOpenIddictApplicationManager applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

            CancellationToken cancellationToken = TestContext.Current.CancellationToken;

            // The fixed scope catalog with its platform audiences.
            foreach (string name in (string[])["tellma_api", "tellma_identity", "tellma_control_plane"])
            {
                Assert.NotNull(await scopeManager.FindByNameAsync(name, cancellationToken));
            }

            object? identityScope = await scopeManager.FindByNameAsync("tellma_identity", cancellationToken);
            OpenIddictScopeDescriptor identityScopeDescriptor = new();
            await scopeManager.PopulateAsync(identityScopeDescriptor, identityScope!, cancellationToken);
            Assert.Contains("http://localhost", identityScopeDescriptor.Resources);

            // The CLI: public, native (portless loopback), PKCE-required, device-grant capable.
            object? cli = await applicationManager.FindByClientIdAsync("tellma-cli", cancellationToken);
            Assert.NotNull(cli);
            Assert.Equal(ClientTypes.Public, await applicationManager.GetClientTypeAsync(cli, cancellationToken));
            Assert.True(await applicationManager.HasPermissionAsync(cli, Permissions.GrantTypes.DeviceCode, cancellationToken));
            Assert.True(await applicationManager.HasRequirementAsync(cli, Requirements.Features.ProofKeyForCodeExchange, cancellationToken));

            // The control plane: confidential client-credentials caller.
            object? controlPlane = await applicationManager.FindByClientIdAsync("tellma-control-plane", cancellationToken);
            Assert.NotNull(controlPlane);
            Assert.Equal(ClientTypes.Confidential, await applicationManager.GetClientTypeAsync(controlPlane, cancellationToken));
            Assert.True(await applicationManager.HasPermissionAsync(controlPlane, Permissions.GrantTypes.ClientCredentials, cancellationToken));

            // The dev admin with its fixed subject (Development environment).
            Data.TellmaIdentityDbContext context = scope.ServiceProvider.GetRequiredService<Data.TellmaIdentityDbContext>();
            Data.TellmaIdentityUser? admin = await context.Users.FindAsync(
                ["00000000-0000-0000-0000-000000000001"], cancellationToken);
            Assert.NotNull(admin);
            Assert.Equal("admin@localhost", admin.Email);
        }
    }
}
