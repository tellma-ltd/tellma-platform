// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Services;
using Tellma.Identity.Services.Audit;
using Tellma.Identity.Services.ClientMetadata;

namespace Tellma.Identity.Tests.Services.ClientMetadata
{
    /// <summary>
    ///     The resolver: what it serves from the cache and for how long, when it fetches again,
    ///     that one fetch serves every concurrent request, that a failed fetch serves a standing
    ///     copy and nothing once that copy expires, and what each outcome audits and counts —
    ///     the counter carrying the outcome alone, never the client id.
    /// </summary>
    public sealed class ClientMetadataDocumentResolverTests
    {
        private static readonly Uri ClientId = new(MetadataDocuments.ClientId);

        [Fact]
        public async Task A_registered_client_id_is_not_a_document_client()
        {
            using ResolverRig rig = new();

            Assert.Null(await rig.Resolver.ResolveAsync(new Uri("urn:example:not-a-url"), TestContext.Current.CancellationToken));
            Assert.Empty(rig.Server.Requests);
        }

        [Fact]
        public async Task A_document_is_fetched_validated_audited_and_counted()
        {
            using ResolverRig rig = new();
            rig.Server.Serve(MetadataDocuments.ClientId, MetadataDocuments.Public().ToJsonString(), maxAge: TimeSpan.FromMinutes(10));

            ClientMetadataDocument? document = await rig.Resolver.ResolveAsync(ClientId, TestContext.Current.CancellationToken);

            Assert.NotNull(document);
            Assert.Equal("Example Agent", document.ClientName);
            Assert.Equal(rig.Time.GetUtcNow() + TimeSpan.FromMinutes(10), document.ExpiresAt);

            AuditEventEntry entry = Assert.Single(rig.Audit.Entries);
            Assert.Equal(AuditActions.ClientMetadataFetched, entry.Action);
            Assert.Equal(MetadataDocuments.ClientId, entry.ClientId);
            Assert.Equal("agent.example.com", JsonDocument.Parse(entry.DetailsJson!).RootElement.GetProperty("host").GetString());

            AssertCounted(rig, ClientMetadataFetchOutcomes.Fetched);
        }

        [Fact]
        public async Task A_standing_copy_is_served_without_a_fetch()
        {
            using ResolverRig rig = new();
            rig.Server.Serve(MetadataDocuments.ClientId, MetadataDocuments.Public().ToJsonString(), maxAge: TimeSpan.FromMinutes(10));

            ClientMetadataDocument? first = await rig.Resolver.ResolveAsync(ClientId, TestContext.Current.CancellationToken);
            rig.Time.Advance(TimeSpan.FromMinutes(9));
            ClientMetadataDocument? second = await rig.Resolver.ResolveAsync(ClientId, TestContext.Current.CancellationToken);

            Assert.Same(first, second);
            Assert.Single(rig.Server.Requests);
        }

        [Fact]
        public async Task An_expired_copy_is_fetched_again()
        {
            using ResolverRig rig = new();
            rig.Server.Serve(MetadataDocuments.ClientId, MetadataDocuments.Public().ToJsonString(), maxAge: TimeSpan.FromMinutes(10));

            await rig.Resolver.ResolveAsync(ClientId, TestContext.Current.CancellationToken);
            rig.Time.Advance(TimeSpan.FromMinutes(10));
            await rig.Resolver.ResolveAsync(ClientId, TestContext.Current.CancellationToken);

            Assert.Equal(2, rig.Server.Requests.Count);
        }

        [Theory]
        [InlineData(null, 60)]
        [InlineData(5, 60)]
        [InlineData(3600, 3600)]
        [InlineData(10_000_000, 86_400)]
        public async Task The_cache_lifetime_is_clamped(int? maxAgeSeconds, int expectedSeconds)
        {
            // A response that states nothing, or asks for seconds, is still kept a minute; one
            // that asks for months is kept a day.
            using ResolverRig rig = new();
            rig.Server.Serve(
                MetadataDocuments.ClientId,
                MetadataDocuments.Public().ToJsonString(),
                maxAge: maxAgeSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null);

            ClientMetadataDocument? document = await rig.Resolver.ResolveAsync(ClientId, TestContext.Current.CancellationToken);

            Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), document!.ExpiresAt - document.FetchedAt);
        }

        [Fact]
        public async Task Concurrent_requests_share_one_fetch()
        {
            using ResolverRig rig = new();
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            string json = MetadataDocuments.Public().ToJsonString();
            rig.Server.Handle(MetadataDocuments.ClientId, async (_, _) =>
            {
                await release.Task;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
            });

            Task<ClientMetadataDocument?>[] resolutions = [.. Enumerable.Range(0, 8).Select(_ => rig.Resolver.ResolveAsync(ClientId, TestContext.Current.CancellationToken))];
            release.SetResult();
            ClientMetadataDocument?[] documents = await Task.WhenAll(resolutions);

            Assert.Single(rig.Server.Requests);
            Assert.All(documents, document => Assert.Same(documents[0], document));
            Assert.Single(rig.Audit.Entries);
        }

        [Fact]
        public async Task A_failed_fetch_with_nothing_cached_is_refused()
        {
            using ResolverRig rig = new();
            rig.Server.Fail(MetadataDocuments.ClientId);

            ClientMetadataException exception = await Assert.ThrowsAsync<ClientMetadataException>(() =>
                rig.Resolver.ResolveAsync(ClientId, TestContext.Current.CancellationToken));

            Assert.Equal(ClientMetadataRejectionReasons.Unreachable, exception.Reason);
            AuditEventEntry entry = Assert.Single(rig.Audit.Entries);
            Assert.Equal(AuditActions.ClientMetadataRejected, entry.Action);
            Assert.Equal("unreachable", JsonDocument.Parse(entry.DetailsJson!).RootElement.GetProperty("reason").GetString());
            AssertCounted(rig, ClientMetadataFetchOutcomes.Failed);
        }

        [Fact]
        public async Task A_failed_early_fetch_serves_the_standing_copy_until_it_expires()
        {
            using ResolverRig rig = new();
            rig.Server.Serve(MetadataDocuments.ClientId, MetadataDocuments.Public().ToJsonString(), maxAge: TimeSpan.FromMinutes(10));
            ClientMetadataDocument? cached = await rig.Resolver.ResolveAsync(ClientId, TestContext.Current.CancellationToken);

            // A fetch ahead of expiry fails: the copy that still stands is served...
            rig.Server.Fail(MetadataDocuments.ClientId);
            ClientMetadataDocument? served = await rig.Resolver.RevalidateAsync(ClientId, TestContext.Current.CancellationToken);
            Assert.Same(cached, served);
            Assert.True(JsonDocument.Parse(rig.Audit.Entries.Last().DetailsJson!).RootElement.GetProperty("servedCached").GetBoolean());

            // ...and once it expires, the next failed fetch refuses the client.
            rig.Time.Advance(TimeSpan.FromMinutes(10));
            await Assert.ThrowsAsync<ClientMetadataException>(() => rig.Resolver.ResolveAsync(ClientId, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task A_rate_limited_early_fetch_serves_the_standing_copy()
        {
            using ResolverRig rig = new(static options => options.FetchesPerHostPerMinute = 1);
            rig.Server.Serve(MetadataDocuments.ClientId, MetadataDocuments.Public().ToJsonString(), maxAge: TimeSpan.FromMinutes(10));
            ClientMetadataDocument? cached = await rig.Resolver.ResolveAsync(ClientId, TestContext.Current.CancellationToken);

            Assert.Same(cached, await rig.Resolver.RevalidateAsync(ClientId, TestContext.Current.CancellationToken));
            Assert.Single(rig.Server.Requests);
            AssertCounted(rig, ClientMetadataFetchOutcomes.Fetched, ClientMetadataFetchOutcomes.Rejected);
        }

        [Fact]
        public async Task A_document_that_turned_invalid_drops_the_standing_copy()
        {
            using ResolverRig rig = new();
            rig.Server.Serve(MetadataDocuments.ClientId, MetadataDocuments.Public().ToJsonString(), maxAge: TimeSpan.FromMinutes(10));
            await rig.Resolver.ResolveAsync(ClientId, TestContext.Current.CancellationToken);

            // The document on the server now names another client: that is the document's answer,
            // not a fetch failure, so the old copy no longer speaks for it.
            JsonObject changed = MetadataDocuments.Public();
            changed["client_id"] = "https://agent.example.com/other.json";
            rig.Server.Serve(MetadataDocuments.ClientId, changed.ToJsonString());

            ClientMetadataException exception = await Assert.ThrowsAsync<ClientMetadataException>(() =>
                rig.Resolver.RevalidateAsync(ClientId, TestContext.Current.CancellationToken));
            Assert.Equal(ClientMetadataRejectionReasons.ClientIdMismatch, exception.Reason);
            Assert.False(rig.Cache.TryGet(MetadataDocuments.ClientId, out _));
            AssertCounted(rig, ClientMetadataFetchOutcomes.Fetched, ClientMetadataFetchOutcomes.Rejected);
        }

        [Fact]
        public async Task A_private_key_jwt_document_carries_its_key_set_and_the_shorter_lifetime()
        {
            using ResolverRig rig = new();
            rig.Server.Serve(MetadataDocuments.ClientId, MetadataDocuments.PrivateKeyJwt().ToJsonString(), maxAge: TimeSpan.FromHours(2));
            rig.Server.Serve(MetadataDocuments.JwksUri, MetadataDocuments.KeySet(MetadataDocuments.RsaKey()), "application/jwk-set+json", TimeSpan.FromMinutes(5));

            ClientMetadataDocument? document = await rig.Resolver.ResolveAsync(ClientId, TestContext.Current.CancellationToken);

            Assert.NotNull(document!.Keys);
            Assert.Single(document.Keys.Keys);
            Assert.Equal(TimeSpan.FromMinutes(5), document.ExpiresAt - document.FetchedAt);
            Assert.Equal([new Uri(MetadataDocuments.ClientId), new Uri(MetadataDocuments.JwksUri)], rig.Server.Requests);
        }

        [Fact]
        public async Task A_key_set_that_cannot_be_fetched_refuses_the_client()
        {
            using ResolverRig rig = new();
            rig.Server.Serve(MetadataDocuments.ClientId, MetadataDocuments.PrivateKeyJwt().ToJsonString());
            rig.Server.Respond(MetadataDocuments.JwksUri, static () => new HttpResponseMessage(HttpStatusCode.Found));

            ClientMetadataException exception = await Assert.ThrowsAsync<ClientMetadataException>(() =>
                rig.Resolver.ResolveAsync(ClientId, TestContext.Current.CancellationToken));

            Assert.Equal(ClientMetadataRejectionReasons.Redirect, exception.Reason);
        }

        [Fact]
        public async Task A_key_set_with_a_private_key_refuses_the_client()
        {
            using ResolverRig rig = new();
            JsonObject key = MetadataDocuments.RsaKey();
            key["d"] = "AQAB";
            rig.Server.Serve(MetadataDocuments.ClientId, MetadataDocuments.PrivateKeyJwt().ToJsonString());
            rig.Server.Serve(MetadataDocuments.JwksUri, MetadataDocuments.KeySet(key));

            ClientMetadataException exception = await Assert.ThrowsAsync<ClientMetadataException>(() =>
                rig.Resolver.ResolveAsync(ClientId, TestContext.Current.CancellationToken));

            Assert.Equal(ClientMetadataRejectionReasons.KeySet, exception.Reason);
        }

        [Fact]
        public async Task A_client_id_over_100_characters_is_refused_before_any_fetch()
        {
            using ResolverRig rig = new();
            Uri longId = new("https://agent.example.com/" + new string('a', 100));

            ClientMetadataException exception = await Assert.ThrowsAsync<ClientMetadataException>(() =>
                rig.Resolver.ResolveAsync(longId, TestContext.Current.CancellationToken));

            Assert.Equal(ClientMetadataRejectionReasons.ClientIdTooLong, exception.Reason);
            Assert.Empty(rig.Server.Requests);

            // The column cannot hold it, so the row leaves it out and the details carry it.
            AuditEventEntry entry = Assert.Single(rig.Audit.Entries);
            Assert.Null(entry.ClientId);
            Assert.Equal(longId.OriginalString, JsonDocument.Parse(entry.DetailsJson!).RootElement.GetProperty("clientId").GetString());
            AssertCounted(rig, ClientMetadataFetchOutcomes.Rejected);
        }

        [Fact]
        public async Task Document_clients_turned_off_refuse_every_url_client_id()
        {
            using ResolverRig rig = new(static options => options.Enabled = false);
            rig.Server.Serve(MetadataDocuments.ClientId, MetadataDocuments.Public().ToJsonString());

            ClientMetadataException exception = await Assert.ThrowsAsync<ClientMetadataException>(() =>
                rig.Resolver.ResolveAsync(ClientId, TestContext.Current.CancellationToken));

            Assert.Equal(ClientMetadataRejectionReasons.Disabled, exception.Reason);
            Assert.Empty(rig.Server.Requests);
        }

        [Fact]
        public void The_cache_evicts_the_entry_nearest_expiry_when_full()
        {
            using ResolverRig rig = new(static options => options.MaxClients = 2);
            DateTimeOffset now = rig.Time.GetUtcNow();

            rig.Cache.Set(Document("https://a.example.com/c", now, TimeSpan.FromMinutes(30)), now);
            rig.Cache.Set(Document("https://b.example.com/c", now, TimeSpan.FromMinutes(5)), now);
            rig.Cache.Set(Document("https://c.example.com/c", now, TimeSpan.FromMinutes(20)), now);

            Assert.Equal(2, rig.Cache.Count);
            Assert.True(rig.Cache.TryGet("https://a.example.com/c", out _));
            Assert.False(rig.Cache.TryGet("https://b.example.com/c", out _));
            Assert.True(rig.Cache.TryGet("https://c.example.com/c", out _));
        }

        [Fact]
        public void The_cache_sweeps_expired_entries_before_evicting_a_standing_one()
        {
            using ResolverRig rig = new(static options => options.MaxClients = 2);
            DateTimeOffset now = rig.Time.GetUtcNow();
            rig.Cache.Set(Document("https://a.example.com/c", now, TimeSpan.FromMinutes(1)), now);
            rig.Cache.Set(Document("https://b.example.com/c", now, TimeSpan.FromMinutes(5)), now);

            DateTimeOffset later = now.AddMinutes(2);
            rig.Cache.Set(Document("https://c.example.com/c", later, TimeSpan.FromMinutes(1)), later);

            Assert.False(rig.Cache.TryGet("https://a.example.com/c", out _));
            Assert.True(rig.Cache.TryGet("https://b.example.com/c", out _));
        }

        /// <summary>A minimal document for cache tests.</summary>
        private static ClientMetadataDocument Document(string clientId, DateTimeOffset fetchedAt, TimeSpan lifetime)
        {
            return new ClientMetadataDocument(
                new Uri(clientId), null, [new Uri(MetadataDocuments.LoopbackRedirect)], "none", null, null, fetchedAt, fetchedAt + lifetime);
        }

        /// <summary>Asserts the fetch counter's measurements, in order, each tagged with its outcome alone.</summary>
        private static void AssertCounted(ResolverRig rig, params string[] outcomes)
        {
            IReadOnlyList<Microsoft.Extensions.Diagnostics.Metrics.Testing.CollectedMeasurement<long>> measurements = rig.Fetches.GetMeasurementSnapshot();
            Assert.Equal(outcomes, measurements.Select(static measurement => (string)measurement.Tags[IdentityMetrics.ClientMetadataOutcomeTag]!));
            Assert.All(measurements, static measurement => Assert.Equal([IdentityMetrics.ClientMetadataOutcomeTag], measurement.Tags.Keys));
        }
    }
}
