// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using System.Net;
using Tellma.Identity.Data;
using Tellma.Identity.Data.Entities;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.AuthenticationPolicy;
using Tellma.Identity.TestSupport;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     One user signing in to one client from two tabs of one session at once. Both grants read
    ///     the client's permanent authorization and the session's registration of the client, and
    ///     both write them. Neither sign-in may fail for the race: a grant whose write loses reads
    ///     again and goes on from what the other grant wrote, so both tabs get a code, the one
    ///     authorization ends up covering every place either tab asked for, and the session holds
    ///     one registration of the client.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class ConcurrentGrantTests(SqlServerFixture fixture)
    {
        private const string ClientId = "tellma-cli";
        private const string RedirectUri = "http://127.0.0.1/callback";
        private const string Origin = "https://acme.app.tellma.com";
        private const string Tenant17 = Origin + "/17/mcp";
        private const string Tenant18 = Origin + "/18/mcp";
        private const string Tenant19 = Origin + "/19/mcp";

        /// <summary>A public CLI client, which onboarding grants each distribution's origin.</summary>
        private static readonly Dictionary<string, string?> CliSeed = new()
        {
            ["TellmaIdentity:Seed:Clients:0:ClientId"] = ClientId,
            ["TellmaIdentity:Seed:Clients:0:DisplayName"] = "Tellma CLI",
            ["TellmaIdentity:Seed:Clients:0:Kind"] = "Cli",
            ["TellmaIdentity:Seed:Clients:0:RedirectUris:0"] = RedirectUri,
        };

        [Theory]
        [InlineData(Tenant18, Tenant18)]
        [InlineData(Tenant18, Tenant19)]
        public async Task Two_tabs_extending_one_authorization_at_once_both_get_a_code(string first, string second)
        {
            // Each grant's write to the authorization waits until the other's has arrived too, so
            // both extend the copy they read before either was written: the race the timing of
            // two real tabs only sometimes produces.
            SaveBarrier barrier = new(static tracker => Has<OpenIddictEntityFrameworkCoreAuthorization>(tracker, EntityState.Modified));
            using StandaloneFactory factory = CreateFactory("idgrantrace", barrier);
            using OidcFlowClient flow = factory.CreateFlowClient();
            await ConsentTests.SignInAsync(factory, flow, "tabs@example.com");

            // The first grant creates the client's authorization, for one tenant.
            using (HttpResponseMessage created = await AuthorizeAsync(flow, Tenant17))
            {
                Assert.NotNull(Code(created));
            }

            // Two tabs then ask, at once, for places the authorization does not cover yet.
            barrier.Arm();
            await AuthorizeInTwoTabsAsync(flow, first, second);
            Assert.True(barrier.BothArrived, "The two grants did not both write to the authorization.");

            // Still one authorization for the client, now covering every place either tab asked
            // for: the losing write was made again over what the winner wrote, never beside it.
            string[] expected = [.. new[] { Tenant17, first, second }.Distinct().Order(StringComparer.Ordinal)];
            Assert.Equal(expected, await CoveredResourcesAsync(factory));
        }

        [Fact]
        public async Task Two_tabs_making_a_sessions_first_grant_to_a_client_at_once_both_get_a_code()
        {
            // Both grants create the authorization and then register the client under the session,
            // each held until the other arrives, so both find nothing before either inserts.
            SaveBarrier creates = new(static tracker => Has<OpenIddictEntityFrameworkCoreAuthorization>(tracker, EntityState.Added));
            SaveBarrier registrations = new(static tracker => Has<IdentitySessionClient>(tracker, EntityState.Added));
            using StandaloneFactory factory = CreateFactory("idgrantfirst", creates, registrations);
            using OidcFlowClient flow = factory.CreateFlowClient();
            await ConsentTests.SignInAsync(factory, flow, "first@example.com");

            creates.Arm();
            registrations.Arm();
            await AuthorizeInTwoTabsAsync(flow, Tenant17, Tenant17);
            Assert.True(creates.BothArrived, "The two grants did not both create an authorization.");
            Assert.True(registrations.BothArrived, "The two grants did not both register the client.");

            // The session holds one registration of the client, naming an authorization of the
            // client's that covers the tenant: the one global logout revokes.
            using IServiceScope scope = factory.Services.CreateScope();
            TellmaIdentityDbContext db = scope.ServiceProvider.GetRequiredService<TellmaIdentityDbContext>();
            IdentitySessionClient registration = Assert.Single(await db.Set<IdentitySessionClient>()
                .Where(static row => row.ClientId == ClientId)
                .ToListAsync(TestContext.Current.CancellationToken));
            IOpenIddictAuthorizationManager authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
            object authorization = (await authorizations.FindByIdAsync(registration.AuthorizationId!, TestContext.Current.CancellationToken))!;
            Assert.Equal(
                [Tenant17],
                AuthorizationResources.Read(await authorizations.GetPropertiesAsync(authorization, TestContext.Current.CancellationToken)));
        }

        [Fact]
        public async Task A_grant_that_keeps_losing_its_write_gives_up_after_three_attempts()
        {
            // Every write to the authorization is overtaken by another writer, as if one tab kept
            // racing a stream of others. The grant stops after three attempts, and the client is
            // told to try again rather than given a code the authorization does not record.
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idgrantlost", CliSeed);
            AuthorizationWriteSaboteur saboteur = new(factory.ConfigurationOverrides["TellmaIdentity:ConnectionString"]!);
            factory.ServiceOverrides.Add(services => services.ConfigureDbContext<TellmaIdentityDbContext>(
                (_, builder) => builder.AddInterceptors(saboteur)));
            using OidcFlowClient flow = factory.CreateFlowClient();
            await ConsentTests.SignInAsync(factory, flow, "lost@example.com");

            using (HttpResponseMessage created = await AuthorizeAsync(flow, Tenant17))
            {
                Assert.NotNull(Code(created));
            }

            using HttpResponseMessage refused = await AuthorizeAsync(flow, Tenant18);

            Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode);
            Dictionary<string, StringValues> query = QueryHelpers.ParseQuery(refused.Headers.Location!.Query);
            Assert.Equal("temporarily_unavailable", (string?)query["error"]);
            Assert.False(query.ContainsKey("code"));
            Assert.Equal(3, saboteur.Attempts);
            Assert.Equal([Tenant17], await CoveredResourcesAsync(factory));
        }

        /// <summary>Boots a host with the CLI seeded and the given interceptors on its store context.</summary>
        private StandaloneFactory CreateFactory(string databasePrefix, params IInterceptor[] interceptors)
        {
            StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, databasePrefix, CliSeed);
            factory.ServiceOverrides.Add(services => services.ConfigureDbContext<TellmaIdentityDbContext>(
                (_, builder) => builder.AddInterceptors(interceptors)));
            return factory;
        }

        /// <summary>Starts two of the CLI's authorizations at once on one session, and asserts each returns a code.</summary>
        private static async Task AuthorizeInTwoTabsAsync(OidcFlowClient flow, string first, string second)
        {
            HttpResponseMessage[] responses = await Task.WhenAll(
                Task.Run(() => AuthorizeAsync(flow, first), TestContext.Current.CancellationToken),
                Task.Run(() => AuthorizeAsync(flow, second), TestContext.Current.CancellationToken));
            try
            {
                foreach (HttpResponseMessage response in responses)
                {
                    Assert.True(
                        Code(response) is not null,
                        $"No code returned: {(int)response.StatusCode} {response.Headers.Location}");
                }
            }
            finally
            {
                foreach (HttpResponseMessage response in responses)
                {
                    response.Dispose();
                }
            }
        }

        /// <summary>Starts the CLI's authorization for one resource on the established session.</summary>
        private static async Task<HttpResponseMessage> AuthorizeAsync(OidcFlowClient flow, string resource)
        {
            (_, string challenge) = OidcFlowClient.CreatePkcePair();
            string url = QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
            {
                ["client_id"] = ClientId,
                ["redirect_uri"] = RedirectUri,
                ["response_type"] = "code",
                ["scope"] = "openid offline_access tellma_api",
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["state"] = "state-1",
                ["resource"] = resource,
            });

            return await flow.Browser.GetAsync(new Uri(url, UriKind.Relative), TestContext.Current.CancellationToken);
        }

        /// <summary>The code a redirect back to the client carries, if it carries one.</summary>
        private static string? Code(HttpResponseMessage response)
        {
            return response.StatusCode == HttpStatusCode.Redirect
                && response.Headers.Location is { } location
                && QueryHelpers.ParseQuery(location.Query).TryGetValue("code", out StringValues code)
                    ? (string?)code
                    : null;
        }

        /// <summary>The resources the CLI's one authorization covers, in ordinal order.</summary>
        private static async Task<List<string>> CoveredResourcesAsync(StandaloneFactory factory)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            IOpenIddictApplicationManager applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            IOpenIddictAuthorizationManager authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
            object client = (await applications.FindByClientIdAsync(ClientId, TestContext.Current.CancellationToken))!;
            List<object> rows = [];
            await foreach (object row in authorizations.FindByApplicationIdAsync(
                (await applications.GetIdAsync(client, TestContext.Current.CancellationToken))!, TestContext.Current.CancellationToken))
            {
                rows.Add(row);
            }

            object authorization = Assert.Single(rows);
            return [.. AuthorizationResources
                .Read(await authorizations.GetPropertiesAsync(authorization, TestContext.Current.CancellationToken))
                .Order(StringComparer.Ordinal)];
        }

        /// <summary>Whether a save is about to write an entity of a type in a state.</summary>
        private static bool Has<TEntity>(ChangeTracker tracker, EntityState state)
            where TEntity : class
        {
            return tracker.Entries<TEntity>().Any(entry => entry.State == state);
        }

        /// <summary>
        ///     Once armed, holds the first two saves that match a condition until both have
        ///     arrived, so each grant read what it is about to write before the other wrote it.
        ///     Later saves pass straight through. A save waits at most 30 seconds for the other, so
        ///     a grant that never makes the write fails the test's assertion rather than hanging it.
        /// </summary>
        /// <param name="holds">Whether a save, by what its context is about to write, is held.</param>
        private sealed class SaveBarrier(Func<ChangeTracker, bool> holds) : SaveChangesInterceptor
        {
            /// <summary>Completes when the second save arrives.</summary>
            private readonly TaskCompletionSource _bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>How many matching saves have arrived.</summary>
            private int _arrivals;

            /// <summary>Whether matching saves are held yet (1) or not (0).</summary>
            private int _armed;

            /// <summary>Whether both of the held saves arrived.</summary>
            public bool BothArrived => _bothArrived.Task.IsCompleted;

            /// <summary>Starts holding matching saves: the setup before the race writes freely.</summary>
            public void Arm()
            {
                Volatile.Write(ref _armed, 1);
            }

            /// <inheritdoc />
            public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
                DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(eventData);
                if (Volatile.Read(ref _armed) == 1 && eventData.Context is { } context && holds(context.ChangeTracker))
                {
                    int arrival = Interlocked.Increment(ref _arrivals);
                    if (arrival == 2)
                    {
                        _bothArrived.TrySetResult();
                    }

                    if (arrival <= 2)
                    {
                        await Task.WhenAny(_bothArrived.Task, Task.Delay(TimeSpan.FromSeconds(30), cancellationToken));
                    }
                }

                return await base.SavingChangesAsync(eventData, result, cancellationToken);
            }
        }

        /// <summary>
        ///     Lets another writer change an authorization just before every save that updates it,
        ///     so every such save loses on the authorization's concurrency token, and counts them.
        /// </summary>
        /// <param name="connectionString">The store's connection string.</param>
        private sealed class AuthorizationWriteSaboteur(string connectionString) : SaveChangesInterceptor
        {
            /// <summary>How many authorization updates were attempted.</summary>
            private int _attempts;

            /// <summary>How many authorization updates were attempted.</summary>
            public int Attempts => Volatile.Read(ref _attempts);

            /// <inheritdoc />
            public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
                DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(eventData);
                List<string> updated = eventData.Context is { } context
                    ? [.. context.ChangeTracker
                        .Entries<OpenIddictEntityFrameworkCoreAuthorization>()
                        .Where(static entry => entry.State == EntityState.Modified)
                        .Select(static entry => entry.Entity.Id!)]
                    : [];
                if (updated.Count > 0)
                {
                    Interlocked.Increment(ref _attempts);
                }

                foreach (string id in updated)
                {
                    // On a connection of its own, as a concurrent grant's write would be.
                    await using SqlConnection connection = new(connectionString);
                    await connection.OpenAsync(cancellationToken);
                    await using SqlCommand command = new(
                        "UPDATE [idsvr].[OpenIddictAuthorizations] SET [ConcurrencyToken] = CONVERT(nvarchar(50), NEWID()) WHERE [Id] = @id;",
                        connection);
                    command.Parameters.AddWithValue("@id", id);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                return await base.SavingChangesAsync(eventData, result, cancellationToken);
            }
        }
    }
}
