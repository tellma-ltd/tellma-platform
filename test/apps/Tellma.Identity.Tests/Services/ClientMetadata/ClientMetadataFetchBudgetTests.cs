// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using Tellma.Identity.Services;
using Tellma.Identity.Services.ClientMetadata;

namespace Tellma.Identity.Tests.Services.ClientMetadata
{
    /// <summary>
    ///     The resolver's fetch budgets under the attacks they exist for: anyone can name a client
    ///     id, so first-time ids, early re-fetches and a known id whose document keeps failing
    ///     must never spend what a host's known clients need to refresh, and a flood of slow
    ///     first-time fetches must never take the slots a known client's refresh runs in.
    /// </summary>
    public sealed class ClientMetadataFetchBudgetTests
    {
        private static readonly Uri ClientId = new(MetadataDocuments.ClientId);

        [Fact]
        public async Task Unknown_ids_spending_a_hosts_budget_do_not_stop_a_cached_client_from_refreshing()
        {
            using ResolverRig rig = new(static options =>
            {
                options.FetchesPerHostPerMinute = 1;
                options.MinCache = TimeSpan.FromSeconds(10);
            });
            rig.Server.Serve(MetadataDocuments.ClientId, MetadataDocuments.Public().ToJsonString());
            await rig.Resolver.ResolveAsync(ClientId, TestContext.Current.CancellationToken);

            // The client's copy expires while the host's first-time quota is still spent, and an
            // unknown id on the same host is refused for it.
            rig.Time.Advance(TimeSpan.FromSeconds(11));
            await AssertRefusedAsync(rig, new Uri("https://agent.example.com/x-1"), ClientMetadataRejectionReasons.RateLimited);

            // The known client refreshes on a budget of its own.
            ClientMetadataDocument? refreshed = await rig.Resolver.ResolveAsync(ClientId, TestContext.Current.CancellationToken);

            Assert.NotNull(refreshed);
            Assert.Equal(2, rig.Server.Requests.Count(static url => url == ClientId));
        }

        [Fact]
        public async Task A_stored_client_the_cache_has_forgotten_refreshes_on_its_own_budget()
        {
            using ResolverRig rig = new(static options => options.FetchesPerHostPerMinute = 1);
            rig.Server.Serve(MetadataDocuments.ClientId, MetadataDocuments.Public().ToJsonString());
            rig.Rows.Add(MetadataDocuments.ClientId);

            // After a restart the cache is empty; the row is what marks the client as known.
            await AssertRefusedAsync(rig, new Uri("https://agent.example.com/x-1"), ClientMetadataRejectionReasons.Status);
            await AssertRefusedAsync(rig, new Uri("https://agent.example.com/x-2"), ClientMetadataRejectionReasons.RateLimited);

            Assert.NotNull(await rig.Resolver.ResolveAsync(ClientId, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task A_row_holding_another_spelling_of_the_id_does_not_make_it_known()
        {
            using ResolverRig rig = new(static options => options.FetchesPerHostPerMinute = 1);
            rig.Server.Serve(MetadataDocuments.ClientId, MetadataDocuments.Public().ToJsonString());
            rig.Rows.Add("https://agent.example.com/OAUTH/CLIENT.JSON");

            // The store's comparison ignores case, but a case variant is another client id, and
            // anyone could mint one per known client to draw on the refresh budget.
            await AssertRefusedAsync(rig, new Uri("https://agent.example.com/x-1"), ClientMetadataRejectionReasons.Status);
            await AssertRefusedAsync(rig, ClientId, ClientMetadataRejectionReasons.RateLimited);
            Assert.Equal(2, rig.Rows.Lookups);
        }

        [Fact]
        public async Task An_early_refetch_draws_on_the_first_time_budget_not_the_refresh_budget()
        {
            using ResolverRig rig = new(static options =>
            {
                options.FetchesPerHostPerMinute = 1;
                options.MinCache = TimeSpan.FromSeconds(10);
            });
            rig.Server.Serve(MetadataDocuments.ClientId, MetadataDocuments.Public().ToJsonString());
            await rig.Resolver.ResolveAsync(ClientId, TestContext.Current.CancellationToken);
            rig.Time.Advance(TimeSpan.FromSeconds(11));

            // Anyone can ask for an early re-fetch by naming a redirect URI the document does not
            // list; it is refused on the spent first-time quota rather than taking the refresh.
            ClientMetadataException early = await Assert.ThrowsAsync<ClientMetadataException>(() =>
                rig.Resolver.RevalidateAsync(ClientId, TestContext.Current.CancellationToken));
            Assert.Equal(ClientMetadataRejectionReasons.RateLimited, early.Reason);

            Assert.NotNull(await rig.Resolver.ResolveAsync(ClientId, TestContext.Current.CancellationToken));
            Assert.Equal(2, rig.Server.Requests.Count);
        }

        [Fact]
        public async Task A_known_id_whose_document_keeps_failing_draws_on_the_refresh_budget_once_per_window()
        {
            const string Failing = "https://agent.example.com/oauth/gone.json";
            using ResolverRig rig = new(static options => options.FetchesPerHostPerMinute = 2);
            rig.Server.Serve(MetadataDocuments.ClientId, MetadataDocuments.Public().ToJsonString());
            rig.Rows.Add(Failing);
            rig.Rows.Add(MetadataDocuments.ClientId);

            // A stored client whose document is gone fails on every request. Its first fetch is a
            // refresh, its repeats are probes, so naming it over and over spends only the probes.
            await AssertRefusedAsync(rig, new Uri(Failing), ClientMetadataRejectionReasons.Status);
            await AssertRefusedAsync(rig, new Uri(Failing), ClientMetadataRejectionReasons.Status);
            await AssertRefusedAsync(rig, new Uri(Failing), ClientMetadataRejectionReasons.Status);
            await AssertRefusedAsync(rig, new Uri(Failing), ClientMetadataRejectionReasons.RateLimited);

            Assert.NotNull(await rig.Resolver.ResolveAsync(ClientId, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task A_first_time_fetch_finding_no_free_slot_is_refused_while_a_refresh_still_runs()
        {
            using ResolverRig rig = new(static options => options.MaxConcurrentFetches = 1);
            TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Block(rig, "https://slow.example.com/c.json", started, release);
            rig.Server.Serve("https://other.example.com/c.json", MetadataDocuments.Public("https://other.example.com/c.json").ToJsonString());
            rig.Server.Serve(MetadataDocuments.ClientId, MetadataDocuments.Public().ToJsonString());
            rig.Rows.Add(MetadataDocuments.ClientId);

            // One slow first-time fetch holds the only first-time slot.
            Task<ClientMetadataDocument?> slow = rig.Resolver.ResolveAsync(new Uri("https://slow.example.com/c.json"), TestContext.Current.CancellationToken);
            await started.Task.WaitAsync(TestContext.Current.CancellationToken);

            // Another first-time id, on another host, is refused without a request...
            await AssertRefusedAsync(rig, new Uri("https://other.example.com/c.json"), ClientMetadataRejectionReasons.RateLimited);
            Assert.DoesNotContain(rig.Server.Requests, static url => url.Host == "other.example.com");

            // ...while a known client's refresh has slots of its own.
            Assert.NotNull(await rig.Resolver.ResolveAsync(ClientId, TestContext.Current.CancellationToken));

            release.SetResult();
            Assert.NotNull(await slow);
        }

        [Fact]
        public async Task A_refresh_waits_for_a_free_slot_and_gives_up_at_the_timeout()
        {
            const string First = "https://agent.example.com/oauth/first.json";
            const string Second = "https://agent.example.com/oauth/second.json";
            const string Third = "https://agent.example.com/oauth/third.json";
            using ResolverRig rig = new(static options =>
            {
                options.MaxConcurrentFetches = 1;
                options.Timeout = TimeSpan.FromSeconds(5);
            });
            TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Block(rig, First, started, release);
            rig.Server.Serve(Second, MetadataDocuments.Public(Second).ToJsonString());
            rig.Server.Serve(Third, MetadataDocuments.Public(Third).ToJsonString());
            rig.Rows.Add(First);
            rig.Rows.Add(Second);
            rig.Rows.Add(Third);

            Task<ClientMetadataDocument?> first = rig.Resolver.ResolveAsync(new Uri(First), TestContext.Current.CancellationToken);
            await started.Task.WaitAsync(TestContext.Current.CancellationToken);

            // A refresh with no free slot waits rather than failing at once...
            Task<ClientMetadataDocument?> second = rig.Resolver.ResolveAsync(new Uri(Second), TestContext.Current.CancellationToken);
            Assert.NotSame(second, await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken)));

            // ...but no longer than the fetch timeout.
            rig.Time.Advance(TimeSpan.FromSeconds(5));
            ClientMetadataException timedOut = await Assert.ThrowsAsync<ClientMetadataException>(() => second);
            Assert.Equal(ClientMetadataRejectionReasons.RateLimited, timedOut.Reason);
            Assert.DoesNotContain(rig.Server.Requests, static url => url.AbsoluteUri == Second);

            // A waiting refresh takes the slot as soon as it frees.
            Task<ClientMetadataDocument?> third = rig.Resolver.ResolveAsync(new Uri(Third), TestContext.Current.CancellationToken);
            release.SetResult();
            Assert.NotNull(await third);

            // The first fetch's own timeout fired during the advance; how it ended is not the point.
            _ = await Record.ExceptionAsync(() => first);
        }

        /// <summary>Makes a URL's fetch hang, ignoring cancellation, until released, then answer with its document.</summary>
        private static void Block(ResolverRig rig, string url, TaskCompletionSource started, TaskCompletionSource release)
        {
            string body = MetadataDocuments.Public(url).ToJsonString();
            rig.Server.Handle(url, async (_, _) =>
            {
                started.TrySetResult();
                await release.Task;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
                };
            });
        }

        /// <summary>Asserts a resolution is refused under a reason.</summary>
        private static async Task AssertRefusedAsync(ResolverRig rig, Uri clientId, string reason)
        {
            ClientMetadataException exception = await Assert.ThrowsAsync<ClientMetadataException>(() =>
                rig.Resolver.ResolveAsync(clientId, TestContext.Current.CancellationToken));
            Assert.Equal(reason, exception.Reason);
        }
    }
}
