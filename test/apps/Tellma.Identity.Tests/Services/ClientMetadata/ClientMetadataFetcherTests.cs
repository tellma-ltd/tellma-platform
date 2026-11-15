// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Tellma.Identity.Services.ClientMetadata;

namespace Tellma.Identity.Tests.Services.ClientMetadata
{
    /// <summary>
    ///     The response and limit guards of one fetch, against a fake server: only a 200
    ///     <c>application/json</c> body within the size limit passes; a redirect, another status,
    ///     another type, an oversized body, a timeout and a spent per-host quota are each refused
    ///     under their own reason, and a refused URL is never requested at all.
    /// </summary>
    public sealed class ClientMetadataFetcherTests
    {
        private const string Url = MetadataDocuments.ClientId;

        [Fact]
        public async Task A_json_document_passes_through_the_named_client()
        {
            using ResolverRig rig = new();
            rig.Server.Serve(Url, "{}", maxAge: TimeSpan.FromMinutes(10));

            ClientMetadataFetchResult result = await rig.Fetcher.FetchAsync(new Uri(Url), ClientMetadataResource.Document, TestContext.Current.CancellationToken);

            Assert.Equal("{}"u8.ToArray(), result.Body);
            Assert.Equal(TimeSpan.FromMinutes(10), result.Lifetime);
            Assert.Equal(rig.Time.GetUtcNow(), result.FetchedAt);
            Assert.Equal(ClientMetadataHttp.ClientName, Assert.Single(rig.HttpClients.Names));
        }

        [Theory]
        [InlineData(HttpStatusCode.MovedPermanently)]
        [InlineData(HttpStatusCode.Found)]
        [InlineData(HttpStatusCode.TemporaryRedirect)]
        [InlineData(HttpStatusCode.PermanentRedirect)]
        public async Task A_redirect_is_refused_not_followed(HttpStatusCode status)
        {
            using ResolverRig rig = new();
            rig.Server.Respond(Url, () => new HttpResponseMessage(status) { Headers = { Location = new Uri("http://169.254.169.254/") } });

            await AssertRefusedAsync(rig, Url, ClientMetadataRejectionReasons.Redirect);
            Assert.Single(rig.Server.Requests);
        }

        [Theory]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData(HttpStatusCode.NoContent)]
        [InlineData(HttpStatusCode.InternalServerError)]
        public async Task A_status_other_than_200_is_refused(HttpStatusCode status)
        {
            using ResolverRig rig = new();
            rig.Server.Respond(Url, () => new HttpResponseMessage(status) { Content = new StringContent("{}", Encoding.UTF8, "application/json") });

            await AssertRefusedAsync(rig, Url, ClientMetadataRejectionReasons.Status);
        }

        [Theory]
        [InlineData("text/html")]
        [InlineData("text/plain")]
        [InlineData("application/jwk-set+json")]
        public async Task A_document_of_another_content_type_is_refused(string contentType)
        {
            using ResolverRig rig = new();
            rig.Server.Serve(Url, "{}", contentType);

            await AssertRefusedAsync(rig, Url, ClientMetadataRejectionReasons.ContentType);
        }

        [Fact]
        public async Task A_document_without_a_content_type_is_refused()
        {
            using ResolverRig rig = new();
            rig.Server.Respond(Url, () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("{}"u8.ToArray()) });

            await AssertRefusedAsync(rig, Url, ClientMetadataRejectionReasons.ContentType);
        }

        [Theory]
        [InlineData("application/json")]
        [InlineData("application/jwk-set+json")]
        [InlineData("application/json; charset=utf-8")]
        public async Task A_key_set_accepts_both_json_types(string contentType)
        {
            using ResolverRig rig = new();
            rig.Server.Respond(MetadataDocuments.JwksUri, () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("{}"u8.ToArray()) { Headers = { ContentType = MediaTypeHeaderValue.Parse(contentType) } },
            });

            ClientMetadataFetchResult result = await rig.Fetcher.FetchAsync(
                new Uri(MetadataDocuments.JwksUri), ClientMetadataResource.KeySet, TestContext.Current.CancellationToken);

            Assert.Equal(2, result.Body.Length);
        }

        [Fact]
        public async Task A_body_of_exactly_the_limit_is_accepted()
        {
            using ResolverRig rig = new(static options => options.MaxBytes = 64);
            rig.Server.Serve(Url, new string(' ', 62) + "{}");

            ClientMetadataFetchResult result = await rig.Fetcher.FetchAsync(new Uri(Url), ClientMetadataResource.Document, TestContext.Current.CancellationToken);

            Assert.Equal(64, result.Body.Length);
        }

        [Fact]
        public async Task A_declared_length_over_the_limit_is_refused()
        {
            using ResolverRig rig = new(static options => options.MaxBytes = 64);
            rig.Server.Serve(Url, new string(' ', 63) + "{}");

            await AssertRefusedAsync(rig, Url, ClientMetadataRejectionReasons.Size);
        }

        [Fact]
        public async Task A_streamed_body_over_the_limit_is_refused_without_reading_it_all()
        {
            // No Content-Length: the limit is enforced on the bytes read, so a server that streams
            // without one, or lies in it, cannot get a larger body through.
            using ResolverRig rig = new(static options => options.MaxBytes = 64);
            EndlessStream body = new();
            rig.Server.Respond(Url, () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(body) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } },
            });

            await AssertRefusedAsync(rig, Url, ClientMetadataRejectionReasons.Size);
            Assert.True(body.BytesRead <= 65 + 4096, $"Read {body.BytesRead} bytes of an endless body.");
        }

        [Fact]
        public async Task A_fetch_that_outlasts_the_timeout_is_refused()
        {
            using ResolverRig rig = new(static options => options.Timeout = TimeSpan.FromSeconds(5));
            TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            rig.Server.Handle(Url, async (_, cancellationToken) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

            Task<ClientMetadataFetchResult> fetch = rig.Fetcher.FetchAsync(new Uri(Url), ClientMetadataResource.Document, TestContext.Current.CancellationToken);
            await started.Task.WaitAsync(TestContext.Current.CancellationToken);
            rig.Time.Advance(TimeSpan.FromSeconds(5));

            ClientMetadataException exception = await Assert.ThrowsAsync<ClientMetadataException>(() => fetch);
            Assert.Equal(ClientMetadataRejectionReasons.Timeout, exception.Reason);
            Assert.True(exception.IsFetchFailure);
        }

        [Fact]
        public async Task The_callers_cancellation_is_not_a_timeout()
        {
            using ResolverRig rig = new();
            rig.Server.Handle(Url, async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            using CancellationTokenSource caller = new();

            Task<ClientMetadataFetchResult> fetch = rig.Fetcher.FetchAsync(new Uri(Url), ClientMetadataResource.Document, caller.Token);
            await caller.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetch);
        }

        [Fact]
        public async Task An_unreachable_host_is_a_fetch_failure()
        {
            using ResolverRig rig = new();
            rig.Server.Fail(Url);

            ClientMetadataException exception = await AssertRefusedAsync(rig, Url, ClientMetadataRejectionReasons.Unreachable);
            Assert.True(exception.IsFetchFailure);
        }

        [Fact]
        public async Task The_connection_steps_refusal_is_the_address_reason()
        {
            using ResolverRig rig = new();
            rig.Server.Handle(Url, static (_, _) => throw new HttpRequestException(
                "Connection refused.", new ClientMetadataAddressException("The host resolves to 10.0.0.1.")));

            ClientMetadataException exception = await AssertRefusedAsync(rig, Url, ClientMetadataRejectionReasons.Address);
            Assert.False(exception.IsFetchFailure);
        }

        [Theory]
        [InlineData("http://agent.example.com/client.json")]
        [InlineData("https://127.0.0.1/client.json")]
        [InlineData("https://[::1]/client.json")]
        [InlineData("https://10.0.0.1/client.json")]
        public async Task A_url_the_address_guard_refuses_is_never_requested(string url)
        {
            using ResolverRig rig = new();
            rig.Server.Serve(url, "{}");

            await AssertRefusedAsync(rig, url, ClientMetadataRejectionReasons.Address);
            Assert.Empty(rig.Server.Requests);
        }

        [Fact]
        public async Task The_per_host_quota_refuses_the_next_fetch_without_sending_it()
        {
            using ResolverRig rig = new(static options => options.FetchesPerHostPerMinute = 2);
            rig.Server.Serve(Url, "{}");
            rig.Server.Serve(MetadataDocuments.JwksUri, "{}");

            await rig.Fetcher.FetchAsync(new Uri(Url), ClientMetadataResource.Document, TestContext.Current.CancellationToken);

            // Documents and key sets on one host share the quota.
            await rig.Fetcher.FetchAsync(new Uri(MetadataDocuments.JwksUri), ClientMetadataResource.KeySet, TestContext.Current.CancellationToken);
            await AssertRefusedAsync(rig, Url, ClientMetadataRejectionReasons.RateLimited);
            Assert.Equal(2, rig.Server.Requests.Count);

            // Another host has a quota of its own.
            rig.Server.Serve("https://other.example.com/c.json", "{}");
            await rig.Fetcher.FetchAsync(new Uri("https://other.example.com/c.json"), ClientMetadataResource.Document, TestContext.Current.CancellationToken);
        }

        [Fact]
        public void The_quota_slides_with_the_clock()
        {
            using ResolverRig rig = new(static options => options.FetchesPerHostPerMinute = 1);

            Assert.True(rig.RateLimiter.TryAcquire("agent.example.com"));
            Assert.False(rig.RateLimiter.TryAcquire("AGENT.example.com"));

            rig.Time.Advance(TimeSpan.FromSeconds(59));
            Assert.False(rig.RateLimiter.TryAcquire("agent.example.com"));

            rig.Time.Advance(TimeSpan.FromSeconds(1));
            Assert.True(rig.RateLimiter.TryAcquire("agent.example.com"));
        }

        [Fact]
        public void A_flood_of_hosts_cannot_grow_the_quota_table_without_bound()
        {
            using ResolverRig rig = new(static options => options.FetchesPerHostPerMinute = 1);
            for (int host = 0; host < ClientMetadataRateLimiter.MaxTrackedHosts; host++)
            {
                Assert.True(rig.RateLimiter.TryAcquire("h" + host + ".example.com"));
            }

            // Every tracked host is still inside its window, so a new one is refused...
            Assert.False(rig.RateLimiter.TryAcquire("new.example.com"));

            // ...until the window passes and the idle hosts are swept.
            rig.Time.Advance(ClientMetadataRateLimiter.Window);
            Assert.True(rig.RateLimiter.TryAcquire("new.example.com"));
        }

        /// <summary>Asserts a fetch is refused under a reason.</summary>
        private static async Task<ClientMetadataException> AssertRefusedAsync(ResolverRig rig, string url, string reason)
        {
            ClientMetadataException exception = await Assert.ThrowsAsync<ClientMetadataException>(() =>
                rig.Fetcher.FetchAsync(new Uri(url), ClientMetadataResource.Document, TestContext.Current.CancellationToken));
            Assert.Equal(reason, exception.Reason);
            return exception;
        }

        /// <summary>A body that never ends, counting what was read from it.</summary>
        private sealed class EndlessStream : Stream
        {
            /// <summary>How many bytes were read.</summary>
            public long BytesRead { get; private set; }

            /// <inheritdoc />
            public override bool CanRead => true;

            /// <inheritdoc />
            public override bool CanSeek => false;

            /// <inheritdoc />
            public override bool CanWrite => false;

            /// <inheritdoc />
            public override long Length => throw new NotSupportedException();

            /// <inheritdoc />
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            /// <inheritdoc />
            public override void Flush()
            {
            }

            /// <inheritdoc />
            public override int Read(byte[] buffer, int offset, int count)
            {
                Array.Fill(buffer, (byte)' ', offset, count);
                BytesRead += count;
                return count;
            }

            /// <inheritdoc />
            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc />
            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc />
            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }
        }
    }
}
