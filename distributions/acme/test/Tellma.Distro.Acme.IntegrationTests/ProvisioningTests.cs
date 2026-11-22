// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Net;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.AspNetCore;
using Tellma.Core.Data;
using Tellma.Core.Identity;
using Tellma.Core.Migrator;
using Tellma.Distro.Acme.Development;
using Tellma.Distro.Acme.IntegrationTests.Infrastructure;
using Tellma.Distro.Acme.Migrator;
using Tellma.Identity.Data;
using Tellma.Identity.TestSupport;

namespace Tellma.Distro.Acme.IntegrationTests
{
    /// <summary>
    ///     Acme's provisioning from an empty server and both halves of its first-administrator
    ///     bootstrap: <c>migrate</c> creating every table the composed model maps, with the platform's
    ///     and the sample data's steps recorded at version 1 and no identity column, row version or
    ///     trigger anywhere; the development administrator bound without an invitation and joining on
    ///     first sign-in; and a deployed-style <c>provision --admin-email</c> inviting its
    ///     administrator through the in-process identity engine, queueing the welcome email and
    ///     inviting again when rerun, while a sandbox's administrator without an account is refused
    ///     with nobody created or emailed.
    /// </summary>
    /// <remarks>
    ///     The class migrates one deployment and starts its web host once. The administrator's sign-in
    ///     writes into tenant 1 alone, and each provisioning registers a tenant of its own.
    /// </remarks>
    /// <param name="deployment">The class's migrated deployment and its running web host.</param>
    [Trait("Category", "Integration")]
    public sealed class ProvisioningTests(ProvisioningTests.Deployment deployment) : IClassFixture<ProvisioningTests.Deployment>
    {
        /// <summary>The live seed tenant.</summary>
        private const int Live = 1;

        /// <summary>The header a browser's API call carries, as the client shell sends it.</summary>
        private const string ClientHeaderValue = "web/1.0";

        /// <summary>The owner key the outbox correlates the emails it sends with.</summary>
        private const string OutboxOwnerKey = "outbox";

        /// <summary>The fixed subject of the development administrator, which the in-process engine seeds too.</summary>
        private const string DevelopmentAdminSubject = "00000000-0000-0000-0000-000000000001";

        /// <summary>The service-client secret the deployment's two hosts share, as a deployment's secret store supplies it.</summary>
        private const string ServiceClientSecret = "acme-provisioning-test-service-client-secret";

        /// <summary>How long the engine may take to send an invitation email it queued.</summary>
        private static readonly TimeSpan DeliveryBound = TimeSpan.FromSeconds(30);

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Migrate_creates_every_mapped_table_records_the_sample_steps_at_version_one_and_uses_no_identity_rowversion_or_trigger()
        {
            AcmeMigrator acme = deployment.Acme;
            IReadOnlyList<string> mapped = MappedTables();

            foreach (int tenant in (int[])[Live, 2])
            {
                string db = acme.TenantConnectionString(tenant);

                // --- Every table the composed model maps, history tables included, and the platform's own ---
                List<string> tables = await ColumnAsync(db, "SELECT CONCAT(S.[name], '.', T.[name]) FROM [sys].[tables] AS T JOIN [sys].[schemas] AS S ON S.[schema_id] = T.[schema_id];");
                Assert.Empty(mapped.Except(tables, StringComparer.OrdinalIgnoreCase));
                Assert.Subset(
                    tables.ToHashSet(StringComparer.OrdinalIgnoreCase),
                    new HashSet<string>(["gl.Centers", "acme.Currencies", "acme.Customers", "acme.CustomerContacts", "acme.SalesInvoices", "acme.SalesInvoiceLines", "acme.SalesInvoiceAttachments", "dbo.__TellmaProvisioning", "dbo.__TellmaSchema"], StringComparer.OrdinalIgnoreCase));

                // --- The table types the stacks' statements bind, each under its versioned name ---
                foreach (string type in (string[])["IdList", "UserInvitationOutcomeList", "UsersList", "CentersList", "CustomersList", "SalesInvoiceLinesList"])
                {
                    Assert.Equal(1, await ScalarAsync<int>(db, $"SELECT COUNT(*) FROM [sys].[table_types] WHERE [is_user_defined] = 1 AND [name] LIKE N'{type}[_]________';"));
                }

                // --- The steps that provisioned the tenant, the sample data's among them, at version 1 ---
                foreach (string step in (string[])["core.bootstrap-administrator", "gl.sample-centers", AcmeSampleDataStep.StepName])
                {
                    Assert.Equal(1, await ScalarAsync<int>(db, $"SELECT [Version] FROM [dbo].[__TellmaProvisioning] WHERE [Step] = N'{step}';"));
                }

                // --- Ids come from sequences, concurrency from audit stamps, and nothing fires behind a write ---
                Assert.Equal(0, await ScalarAsync<int>(db, "SELECT COUNT(*) FROM [sys].[identity_columns] AS C JOIN [sys].[tables] AS T ON T.[object_id] = C.[object_id];"));
                Assert.Equal(0, await ScalarAsync<int>(db, "SELECT COUNT(*) FROM [sys].[columns] AS C JOIN [sys].[tables] AS T ON T.[object_id] = C.[object_id] WHERE C.[system_type_id] = 189;"));
                Assert.Equal(0, await ScalarAsync<int>(db, "SELECT COUNT(*) FROM [sys].[triggers];"));
            }
        }

        [Fact]
        public async Task The_development_administrator_is_bound_without_an_invitation_and_joins_on_first_sign_in()
        {
            string db = deployment.Acme.TenantConnectionString(Live);
            string where = $"WHERE [Email] = N'{AcmeSignIn.Administrator}'";

            // --- Bound to the engine's development administrator, so the row reads invited; no invitation was sent ---
            Assert.Equal(
                $"{DevelopmentAdminSubject}|Invited",
                await ScalarAsync<string>(db, $"SELECT CONCAT([Subject] COLLATE DATABASE_DEFAULT, '|', [InviteStatus]) FROM [core].[Users] {where};"));
            Assert.Equal(0, await ScalarAsync<int>(db, $"""
                SELECT COUNT(*) FROM [core].[OutgoingEmails] AS E JOIN [core].[Users] AS U ON U.[Id] = E.[RecipientUserId]
                 WHERE U.[Email] = N'{AcmeSignIn.Administrator}';
                """));

            // --- The first request of the first sign-in joins the administrator ---
            using OidcFlowClient flow = deployment.Host.CreateFlowClient();
            await AcmeSignIn.SignInAsync(flow);
            using HttpRequestMessage request = new(HttpMethod.Post, new Uri($"/{Live}/api/web/users/me", UriKind.Relative));
            _ = request.Headers.TryAddWithoutValidation(TellmaAuthentication.CsrfHeaderName, ClientHeaderValue);
            using HttpResponseMessage me = await flow.Browser.SendAsync(request, Token);
            Assert.True(me.StatusCode == HttpStatusCode.OK, $"{(int)me.StatusCode} {await me.Content.ReadAsStringAsync(Token)}\n{deployment.Host.Warnings}");
            Assert.Equal("Joined", await ScalarAsync<string>(db, $"SELECT [State] FROM [core].[Users] {where};"));
        }

        [Fact]
        public async Task A_deployed_style_provision_invites_its_administrator_through_the_engine_queues_the_welcome_and_a_rerun_invites_again()
        {
            const string Owner = "owner@example.com";
            AcmeMigrator acme = deployment.Acme;
            AcmeHost host = deployment.Host;

            // --- The provisioning names an administrator and no subject, as a deployment's operator does ---
            MigratorResult provisioned = await acme.RunAsync("provision", "--name", "Acme Trading", "--category", "Live", "--admin-email", Owner);
            Assert.True(provisioned.ExitCode == MigratorExitCodes.Converged, $"{provisioned}\n{host.Warnings}");
            int tenantId = RegisteredTenant(provisioned);
            Assert.Equal("Active", await acme.StateAsync(tenantId));

            // --- The engine created the account the tenant's administrator is linked to, and emailed its invitation ---
            string db = acme.TenantConnectionString(tenantId);
            string where = $"WHERE [Email] = N'{Owner}'";
            TellmaIdentityUser account = await AccountAsync(host, Owner)
                ?? throw new InvalidOperationException($"The engine holds no account for {Owner}.");
            Assert.Equal(
                $"Invited|Invited|{account.Id}",
                await ScalarAsync<string>(db, $"SELECT CONCAT([State], '|', [InviteStatus], '|', [Subject] COLLATE DATABASE_DEFAULT) FROM [core].[Users] {where};"));
            await AwaitEngineMailAsync(host, Owner);

            // --- The membership hint, and the welcome email the outbox holds for the administrator ---
            Assert.Equal(1, await ScalarAsync<int>(acme.CatalogConnectionString, string.Create(
                CultureInfo.InvariantCulture,
                $"SELECT COUNT(*) FROM [catalog].[TenantMemberships] WHERE [TenantId] = {tenantId} AND [Subject] = '{account.Id}' AND [IsActive] = 1;")));
            Assert.Equal(1, await ScalarAsync<int>(db, $"""
                SELECT COUNT(*) FROM [core].[OutgoingEmails] AS E JOIN [core].[Users] AS U ON U.[Id] = E.[RecipientUserId]
                 WHERE U.[Email] = N'{Owner}' AND E.[RegardingEntity] = N'{CoreResources.User}';
                """));

            // --- A rerun naming the administrator, who holds no credential yet, invites again ---
            MigratorResult rerun = await acme.RunAsync("provision", "--tenant", tenantId.ToString(CultureInfo.InvariantCulture), "--admin-email", Owner);
            Assert.True(rerun.ExitCode == MigratorExitCodes.Converged, $"{rerun}\n{host.Warnings}");
            Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"tenant {tenantId}: provisioned (already; 1 administrator step(s))"), rerun.Lines);
            Assert.Equal(nameof(InviteStatus.Reinvited), await ScalarAsync<string>(db, $"SELECT [InviteStatus] FROM [core].[Users] {where};"));
        }

        [Fact]
        public async Task A_sandbox_provision_for_an_administrator_the_engine_does_not_know_fails_and_creates_or_emails_nobody()
        {
            const string Stranger = "stranger@example.com";
            AcmeMigrator acme = deployment.Acme;
            AcmeHost host = deployment.Host;

            MigratorResult failed = await acme.RunAsync("provision", "--name", "Acme Trials", "--category", "Sandbox", "--live-tenant", "1", "--admin-email", Stranger);

            // --- The report names the sandbox's code; the tenant stays provisioning ---
            Assert.True(failed.ExitCode == MigratorExitCodes.PartialFailure, failed.ToString());
            int tenantId = RegisteredTenant(failed);
            Assert.Contains(failed.Lines, line => line.StartsWith(string.Create(CultureInfo.InvariantCulture, $"tenant {tenantId}: failed"), StringComparison.Ordinal)
                && line.Contains(AccessCodes.SandboxRequiresExistingIdentity, StringComparison.Ordinal));
            Assert.Equal("Provisioning", await acme.StateAsync(tenantId));

            // --- The engine created no account and sent no mail ---
            Assert.Null(await AccountAsync(host, Stranger));
            Assert.Equal(0, host.Emails.CountFor(Stranger));
        }

        // --- Helpers ---

        /// <summary>
        ///     Every table the composed tenant model maps, its temporal history tables included, as
        ///     <c>schema.name</c>, read from the design-time model, which alone keeps the history tables' names.
        /// </summary>
        /// <returns>The tables.</returns>
        private static List<string> MappedTables()
        {
            using TellmaDbContext context = new AcmeDesignTimeFactory().CreateDbContext([]);
            List<string> tables = [];
            foreach (IEntityType entity in context.GetService<IDesignTimeModel>().Model.GetEntityTypes().Where(static entity => entity.GetTableName() is not null))
            {
                tables.Add($"{entity.GetSchema()}.{entity.GetTableName()}");
                if (entity.IsTemporal())
                {
                    tables.Add($"{entity.GetHistoryTableSchema() ?? entity.GetSchema()}.{entity.GetHistoryTableName()}");
                }
            }

            return [.. tables.Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        /// <summary>The tenant a provisioning registered, from the first line its report prints.</summary>
        /// <param name="result">The provisioning's result.</param>
        /// <returns>The tenant's id.</returns>
        private static int RegisteredTenant(MigratorResult result)
        {
            const string Prefix = "provision: registered tenant ";
            string line = Assert.Single(result.Lines, static line => line.StartsWith(Prefix, StringComparison.Ordinal));
            return int.Parse(line[Prefix.Length..], CultureInfo.InvariantCulture);
        }

        /// <summary>The engine's account of an address, if any.</summary>
        /// <param name="host">The host the engine runs in.</param>
        /// <param name="email">The address.</param>
        /// <returns>The account, or null.</returns>
        private static async Task<TellmaIdentityUser?> AccountAsync(AcmeHost host, string email)
        {
            await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<UserManager<TellmaIdentityUser>>().FindByEmailAsync(email);
        }

        /// <summary>Waits until the engine, not the outbox, sent a message to an address.</summary>
        /// <param name="host">The host the engine runs in.</param>
        /// <param name="email">The address.</param>
        /// <returns>A task that completes when the message was sent.</returns>
        private static async Task AwaitEngineMailAsync(AcmeHost host, string email)
        {
            _ = await host.Emails.WaitForAsync(
                captured => captured.Any(message => message.Message.Correlation?.OwnerKey != OutboxOwnerKey
                    && message.Message.To.Any(address => string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase))),
                DeliveryBound);
        }

        /// <summary>Reads one value.</summary>
        /// <typeparam name="T">The value's type.</typeparam>
        /// <param name="connectionString">The database.</param>
        /// <param name="query">The query.</param>
        /// <returns>The value.</returns>
        private static async Task<T> ScalarAsync<T>(string connectionString, string query)
        {
            return (await SqlServerFixture.ScalarAsync<T>(connectionString, query))!;
        }

        /// <summary>Reads one text column.</summary>
        /// <param name="connectionString">The database.</param>
        /// <param name="query">The query.</param>
        /// <returns>The values, in row order.</returns>
        private static async Task<List<string>> ColumnAsync(string connectionString, string query)
        {
            await using SqlConnection connection = new(connectionString);
            await connection.OpenAsync(Token);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = query;
            await using SqlDataReader reader = await command.ExecuteReaderAsync(Token);
            List<string> values = [];
            while (await reader.ReadAsync(Token))
            {
                values.Add(reader.GetString(0));
            }

            return values;
        }

        /// <summary>
        ///     The class's deployment: one migrated from an empty server, as a fresh checkout's
        ///     <c>migrate</c> leaves it, and its web host, the two sharing one service-client secret
        ///     as a deployment's hosts do, so the migrator's identity-server client reaches the engine
        ///     the web host runs.
        /// </summary>
        /// <param name="sql">The shared SQL Server.</param>
        public sealed class Deployment(SqlServerFixture sql) : IAsyncLifetime
        {
            /// <summary>The deployment's migrator.</summary>
            internal AcmeMigrator Acme { get; } = new(sql, "acme_provisioning");

            /// <summary>The web host over the deployment, started once it is migrated.</summary>
            internal AcmeHost Host { get; private set; } = null!;

            /// <inheritdoc />
            public async ValueTask InitializeAsync()
            {
                Acme.Values["Tellma:Identity:ServiceClientSecret"] = ServiceClientSecret;
                _ = await Acme.MigrateAsync();
                Host = await AcmeHost.StartAsync(Acme.Values);

                // Every later run's identity-server client reaches the engine through the host's test server.
                Acme.Services = services => services
                    .AddHttpClient(IdentityServerClient.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => Host.Server.CreateHandler());
            }

            /// <inheritdoc />
            public async ValueTask DisposeAsync()
            {
                if (Host is not null)
                {
                    await Host.DisposeAsync();
                }
            }
        }
    }
}
