// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Identity.Services.ClientMetadata;

namespace Tellma.Identity.Tests.Services.ClientMetadata
{
    /// <summary>
    ///     The fetch limits on their own: per-host quotas per budget that slide with the clock,
    ///     bounded tables that forget the least recently used host instead of refusing a new one,
    ///     one refresh claim per client id per window, and the resolution slots.
    /// </summary>
    public sealed class ClientMetadataRateLimiterTests
    {
        [Theory]
        [InlineData(ClientMetadataFetchBudget.Refresh)]
        [InlineData(ClientMetadataFetchBudget.Probe)]
        public void A_hosts_quota_slides_with_the_clock(ClientMetadataFetchBudget budget)
        {
            using ResolverRig rig = new(static options => options.FetchesPerHostPerMinute = 1);

            Assert.True(rig.RateLimiter.TryAcquire(budget, "agent.example.com"));
            Assert.False(rig.RateLimiter.TryAcquire(budget, "AGENT.example.com"));

            rig.Time.Advance(TimeSpan.FromSeconds(59));
            Assert.False(rig.RateLimiter.TryAcquire(budget, "agent.example.com"));

            rig.Time.Advance(TimeSpan.FromSeconds(1));
            Assert.True(rig.RateLimiter.TryAcquire(budget, "agent.example.com"));
        }

        [Fact]
        public void The_two_budgets_of_a_host_are_counted_apart()
        {
            using ResolverRig rig = new(static options => options.FetchesPerHostPerMinute = 1);

            Assert.True(rig.RateLimiter.TryAcquire(ClientMetadataFetchBudget.Probe, "agent.example.com"));
            Assert.False(rig.RateLimiter.TryAcquire(ClientMetadataFetchBudget.Probe, "agent.example.com"));

            Assert.True(rig.RateLimiter.TryAcquire(ClientMetadataFetchBudget.Refresh, "agent.example.com"));
            Assert.False(rig.RateLimiter.TryAcquire(ClientMetadataFetchBudget.Refresh, "agent.example.com"));
        }

        [Fact]
        public void A_flood_of_hosts_never_refuses_a_new_host_and_keeps_the_table_bounded()
        {
            using ResolverRig rig = new(static options => options.FetchesPerHostPerMinute = 1);
            for (int host = 0; host < ClientMetadataRateLimiter.MaxTracked; host++)
            {
                Assert.True(rig.RateLimiter.TryAcquire(ClientMetadataFetchBudget.Probe, "h" + host + ".example.com"));
            }

            // The first host is used again (and refused, its quota spent), so the second is now
            // the least recently used.
            Assert.False(rig.RateLimiter.TryAcquire(ClientMetadataFetchBudget.Probe, "h0.example.com"));

            // Every tracked host is inside its window, yet a new host is served: the least
            // recently used one is forgotten instead, and the table does not grow.
            Assert.True(rig.RateLimiter.TryAcquire(ClientMetadataFetchBudget.Probe, "new.example.com"));
            Assert.Equal(ClientMetadataRateLimiter.MaxTracked, rig.RateLimiter.TrackedHosts(ClientMetadataFetchBudget.Probe));

            // The forgotten host starts afresh; the one used recently is still counted.
            Assert.True(rig.RateLimiter.TryAcquire(ClientMetadataFetchBudget.Probe, "h1.example.com"));
            Assert.False(rig.RateLimiter.TryAcquire(ClientMetadataFetchBudget.Probe, "h0.example.com"));

            // A flood in one budget never touches the other's table.
            Assert.Equal(0, rig.RateLimiter.TrackedHosts(ClientMetadataFetchBudget.Refresh));
        }

        [Fact]
        public void A_client_id_claims_one_refresh_per_window()
        {
            using ResolverRig rig = new();

            Assert.True(rig.RateLimiter.TryClaimRefresh(MetadataDocuments.ClientId));
            Assert.False(rig.RateLimiter.TryClaimRefresh(MetadataDocuments.ClientId));

            // Client ids compare exactly: another spelling is another id.
            Assert.True(rig.RateLimiter.TryClaimRefresh(MetadataDocuments.ClientId.ToUpperInvariant()));

            rig.Time.Advance(ClientMetadataRateLimiter.Window);
            Assert.True(rig.RateLimiter.TryClaimRefresh(MetadataDocuments.ClientId));
        }

        [Fact]
        public async Task Each_budget_has_its_own_slots_and_a_probe_never_waits_for_one()
        {
            using ResolverRig rig = new(static options => options.MaxConcurrentFetches = 1);

            Assert.True(await rig.RateLimiter.TryEnterAsync(ClientMetadataFetchBudget.Probe));
            Assert.False(await rig.RateLimiter.TryEnterAsync(ClientMetadataFetchBudget.Probe));
            Assert.True(await rig.RateLimiter.TryEnterAsync(ClientMetadataFetchBudget.Refresh));

            rig.RateLimiter.Exit(ClientMetadataFetchBudget.Probe);
            Assert.True(await rig.RateLimiter.TryEnterAsync(ClientMetadataFetchBudget.Probe));
        }
    }
}
