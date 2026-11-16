// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.IntegrationTests.Access
{
    /// <summary>
    ///     A frozen tenant's database is read-only. Its callers connect and read through the
    ///     guarded runner with the real subject prologue, whose bookkeeping writes never execute.
    ///     Nightly: changing a database's state is slow and drops its sessions.
    /// </summary>
    /// <param name="fixture">The two tenant databases and their composition; this class owns them.</param>
    [Trait("Category", "Integration")]
    [Trait("Category", "Nightly")]
    public sealed class FrozenTenantTests(ConnectDatabase fixture) : IClassFixture<ConnectDatabase>
    {
        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_frozen_tenants_read_only_database_connects_an_invited_caller_and_reads_with_no_prologue_write()
        {
            (int userId, string subject) = await fixture.SeedCallerAsync("Vic", joined: false);

            // --- The database is read-only, as a frozen tenant's is ---
            await fixture.SetReadOnlyAsync(ConnectDatabase.TenantA, readOnly: true);
            try
            {
                // --- A cold connect and a guarded read: a write would fail with 3906 ---
                await using AsyncServiceScope scope = await fixture.ScopeAsync(subject, state: TenantState.Frozen);
                (ConnectedUser user, int roundTrips) = await scope.ServiceProvider.GetRequiredService<IGuardedBatchRunner>().RunAsync(
                    BatchPurpose.Read,
                    static (user, batch) =>
                    {
                        _ = batch.Sql($"SELECT 1 AS [One];", new SqlOptions(resultSets: 1));
                        return ValueTask.CompletedTask;
                    },
                    static (user, outcome) => (user, outcome.RoundTrips),
                    Token);

                Assert.Equal((userId, UserState.Invited, 1), (user.UserId, user.State, roundTrips));
            }
            finally
            {
                await fixture.SetReadOnlyAsync(ConnectDatabase.TenantA, readOnly: false);
            }

            // --- Nothing was stamped and nothing flipped ---
            Assert.Null(await fixture.ScalarAsync<DateTimeOffset?>(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"SELECT [LastActiveAt] FROM [core].[UserStamps] WHERE [UserId] = {userId};")));
            Assert.Null(await fixture.ScalarAsync<DateTimeOffset?>(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"SELECT [JoinedAt] FROM [core].[Users] WHERE [Id] = {userId};")));
        }
    }
}
