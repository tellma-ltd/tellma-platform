// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Options;
using Tellma.Identity.Services.Provisioning;

namespace Tellma.Identity.IntegrationTests
{
    /// <summary>
    ///     Instances that start together converge the same registrations at once — every instance
    ///     of a deployment restarts when a secret is rotated. Each must end successfully, and the
    ///     store must end with one registration per client holding the values they all converged
    ///     on: a write lost to a concurrent writer is converged again, never duplicated and never
    ///     left half-applied.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class ConcurrentConvergenceTests(SqlServerFixture fixture)
    {
        /// <summary>How many instances converge at once.</summary>
        private const int Instances = 4;

        /// <summary>How many rotations each test runs, each a fresh race.</summary>
        private const int Rounds = 5;

        private const string Origin = "https://race.example.com";

        [Fact]
        public async Task Instances_converging_one_distribution_at_once_all_succeed_with_one_registration()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idrace");
            using (factory.CreateClient())
            {
                // Started: the store exists, migrated and seeded.
            }

            string connectionString = factory.ConfigurationOverrides["TellmaIdentity:ConnectionString"]!;
            for (int round = 0; round < Rounds; round++)
            {
                // The first round races the creates, every later one races the updates a rotation makes.
                DistributionClientRegistration registration = new(
                    "race",
                    new Uri(Origin),
                    new Uri(Origin + "/bff/backchannel-logout"),
                    $"race-bff-secret-{round}-0123456789abcdef",
                    $"race-svc-secret-{round}-0123456789abcdef",
                    allowTokenExchange: false);

                DistributionConvergence[] outcomes = await Task.WhenAll(Enumerable.Range(0, Instances).Select(_ => Task.Run(
                    async () =>
                    {
                        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
                        return await scope.ServiceProvider.GetRequiredService<ClientRegistrationConverger>()
                            .ConvergeDistributionAsync(registration, TestContext.Current.CancellationToken);
                    },
                    TestContext.Current.CancellationToken)));

                // Every client was written by at least one instance, and the store holds exactly one
                // row per client, carrying this round's secrets.
                Assert.Contains(outcomes, static o => o.Client != ClientConvergence.Unchanged);
                Assert.Contains(outcomes, static o => o.ServiceClient != ClientConvergence.Unchanged);
                Assert.Equal(1, await CountAsync(connectionString, "race"));
                Assert.Equal(1, await CountAsync(connectionString, "race-svc"));
                Assert.True(await ValidateSecretAsync(factory, "race", registration.ClientSecret));
                Assert.True(await ValidateSecretAsync(factory, "race-svc", registration.ServiceClientSecret));
            }

            using IServiceScope check = factory.Services.CreateScope();
            IOpenIddictScopeManager scopes = check.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
            object api = (await scopes.FindByNameAsync(TellmaIdentityConstants.ApiScope, TestContext.Current.CancellationToken))!;
            Assert.Single(await scopes.GetResourcesAsync(api, TestContext.Current.CancellationToken), static r => r == Origin);
        }

        [Fact]
        public async Task Instances_converging_one_platform_client_at_once_all_succeed_with_one_registration()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idracecli");
            using (factory.CreateClient())
            {
                // Started: the store exists, migrated and seeded.
            }

            string connectionString = factory.ConfigurationOverrides["TellmaIdentity:ConnectionString"]!;
            for (int round = 0; round < Rounds; round++)
            {
                // A changed display name makes every round after the first an update.
                TellmaIdentitySeedClientOptions seed = new()
                {
                    ClientId = "race-cli",
                    DisplayName = $"Race CLI {round}",
                    Kind = TellmaIdentitySeedClientKind.Cli,
                    RedirectUris = { "http://127.0.0.1/callback" },
                };

                ClientConvergence[] outcomes = await Task.WhenAll(Enumerable.Range(0, Instances).Select(_ => Task.Run(
                    async () =>
                    {
                        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
                        return await scope.ServiceProvider.GetRequiredService<ClientRegistrationConverger>()
                            .ConvergeSeededClientAsync(seed, TestContext.Current.CancellationToken);
                    },
                    TestContext.Current.CancellationToken)));

                Assert.Contains(outcomes, static o => o != ClientConvergence.Unchanged);
                Assert.Equal(1, await CountAsync(connectionString, "race-cli"));

                using IServiceScope check = factory.Services.CreateScope();
                IOpenIddictApplicationManager applications = check.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
                object cli = (await applications.FindByClientIdAsync("race-cli", TestContext.Current.CancellationToken))!;
                Assert.Equal($"Race CLI {round}", await applications.GetDisplayNameAsync(cli, TestContext.Current.CancellationToken));
            }
        }

        /// <summary>How many stored applications carry the client id.</summary>
        private static async Task<int> CountAsync(string connectionString, string clientId)
        {
            await using SqlConnection connection = new(connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM [idsvr].[OpenIddictApplications] WHERE [ClientId] = @clientId";
            command.Parameters.AddWithValue("@clientId", clientId);
            return (int)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        }

        /// <summary>Whether the stored client accepts the secret.</summary>
        private static async Task<bool> ValidateSecretAsync(StandaloneFactory factory, string clientId, string secret)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            IOpenIddictApplicationManager applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            object application = (await applications.FindByClientIdAsync(clientId, TestContext.Current.CancellationToken))!;
            return await applications.ValidateClientSecretAsync(application, secret, TestContext.Current.CancellationToken);
        }
    }
}
