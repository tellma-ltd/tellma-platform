// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using System.Text.Json;
using Tellma.Identity.Options;
using Tellma.Identity.Services.Audit;
using Tellma.Identity.Services.Provisioning;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tellma.Identity.Tests.Provisioning
{
    /// <summary>
    ///     A <c>Distribution</c> seed entry registers the same two applications onboarding does,
    ///     converges on every start without rewriting what already matches, applies a rotated
    ///     secret or a moved origin, and never deletes an application. Runs OpenIddict's real
    ///     managers over in-memory stores, so secrets are hashed and descriptors validated exactly
    ///     as against SQL Server.
    /// </summary>
    public sealed class DistributionSeedTests : IDisposable
    {
        private const string Issuer = "https://identity.tellma.com";
        private const string Origin = "https://acme.example.com";

        private readonly ServiceProvider _services = BuildServices();

        [Fact]
        public async Task A_seed_entry_registers_the_applications_onboarding_registers()
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;

            // Onboarding, on its own store, with token exchange off as the seed kind has it and the
            // back-channel endpoint the entry defaults to.
            using ServiceProvider onboardingServices = BuildServices();
            await SeedPlatformClientsAsync(onboardingServices);
            using (IServiceScope scope = onboardingServices.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IClientProvisioningService>().CreateDistributionAsync(
                    "acme", new Uri(Origin), new Uri(Origin + "/bff/backchannel-logout"), allowTokenExchange: false, cancellationToken);
            }

            await SeedPlatformClientsAsync(_services);
            await ConvergeAsync(Entry());

            InMemoryApplicationStore onboarded = onboardingServices.GetRequiredService<InMemoryApplicationStore>();
            InMemoryApplicationStore seeded = _services.GetRequiredService<InMemoryApplicationStore>();
            foreach (string clientId in (string[])["acme", "acme-svc", "tellma-cli", "tellma-control-plane"])
            {
                AssertSameRegistration(onboarded.Find(clientId)!, seeded.Find(clientId)!);
            }

            Assert.Equal(
                onboardingServices.GetRequiredService<InMemoryScopeStore>().Find(TellmaIdentityConstants.ApiScope)!.Resources,
                _services.GetRequiredService<InMemoryScopeStore>().Find(TellmaIdentityConstants.ApiScope)!.Resources);
        }

        [Fact]
        public async Task The_backend_client_holds_the_issuer_origin_and_no_token_exchange()
        {
            await ConvergeAsync(Entry());

            InMemoryApplication service = Applications.Find("acme-svc")!;
            Assert.Contains(Permissions.Prefixes.Resource + Issuer, service.Permissions);
            Assert.Contains(Permissions.Prefixes.Resource + Origin, service.Permissions);
            Assert.Contains(Permissions.GrantTypes.ClientCredentials, service.Permissions);
            Assert.DoesNotContain(Permissions.GrantTypes.TokenExchange, service.Permissions);
            Assert.Equal(ClientTypes.Confidential, service.ClientType);
        }

        [Fact]
        public async Task The_bff_client_takes_its_redirects_and_back_channel_endpoint_from_the_origin()
        {
            await ConvergeAsync(Entry());

            InMemoryApplication bff = Applications.Find("acme")!;
            Assert.Equal([Origin + "/signin-oidc"], bff.RedirectUris);
            Assert.Equal([Origin + "/signout-callback-oidc"], bff.PostLogoutRedirectUris);
            Assert.Equal(
                Origin + "/bff/backchannel-logout",
                TellmaClientProperties.Get(bff.Properties, TellmaClientProperties.BackchannelLogoutUri));
            Assert.Contains(Permissions.Prefixes.Resource + Origin, bff.Permissions);
            Assert.Equal(ClientTypes.Confidential, bff.ClientType);
        }

        [Fact]
        public async Task An_explicit_back_channel_endpoint_is_kept()
        {
            TellmaIdentitySeedClientOptions entry = Entry();
            entry.BackchannelLogoutUri = Origin + "/custom/logout";

            await ConvergeAsync(entry);

            Assert.Equal(
                Origin + "/custom/logout",
                TellmaClientProperties.Get(Applications.Find("acme")!.Properties, TellmaClientProperties.BackchannelLogoutUri));
        }

        [Fact]
        public async Task The_origin_joins_the_api_audiences_and_reaches_only_clients_that_call_distribution_apis()
        {
            await SeedPlatformClientsAsync(_services);

            DistributionConvergence convergence = await ConvergeAsync(Entry());

            Assert.True(convergence.AudienceAdded);
            Assert.Equal(1, convergence.GrantsAdded);
            Assert.Contains(Origin, Scopes.Find(TellmaIdentityConstants.ApiScope)!.Resources);
            Assert.Contains(Permissions.Prefixes.Resource + Origin, Applications.Find("tellma-cli")!.Permissions);
            Assert.DoesNotContain(Permissions.Prefixes.Resource + Origin, Applications.Find("tellma-control-plane")!.Permissions);
        }

        [Fact]
        public async Task Converging_an_unchanged_entry_again_writes_nothing()
        {
            await SeedPlatformClientsAsync(_services);
            DistributionConvergence first = await ConvergeAsync(Entry());
            string bffHash = Applications.Find("acme")!.ClientSecret!;
            string serviceHash = Applications.Find("acme-svc")!.ClientSecret!;
            (int creates, int updates, int scopeUpdates) = (Applications.Creates, Applications.Updates, Scopes.Updates);

            DistributionConvergence second = await ConvergeAsync(Entry());

            Assert.True(first.Changed);
            Assert.Equal(new DistributionConvergence(ClientConvergence.Unchanged, ClientConvergence.Unchanged, false, 0), second);
            Assert.False(second.Changed);
            Assert.Equal((creates, updates, scopeUpdates), (Applications.Creates, Applications.Updates, Scopes.Updates));

            // Not even a fresh salt: the stored hashes are the ones the first run wrote.
            Assert.Equal(bffHash, Applications.Find("acme")!.ClientSecret);
            Assert.Equal(serviceHash, Applications.Find("acme-svc")!.ClientSecret);
        }

        [Fact]
        public async Task A_rotated_secret_is_applied_to_its_client_alone()
        {
            await ConvergeAsync(Entry());
            TellmaIdentitySeedClientOptions rotated = Entry();
            rotated.ClientSecret = "bff-secret-rotated-0123456789abcdef";

            DistributionConvergence convergence = await ConvergeAsync(rotated);

            Assert.Equal(ClientConvergence.Updated, convergence.Client);
            Assert.Equal(ClientConvergence.Unchanged, convergence.ServiceClient);
            Assert.True(await ValidateSecretAsync("acme", "bff-secret-rotated-0123456789abcdef"));
            Assert.False(await ValidateSecretAsync("acme", Entry().ClientSecret!));
            Assert.True(await ValidateSecretAsync("acme-svc", Entry().ServiceClientSecret!));
        }

        [Fact]
        public async Task A_rotated_service_secret_is_applied_to_the_backend_client_alone()
        {
            await ConvergeAsync(Entry());
            TellmaIdentitySeedClientOptions rotated = Entry();
            rotated.ServiceClientSecret = "svc-secret-rotated-0123456789abcdef";

            DistributionConvergence convergence = await ConvergeAsync(rotated);

            Assert.Equal(ClientConvergence.Unchanged, convergence.Client);
            Assert.Equal(ClientConvergence.Updated, convergence.ServiceClient);
            Assert.True(await ValidateSecretAsync("acme-svc", "svc-secret-rotated-0123456789abcdef"));
            Assert.False(await ValidateSecretAsync("acme-svc", Entry().ServiceClientSecret!));
        }

        [Fact]
        public async Task A_moved_origin_rewrites_both_clients_and_their_redirects()
        {
            await SeedPlatformClientsAsync(_services);
            await ConvergeAsync(Entry());
            TellmaIdentitySeedClientOptions moved = Entry();
            moved.Origin = "https://acme.tellma.example";

            DistributionConvergence convergence = await ConvergeAsync(moved);

            Assert.Equal(ClientConvergence.Updated, convergence.Client);
            Assert.Equal(ClientConvergence.Updated, convergence.ServiceClient);
            Assert.True(convergence.AudienceAdded);
            InMemoryApplication bff = Applications.Find("acme")!;
            Assert.Equal(["https://acme.tellma.example/signin-oidc"], bff.RedirectUris);
            Assert.Contains(Permissions.Prefixes.Resource + "https://acme.tellma.example", bff.Permissions);
            Assert.DoesNotContain(Permissions.Prefixes.Resource + Origin, bff.Permissions);
            Assert.Contains(Permissions.Prefixes.Resource + "https://acme.tellma.example", Applications.Find("tellma-cli")!.Permissions);

            // The secret did not change, so the update kept the stored hash rather than re-hashing it.
            Assert.True(await ValidateSecretAsync("acme", Entry().ClientSecret!));
        }

        [Fact]
        public async Task A_drifted_registration_is_converged_back_whoever_created_it()
        {
            await ConvergeAsync(Entry());

            // An operator widened the BFF client by hand.
            InMemoryApplication bff = Applications.Find("acme")!;
            bff.Permissions = bff.Permissions.Add(Permissions.GrantTypes.ClientCredentials);

            DistributionConvergence convergence = await ConvergeAsync(Entry());

            Assert.Equal(ClientConvergence.Updated, convergence.Client);
            Assert.DoesNotContain(Permissions.GrantTypes.ClientCredentials, Applications.Find("acme")!.Permissions);
        }

        [Fact]
        public async Task Seeding_never_deletes_an_application()
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            await SeedPlatformClientsAsync(_services);

            // An application another path registered: onboarding of a second distribution.
            using (IServiceScope scope = _services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IClientProvisioningService>().CreateDistributionAsync(
                    "other", new Uri("https://other.example.com"), null, allowTokenExchange: true, cancellationToken);
            }

            int before = Applications.Rows.Count;
            await ConvergeAsync(Entry());
            TellmaIdentitySeedClientOptions moved = Entry();
            moved.Origin = "https://acme.tellma.example";
            moved.ClientSecret = "bff-secret-rotated-0123456789abcdef";
            await ConvergeAsync(moved);

            Assert.Equal(0, Applications.Deletes);
            Assert.Equal(before + 2, Applications.Rows.Count);
            Assert.NotNull(Applications.Find("other"));
            Assert.NotNull(Applications.Find("other-svc"));
        }

        [Fact]
        public void A_seed_entry_without_its_secrets_is_never_given_generated_ones()
        {
            TellmaIdentitySeedClientOptions entry = Entry();
            entry.ServiceClientSecret = null;

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => ClientDescriptorFactory.SeededDistribution(entry));
            Assert.Contains("'acme'", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_seed_entry_turns_token_exchange_off()
        {
            Assert.False(ClientDescriptorFactory.SeededDistribution(Entry()).AllowTokenExchange);
        }

        [Fact]
        public void A_distribution_entry_is_not_a_single_seeded_client()
        {
            TellmaIdentityOptions options = new() { Issuer = new Uri(Issuer) };

            Assert.Throws<ArgumentException>(() => ClientDescriptorFactory.SeededClient(options, Entry()));
        }

        [Fact]
        public void A_registration_never_prints_its_secrets()
        {
            DistributionClientRegistration registration = ClientDescriptorFactory.SeededDistribution(Entry());

            Assert.DoesNotContain(Entry().ClientSecret!, registration.ToString(), StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _services.Dispose();
        }

        /// <summary>The application rows of the test's store.</summary>
        private InMemoryApplicationStore Applications => _services.GetRequiredService<InMemoryApplicationStore>();

        /// <summary>The scope rows of the test's store.</summary>
        private InMemoryScopeStore Scopes => _services.GetRequiredService<InMemoryScopeStore>();

        /// <summary>The entry under test, as a deployment's configuration would bind it.</summary>
        private static TellmaIdentitySeedClientOptions Entry()
        {
            return new TellmaIdentitySeedClientOptions
            {
                ClientId = "acme",
                Kind = TellmaIdentitySeedClientKind.Distribution,
                Origin = Origin,
                ClientSecret = "bff-secret-0123456789abcdef0123456789",
                ServiceClientSecret = "svc-secret-0123456789abcdef0123456789",
            };
        }

        /// <summary>The provisioning service over the in-memory stores, with a recording audit log.</summary>
        private static ServiceProvider BuildServices()
        {
            ServiceCollection services = new();
            services.AddInMemoryOpenIddict();
            services.AddSingleton<IAuditLogger, RecordingAuditLogger>();
            services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new TellmaIdentityOptions { Issuer = new Uri(Issuer) }));
            services.AddSingleton(TimeProvider.System);
            services.AddScoped<IClientProvisioningService, ClientProvisioningService>();
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }

        /// <summary>Seeds a CLI (which calls distribution APIs) and a control plane (which does not).</summary>
        private static async Task SeedPlatformClientsAsync(ServiceProvider services)
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            TellmaIdentityOptions options = new() { Issuer = new Uri(Issuer) };
            using IServiceScope scope = services.CreateScope();
            IOpenIddictApplicationManager manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            await manager.CreateAsync(
                ClientDescriptorFactory.SeededClient(
                    options,
                    new TellmaIdentitySeedClientOptions
                    {
                        ClientId = "tellma-cli",
                        Kind = TellmaIdentitySeedClientKind.Cli,
                        RedirectUris = { "http://127.0.0.1/callback" },
                    }),
                cancellationToken);
            await manager.CreateAsync(
                ClientDescriptorFactory.SeededClient(
                    options,
                    new TellmaIdentitySeedClientOptions
                    {
                        ClientId = "tellma-control-plane",
                        Kind = TellmaIdentitySeedClientKind.ControlPlane,
                        ClientSecret = "control-plane-secret-0123456789abcdef",
                    }),
                cancellationToken);
        }

        /// <summary>Converges an entry the way the seeder does.</summary>
        private async Task<DistributionConvergence> ConvergeAsync(TellmaIdentitySeedClientOptions entry)
        {
            using IServiceScope scope = _services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IClientProvisioningService>().ConvergeDistributionAsync(
                ClientDescriptorFactory.SeededDistribution(entry), TestContext.Current.CancellationToken);
        }

        /// <summary>Whether the stored client accepts the secret.</summary>
        private async Task<bool> ValidateSecretAsync(string clientId, string secret)
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            using IServiceScope scope = _services.CreateScope();
            IOpenIddictApplicationManager manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            object application = (await manager.FindByClientIdAsync(clientId, cancellationToken))!;
            return await manager.ValidateClientSecretAsync(application, secret, cancellationToken);
        }

        /// <summary>Asserts two stored applications register the same client, secrets aside.</summary>
        private static void AssertSameRegistration(InMemoryApplication expected, InMemoryApplication actual)
        {
            Assert.Equal(expected.ClientId, actual.ClientId);
            Assert.Equal(expected.ClientType, actual.ClientType);
            Assert.Equal(expected.ApplicationType, actual.ApplicationType);
            Assert.Equal(expected.ConsentType, actual.ConsentType);
            Assert.Equal(expected.DisplayName, actual.DisplayName);
            Assert.Equal(expected.Permissions.Order(StringComparer.Ordinal), actual.Permissions.Order(StringComparer.Ordinal));
            Assert.Equal(expected.Requirements.Order(StringComparer.Ordinal), actual.Requirements.Order(StringComparer.Ordinal));
            Assert.Equal(expected.RedirectUris.Order(StringComparer.Ordinal), actual.RedirectUris.Order(StringComparer.Ordinal));
            Assert.Equal(
                expected.PostLogoutRedirectUris.Order(StringComparer.Ordinal),
                actual.PostLogoutRedirectUris.Order(StringComparer.Ordinal));
            Assert.Equal(expected.Properties.Keys.Order(StringComparer.Ordinal), actual.Properties.Keys.Order(StringComparer.Ordinal));
            foreach ((string key, JsonElement value) in expected.Properties)
            {
                Assert.True(JsonElement.DeepEquals(value, actual.Properties[key]), $"Property '{key}' differs.");
            }
        }

        /// <summary>An audit log that keeps what it is given.</summary>
        private sealed class RecordingAuditLogger : IAuditLogger
        {
            /// <summary>Every recorded entry.</summary>
            public List<AuditEventEntry> Entries { get; } = [];

            /// <inheritdoc />
            public Task LogAsync(AuditEventEntry entry, CancellationToken cancellationToken = default)
            {
                Entries.Add(entry);
                return Task.CompletedTask;
            }
        }
    }
}
