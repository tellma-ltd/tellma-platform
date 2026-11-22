// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.IntegrationTests.Infrastructure;
using Tellma.Core.Identity;
using Tellma.Core.Migrator;
using Tellma.Core.Tenancy;
using Tellma.Distro.Acme.Development;
using Tellma.Distro.Acme.TestSupport;

// One deployment for the whole assembly, so every class shares its tenants, its members and its
// host instead of migrating a catalog of its own.
[assembly: AssemblyFixture(typeof(AcmeDeployment))]

namespace Tellma.Core.AspNetCore.IntegrationTests.Infrastructure
{
    /// <summary>
    ///     The suite's deployment of the reference distribution: a catalog and its tenants made by
    ///     Acme's migrator from an empty server, and Acme's web host over them on a test server.
    ///     <list type="bullet">
    ///         <item>
    ///             The live tenant, seeded by Development's sample data, holds the members: the
    ///             administrator; a restricted member reading only some centers and saving none; an
    ///             editor saving some centers and deleting none; a former member, deactivated; and the
    ///             sales member, whom only the activity case signs in. A stranger no tenant knows
    ///             signs in too.
    ///         </item>
    ///         <item>The sandbox tenant, of the live tenant, holds a member of its own.</item>
    ///         <item>
    ///             A third tenant, provisioned by the migrator with an invitation the identity server
    ///             never answered, stays <c>Provisioning</c> for the tenant-state case to drive through
    ///             every state.
    ///         </item>
    ///     </list>
    ///     The editor and the former member are made the way an administrator makes them, through
    ///     the web surface: a role saved, the users saved and invited through the in-process identity
    ///     engine, and the former member deactivated.
    /// </summary>
    public sealed class AcmeDeployment : IAsyncLifetime
    {
        /// <summary>The live tenant Development seeds.</summary>
        public const int LiveTenant = 1;

        /// <summary>The sandbox tenant Development seeds, of the live tenant.</summary>
        public const int SandboxTenant = AcmeDevelopmentPrincipals.SandboxTenantId;

        /// <summary>The name of the role the editor holds.</summary>
        public const string EditorsRoleName = "Center editors";

        /// <summary>The filter of the editor's <c>Save</c> on centers: sale centers alone.</summary>
        public const string EditorsSaveFilter = "CenterType = 'Sale'";

        /// <summary>The editor's email address.</summary>
        private const string EditorEmail = "editor@example.com";

        /// <summary>The former member's email address.</summary>
        private const string FormerEmail = "former@example.com";

        /// <summary>The server every database of the deployment lives on.</summary>
        private readonly SqlServerFixture _sql = new();

        /// <summary>
        ///     Where the migrator's identity-server client goes: the host's test server, or a refusal
        ///     while the third tenant's invitation must fail.
        /// </summary>
        private Func<HttpMessageHandler> _identityRoute = static () => new RefusingHandler();

        private AcmeMigrator? _acme;
        private AcmeHost? _host;

        /// <summary>The deployment's migrator.</summary>
        internal AcmeMigrator Acme => _acme ?? throw new InvalidOperationException("The deployment is not initialized.");

        /// <summary>The web host every class shares.</summary>
        internal AcmeHost Host => _host ?? throw new InvalidOperationException("The deployment is not initialized.");

        /// <summary>The log events the host wrote with their scopes.</summary>
        internal ScopeLog Logs { get; } = new();

        /// <summary>The third tenant, left <c>Provisioning</c>.</summary>
        internal int StateTenant { get; private set; }

        /// <summary>The live tenant's administrator, a member of every tenant of the deployment.</summary>
        internal Member Administrator { get; private set; } = null!;

        /// <summary>The live tenant's member who reads only sale centers and saves none.</summary>
        internal Member Restricted { get; private set; } = null!;

        /// <summary>The live tenant's member who reads every center, saves sale centers and deletes none.</summary>
        internal Member Editor { get; private set; } = null!;

        /// <summary>The live tenant's former member: invited, then deactivated.</summary>
        internal Member Former { get; private set; } = null!;

        /// <summary>The live tenant's sales member, whom only the activity case signs in.</summary>
        internal Member Sales { get; private set; } = null!;

        /// <summary>The sandbox tenant's own member, a stranger to the live tenant.</summary>
        internal Member SandboxMember { get; private set; } = null!;

        /// <summary>Someone the identity server may know but no tenant does.</summary>
        internal Member Stranger { get; } = new(Guid.NewGuid().ToString(), "stranger@example.com", "Stranger", null);

        /// <summary>The editor's role.</summary>
        internal int EditorsRoleId { get; private set; }

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            await _sql.InitializeAsync();

            // --- The deployment: Acme's files, with fixed client secrets, as a deployment's hosts
            // share them, so the migrator's identity-server client and every host of the suite
            // reach the one engine; the suite's client named; the per-user rate far above what the
            // suite sends, so one member's requests across the classes are never throttled ---
            _acme = new AcmeMigrator(_sql, "web_suite");
            _acme.Values["Tellma:Identity:ClientSecret"] = "web-suite-client-secret";
            _acme.Values["Tellma:Identity:ServiceClientSecret"] = "web-suite-service-client-secret";
            _acme.Values["Tellma:Api:ClientNames:0"] = "web";
            _acme.Values["Tellma:Api:ClientNames:1"] = "test";
            _acme.Values["Tellma:Api:RequestsPerMinutePerUser"] = "100000";
            _acme.Values["Tellma:Api:RequestsPerMinutePerUserAcrossTenants"] = "100000";
            _acme.Services = services => services
                .AddHttpClient(IdentityServerClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => _identityRoute());
            _ = await _acme.MigrateAsync();

            _host = await AcmeHost.StartAsync(_acme.Values, new AcmeHostOptions
            {
                Services = services => services.AddSingleton<ILoggerProvider>(Logs),
            });

            // --- The members Development seeded ---
            Administrator = await MemberAsync(LiveTenant, AcmeSignIn.Administrator);
            Restricted = await MemberAsync(LiveTenant, AcmeDevelopmentPrincipals.CenterReader.Email);
            Sales = await MemberAsync(LiveTenant, AcmeDevelopmentPrincipals.SalesMember.Email);
            SandboxMember = await MemberAsync(SandboxTenant, AcmeDevelopmentPrincipals.SandboxOnly.Email);

            // --- The members an administrator makes through the web surface ---
            await AddMembersAsync();
            Editor = await MemberAsync(LiveTenant, EditorEmail);
            Former = await MemberAsync(LiveTenant, FormerEmail);

            // --- The third tenant: its administrator's invitation refused, so it stays provisioning ---
            MigratorResult provisioned = await Acme.RunAsync(
                "provision", "--name", "Acme States", "--category", "Live", "--admin-email", AcmeSignIn.Administrator);
            Assert.True(provisioned.ExitCode == MigratorExitCodes.PartialFailure, provisioned.ToString());
            StateTenant = RegisteredTenant(provisioned);
            Assert.Equal(nameof(TenantState.Provisioning), await Acme.StateAsync(StateTenant));
            _identityRoute = Host.Server.CreateHandler;
            await RefreshCatalogAsync();
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_host is not null)
            {
                await _host.DisposeAsync();
            }

            // The pooled connections hold the deployment's databases open; the drop must not wait on them.
            SqlConnection.ClearAllPools();
            Logs.Dispose();
            await _sql.DisposeAsync();
        }

        /// <summary>A client signed in as a member, at the first assurance tier a plain sign-in reaches.</summary>
        /// <param name="member">The member.</param>
        /// <returns>The client; the caller disposes it.</returns>
        internal async Task<SpaClient> SignInAsync(Member member)
        {
            return new SpaClient(Host, await WebSessions.SignInAsync(Host, member));
        }

        /// <summary>A client signed in as a member who stepped up to the host's bar moments ago.</summary>
        /// <param name="member">The member.</param>
        /// <returns>The client; the caller disposes it.</returns>
        internal async Task<SpaClient> SteppedUpAsync(Member member)
        {
            return new SpaClient(Host, await WebSessions.SteppedUpAsync(Host, member));
        }

        /// <summary>A client with no session.</summary>
        /// <returns>The client; the caller disposes it.</returns>
        internal SpaClient Anonymous()
        {
            return new SpaClient(Host, cookie: null);
        }

        /// <summary>Reloads the host's tenant snapshot now, as a catalog write's own refresh does.</summary>
        /// <returns>A task that completes when the snapshot holds the catalog's current state.</returns>
        internal Task RefreshCatalogAsync()
        {
            return Host.Services.GetRequiredService<ITenantRegistry>().RefreshAsync(force: true, Token);
        }

        /// <summary>Reads one value from a tenant's database.</summary>
        /// <typeparam name="T">The value's type.</typeparam>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="query">The query.</param>
        /// <returns>The value, or default for no row.</returns>
        internal Task<T?> ScalarAsync<T>(int tenantId, string query)
        {
            return SqlServerFixture.ScalarAsync<T>(Acme.TenantConnectionString(tenantId), query);
        }

        /// <summary>The live tenant's administrator's user id in another tenant.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The id.</returns>
        internal async Task<int> AdministratorIdAsync(int tenantId)
        {
            return await ScalarAsync<int?>(tenantId, $"SELECT [Id] FROM [core].[Users] WHERE [Email] = N'{AcmeSignIn.Administrator}';")
                ?? throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"Tenant {tenantId} has no user {AcmeSignIn.Administrator}."));
        }

        /// <summary>
        ///     Adds the editor and the former member as an administrator does: the role, the two users
        ///     holding it, their invitation through the identity engine, and the former member's
        ///     deactivation.
        /// </summary>
        /// <returns>A task that completes when both are members of the live tenant, one deactivated.</returns>
        private async Task AddMembersAsync()
        {
            using SpaClient admin = await SteppedUpAsync(Administrator);

            // --- The role: every center read, sale centers saved, nothing deleted ---
            JsonNode role = (await admin.PostAsync(LiveTenant, "roles/save", new JsonObject
            {
                ["entities"] = new JsonArray(new JsonObject
                {
                    ["id"] = 0,
                    ["name"] = EditorsRoleName,
                    ["code"] = "center-editors",
                    ["permissions"] = new JsonArray(
                        new JsonObject { ["id"] = 0, ["resource"] = "gl.Center", ["action"] = "Read" },
                        new JsonObject { ["id"] = 0, ["resource"] = "gl.Center", ["action"] = "Save", ["filter"] = EditorsSaveFilter }),
                }),
            })).Expect(HttpStatusCode.OK);
            EditorsRoleId = role["ids"]![0]!.GetValue<int>();

            // --- The users, holding the role ---
            JsonNode users = (await admin.PostAsync(LiveTenant, "users/save", new JsonObject
            {
                ["entities"] = new JsonArray(User(EditorEmail, "Center Editor"), User(FormerEmail, "Former Member")),
                ["returnEntities"] = false,
            })).Expect(HttpStatusCode.OK);
            int editor = users["ids"]![0]!.GetValue<int>();
            int former = users["ids"]![1]!.GetValue<int>();

            // --- Their invitation, which links each to the account the engine made for the address ---
            _ = (await admin.PostAsync(LiveTenant, "users/invite", new JsonObject { ["ids"] = new JsonArray(editor, former) }))
                .Expect(HttpStatusCode.OK);

            // --- The former member, deactivated ---
            _ = (await admin.PostAsync(LiveTenant, "users/deactivate", new JsonObject { ["ids"] = new JsonArray(former), ["returnEntities"] = false }))
                .Expect(HttpStatusCode.OK);

            JsonObject User(string email, string name)
            {
                return new JsonObject
                {
                    ["id"] = 0,
                    ["email"] = email,
                    ["name"] = name,
                    ["roleMemberships"] = new JsonArray(new JsonObject { ["id"] = 0, ["roleId"] = EditorsRoleId }),
                };
            }
        }

        /// <summary>A tenant's user as the suite signs in as them: the subject the tenant binds, the address and the name.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="email">The user's address.</param>
        /// <returns>The member.</returns>
        internal async Task<Member> MemberAsync(int tenantId, string email)
        {
            string row = await ScalarAsync<string>(
                tenantId,
                $"SELECT CONCAT([Id], '|', [Subject] COLLATE DATABASE_DEFAULT, '|', [Name]) FROM [core].[Users] WHERE [Email] = N'{email}';")
                ?? throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"Tenant {tenantId} has no user {email}."));
            string[] parts = row.Split('|', 3);
            Assert.False(string.IsNullOrEmpty(parts[1]), $"Tenant {tenantId}'s user {email} is bound to no subject.");
            return new Member(parts[1], email, parts[2], int.Parse(parts[0], CultureInfo.InvariantCulture));
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

        /// <summary>An identity server that never answers: every request fails as a refused connection does.</summary>
        private sealed class RefusingHandler : HttpMessageHandler
        {
            /// <inheritdoc />
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                throw new HttpRequestException("The identity server refused the connection.", null, HttpStatusCode.ServiceUnavailable);
            }
        }
    }
}
