// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Blobs;
using Tellma.Core.Data.Batches;
using Tellma.Core.IntegrationTests.Access;
using Tellma.Core.IntegrationTests.Data;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Blobs
{
    /// <summary>
    ///     The statements that meet a confirm outside a save: the reconcile, which releases a
    ///     committed blob no owner row references once its day of grace passed and leaves a young
    ///     orphan and a referenced blob alone; and the sweep's claim, as the sweep composes it, under
    ///     read-committed snapshot, which blocks on the row a save's confirm holds and, once the save
    ///     commits, skips it.
    /// </summary>
    /// <param name="fixture">The tenant databases and the seeding helpers.</param>
    [Trait("Category", "Integration")]
    public sealed class BlobLifecycleStatementTests(BlobReferenceDatabase fixture) : IClassFixture<BlobReferenceDatabase>
    {
        /// <summary>The application lock the race's save waits on, holding its confirm's lock; the save's statement names it literally.</summary>
        private const string Gate = "fixture-blob-race";

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task The_reconcile_releases_an_old_orphan_and_leaves_a_young_one_and_a_referenced_blob_alone()
        {
            int orphan = await fixture.CommitAsync(BlobReferenceDatabase.FileKind, "DATEADD(day, -2, SYSUTCDATETIME())");
            int young = await fixture.CommitAsync(BlobReferenceDatabase.FileKind, "DATEADD(hour, -1, SYSUTCDATETIME())");
            int referenced = await fixture.CommitAsync(BlobReferenceDatabase.FileKind, "DATEADD(day, -2, SYSUTCDATETIME())");
            int otherKind = await fixture.CommitAsync(BlobReferenceDatabase.PhotoKind, "DATEADD(day, -2, SYSUTCDATETIME())");
            await fixture.Connect.ExecuteAsync(
                ConnectDatabase.TenantA,
                DataQueryDatabase.Stamped("[fixture].[BlobOwners]", "[Id], [Name], [FileId]", FormattableString.Invariant($"({referenced}, N'referenced', {referenced})")));

            // --- The file kind's reconcile releases its one old orphan; a second run releases nothing more ---
            Assert.Equal(1, await ReconcileAsync(BlobReferenceDatabase.FileKind));
            Assert.Equal(0, await ReconcileAsync(BlobReferenceDatabase.FileKind));

            Assert.Equal(
                ("Released|c|e", "Committed|c|-", "Committed|c|-", "Committed|c|-"),
                (await fixture.StateAsync(orphan), await fixture.StateAsync(young), await fixture.StateAsync(referenced), await fixture.StateAsync(otherKind)));
        }

        [Fact]
        public async Task The_sweeps_claim_blocks_on_a_row_a_save_is_confirming_and_skips_it_once_the_save_commits()
        {
            // The second tenant holds no other blob, so the claim finds this row alone.
            const int Tenant = ConnectDatabase.TenantB;
            (int userId, string subject) = await fixture.SeedCallerAsync(Tenant, ("fixture.BlobOwner", AccessActions.Read, null), ("fixture.BlobOwner", AccessActions.Save, null));
            int file = await fixture.StageAsync(userId, BlobReferenceDatabase.FileKind, "DATEADD(minute, 30, SYSUTCDATETIME())", Tenant);

            // --- The gate the save's persist waits on once its confirm ran, held on a connection of the test's ---
            await using SqlConnection gate = await OpenAsync(Tenant);
            await using var held = (SqlTransaction)await gate.BeginTransactionAsync(Token);
            await ExecuteAsync(gate, held, $"EXEC sp_getapplock @Resource = N'{Gate}', @LockMode = N'Exclusive', @LockOwner = N'Transaction';");

            // --- The save: its confirm commits the row in the persist's transaction, then it waits at the gate ---
            Task<EntitiesResult<BlobOwner>> saving = Task.Run(async () =>
            {
                await using AsyncServiceScope scope = await fixture.ScopeAsync(subject, Tenant);
                SaveOptions options = new() { ReturnEntities = false, OnPersist = static batch => WaitAtGate(batch) };
                return await scope.ServiceProvider.GetRequiredService<EntityService<BlobOwner, int>>().SaveAsync([new BlobOwner { Name = "racing", FileId = file }], options, Token);
            }, Token);
            await WaitUntilAsync(Tenant, $"SELECT COUNT(*) FROM sys.dm_exec_requests WHERE [wait_type] LIKE N'LCK_M_%' AND [wait_resource] LIKE N'%{Gate}%';", saving);

            // --- The claim, on a second connection, sees the row due in its snapshot and blocks on the confirm's lock ---
            await using SqlConnection sweeper = await OpenAsync(Tenant);
            int spid = await ScalarAsync(sweeper, "SELECT @@SPID;");

            // The property is the one under read-committed snapshot, where the claim's read sees the row as it was before the confirm.
            Assert.Equal(1, await ScalarAsync(sweeper, "SELECT CAST([is_read_committed_snapshot_on] AS int) FROM sys.databases WHERE [name] = DB_NAME();"));
            Task<List<int>> claiming = ClaimAsync(sweeper, await ClaimCommandAsync(Tenant));
            await WaitUntilAsync(Tenant, FormattableString.Invariant($"SELECT COUNT(*) FROM sys.dm_exec_requests WHERE [session_id] = {spid} AND [wait_type] LIKE N'LCK_M_%' AND [blocking_session_id] <> 0;"), claiming);
            Assert.False(claiming.IsCompleted, "The claim did not wait for the confirm's lock.");

            // --- The gate opens: the save commits, and the claim re-reads the row and skips it ---
            await held.CommitAsync(Token);
            _ = await saving;
            Assert.Empty(await claiming);
            Assert.Equal("Committed|c|-", await fixture.StateAsync(file, Tenant));
        }

        /// <summary>The save's caller statement that waits at the gate inside the persist's transaction.</summary>
        /// <param name="batch">The persist batch.</param>
        private static void WaitAtGate(IDataBatch batch)
        {
            // The statement writes nothing: it takes a shared hold of the gate the test keeps exclusive,
            // so the persist's transaction stays open, its confirm's row lock held, until the test opens it.
            _ = batch.Sql(
                $"EXEC sp_getapplock @Resource = N'fixture-blob-race', @LockMode = N'Shared', @LockOwner = N'Transaction', @LockTimeout = 60000;",
                new SqlOptions());
        }

        /// <summary>Runs the reconcile of a kind on a maintenance batch of the platform's own scope.</summary>
        /// <param name="kind">The kind.</param>
        /// <returns>How many blobs it released.</returns>
        private async Task<int> ReconcileAsync(string kind)
        {
            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject: null, kind: PrincipalKind.System, background: true, services: fixture.Instance);
            BlobKindDescriptor descriptor = scope.ServiceProvider.GetRequiredService<IBlobKindRegistry>().Find(kind) ?? throw new InvalidOperationException($"No kind '{kind}'.");
            var batch = (DataBatch)scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Maintenance);
            BatchResult<int> released = BlobStatements.Reconcile(batch, descriptor);
            _ = await batch.ExecuteAsync(Token);
            return released.Value;
        }

        /// <summary>
        ///     The sweep's claim, as the sweep job composes it on a maintenance batch of a tenant, its
        ///     declared instant moved an hour past the database's clock so a row staged for half an
        ///     hour is due, and its parameters bound.
        /// </summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The command's text and parameters.</returns>
        private async Task<(string Text, SqlParameter[] Parameters)> ClaimCommandAsync(int tenantId)
        {
            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject: null, kind: PrincipalKind.System, tenantId: tenantId, background: true, services: fixture.Instance);
            var batch = (DataBatch)scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Maintenance);
            _ = BlobStatements.Claim(batch, 100, TimeSpan.FromMinutes(60));

            // --- The claim follows the batch's own preamble; its declared instant is moved an hour ahead ---
            BatchStatement statement = batch.Statements[^1];
            string instant = string.Create(CultureInfo.InvariantCulture, $"DECLARE @tb{statement.Ordinal}_now datetimeoffset(3) = SYSUTCDATETIME();");
            Assert.StartsWith(instant, statement.Text, StringComparison.Ordinal);
            string text = string.Create(CultureInfo.InvariantCulture, $"DECLARE @tb{statement.Ordinal}_now datetimeoffset(3) = DATEADD(hour, 1, SYSUTCDATETIME());") + statement.Text[instant.Length..];
            return (text, [.. statement.Parameters.Select(static parameter => parameter.Create())]);
        }

        /// <summary>Runs the claim on a connection and returns the ids it claimed.</summary>
        /// <param name="connection">The connection.</param>
        /// <param name="claim">The claim's text and parameters.</param>
        /// <returns>The claimed ids.</returns>
        private static async Task<List<int>> ClaimAsync(SqlConnection connection, (string Text, SqlParameter[] Parameters) claim)
        {
            await using SqlCommand command = new(claim.Text, connection) { CommandTimeout = 60 };
            command.Parameters.AddRange(claim.Parameters);
            await using SqlDataReader reader = await command.ExecuteReaderAsync(Token);
            List<int> claimed = [];
            while (await reader.ReadAsync(Token))
            {
                claimed.Add(reader.GetInt32(0));
            }

            return claimed;
        }

        /// <summary>
        ///     Waits until a count over a tenant's database is positive, failing early when the task it
        ///     waits for ended first, for 30 seconds at most.
        /// </summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="count">The count.</param>
        /// <param name="running">The task that must still be running.</param>
        /// <returns>A task that completes when the count is positive.</returns>
        private async Task WaitUntilAsync(int tenantId, string count, Task running)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (await fixture.Connect.ScalarAsync<int>(tenantId, count) == 0)
            {
                if (running.IsCompleted)
                {
                    await running;
                    Assert.Fail("The task ended before it waited for a lock.");
                }

                Assert.True(DateTime.UtcNow < deadline, "No request waited for the lock within 30 seconds.");
                await Task.Delay(TimeSpan.FromMilliseconds(25), Token);
            }
        }

        /// <summary>Opens a connection to a tenant's database.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The open connection.</returns>
        private async Task<SqlConnection> OpenAsync(int tenantId)
        {
            SqlConnection connection = new(fixture.Connect.ConnectionString(tenantId));
            await connection.OpenAsync(Token);
            return connection;
        }

        /// <summary>Runs a statement on a connection, in a transaction.</summary>
        /// <param name="connection">The connection.</param>
        /// <param name="transaction">The transaction.</param>
        /// <param name="statement">The statement.</param>
        /// <returns>A task that completes when the statement ran.</returns>
        private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction transaction, string statement)
        {
            await using SqlCommand command = new(statement, connection, transaction);
            _ = await command.ExecuteNonQueryAsync(Token);
        }

        /// <summary>Reads one integer on a connection.</summary>
        /// <param name="connection">The connection.</param>
        /// <param name="query">The query.</param>
        /// <returns>The integer.</returns>
        private static async Task<int> ScalarAsync(SqlConnection connection, string query)
        {
            await using SqlCommand command = new(query, connection);
            return Convert.ToInt32(await command.ExecuteScalarAsync(Token), CultureInfo.InvariantCulture);
        }
    }
}
