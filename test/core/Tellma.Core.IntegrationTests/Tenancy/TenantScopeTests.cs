// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Globalization;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.IntegrationTests.Infrastructure;
using Tellma.Core.Tenancy;

namespace Tellma.Core.IntegrationTests.Tenancy
{
    /// <summary>
    ///     The tenant scope factory over a real catalog: a scope for every state the catalog can hold,
    ///     with and without the maintenance door; the scope's own connection reaching its tenant's
    ///     database; a state another instance wrote; and no activity started.
    /// </summary>
    [Trait("Category", "Integration")]
    public class TenantScopeTests(SqlServerFixture sql)
    {
        /// <summary>The id the stand-in for the user connection binds for the platform's own principal.</summary>
        private const int SystemUserId = 1;

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_scope_is_created_for_each_state_as_the_state_table_says()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "scope_states", configureServices: AddSystemConnection);

            // --- One tenant in each state, reached through the catalog's own transitions ---
            Dictionary<TenantState, int> tenants = new()
            {
                [TenantState.Provisioning] = await RegisterAsync(host, "Provisioning"),
                [TenantState.Active] = await RegisterAsync(host, "Active", TenantState.Active),
                [TenantState.ReadOnly] = await RegisterAsync(host, "ReadOnly", TenantState.Active, TenantState.ReadOnly),
                [TenantState.Frozen] = await RegisterAsync(host, "Frozen", TenantState.Active, TenantState.Frozen),
                [TenantState.Suspended] = await RegisterAsync(host, "Suspended", TenantState.Active, TenantState.Suspended),
                [TenantState.Retired] = await RegisterAsync(host, "Retired", TenantState.Active, TenantState.Retired),
            };

            // --- The state table: served, or the code of the refusal ---
            (TenantState State, bool AllowNonActive, string? Refusal)[] table =
            [
                (TenantState.Provisioning, false, "tenant-not-found"),
                (TenantState.Active, false, null),
                (TenantState.ReadOnly, false, null),
                (TenantState.Frozen, false, "tenant-frozen"),
                (TenantState.Suspended, false, "tenant-suspended"),
                (TenantState.Retired, false, "tenant-not-found"),
                (TenantState.Provisioning, true, null),
                (TenantState.Active, true, null),
                (TenantState.ReadOnly, true, null),
                (TenantState.Frozen, true, null),
                (TenantState.Suspended, true, "tenant-suspended"),
                (TenantState.Retired, true, "tenant-not-found"),
            ];

            foreach ((TenantState state, bool allowNonActive, string? refusal) in table)
            {
                RequestContextSnapshot snapshot = new(tenants[state], PrincipalKind.System, "system", null, "worker", null);
                string row = $"{state}, allowNonActive: {allowNonActive}";

                if (refusal is null)
                {
                    await using TenantScope scope = await host.Scopes.CreateScopeAsync(snapshot, allowNonActive, Token);
                    RequestContext context = scope.Services.GetRequiredService<IRequestContextAccessor>().Current;

                    Assert.True(context.Tenant is { } tenant && tenant.Id == tenants[state] && tenant.State == state, row);
                    Assert.True(context is { Kind: PrincipalKind.System, Subject: "system", UserId: SystemUserId, Client: "worker" }, row);
                }
                else
                {
                    TenantUnavailableException refused = await Assert.ThrowsAsync<TenantUnavailableException>(
                        () => host.Scopes.CreateScopeAsync(snapshot, allowNonActive, Token));

                    Assert.True(refused.Code == refusal && refused.State == state && refused.TenantId == tenants[state] && refused.RetryAfter is null, row);
                }
            }
        }

        [Fact]
        public async Task The_maintenance_door_is_closed_to_every_principal_but_the_platforms_own()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "scope_door", configureServices: AddSystemConnection);
            int frozen = await RegisterAsync(host, "Frozen", TenantState.Active, TenantState.Frozen);

            _ = await Assert.ThrowsAsync<InvalidOperationException>(
                () => host.Scopes.CreateScopeAsync(new RequestContextSnapshot(frozen, PrincipalKind.User, "sub-1", 5, "worker", null), allowNonActive: true, Token));
            _ = await Assert.ThrowsAsync<InvalidOperationException>(
                () => host.Scopes.CreateScopeAsync(new RequestContextSnapshot(frozen, PrincipalKind.ServiceAccount, "client-1", 5, "worker", null), allowNonActive: true, Token));

            // Without the door a user's work is refused by the state, like any job's.
            TenantUnavailableException refused = await Assert.ThrowsAsync<TenantUnavailableException>(
                () => host.Scopes.CreateScopeAsync(new RequestContextSnapshot(frozen, PrincipalKind.User, "sub-1", 5, "worker", null), cancellationToken: Token));
            Assert.Equal("tenant-frozen", refused.Code);
        }

        [Fact]
        public async Task A_scopes_own_connection_reaches_its_tenants_database_and_its_sandbox_flag_reads_the_category()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "scope_connect");
            string liveDatabase = sql.NewDatabaseName("scope_live");
            string sandboxDatabase = sql.NewDatabaseName("scope_sandbox");
            await SqlDatabaseCreation.CreateAsync(sql.MasterConnectionString, liveDatabase, cancellationToken: Token);
            await SqlDatabaseCreation.CreateAsync(sql.MasterConnectionString, sandboxDatabase, cancellationToken: Token);

            TenantDescriptor live = await host.Catalog.RegisterAsync(
                new TenantRegistration("Acme", TenantCategory.Live, null, new TenantLocation(sql.Server, liveDatabase, "default")), "migrator", cancellationToken: Token);
            TenantDescriptor sandbox = await host.Catalog.RegisterAsync(
                new TenantRegistration("Acme Sandbox", TenantCategory.Sandbox, live.Id, new TenantLocation(sql.Server, sandboxDatabase, "default")), "migrator", cancellationToken: Token);
            _ = await host.Catalog.SetStateAsync(live.Id, TenantState.Active, null, "migrator", Token);
            _ = await host.Catalog.SetStateAsync(sandbox.Id, TenantState.Active, null, "migrator", Token);

            await using TenantScope liveScope = await host.Scopes.CreateScopeAsync(
                new RequestContextSnapshot(live.Id, PrincipalKind.User, "sub-1", 5, "worker", null), cancellationToken: Token);
            await using TenantScope sandboxScope = await host.Scopes.CreateScopeAsync(
                new RequestContextSnapshot(sandbox.Id, PrincipalKind.User, "sub-1", 5, "worker", null), cancellationToken: Token);

            // --- Each scope's provider opens its own tenant's database, never the other's ---
            Assert.Equal(liveDatabase, await DatabaseOfAsync(liveScope));
            Assert.Equal(sandboxDatabase, await DatabaseOfAsync(sandboxScope));
            Assert.Equal(live.Id, liveScope.Services.GetRequiredService<ITenantConnectionProvider>().Tenant.Id);

            // --- The sandbox flag every connector routes on ---
            Assert.False(liveScope.Services.GetRequiredService<ISandboxContext>().IsSandbox);
            Assert.True(sandboxScope.Services.GetRequiredService<ISandboxContext>().IsSandbox);

            // --- Outside a tenant scope neither answers ---
            await using AsyncServiceScope tenantless = host.Services.CreateAsyncScope();
            _ = Assert.Throws<InvalidOperationException>(() => tenantless.ServiceProvider.GetRequiredService<ISandboxContext>().IsSandbox);
            _ = await Assert.ThrowsAsync<InvalidOperationException>(
                () => tenantless.ServiceProvider.GetRequiredService<ITenantConnectionProvider>().OpenAsync(Token));
        }

        [Fact]
        public async Task A_suspension_another_instance_wrote_refuses_the_next_scope_after_a_refresh()
        {
            await using CatalogHost writer = await CatalogHost.CreateAsync(sql, "scope_converge");
            await using CatalogHost worker = await CatalogHost.ComposeAsync(sql, writer.ConnectionString);
            TenantDescriptor tenant = await writer.Catalog.RegisterAsync(new TenantRegistration("Acme", TenantCategory.Live, null, null), "migrator", cancellationToken: Token);
            _ = await writer.Catalog.SetStateAsync(tenant.Id, TenantState.Active, null, "migrator", Token);
            await worker.Registry.RefreshAsync(force: false, Token);
            RequestContextSnapshot snapshot = new(tenant.Id, PrincipalKind.User, "sub-1", 5, "worker", null);

            await using (await worker.Scopes.CreateScopeAsync(snapshot, cancellationToken: Token))
            {
            }

            _ = await writer.Catalog.SetStateAsync(tenant.Id, TenantState.Suspended, "Unpaid.", "control-plane", Token);
            await worker.Registry.RefreshAsync(force: false, Token);

            TenantUnavailableException refused = await Assert.ThrowsAsync<TenantUnavailableException>(
                () => worker.Scopes.CreateScopeAsync(snapshot, cancellationToken: Token));
            Assert.Equal(("tenant-suspended", (TenantState?)TenantState.Suspended), (refused.Code, refused.State));
        }

        [Fact]
        public async Task Creating_a_scope_and_opening_its_connection_start_no_activity_of_the_platforms()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "scope_activity", configureServices: AddSystemConnection);
            string database = sql.NewDatabaseName("scope_traced");
            await SqlDatabaseCreation.CreateAsync(sql.MasterConnectionString, database, cancellationToken: Token);
            TenantDescriptor tenant = await host.Catalog.RegisterAsync(
                new TenantRegistration("Acme", TenantCategory.Live, null, new TenantLocation(sql.Server, database, "default")), "migrator", cancellationToken: Token);
            _ = await host.Catalog.SetStateAsync(tenant.Id, TenantState.Active, null, "migrator", Token);
            const string TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

            // A listener over every source, so an activity started anywhere in the caller's flow is created and seen.
            List<(ActivityTraceId Trace, string Source, string Name)> started = [];
            using ActivityListener listener = new()
            {
                ShouldListenTo = static _ => true,
                Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = activity =>
                {
                    lock (started)
                    {
                        started.Add((activity.TraceId, activity.Source.Name, activity.OperationName));
                    }
                },
            };
            ActivitySource.AddActivityListener(listener);
            using ActivitySource callers = new("Tellma.Core.IntegrationTests.Caller");
            using Activity? caller = callers.StartActivity("process job");
            Assert.NotNull(caller);

            await using (TenantScope scope = await host.Scopes.CreateScopeAsync(
                new RequestContextSnapshot(tenant.Id, PrincipalKind.System, "system", null, "worker", TraceParent), cancellationToken: Token))
            {
                // The scope runs under the caller's activity; the snapshot's trace parent is log data only.
                Assert.Same(caller, Activity.Current);
                Assert.Null(caller.ParentId);
                Assert.Empty(caller.Links);
                Assert.NotEqual("0af7651916cd43dd8448eb211c80319c", caller.TraceId.ToHexString());
                Assert.Equal(TraceParent, scope.Services.GetRequiredService<IRequestContextAccessor>().Current.OriginTraceParent);
                Assert.Equal(database, await DatabaseOfAsync(scope));
            }

            // Whatever joined the caller's trace beside the caller itself came from the SQL client's own
            // instrumentation, never from a source of the platform's.
            lock (started)
            {
                List<(ActivityTraceId Trace, string Source, string Name)> inFlow = [.. started.Where(entry => entry.Trace == caller.TraceId)];
                Assert.Contains(inFlow, static entry => entry is { Source: "Tellma.Core.IntegrationTests.Caller", Name: "process job" });
                Assert.DoesNotContain(
                    inFlow,
                    static entry => entry.Source.StartsWith("Tellma", StringComparison.Ordinal) && entry.Source != "Tellma.Core.IntegrationTests.Caller");
            }
        }

        [Fact]
        public async Task A_tenant_whose_database_is_missing_fails_its_open_at_once_rather_than_after_a_connect_retry_wait()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "scope_missing");

            // Registered on a database nobody created: every login to it is refused.
            string absent = sql.NewDatabaseName("scope_absent");
            TenantDescriptor tenant = await host.Catalog.RegisterAsync(
                new TenantRegistration("Acme", TenantCategory.Live, null, new TenantLocation(sql.Server, absent, "default")), "migrator", cancellationToken: Token);
            _ = await host.Catalog.SetStateAsync(tenant.Id, TenantState.Active, null, "migrator", Token);
            await using TenantScope scope = await host.Scopes.CreateScopeAsync(
                new RequestContextSnapshot(tenant.Id, PrincipalKind.User, "sub-1", 5, "worker", null), cancellationToken: Token);

            // SqlClient's own connect retry would sleep a whole retry interval (10 s by default) before
            // its second login; the data executor owns retry, so the open fails as soon as the server
            // answers. A retried open cannot finish within that interval, while a single login, even a
            // slow one under load, does: the bound is the widest that still tells the two apart.
            var retryInterval = TimeSpan.FromSeconds(new SqlConnectionStringBuilder().ConnectRetryInterval);
            var elapsed = Stopwatch.StartNew();
            SqlException refused = await Assert.ThrowsAsync<SqlException>(() => DatabaseOfAsync(scope));
            elapsed.Stop();

            // --- The refusal, in either of the two forms it reaches the client in ---
            // Over TCP the server's account arrives whole: 4060, then the login failure 18456. Over
            // named pipes and shared memory, LocalDB's only transports, the server sometimes ends the
            // session before the client reads that account, and the same refusal arrives as the
            // client's 233 ("no process is on the other end of the pipe") alone. The executor treats
            // both alike: a failure before the send, which a re-run may cure.
            int[] numbers = [.. refused.Errors.Cast<SqlError>().Select(static error => error.Number)];
            Assert.True(
                numbers.Contains(CatalogStatements.CannotOpenDatabase) || numbers is [CatalogStatements.LoginInterrupted],
                $"The open failed with errors {string.Join(", ", numbers)}, not the refusal of a missing database.");

            // A cut-off login proves nothing by itself: the server, still up, confirms the database is absent.
            Assert.False(await DatabaseExistsAsync(absent));

            Assert.True(elapsed.Elapsed < retryInterval, $"The open failed after {elapsed.Elapsed}, at least SqlClient's connect retry interval of {retryInterval}.");
        }

        [Fact]
        public async Task A_pooled_connection_whose_session_ended_while_idle_is_replaced_on_the_next_open()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "scope_pooled");
            string database = sql.NewDatabaseName("scope_pooled");
            await SqlDatabaseCreation.CreateAsync(sql.MasterConnectionString, database, cancellationToken: Token);
            TenantDescriptor tenant = await host.Catalog.RegisterAsync(
                new TenantRegistration("Acme", TenantCategory.Live, null, new TenantLocation(sql.Server, database, "default")), "migrator", cancellationToken: Token);
            _ = await host.Catalog.SetStateAsync(tenant.Id, TenantState.Active, null, "migrator", Token);
            await using TenantScope scope = await host.Scopes.CreateScopeAsync(
                new RequestContextSnapshot(tenant.Id, PrincipalKind.User, "sub-1", 5, "worker", null), cancellationToken: Token);

            // --- A session returned to the pool, then ended by the server (a failover, an idle cut) ---
            short session = await SessionOfAsync(scope);
            await SqlServerFixture.ExecuteAsync(sql.MasterConnectionString, string.Create(CultureInfo.InvariantCulture, $"KILL {session};"));
            await Task.Delay(TimeSpan.FromMilliseconds(200), Token);

            // Without SqlClient's connect retry, the pool still finds the dead connection when it hands
            // it out and opens a fresh one, so the next unit of work never sees the ended session.
            Assert.Equal(database, await DatabaseOfAsync(scope));
        }

        /// <summary>Registers the stand-in for the user connection, which a system snapshot relies on to bind its user.</summary>
        /// <param name="services">The composed services.</param>
        private static void AddSystemConnection(IServiceCollection services)
        {
            _ = services.AddScoped<IRequestContextInitializer, SystemConnection>();
        }

        /// <summary>Registers a tenant and moves it through the given states.</summary>
        /// <param name="host">The host.</param>
        /// <param name="name">The tenant's name.</param>
        /// <param name="states">The states to move through, in order.</param>
        /// <returns>The tenant's id.</returns>
        private static async Task<int> RegisterAsync(CatalogHost host, string name, params TenantState[] states)
        {
            TenantDescriptor tenant = await host.Catalog.RegisterAsync(new TenantRegistration(name, TenantCategory.Live, null, null), "migrator", cancellationToken: Token);
            foreach (TenantState state in states)
            {
                _ = await host.Catalog.SetStateAsync(tenant.Id, state, null, "migrator", Token);
            }

            return tenant.Id;
        }

        /// <summary>The database a scope's own connection reaches.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The database name.</returns>
        private static async Task<string> DatabaseOfAsync(TenantScope scope)
        {
            await using SqlConnection connection = await scope.Services.GetRequiredService<ITenantConnectionProvider>().OpenAsync(Token);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT DB_NAME();";
            return await command.ExecuteScalarAsync(Token) as string ?? throw new InvalidOperationException("The server named no database.");
        }

        /// <summary>Whether a database exists on the test server, asked of <c>master</c>.</summary>
        /// <param name="database">The database's name.</param>
        /// <returns>True when the server knows the database.</returns>
        private async Task<bool> DatabaseExistsAsync(string database)
        {
            await using SqlConnection connection = new(sql.MasterConnectionString);
            await connection.OpenAsync(Token);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT DB_ID(@database);";
            _ = command.Parameters.Add(new SqlParameter("@database", System.Data.SqlDbType.NVarChar, 128) { Value = database });
            return await command.ExecuteScalarAsync(Token) is not (null or DBNull);
        }

        /// <summary>The server session of a connection of the scope's, which goes back to the pool when this returns.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The session id.</returns>
        private static async Task<short> SessionOfAsync(TenantScope scope)
        {
            await using SqlConnection connection = await scope.Services.GetRequiredService<ITenantConnectionProvider>().OpenAsync(Token);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT @@SPID;";
            return (short)(await command.ExecuteScalarAsync(Token) ?? throw new InvalidOperationException("The server named no session."));
        }

        /// <summary>A stand-in for the user connection: binds the platform's own principal to a fixed user, and leaves every other principal as the snapshot named it.</summary>
        private sealed class SystemConnection : IRequestContextInitializer
        {
            /// <inheritdoc />
            public int Order => 100;

            /// <inheritdoc />
            public Task<RequestContext> InitializeAsync(RequestContext context, RequestContextInputs inputs, CancellationToken cancellationToken)
            {
                return Task.FromResult(context.IsSystem ? context with { UserId = SystemUserId } : context);
            }
        }
    }
}
