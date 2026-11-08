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

        /// <summary>One change per member the convergence compares, each enough to make it rewrite the row.</summary>
        private static readonly Dictionary<string, Action<OpenIddictApplicationDescriptor>> Changes = new()
        {
            ["ClientId"] = static d => d.ClientId = "other",
            ["ClientSecret"] = static d => d.ClientSecret = "other-hash",
            ["ClientType"] = static d => d.ClientType = ClientTypes.Public,
            ["ApplicationType"] = static d => d.ApplicationType = ApplicationTypes.Native,
            ["ConsentType"] = static d => d.ConsentType = ConsentTypes.Explicit,
            ["DisplayName"] = static d => d.DisplayName = "other",
            ["DisplayNames"] = static d => d.DisplayNames.Clear(),
            ["JsonWebKeySet"] = static d => d.JsonWebKeySet = new Microsoft.IdentityModel.Tokens.JsonWebKeySet(),
            ["Permissions"] = static d => d.Permissions.Remove(Permissions.Endpoints.Revocation),
            ["Requirements"] = static d => d.Requirements.Remove(Requirements.Features.PushedAuthorizationRequests),
            ["RedirectUris"] = static d => d.RedirectUris.Add(new Uri(Origin + "/other")),
            ["PostLogoutRedirectUris"] = static d => d.PostLogoutRedirectUris.Clear(),
            ["Properties"] = static d => d.Properties.Remove(TellmaClientProperties.BackchannelLogoutUri),
            ["PropertyValue"] = static d => TellmaClientProperties.Set(
                d.Properties, TellmaClientProperties.BackchannelLogoutUri, Origin + "/other"),
            ["Settings"] = static d => d.Settings.Clear(),
            ["SettingValue"] = static d => d.Settings["tkn_lft:act"] = "00:20:00",
        };

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

        [Fact]
        public async Task A_changed_back_channel_endpoint_alone_rewrites_the_bff_client()
        {
            await ConvergeAsync(Entry());
            TellmaIdentitySeedClientOptions moved = Entry();
            moved.BackchannelLogoutUri = Origin + "/moved/logout";

            DistributionConvergence convergence = await ConvergeAsync(moved);

            Assert.Equal(ClientConvergence.Updated, convergence.Client);
            Assert.Equal(ClientConvergence.Unchanged, convergence.ServiceClient);
            Assert.Equal(
                Origin + "/moved/logout",
                TellmaClientProperties.Get(Applications.Find("acme")!.Properties, TellmaClientProperties.BackchannelLogoutUri));
        }

        [Fact]
        public async Task A_requirement_removed_by_hand_is_restored()
        {
            await ConvergeAsync(Entry());

            // An operator dropped pushed authorization requests from the BFF client: a downgrade.
            InMemoryApplication bff = Applications.Find("acme")!;
            bff.Requirements = bff.Requirements.Remove(Requirements.Features.PushedAuthorizationRequests);

            DistributionConvergence convergence = await ConvergeAsync(Entry());

            Assert.Equal(ClientConvergence.Updated, convergence.Client);
            Assert.Contains(Requirements.Features.PushedAuthorizationRequests, Applications.Find("acme")!.Requirements);
        }

        [Theory]
        [MemberData(nameof(SingleMemberChanges))]
        public void Registrations_that_differ_in_one_member_are_not_the_same(string member)
        {
            OpenIddictApplicationDescriptor left = Descriptor();
            OpenIddictApplicationDescriptor right = Descriptor();
            Assert.True(ClientRegistrationConverger.HaveSameRegistration(left, right));

            Change(right, member);

            Assert.False(ClientRegistrationConverger.HaveSameRegistration(left, right), member);
            Assert.False(ClientRegistrationConverger.HaveSameRegistration(right, left), member);
        }

        [Fact]
        public void Registrations_that_list_the_same_members_in_another_order_are_the_same()
        {
            OpenIddictApplicationDescriptor left = Descriptor();
            OpenIddictApplicationDescriptor right = new()
            {
                ClientId = left.ClientId,
                ClientSecret = left.ClientSecret,
                ClientType = left.ClientType,
                ApplicationType = left.ApplicationType,
                ConsentType = left.ConsentType,
                DisplayName = left.DisplayName,
            };
            right.Permissions.UnionWith(left.Permissions.Reverse());
            right.Requirements.UnionWith(left.Requirements.Reverse());
            right.RedirectUris.UnionWith(left.RedirectUris.Reverse());
            right.PostLogoutRedirectUris.UnionWith(left.PostLogoutRedirectUris.Reverse());
            foreach ((string key, JsonElement value) in left.Properties.Reverse())
            {
                right.Properties[key] = value;
            }

            foreach ((string key, string value) in left.Settings.Reverse())
            {
                right.Settings[key] = value;
            }

            foreach ((System.Globalization.CultureInfo culture, string name) in left.DisplayNames)
            {
                right.DisplayNames[culture] = name;
            }

            Assert.True(ClientRegistrationConverger.HaveSameRegistration(left, right));
        }

        [Fact]
        public async Task An_update_lost_to_a_concurrent_writer_is_converged_again()
        {
            await ConvergeAsync(Entry());
            int failures = 0;
            Applications.BeforeUpdate = _ => failures++ == 0
                ? new OpenIddictExceptions.ConcurrencyException("Another instance updated the row.")
                : null;
            TellmaIdentitySeedClientOptions rotated = Entry();
            rotated.ClientSecret = "bff-secret-rotated-0123456789abcdef";

            DistributionConvergence convergence = await ConvergeAsync(rotated);

            Assert.Equal(2, failures);
            Assert.Equal(ClientConvergence.Updated, convergence.Client);
            Assert.True(await ValidateSecretAsync("acme", "bff-secret-rotated-0123456789abcdef"));
        }

        [Fact]
        public async Task A_create_lost_to_a_concurrent_writer_is_converged_against_the_winners_row()
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            bool raced = false;

            // Just before this instance creates the BFF client, another instance creates it with
            // the same values, and this create then fails as the manager's validation would.
            Applications.BeforeCreate = application =>
            {
                if (raced || application.ClientId != "acme")
                {
                    return null;
                }

                raced = true;
                using IServiceScope scope = _services.CreateScope();
                scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>()
                    .CreateAsync(
                        ClientDescriptorFactory.Distribution(
                            "acme", new Uri(Origin), new Uri(Origin + "/bff/backchannel-logout"), Entry().ClientSecret!),
                        cancellationToken)
                    .AsTask().GetAwaiter().GetResult();
                return new OpenIddictExceptions.ValidationException(
                    "The client identifier is already in use.", []);
            };

            DistributionConvergence convergence = await ConvergeAsync(Entry());

            Assert.True(raced);
            Assert.Single(Applications.Rows, static row => row.ClientId == "acme");
            Assert.Equal(ClientConvergence.Unchanged, convergence.Client);
            Assert.Equal(ClientConvergence.Created, convergence.ServiceClient);
            Assert.True(await ValidateSecretAsync("acme", Entry().ClientSecret!));
        }

        [Fact]
        public async Task A_conflict_that_persists_past_the_bounded_attempts_is_thrown()
        {
            await ConvergeAsync(Entry());
            int failures = 0;
            Applications.BeforeUpdate = _ =>
            {
                failures++;
                return new OpenIddictExceptions.ConcurrencyException("Another instance updated the row.");
            };
            TellmaIdentitySeedClientOptions rotated = Entry();
            rotated.ClientSecret = "bff-secret-rotated-0123456789abcdef";

            await Assert.ThrowsAsync<OpenIddictExceptions.ConcurrencyException>(() => ConvergeAsync(rotated));
            Assert.Equal(ClientRegistrationConverger.MaxAttemptsPerWrite, failures);
        }

        [Fact]
        public async Task Every_write_point_losing_its_writes_up_to_the_bound_still_converges()
        {
            // A first start with two seeded clients calling distribution APIs has five write points:
            // two creates, the audience append and two grants. Each loses all but its last allowed
            // attempt, many more lost writes in total than any one point's bound.
            await SeedPlatformClientsAsync(_services);
            await SeedNativeClientAsync(_services);
            int losses = 0;
            Dictionary<string, int> applicationLosses = [];
            Applications.BeforeCreate = application => Lose(applicationLosses, "create:" + application.ClientId, ref losses);
            Applications.BeforeUpdate = application => Lose(applicationLosses, "update:" + application.ClientId, ref losses);
            Dictionary<string, int> scopeLosses = [];
            Scopes.BeforeCreate = scope => Lose(scopeLosses, "create:" + scope.Name, ref losses);
            Scopes.BeforeUpdate = scope => Lose(scopeLosses, "update:" + scope.Name, ref losses);

            DistributionConvergence convergence = await ConvergeAsync(Entry());

            int bound = ClientRegistrationConverger.MaxAttemptsPerWrite - 1;
            Assert.Equal(5 * bound, losses);
            Assert.Equal(new DistributionConvergence(ClientConvergence.Created, ClientConvergence.Created, true, 2), convergence);
            Assert.Single(Applications.Rows, static row => row.ClientId == "acme");
            Assert.Single(Applications.Rows, static row => row.ClientId == "acme-svc");
            Assert.Contains(Origin, Scopes.Find(TellmaIdentityConstants.ApiScope)!.Resources);
            Assert.Contains(Permissions.Prefixes.Resource + Origin, Applications.Find("tellma-cli")!.Permissions);
            Assert.Contains(Permissions.Prefixes.Resource + Origin, Applications.Find("tellma-native")!.Permissions);
            Assert.True(await ValidateSecretAsync("acme", Entry().ClientSecret!));
        }

        [Fact]
        public async Task A_refused_create_that_no_concurrent_writer_explains_is_thrown_at_once()
        {
            int failures = 0;
            Applications.BeforeCreate = _ =>
            {
                failures++;
                return new OpenIddictExceptions.ValidationException("The descriptor is invalid.", []);
            };

            await Assert.ThrowsAsync<OpenIddictExceptions.ValidationException>(() => ConvergeAsync(Entry()));
            Assert.Equal(1, failures);
        }

        [Fact]
        public async Task A_failure_that_is_not_a_conflict_is_thrown_at_once()
        {
            await ConvergeAsync(Entry());
            int failures = 0;
            Applications.BeforeUpdate = _ =>
            {
                failures++;
                return new InvalidOperationException("The store is unavailable.");
            };
            TellmaIdentitySeedClientOptions rotated = Entry();
            rotated.ClientSecret = "bff-secret-rotated-0123456789abcdef";

            await Assert.ThrowsAsync<InvalidOperationException>(() => ConvergeAsync(rotated));
            Assert.Equal(1, failures);
        }

        [Fact]
        public async Task An_unchanged_seeded_platform_client_is_left_untouched_and_keeps_its_grants()
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            TellmaIdentitySeedClientOptions cli = new()
            {
                ClientId = "tellma-cli",
                Kind = TellmaIdentitySeedClientKind.Cli,
                RedirectUris = { "http://127.0.0.1/callback" },
            };
            using IServiceScope scope = _services.CreateScope();
            ClientRegistrationConverger converger = scope.ServiceProvider.GetRequiredService<ClientRegistrationConverger>();

            Assert.Equal(ClientConvergence.Created, await converger.ConvergeSeededClientAsync(cli, cancellationToken));
            await ConvergeAsync(Entry());
            int updates = Applications.Updates;

            Assert.Equal(ClientConvergence.Unchanged, await converger.ConvergeSeededClientAsync(cli, cancellationToken));
            Assert.Equal(updates, Applications.Updates);
            Assert.Contains(Permissions.Prefixes.Resource + Origin, Applications.Find("tellma-cli")!.Permissions);

            cli.DisplayName = "Tellma command line";
            Assert.Equal(ClientConvergence.Updated, await converger.ConvergeSeededClientAsync(cli, cancellationToken));
            Assert.Contains(Permissions.Prefixes.Resource + Origin, Applications.Find("tellma-cli")!.Permissions);
        }

        /// <summary>Every member a single-member change test flips, by name.</summary>
        public static TheoryData<string> SingleMemberChanges => [.. Changes.Keys];

        /// <summary>A descriptor with every compared member set.</summary>
        private static OpenIddictApplicationDescriptor Descriptor()
        {
            OpenIddictApplicationDescriptor descriptor = ClientDescriptorFactory.Distribution(
                "acme", new Uri(Origin), new Uri(Origin + "/bff/backchannel-logout"), "stored-hash");
            descriptor.DisplayNames[System.Globalization.CultureInfo.GetCultureInfo("ar")] = "أكمي";
            descriptor.Settings["tkn_lft:act"] = "00:10:00";
            return descriptor;
        }

        /// <summary>Changes one named member of a descriptor.</summary>
        private static void Change(OpenIddictApplicationDescriptor descriptor, string member)
        {
            Changes[member](descriptor);
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
            services.AddScoped<ClientRegistrationConverger>();
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

        /// <summary>
        ///     Loses a write at one point with a concurrency conflict until that point has lost all
        ///     but its last allowed attempt, then lets it through.
        /// </summary>
        private static OpenIddictExceptions.ConcurrencyException? Lose(Dictionary<string, int> perPoint, string point, ref int losses)
        {
            int lost = perPoint.GetValueOrDefault(point);
            if (lost >= ClientRegistrationConverger.MaxAttemptsPerWrite - 1)
            {
                return null;
            }

            perPoint[point] = lost + 1;
            losses++;
            return new OpenIddictExceptions.ConcurrencyException($"Another instance wrote {point}.");
        }

        /// <summary>Seeds a native app, a second client that calls distribution APIs.</summary>
        private static async Task SeedNativeClientAsync(ServiceProvider services)
        {
            using IServiceScope scope = services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>().CreateAsync(
                ClientDescriptorFactory.SeededClient(
                    new TellmaIdentityOptions { Issuer = new Uri(Issuer) },
                    new TellmaIdentitySeedClientOptions
                    {
                        ClientId = "tellma-native",
                        Kind = TellmaIdentitySeedClientKind.Native,
                        RedirectUris = { "http://127.0.0.1/native" },
                    }),
                TestContext.Current.CancellationToken);
        }

        /// <summary>Converges an entry the way the seeder does.</summary>
        private async Task<DistributionConvergence> ConvergeAsync(TellmaIdentitySeedClientOptions entry)
        {
            using IServiceScope scope = _services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ClientRegistrationConverger>().ConvergeDistributionAsync(
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
            Assert.Equal(expected.Settings.OrderBy(static pair => pair.Key, StringComparer.Ordinal), actual.Settings.OrderBy(static pair => pair.Key, StringComparer.Ordinal));
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
