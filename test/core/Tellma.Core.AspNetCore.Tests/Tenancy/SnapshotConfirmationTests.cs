// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Tenancy;
using Tellma.Testing.WebHost;

namespace Tellma.Core.AspNetCore.Tests.Tenancy
{
    /// <summary>
    ///     The test host's clock moves that confirm the tenant snapshot again: they end only once the
    ///     registry has confirmed it at the new reading, and a refresh that fails fails the move.
    /// </summary>
    public sealed class SnapshotConfirmationTests
    {
        [Fact]
        public async Task A_long_move_ends_only_once_the_snapshot_is_confirmed_at_the_new_reading()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            ITenantRegistry registry = host.Services.GetRequiredService<ITenantRegistry>();
            TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            host.Catalog.Gate = gate;

            // --- While the catalog holds the refresh, the move has not ended and the snapshot is stale ---
            Task moved = host.AdvanceAsync(TimeSpan.FromDays(4));
            await Patience.WaitAsync(host.Catalog.AtGate.Task);
            Assert.False(moved.IsCompleted);
            Assert.False(registry.IsAvailable);

            // --- Released, the move ends with the snapshot confirmed at the clock's reading ---
            // The gate goes first, so a refresh queued behind the held one passes straight through.
            host.Catalog.Gate = null;
            gate.SetResult();
            await Patience.WaitAsync(moved);
            Assert.Equal(host.Time.GetUtcNow(), registry.SnapshotLoadedAt);
            Assert.True(registry.IsAvailable);
        }

        [Fact]
        public async Task A_move_whose_refresh_fails_fails_with_the_catalog_s_error()
        {
            // A test that wants the snapshot stale keeps the clock's own move.
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            InvalidOperationException outage = new("The catalog is down.");
            host.Catalog.Failure = outage;

            Assert.Same(outage, await Assert.ThrowsAsync<InvalidOperationException>(() => host.AdvanceAsync(TimeSpan.FromMinutes(6))));
            Assert.False(host.Services.GetRequiredService<ITenantRegistry>().IsAvailable);
        }
    }
}
