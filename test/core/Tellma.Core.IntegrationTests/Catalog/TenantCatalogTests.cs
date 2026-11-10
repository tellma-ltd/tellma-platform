// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.IntegrationTests.Infrastructure;
using Tellma.Core.Tenancy;

namespace Tellma.Core.IntegrationTests.Catalog
{
    /// <summary>
    ///     The catalog's write side against a real catalog: registration and its refusals, version
    ///     bumps on every write, transitions that race, renames, relocations and their refusals, and
    ///     the listing from the store.
    /// </summary>
    [Trait("Category", "Integration")]
    public class TenantCatalogTests(SqlServerFixture sql)
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Registration_allocates_from_1000_or_takes_a_free_requested_id_as_provisioning()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "register");

            TenantDescriptor allocated = await host.Catalog.RegisterAsync(new TenantRegistration("Acme", TenantCategory.Live, null, null), "migrator", cancellationToken: Token);
            TenantDescriptor next = await host.Catalog.RegisterAsync(new TenantRegistration("Acme Trading", TenantCategory.Live, null, null), "migrator", cancellationToken: Token);
            TenantDescriptor requested = await host.Catalog.RegisterAsync(new TenantRegistration("Acme Seed", TenantCategory.Live, null, null), "migrator", requestedId: 1, Token);

            Assert.Equal((1000, 1001, 1), (allocated.Id, next.Id, requested.Id));
            Assert.Equal(new TenantDescriptor(1000, "Acme", null, null, ["en"], TenantCategory.Live, TenantState.Provisioning, null), allocated);

            // The default location: the catalog's server, the prefixed database, the default profile.
            TenantInfo stored = (await host.Catalog.ListAsync(Token)).Single(static tenant => tenant.Id == 1000);
            Assert.Equal(new TenantLocation(TenantLocations.Canonicalize(sql.Server), "Tellma.it.1000", "default"), stored.Location);
            Assert.Equal("migrator", await SqlServerFixture.ScalarAsync<string>(host.ConnectionString, "SELECT [StateChangedBy] FROM [catalog].[Tenants] WHERE [Id] = 1000;"));

            // The writing instance's registry holds the tenants at once.
            Assert.Equal([1, 1000, 1001], host.Registry.Tenants.Select(static tenant => tenant.Id));
            Assert.Contains("Initial Catalog=Tellma.it.1000", host.Connections.GetConnectionString(1000), StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_requested_id_already_taken_is_refused()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "register_taken");
            _ = await host.Catalog.RegisterAsync(new TenantRegistration("Acme", TenantCategory.Live, null, null), "migrator", requestedId: 7, Token);

            TenantStateException refused = await Assert.ThrowsAsync<TenantStateException>(() => host.Catalog.RegisterAsync(
                new TenantRegistration("Acme again", TenantCategory.Live, null, new TenantLocation(sql.Server, "other", "default")), "migrator", requestedId: 7, Token));

            Assert.Equal(TenantProblemCodes.RegistrationRefused, refused.Code);
            Assert.Contains("requested id 7 is taken", refused.Detail, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_location_already_another_tenants_is_refused()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "register_location");
            TenantLocation shared = new(sql.Server, "shared", "default");
            _ = await host.Catalog.RegisterAsync(new TenantRegistration("Acme", TenantCategory.Live, null, shared), "migrator", cancellationToken: Token);

            TenantStateException refused = await Assert.ThrowsAsync<TenantStateException>(() => host.Catalog.RegisterAsync(
                new TenantRegistration("Acme two", TenantCategory.Live, null, shared), "migrator", cancellationToken: Token));

            Assert.Equal(TenantProblemCodes.RegistrationRefused, refused.Code);
        }

        [Fact]
        public async Task Another_spelling_of_a_tenants_server_cannot_alias_its_database()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "register_alias");
            _ = await host.Catalog.RegisterAsync(
                new TenantRegistration("Acme", TenantCategory.Live, null, new TenantLocation("sql1.contoso.local", "acme", "default")), "migrator", cancellationToken: Token);
            TenantDescriptor other = await host.Catalog.RegisterAsync(
                new TenantRegistration("Other", TenantCategory.Live, null, new TenantLocation("sql1.contoso.local", "other", "default")), "migrator", cancellationToken: Token);
            _ = await host.Catalog.SetStateAsync(other.Id, TenantState.Active, null, "migrator", Token);
            _ = await host.Catalog.SetStateAsync(other.Id, TenantState.Frozen, null, "migrator", Token);

            foreach (string alias in (string[])["tcp:sql1.contoso.local", "SQL1.Contoso.Local,1433", "tcp:SQL1.CONTOSO.LOCAL,1433"])
            {
                TenantStateException registration = await Assert.ThrowsAsync<TenantStateException>(() => host.Catalog.RegisterAsync(
                    new TenantRegistration("Alias", TenantCategory.Live, null, new TenantLocation(alias, "acme", "default")), "migrator", cancellationToken: Token));
                TenantStateException relocation = await Assert.ThrowsAsync<TenantStateException>(() => host.Catalog.RelocateAsync(
                    other.Id, new TenantLocation(alias, "acme", "default"), "migrator", Token));

                Assert.Equal(TenantProblemCodes.RegistrationRefused, registration.Code);
                Assert.Equal(TenantProblemCodes.LocationRefused, relocation.Code);
            }
        }

        [Fact]
        public async Task No_tenant_may_be_located_at_the_catalog_itself()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "register_catalog");
            string catalogDatabase = new SqlConnectionStringBuilder(host.ConnectionString).InitialCatalog;
            TenantDescriptor tenant = await host.Catalog.RegisterAsync(new TenantRegistration("Acme", TenantCategory.Live, null, null), "migrator", cancellationToken: Token);

            foreach (string server in (string[])[sql.Server, "tcp:" + sql.Server.ToUpperInvariant()])
            {
                TenantLocation catalog = new(server, catalogDatabase.ToUpperInvariant(), "default");
                TenantStateException registration = await Assert.ThrowsAsync<TenantStateException>(() => host.Catalog.RegisterAsync(
                    new TenantRegistration("Catalog", TenantCategory.Live, null, catalog), "migrator", cancellationToken: Token));
                TenantStateException relocation = await Assert.ThrowsAsync<TenantStateException>(() => host.Catalog.RelocateAsync(tenant.Id, catalog, "migrator", Token));

                Assert.Contains("the catalog's own database", registration.Detail, StringComparison.Ordinal);
                Assert.Equal(TenantProblemCodes.LocationRefused, relocation.Code);
            }
        }

        [Fact]
        public async Task A_sandbox_may_name_only_a_live_tenant()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "register_sandbox");
            TenantDescriptor live = await host.Catalog.RegisterAsync(new TenantRegistration("Acme", TenantCategory.Live, null, null), "migrator", cancellationToken: Token);
            TenantDescriptor sandbox = await host.Catalog.RegisterAsync(new TenantRegistration("Acme Training", TenantCategory.Sandbox, live.Id, null), "migrator", cancellationToken: Token);
            TenantDescriptor standalone = await host.Catalog.RegisterAsync(new TenantRegistration("Demo", TenantCategory.Sandbox, null, null), "migrator", cancellationToken: Token);

            TenantStateException ofSandbox = await Assert.ThrowsAsync<TenantStateException>(() => host.Catalog.RegisterAsync(
                new TenantRegistration("Nested", TenantCategory.Sandbox, sandbox.Id, null), "migrator", cancellationToken: Token));
            TenantStateException ofNothing = await Assert.ThrowsAsync<TenantStateException>(() => host.Catalog.RegisterAsync(
                new TenantRegistration("Orphan", TenantCategory.Sandbox, 4242, null), "migrator", cancellationToken: Token));

            Assert.Equal((TenantCategory.Sandbox, live.Id), (sandbox.Category, sandbox.LiveTenantId));
            Assert.Null(standalone.LiveTenantId);
            Assert.Equal(TenantProblemCodes.RegistrationRefused, ofSandbox.Code);
            Assert.Equal(TenantProblemCodes.RegistrationRefused, ofNothing.Code);
        }

        [Fact]
        public async Task Every_write_replaces_the_catalog_version()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "versions");
            List<Guid> versions = [await host.VersionAsync()];

            TenantDescriptor tenant = await host.Catalog.RegisterAsync(new TenantRegistration("Acme", TenantCategory.Live, null, null), "migrator", cancellationToken: Token);
            versions.Add(await host.VersionAsync());
            _ = await host.Catalog.SetStateAsync(tenant.Id, TenantState.Active, null, "migrator", Token);
            versions.Add(await host.VersionAsync());
            await host.Catalog.RenameAsync(tenant.Id, new TenantDisplayNames("Acme", "أكمي", null, ["en", "ar"]), Token);
            versions.Add(await host.VersionAsync());
            _ = await host.Catalog.SetStateAsync(tenant.Id, TenantState.Frozen, "relocation", "operator@acme", Token);
            versions.Add(await host.VersionAsync());
            await host.Catalog.RelocateAsync(tenant.Id, new TenantLocation(sql.Server, "Tellma.it.moved", "default"), "migrator", Token);
            versions.Add(await host.VersionAsync());

            Assert.Equal(versions.Count, versions.Distinct().Count());
            Assert.Equal(versions[^1], host.Registry.SnapshotVersion);
        }

        [Fact]
        public async Task A_transition_moves_the_state_records_who_and_why_and_tells_the_registry()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "transition");
            TenantDescriptor tenant = await host.Catalog.RegisterAsync(new TenantRegistration("Acme", TenantCategory.Live, null, null), "migrator", cancellationToken: Token);

            TenantDescriptor active = await host.Catalog.SetStateAsync(tenant.Id, TenantState.Active, null, "migrator", Token);
            TenantDescriptor suspended = await host.Catalog.SetStateAsync(tenant.Id, TenantState.Suspended, "unpaid", "control-plane", Token);

            Assert.Equal(TenantState.Active, active.State);
            Assert.Equal(tenant with { State = TenantState.Suspended }, suspended);
            Assert.Equal("Suspended", await host.StateAsync(tenant.Id));
            Assert.Equal("unpaid", await SqlServerFixture.ScalarAsync<string>(host.ConnectionString, $"SELECT [StateReason] FROM [catalog].[Tenants] WHERE [Id] = {tenant.Id};"));
            Assert.Equal("control-plane", await SqlServerFixture.ScalarAsync<string>(host.ConnectionString, $"SELECT [StateChangedBy] FROM [catalog].[Tenants] WHERE [Id] = {tenant.Id};"));
            Assert.Equal(TenantState.Suspended, host.Registry.Get(tenant.Id).Descriptor.State);
        }

        [Fact]
        public async Task An_illegal_transition_is_refused_and_changes_nothing()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "illegal");
            TenantDescriptor tenant = await host.Catalog.RegisterAsync(new TenantRegistration("Acme", TenantCategory.Live, null, null), "migrator", cancellationToken: Token);
            Guid before = await host.VersionAsync();

            TenantStateException refused = await Assert.ThrowsAsync<TenantStateException>(() => host.Catalog.SetStateAsync(tenant.Id, TenantState.Suspended, null, "migrator", Token));

            Assert.Equal(TenantProblemCodes.IllegalTransition, refused.Code);
            Assert.Contains("from Provisioning to Suspended", refused.Detail, StringComparison.Ordinal);
            Assert.Equal("Provisioning", await host.StateAsync(tenant.Id));
            Assert.Equal(before, await host.VersionAsync());
            _ = await Assert.ThrowsAsync<TenantNotFoundException>(() => host.Catalog.SetStateAsync(4242, TenantState.Active, null, "migrator", Token));
        }

        [Fact]
        public async Task Of_two_concurrent_transitions_one_wins_and_the_other_is_refused()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "race");
            TenantDescriptor tenant = await host.Catalog.RegisterAsync(new TenantRegistration("Acme", TenantCategory.Live, null, null), "migrator", cancellationToken: Token);
            _ = await host.Catalog.SetStateAsync(tenant.Id, TenantState.Active, null, "migrator", Token);

            // Two instances' write sides, each suspending a tenant both last saw Active. Whichever
            // loses, by the statement's precondition or by reading the winner's state, is refused.
            await using CatalogHost other = await CatalogHost.ComposeAsync(sql, host.ConnectionString);
            Task<TenantDescriptor>[] both =
            [
                host.Catalog.SetStateAsync(tenant.Id, TenantState.Suspended, null, "a", Token),
                other.Catalog.SetStateAsync(tenant.Id, TenantState.Suspended, null, "b", Token),
            ];
            await Task.WhenAll(both).ContinueWith(static _ => { }, TaskScheduler.Default);

            _ = Assert.Single(both, static task => task.IsCompletedSuccessfully);
            Task loser = Assert.Single(both, static task => task.IsFaulted);
            Assert.Equal(TenantProblemCodes.IllegalTransition, Assert.IsType<TenantStateException>(loser.Exception!.InnerException).Code);
            Assert.Equal("Suspended", await host.StateAsync(tenant.Id));
        }

        [Fact]
        public async Task A_transition_validated_from_a_state_that_moved_under_it_loses_inside_the_transaction()
        {
            // Deterministic: a held transaction moves the state after the write side read it, and the
            // write's own precondition, not the earlier read, decides.
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "race_held");
            TenantDescriptor tenant = await host.Catalog.RegisterAsync(new TenantRegistration("Acme", TenantCategory.Live, null, null), "migrator", cancellationToken: Token);
            _ = await host.Catalog.SetStateAsync(tenant.Id, TenantState.Active, null, "migrator", Token);

            await using SqlConnection blocker = new(host.ConnectionString);
            await blocker.OpenAsync(Token);
            await using var transaction = (SqlTransaction)await blocker.BeginTransactionAsync(Token);
            await using (SqlCommand move = blocker.CreateCommand())
            {
                move.Transaction = transaction;
                move.CommandText = $"UPDATE [catalog].[Tenants] SET [State] = N'ReadOnly' WHERE [Id] = {tenant.Id}; SELECT @@SPID;";
                short spid = (short)(await move.ExecuteScalarAsync(Token))!;

                Task<TenantDescriptor> suspend = host.Catalog.SetStateAsync(tenant.Id, TenantState.Suspended, null, "a", Token);
                await WaitUntilBlockedByAsync(spid);
                await transaction.CommitAsync(Token);

                TenantStateException refused = await Assert.ThrowsAsync<TenantStateException>(() => suspend);
                Assert.Equal(TenantProblemCodes.IllegalTransition, refused.Code);
                Assert.Contains("concurrent transition", refused.Detail, StringComparison.Ordinal);
            }

            Assert.Equal("ReadOnly", await host.StateAsync(tenant.Id));
        }

        [Fact]
        public async Task A_rename_mirrors_names_and_codes_in_order()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "rename");
            TenantDescriptor tenant = await host.Catalog.RegisterAsync(new TenantRegistration("Acme", TenantCategory.Live, null, null), "migrator", cancellationToken: Token);

            await host.Catalog.RenameAsync(tenant.Id, new TenantDisplayNames("Acme", "أكمي", "አክሜ", ["en", "ar", "am"]), Token);
            TenantDescriptor renamed = host.Registry.Get(tenant.Id).Descriptor;
            await host.Catalog.RenameAsync(tenant.Id, new TenantDisplayNames("Acme Ltd", null, null, ["en"]), Token);

            Assert.Equal(("أكمي", "አክሜ"), (renamed.Name2, renamed.Name3));
            Assert.Equal(["en", "ar", "am"], renamed.Languages);
            Assert.Equal(new TenantDescriptor(tenant.Id, "Acme Ltd", null, null, ["en"], TenantCategory.Live, TenantState.Provisioning, null), host.Registry.Get(tenant.Id).Descriptor);
            _ = await Assert.ThrowsAsync<TenantNotFoundException>(() => host.Catalog.RenameAsync(4242, new TenantDisplayNames("x", null, null, ["en"]), Token));
        }

        [Fact]
        public async Task Only_a_frozen_or_provisioning_tenant_is_relocated_and_every_instance_sees_the_new_string()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "relocate");
            TenantDescriptor tenant = await host.Catalog.RegisterAsync(new TenantRegistration("Acme", TenantCategory.Live, null, null), "migrator", cancellationToken: Token);
            await using CatalogHost other = await CatalogHost.ComposeAsync(sql, host.ConnectionString);

            // Provisioning: allowed.
            await host.Catalog.RelocateAsync(tenant.Id, new TenantLocation(sql.Server, "Tellma.it.first", "default"), "migrator", Token);

            // Active: refused, and nothing changes.
            _ = await host.Catalog.SetStateAsync(tenant.Id, TenantState.Active, null, "migrator", Token);
            TenantStateException active = await Assert.ThrowsAsync<TenantStateException>(() => host.Catalog.RelocateAsync(
                tenant.Id, new TenantLocation(sql.Server, "Tellma.it.second", "default"), "migrator", Token));
            Assert.Equal(TenantProblemCodes.LocationRefused, active.Code);
            Assert.Contains("Initial Catalog=Tellma.it.first", host.Connections.GetConnectionString(tenant.Id), StringComparison.Ordinal);

            // Frozen: allowed; the other instance picks it up on its next refresh.
            _ = await host.Catalog.SetStateAsync(tenant.Id, TenantState.Frozen, null, "migrator", Token);
            await host.Catalog.RelocateAsync(tenant.Id, new TenantLocation(sql.Server, "Tellma.it.second", "default"), "migrator", Token);
            await other.Registry.RefreshAsync(force: false, Token);

            Assert.Contains("Initial Catalog=Tellma.it.second", host.Connections.GetConnectionString(tenant.Id), StringComparison.Ordinal);
            Assert.Contains("Initial Catalog=Tellma.it.second", other.Connections.GetConnectionString(tenant.Id), StringComparison.Ordinal);
            _ = await Assert.ThrowsAsync<TenantNotFoundException>(() => host.Catalog.RelocateAsync(4242, new TenantLocation(sql.Server, "x", "default"), "migrator", Token));
        }

        [Fact]
        public async Task A_relocation_to_a_server_outside_the_allowed_list_is_refused()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "relocate_server", static values => values["Tellma:Sql:AllowedServers:0"] = ".database.windows.net");

            // The suite's own server is not on the list, so a registration names an allowed host.
            TenantDescriptor tenant = await host.Catalog.RegisterAsync(
                new TenantRegistration("Acme", TenantCategory.Live, null, new TenantLocation("sql01.database.windows.net", "acme", "default")), "migrator", cancellationToken: Token);

            TenantStateException refused = await Assert.ThrowsAsync<TenantStateException>(() => host.Catalog.RelocateAsync(
                tenant.Id, new TenantLocation("attacker.example.com", "acme", "default"), "migrator", Token));

            Assert.Equal(TenantProblemCodes.LocationRefused, refused.Code);
            Assert.Equal("sql01.database.windows.net", (await host.Catalog.ListAsync(Token)).Single().Location.Server);
        }

        [Fact]
        public async Task The_listing_reads_the_store_and_shows_rows_the_snapshot_refuses_without_a_string()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "list", static values => values["Tellma:Sql:AllowedServers:0"] = ".database.windows.net");
            _ = await host.Catalog.RegisterAsync(
                new TenantRegistration("Acme", TenantCategory.Live, null, new TenantLocation("sql01.database.windows.net", "acme", "default")), "migrator", cancellationToken: Token);

            // A row written behind the platform's back, naming a host the deployment does not allow.
            await SqlServerFixture.ExecuteAsync(host.ConnectionString, """
                INSERT INTO [catalog].[Tenants] ([Id], [Name], [Language1], [Category], [State], [StateChangedAt], [Server], [Database], [CredentialProfile], [Version], [CreatedAt], [ModifiedAt])
                VALUES (5, N'Rogue', 'en', N'Live', N'Active', SYSUTCDATETIME(), N'attacker.example.com', N'rogue', N'default', NEWID(), SYSUTCDATETIME(), SYSUTCDATETIME());
                UPDATE [catalog].[CatalogState] SET [Version] = NEWID() WHERE [Id] = 1;
                """);
            await host.Registry.RefreshAsync(force: false, Token);

            IReadOnlyList<TenantInfo> listed = await host.Catalog.ListAsync(Token);
            Assert.Equal([5, 1000], listed.Select(static tenant => tenant.Id));
            Assert.Equal(string.Empty, listed[0].ConnectionString);
            Assert.NotEmpty(listed[1].ConnectionString);
            Assert.Null(host.Registry.Find(5));
            _ = Assert.Throws<TenantNotFoundException>(() => host.Connections.GetConnectionString(5));
            Assert.Contains(host.Logs.GetSnapshot(), static record => record.Id.Name == "TenantServerRefused" && record.Message.Contains("attacker.example.com", StringComparison.Ordinal));
        }

        /// <summary>Waits until a request of another session is blocked by the given one.</summary>
        /// <param name="blockingSpid">The session holding the lock.</param>
        /// <returns>A task that completes when a blocked request is seen.</returns>
        private async Task WaitUntilBlockedByAsync(short blockingSpid)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            while (await SqlServerFixture.ScalarAsync<int>(sql.MasterConnectionString, $"SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id = {blockingSpid};") == 0)
            {
                await Task.Delay(20, timeout.Token);
            }
        }
    }
}
