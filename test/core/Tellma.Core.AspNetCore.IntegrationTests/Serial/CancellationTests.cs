// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics;
using System.Text.Json.Nodes;
using Tellma.Core.AspNetCore.IntegrationTests.Infrastructure;
using Tellma.Core.AspNetCore.IntegrationTests.Stacks;
using Tellma.Distro.Acme.TestSupport;

namespace Tellma.Core.AspNetCore.IntegrationTests.Serial
{
    /// <summary>
    ///     What a browser's abort does on the server: a read waiting on a lock is cancelled there and
    ///     leaves the server at once, while a save whose persist was already sent still commits once
    ///     the lock is released. Each lock is a real one another session holds on the live tenant's
    ///     centers, which every other case would wait on, so the cases run alone. The save is sent to
    ///     an instance listening on a loopback port, so its abort is a closed connection.
    /// </summary>
    /// <param name="deployment">The suite's deployment.</param>
    [Trait("Category", "Integration")]
    public sealed class CancellationTests(AcmeDeployment deployment)
    {
        private const int Tenant = AcmeDeployment.LiveTenant;

        [Fact]
        public async Task An_aborted_read_waiting_on_a_lock_is_cancelled_on_the_server()
        {
            using SpaClient admin = await deployment.SignInAsync(deployment.Administrator);
            _ = await admin.PostAsync(Tenant, "users/me");

            // --- A schema lock on the centers, which even a snapshot read waits on ---
            await using SqlLock schema = await SqlLock.HoldAsync(
                deployment.Acme.TenantConnectionString(Tenant), "ALTER TABLE [gl].[Centers] SET (LOCK_ESCALATION = AUTO);");
            using var abort = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            Task<ApiResponse> read = admin.PostAsync(Tenant, "centers/query", new JsonObject { ["select"] = "Code", ["take"] = 1 }, cancellationToken: abort.Token);
            Assert.Contains("[gl].[Centers]", await schema.AwaitWaiterAsync(), StringComparison.OrdinalIgnoreCase);

            // --- The browser aborts: the server's statement goes while the lock is still held ---
            await abort.CancelAsync();
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
            await schema.AwaitNoWaiterAsync();
        }

        [Fact]
        public async Task A_save_aborted_after_its_persist_was_sent_still_commits()
        {
            // --- A second instance listening on a loopback port: a browser's abort closes its
            // connection, which only a real server observes as a real client sends it ---
            AcmeHost instance = await AcmeHost.StartAsync(deployment.Acme.Values, new AcmeHostOptions { UsesKestrel = true });
            await using (instance)
            {
                using SpaClient admin = new(instance, await WebSessions.SignInAsync(instance, deployment.Administrator));
                int root = await CenterOperationTests.RootAsync(admin);
                _ = await CenterOperationTests.CreateAsync(admin, "CA-WARM", "Warms the id buffer", root);

                // --- An exclusive lock on the centers: a snapshot read passes it, the persist waits ---
                SqlLock table = await SqlLock.HoldAsync(
                    deployment.Acme.TenantConnectionString(Tenant), "SELECT TOP (0) [Id] FROM [gl].[Centers] WITH (TABLOCKX, HOLDLOCK);");
                await using (table)
                {
                    using var abort = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
                    Task<ApiResponse> save = admin.PostAsync(
                        Tenant, "centers/save", Wire.Save(CenterOperationTests.Center(0, "CA-SENT", "Sent before the abort", "Sale", root)), cancellationToken: abort.Token);

                    // The waiting statement is the persist, which writes the centers and commits.
                    string waiting = await table.AwaitWaiterAsync();
                    Assert.Contains("INSERT INTO [gl].[Centers]", waiting, StringComparison.OrdinalIgnoreCase);

                    // --- The browser aborts while the persist waits; then the lock is released ---
                    await abort.CancelAsync();
                    _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => save);
                    await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
                    Assert.True((await table.WaitersAsync()).Count > 0, "The persist sent before the abort left the server with it.");
                    await table.ReleaseAsync();
                }

                // --- The persist ran to its commit ---
                var elapsed = Stopwatch.StartNew();
                while (await deployment.ScalarAsync<int>(Tenant, "SELECT COUNT(*) FROM [gl].[Centers] WHERE [Code] = N'CA-SENT';") == 0)
                {
                    Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(30), "The persist sent before the abort never committed.\n" + instance.Warnings);
                    await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
                }
            }
        }
    }
}
