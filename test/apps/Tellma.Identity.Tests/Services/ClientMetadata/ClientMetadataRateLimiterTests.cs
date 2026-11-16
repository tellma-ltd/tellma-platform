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
        public void A_refresh_that_succeeded_gives_its_claim_back()
        {
            using ResolverRig rig = new();

            Assert.True(rig.RateLimiter.TryClaimRefresh(MetadataDocuments.ClientId));
            rig.RateLimiter.ReleaseRefresh(MetadataDocuments.ClientId);

            Assert.True(rig.RateLimiter.TryClaimRefresh(MetadataDocuments.ClientId));
        }

        [Fact]
        public async Task Each_budget_has_its_own_slots_and_a_probe_never_waits_for_one()
        {
            using ResolverRig rig = new(static options => options.MaxConcurrentFetches = 1);

            Assert.True(await rig.RateLimiter.TryEnterAsync(ClientMetadataFetchBudget.Probe, "a.example.com"));
            Assert.False(await rig.RateLimiter.TryEnterAsync(ClientMetadataFetchBudget.Probe, "b.example.com"));
            Assert.True(await rig.RateLimiter.TryEnterAsync(ClientMetadataFetchBudget.Refresh, "b.example.com"));

            rig.RateLimiter.Exit(ClientMetadataFetchBudget.Probe, "a.example.com");
            Assert.True(await rig.RateLimiter.TryEnterAsync(ClientMetadataFetchBudget.Probe, "b.example.com"));
        }

        [Fact]
        public async Task One_host_holds_at_most_its_share_of_a_budgets_slots()
        {
            using ResolverRig rig = new();
            const string slow = "slow.example.com";

            for (int i = 0; i < ClientMetadataRateLimiter.MaxInFlightPerHost; i++)
            {
                Assert.True(await rig.RateLimiter.TryEnterAsync(ClientMetadataFetchBudget.Probe, slow));
            }

            // The host's share is taken; other hosts are not held back by it, in either case
            // spelling of the host.
            Assert.False(await rig.RateLimiter.TryEnterAsync(ClientMetadataFetchBudget.Probe, slow));
            Assert.False(await rig.RateLimiter.TryEnterAsync(ClientMetadataFetchBudget.Probe, "SLOW.example.com"));
            Assert.True(await rig.RateLimiter.TryEnterAsync(ClientMetadataFetchBudget.Probe, "other.example.com"));

            rig.RateLimiter.Exit(ClientMetadataFetchBudget.Probe, slow);
            Assert.True(await rig.RateLimiter.TryEnterAsync(ClientMetadataFetchBudget.Probe, slow));
        }

        [Fact]
        public async Task A_refresh_waits_for_its_hosts_share_up_to_the_fetch_timeout()
        {
            using ResolverRig rig = new();
            const string host = "agent.example.com";
            for (int i = 0; i < ClientMetadataRateLimiter.MaxInFlightPerHost; i++)
            {
                Assert.True(await rig.RateLimiter.TryEnterAsync(ClientMetadataFetchBudget.Refresh, host));
            }

            // A slot the host gives back is taken by the waiting refresh.
            Task<bool> waiting = rig.RateLimiter.TryEnterAsync(ClientMetadataFetchBudget.Refresh, host);
            Assert.False(waiting.IsCompleted);
            rig.RateLimiter.Exit(ClientMetadataFetchBudget.Refresh, host);
            Assert.True(await waiting);

            // With none given back, the wait ends at the fetch timeout.
            Task<bool> timedOut = rig.RateLimiter.TryEnterAsync(ClientMetadataFetchBudget.Refresh, host);
            rig.Time.Advance(rig.Options.ClientMetadata.Timeout);
            Assert.False(await timedOut);
        }
    }
}
