// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.Provisioning;
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
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(
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

        [Fact]
        public async Task Reseeding_preserves_grants_on_a_client_stored_before_the_marker_existed()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(
                fixture, "idseedmarker", SeedConfiguration);
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            string grant = Permissions.Prefixes.Resource + "https://acme.app.tellma.com";

            using (factory.CreateClient())
            {
                // Reshape the stored CLI into what an upgrade encounters: an accumulated
                // provisioning-time grant, but no calls-distribution-APIs marker (the marker did
                // not exist when the client was first seeded).
                using IServiceScope scope = factory.Services.CreateScope();
                IOpenIddictApplicationManager applicationManager =
                    scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
                object cli = (await applicationManager.FindByClientIdAsync("tellma-cli", cancellationToken))!;
                OpenIddictApplicationDescriptor descriptor = new();
                await applicationManager.PopulateAsync(descriptor, cli, cancellationToken);
                descriptor.Permissions.Add(grant);
                descriptor.Properties.Remove(TellmaClientProperties.CallsDistributionApis);
                await applicationManager.UpdateAsync(cli, descriptor, cancellationToken);
            }

            // The first boot after the upgrade must carry the grant over, not drop it.
            using StandaloneFactory secondBoot = new();
            foreach ((string key, string? value) in factory.ConfigurationOverrides)
            {
                secondBoot.ConfigurationOverrides[key] = value;
            }

            using (secondBoot.CreateClient())
            {
                using IServiceScope scope = secondBoot.Services.CreateScope();
                IOpenIddictApplicationManager applicationManager =
                    scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
                object cli = (await applicationManager.FindByClientIdAsync("tellma-cli", cancellationToken))!;
                Assert.True(await applicationManager.HasPermissionAsync(cli, grant, cancellationToken));
            }
        }

        [Fact]
        public async Task A_distribution_seed_converges_across_starts_and_audits_only_what_it_changed()
        {
            // The distribution entry comes first, ahead of the CLI it grants its origin to: the
            // seeder registers platform clients before distributions whatever the order.
            Dictionary<string, string?> configuration = new()
            {
                ["TellmaIdentity:Seed:Clients:0:ClientId"] = "acme",
                ["TellmaIdentity:Seed:Clients:0:Kind"] = "Distribution",
                ["TellmaIdentity:Seed:Clients:0:Origin"] = "https://acme.example.com",
                ["TellmaIdentity:Seed:Clients:0:ClientSecret"] = "acme-bff-secret-0123456789abcdef",
                ["TellmaIdentity:Seed:Clients:0:ServiceClientSecret"] = "acme-svc-secret-0123456789abcdef",
                ["TellmaIdentity:Seed:Clients:1:ClientId"] = "tellma-cli",
                ["TellmaIdentity:Seed:Clients:1:Kind"] = "Cli",
                ["TellmaIdentity:Seed:Clients:1:RedirectUris:0"] = "http://127.0.0.1/callback",
            };
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idseeddist", configuration);
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            string grant = Permissions.Prefixes.Resource + "https://acme.example.com";

            using (factory.CreateClient())
            {
                using IServiceScope scope = factory.Services.CreateScope();
                IOpenIddictApplicationManager applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
                IOpenIddictScopeManager scopes = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

                object bff = (await applications.FindByClientIdAsync("acme", cancellationToken))!;
                object service = (await applications.FindByClientIdAsync("acme-svc", cancellationToken))!;
                Assert.True(await applications.ValidateClientSecretAsync(bff, "acme-bff-secret-0123456789abcdef", cancellationToken));
                Assert.True(await applications.ValidateClientSecretAsync(service, "acme-svc-secret-0123456789abcdef", cancellationToken));
                Assert.False(await applications.HasPermissionAsync(service, Permissions.GrantTypes.TokenExchange, cancellationToken));
                Assert.Contains(
                    new Uri("https://acme.example.com/signin-oidc"),
                    (await applications.GetRedirectUrisAsync(bff, cancellationToken)).Select(static uri => new Uri(uri)));

                object cli = (await applications.FindByClientIdAsync("tellma-cli", cancellationToken))!;
                Assert.True(await applications.HasPermissionAsync(cli, grant, cancellationToken));

                object api = (await scopes.FindByNameAsync(TellmaIdentityConstants.ApiScope, cancellationToken))!;
                Assert.Contains("https://acme.example.com", await scopes.GetResourcesAsync(api, cancellationToken));
            }

            Assert.Single(await DistributionSeedEventsAsync(factory));

            // An unchanged second start converges without writing, so it records nothing.
            using (StandaloneFactory unchanged = Reboot(factory))
            {
                using (unchanged.CreateClient())
                {
                    Assert.Single(await DistributionSeedEventsAsync(unchanged));
                }
            }

            // A rotated secret is applied, and audited, on the next start.
            using StandaloneFactory rotated = Reboot(factory);
            rotated.ConfigurationOverrides["TellmaIdentity:Seed:Clients:0:ClientSecret"] = "acme-bff-secret-rotated-0123456789";
            using (rotated.CreateClient())
            {
                using IServiceScope scope = rotated.Services.CreateScope();
                IOpenIddictApplicationManager applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
                object bff = (await applications.FindByClientIdAsync("acme", cancellationToken))!;
                Assert.True(await applications.ValidateClientSecretAsync(bff, "acme-bff-secret-rotated-0123456789", cancellationToken));
                Assert.False(await applications.ValidateClientSecretAsync(bff, "acme-bff-secret-0123456789abcdef", cancellationToken));

                // The CLI kept its grant through two re-seeds.
                object cli = (await applications.FindByClientIdAsync("tellma-cli", cancellationToken))!;
                Assert.True(await applications.HasPermissionAsync(cli, grant, cancellationToken));

                List<Data.Entities.AuditEvent> events = await DistributionSeedEventsAsync(rotated);
                Assert.Equal(2, events.Count);
                Assert.Contains("\"client\":\"updated\"", events[^1].DetailsJson, StringComparison.Ordinal);
                Assert.Contains("\"serviceClient\":\"unchanged\"", events[^1].DetailsJson, StringComparison.Ordinal);
                Assert.DoesNotContain("secret-rotated", events[^1].DetailsJson, StringComparison.Ordinal);
            }
        }

        /// <summary>A second host over the same store and configuration.</summary>
        private static StandaloneFactory Reboot(StandaloneFactory factory)
        {
            StandaloneFactory next = new();
            foreach ((string key, string? value) in factory.ConfigurationOverrides)
            {
                next.ConfigurationOverrides[key] = value;
            }

            return next;
        }

        /// <summary>The distribution-seed audit events in the host's store, oldest first.</summary>
        private static async Task<List<Data.Entities.AuditEvent>> DistributionSeedEventsAsync(StandaloneFactory factory)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            Data.TellmaIdentityDbContext context = scope.ServiceProvider.GetRequiredService<Data.TellmaIdentityDbContext>();
            return await context.Set<Data.Entities.AuditEvent>()
                .Where(static e => e.Action == Services.Audit.AuditActions.DistributionSeedApplied)
                .OrderBy(static e => e.Id)
                .ToListAsync(TestContext.Current.CancellationToken);
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
