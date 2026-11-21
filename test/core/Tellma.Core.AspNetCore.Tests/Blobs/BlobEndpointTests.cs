// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Options;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.AspNetCore.Blobs;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Tests.Infrastructure;

namespace Tellma.Core.AspNetCore.Tests.Blobs
{
    /// <summary>
    ///     The blob download as the host maps it, over a scripted blob service and store: a held copy
    ///     answered without the store, a missing or corrupt object never cached, a transfer that
    ///     outlives the web timeout, one that outlives its own deadline, the telemetry stage's
    ///     headers, and the download counter per outcome.
    /// </summary>
    public sealed class BlobEndpointTests
    {
        /// <summary>The blob every test downloads.</summary>
        private const string Address = "/1/blobs/user-image/7";

        /// <summary>The blob's strong tag.</summary>
        private const string Tag = "\"7\"";

        /// <summary>The stored primary's bytes.</summary>
        private static readonly byte[] Bytes = [.. Enumerable.Range(0, 10).Select(static value => (byte)value)];

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_held_copy_is_answered_304_with_its_caching_and_the_store_untouched()
        {
            ScriptedBlobStore store = new(static () => new BlobContent(new MemoryStream(Bytes), Bytes.Length));
            await using TellmaTestHost host = await StartAsync(store);
            using MetricCollector<long> downloads = Downloads(host);

            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Get, Address, host.Cookie());
            request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(Tag));
            using HttpResponseMessage response = await host.Client.SendAsync(request, Token);

            Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
            Assert.Equal(Tag, response.Headers.ETag?.Tag);
            Assert.Equal(BlobResponseHeaders.CommittedCacheControl, CacheControl(response));
            Assert.Equal(0, store.Opens);
            Assert.Equal([BlobTelemetryTagValues.DownloadNotModified], Outcomes(downloads));
        }

        [Fact]
        public async Task A_committed_blob_whose_object_is_missing_answers_a_404_no_cache_keeps_and_without_its_tag()
        {
            ScriptedBlobStore store = new(static () => null);
            await using TellmaTestHost host = await StartAsync(store);
            using MetricCollector<long> downloads = Downloads(host);

            using HttpResponseMessage response = await host.Client.SendAsync(TellmaTestHost.SpaRequest(HttpMethod.Get, Address, host.Cookie()), Token);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("not-found", (await Problem.ReadAsync(response)).Code);
            Assert.Equal(BlobResponseHeaders.RefusalCacheControl, CacheControl(response));
            Assert.Null(response.Headers.ETag);
            Assert.Equal([BlobResponseHeaders.ContentSecurityPolicy], response.Headers.GetValues("Content-Security-Policy"));
            Assert.Equal(1, store.Opens);
            Assert.Equal([BlobTelemetryTagValues.DownloadNotFound], Outcomes(downloads));
        }

        [Fact]
        public async Task A_stored_primary_shorter_than_its_row_answers_a_500_no_cache_keeps()
        {
            ScriptedBlobStore store = new(static () => new BlobContent(new MemoryStream(Bytes[..3]), 3));
            await using TellmaTestHost host = await StartAsync(store);
            using MetricCollector<long> downloads = Downloads(host);

            using HttpResponseMessage response = await host.Client.SendAsync(TellmaTestHost.SpaRequest(HttpMethod.Get, Address, host.Cookie()), Token);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(BlobResponseHeaders.RefusalCacheControl, CacheControl(response));
            Assert.Null(response.Headers.ETag);
            Assert.Empty(Outcomes(downloads));
        }

        [Fact]
        public async Task A_served_blob_carries_its_caching_and_the_telemetry_stage_s_headers_and_is_counted()
        {
            ScriptedBlobStore store = new(static () => new BlobContent(new MemoryStream(Bytes), Bytes.Length));
            await using TellmaTestHost host = await StartAsync(store);
            using MetricCollector<long> downloads = Downloads(host);

            using HttpResponseMessage response = await host.Client.SendAsync(TellmaTestHost.SpaRequest(HttpMethod.Get, Address, host.Cookie()), Token);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(Bytes, await response.Content.ReadAsByteArrayAsync(Token));
            Assert.Equal(Tag, response.Headers.ETag?.Tag);
            Assert.Equal(BlobResponseHeaders.CommittedCacheControl, CacheControl(response));

            // The telemetry stage wraps the blob group: the resolved language and calendar.
            Assert.Equal(["en"], response.Content.Headers.ContentLanguage);
            Assert.False(string.IsNullOrEmpty(Assert.Single(response.Headers.GetValues(TellmaHeaders.Calendar))));
            Assert.Equal([BlobTelemetryTagValues.DownloadServed], Outcomes(downloads));
        }

        [Fact]
        public async Task A_satisfiable_range_is_counted_as_served_and_an_unsatisfiable_one_is_refused_uncounted_and_uncached()
        {
            ScriptedBlobStore store = new(static () => new BlobContent(new MemoryStream(Bytes), Bytes.Length));
            await using TellmaTestHost host = await StartAsync(store);
            using MetricCollector<long> downloads = Downloads(host);

            // --- A satisfiable range: 206 and those bytes ---
            using (HttpRequestMessage ranged = TellmaTestHost.SpaRequest(HttpMethod.Get, Address, host.Cookie()))
            {
                ranged.Headers.Range = new RangeHeaderValue(2, 4);
                using HttpResponseMessage partial = await host.Client.SendAsync(ranged, Token);
                Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
                Assert.Equal(Bytes[2..5], await partial.Content.ReadAsByteArrayAsync(Token));
            }

            // --- An unsatisfiable one: 416, no object, nothing a cache keeps ---
            using (HttpRequestMessage beyond = TellmaTestHost.SpaRequest(HttpMethod.Get, Address, host.Cookie()))
            {
                beyond.Headers.Range = new RangeHeaderValue(100, null);
                using HttpResponseMessage refused = await host.Client.SendAsync(beyond, Token);
                Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, refused.StatusCode);
                Assert.Equal(BlobResponseHeaders.RefusalCacheControl, CacheControl(refused));
                Assert.Null(refused.Headers.ETag);
            }

            Assert.Equal([BlobTelemetryTagValues.DownloadServed], Outcomes(downloads));
        }

        [Fact]
        public async Task A_transfer_outlives_the_web_timeout_once_its_object_is_open()
        {
            // The object's first bytes come only after the web timeout has passed.
            ScriptedBlobStore store = new(static () => new BlobContent(new SlowStream(Bytes, TimeSpan.FromSeconds(3)), Bytes.Length));
            await using TellmaTestHost host = await StartAsync(store, static settings => settings["Tellma:Api:RequestTimeout"] = "00:00:01");

            // A held copy first, which never opens the store, so the timed request's way to its open is warm.
            using (HttpRequestMessage warm = TellmaTestHost.SpaRequest(HttpMethod.Get, Address, host.Cookie()))
            {
                warm.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(Tag));
                using HttpResponseMessage held = await host.Client.SendAsync(warm, Token);
                Assert.Equal(HttpStatusCode.NotModified, held.StatusCode);
            }

            using HttpResponseMessage response = await host.Client.SendAsync(TellmaTestHost.SpaRequest(HttpMethod.Get, Address, host.Cookie()), Token);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(Bytes, await response.Content.ReadAsByteArrayAsync(Token));
        }

        [Fact]
        public async Task A_transfer_past_its_deadline_is_aborted_and_lets_go_of_its_object()
        {
            // A client that stops reading, or an object that stops arriving: the bytes stop after the first chunk.
            const long Length = 1024 * 1024;
            StallingStream stalled = new(firstChunk: 16);
            ScriptedBlobStore store = new(() => new BlobContent(stalled, Length));
            await using TellmaTestHost host = await StartAsync(store, settings: null, Length);
            TimeSpan deadline = BlobEndpoints.TransferDeadline(host.Services.GetRequiredService<IOptions<ApiOptions>>().Value.LongRequestTimeout, Length);

            using HttpResponseMessage response = await host.Client.SendAsync(
                TellmaTestHost.SpaRequest(HttpMethod.Get, Address, host.Cookie()), HttpCompletionOption.ResponseHeadersRead, Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Task<byte[]> body = response.Content.ReadAsByteArrayAsync(Token);

            // --- Just short of the deadline the transfer still holds on ---
            host.Time.Advance(deadline - TimeSpan.FromSeconds(1));
            await Task.Delay(TimeSpan.FromMilliseconds(200), Token);
            Assert.False(stalled.Released.Task.IsCompleted, "The transfer was ended before its deadline.");

            // --- Past it, the connection is aborted and the endpoint returns, its permit with it ---
            host.Time.Advance(TimeSpan.FromSeconds(2));
            Assert.True(await stalled.Released.Task.WaitAsync(TimeSpan.FromSeconds(10), Token));
            _ = await Assert.ThrowsAnyAsync<Exception>(() => body.WaitAsync(TimeSpan.FromSeconds(10), Token));
        }

        [Fact]
        public void A_transfer_s_deadline_is_the_long_timeout_plus_its_length_at_the_minimum_rate()
        {
            Assert.Equal(TimeSpan.FromMinutes(5), BlobEndpoints.TransferDeadline(TimeSpan.FromMinutes(5), 0));
            Assert.Equal(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(3200), BlobEndpoints.TransferDeadline(TimeSpan.FromMinutes(5), 100L * 1024 * 1024));

            // However large the object, the deadline stops at a day, which a timer can still wait.
            Assert.Equal(BlobEndpoints.MaxTransferDeadline, BlobEndpoints.TransferDeadline(TimeSpan.FromMinutes(5), long.MaxValue));
            Assert.Equal(TimeSpan.FromDays(1), BlobEndpoints.MaxTransferDeadline);
        }

        /// <summary>Starts a host whose blob service resolves blob 7 as a committed avatar, over a scripted store.</summary>
        /// <param name="store">The store.</param>
        /// <param name="settings">Changes to the host's settings.</param>
        /// <param name="size">The length the blob's row records.</param>
        /// <returns>The host.</returns>
        private static Task<TellmaTestHost> StartAsync(ScriptedBlobStore store, Action<Dictionary<string, string?>>? settings = null, long? size = null)
        {
            BlobDownload resolved = new(7, "user-image", BlobState.Committed, "user-image/07/7", "image/webp", size ?? Bytes.Length, "me.webp", IsImage: true, Tag);
            return TellmaTestHost.StartAsync(options =>
            {
                settings?.Invoke(options.Settings);
                options.ConfigureServices = services =>
                {
                    services.RemoveAll<IBlobService>();
                    _ = services.AddSingleton<IBlobService>(new ScriptedBlobService(resolved));
                    services.RemoveAll<IBlobStore>();
                    _ = services.AddSingleton<IBlobStore>(store);
                };
            });
        }

        /// <summary>Collects the host's download counter.</summary>
        /// <param name="host">The host.</param>
        /// <returns>The collector.</returns>
        private static MetricCollector<long> Downloads(TellmaTestHost host)
        {
            return new MetricCollector<long>(host.Services.GetRequiredService<IMeterFactory>(), BlobTelemetryNames.MeterName, BlobTelemetryNames.Downloads);
        }

        /// <summary>The outcomes counted so far, each under the avatar kind.</summary>
        /// <param name="downloads">The collector.</param>
        /// <returns>The outcomes, in order.</returns>
        private static string[] Outcomes(MetricCollector<long> downloads)
        {
            return [.. downloads.GetMeasurementSnapshot().Select(static measurement =>
            {
                Assert.Equal("user-image", measurement.Tags[BlobTelemetryNames.KindTag]);
                return (string)measurement.Tags[BlobTelemetryNames.OutcomeTag]!;
            })];
        }

        /// <summary>A response's <c>Cache-Control</c> as the server wrote it.</summary>
        /// <param name="response">The response.</param>
        /// <returns>The header's value; empty when absent.</returns>
        private static string CacheControl(HttpResponseMessage response)
        {
            return response.Headers.NonValidated.TryGetValues("Cache-Control", out HeaderStringValues values) ? values.ToString() : string.Empty;
        }

        /// <summary>A blob service that resolves every download to one blob and stages nothing.</summary>
        /// <param name="resolved">The blob every download resolves to.</param>
        private sealed class ScriptedBlobService(BlobDownload resolved) : IBlobService
        {
            /// <inheritdoc />
            public Task<BlobDescriptor> StageAsync(BlobStageRequest request, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException("These tests stage nothing.");
            }

            /// <inheritdoc />
            public Task<BlobDownload?> ResolveAsync(string kind, int id, string? variant, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<BlobDownload?>(resolved);
            }
        }

        /// <summary>A store whose every read opens what a test scripts, and which counts its opens.</summary>
        /// <param name="open">What a read opens; null for a missing object.</param>
        private sealed class ScriptedBlobStore(Func<BlobContent?> open) : IBlobStore
        {
            private int _opens;

            /// <summary>How many reads were opened.</summary>
            public int Opens => Volatile.Read(ref _opens);

            /// <inheritdoc />
            public Task EnsureTenantAsync(int tenantId, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            /// <inheritdoc />
            public Task WriteAsync(int tenantId, IReadOnlyList<BlobWrite> writes, CancellationToken cancellationToken)
            {
                throw new NotSupportedException("These tests write nothing.");
            }

            /// <inheritdoc />
            public Task<BlobContent?> OpenReadAsync(int tenantId, string name, CancellationToken cancellationToken)
            {
                _ = Interlocked.Increment(ref _opens);
                return Task.FromResult(open());
            }

            /// <inheritdoc />
            public Task DeleteAsync(int tenantId, IReadOnlyList<string> names, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            /// <inheritdoc />
            public async IAsyncEnumerable<BlobStoreEntry> ListAsync(int tenantId, string prefix, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                yield break;
            }

            /// <inheritdoc />
            public Task PurgeIncompleteAsync(int tenantId, DateTimeOffset olderThan, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }

        /// <summary>A seekable object whose first read waits, as a slow store's first bytes do.</summary>
        /// <param name="bytes">The object.</param>
        /// <param name="delay">How long the first read waits.</param>
        private sealed class SlowStream(byte[] bytes, TimeSpan delay) : MemoryStream(bytes, writable: false)
        {
            private bool _waited;

            /// <inheritdoc />
            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (!_waited)
                {
                    _waited = true;
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }

                return await base.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }

            /// <inheritdoc />
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
            }
        }

        /// <summary>
        ///     An object that yields its first chunk and then no more until its read is cancelled,
        ///     and reports when the response lets go of it.
        /// </summary>
        /// <param name="firstChunk">How many bytes the first read yields.</param>
        private sealed class StallingStream(int firstChunk) : Stream
        {
            private bool _yielded;

            /// <summary>Completes when the stream is disposed: the endpoint has returned.</summary>
            public TaskCompletionSource<bool> Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <inheritdoc />
            public override bool CanRead => true;

            /// <inheritdoc />
            public override bool CanSeek => false;

            /// <inheritdoc />
            public override bool CanWrite => false;

            /// <inheritdoc />
            public override long Length => throw new NotSupportedException();

            /// <inheritdoc />
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            /// <inheritdoc />
            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (!_yielded)
                {
                    _yielded = true;
                    int count = Math.Min(firstChunk, buffer.Length);
                    buffer.Span[..count].Clear();
                    return count;
                }

                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                return 0;
            }

            /// <inheritdoc />
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
            }

            /// <inheritdoc />
            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc />
            public override void Flush()
            {
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

            /// <inheritdoc />
            protected override void Dispose(bool disposing)
            {
                _ = Released.TrySetResult(true);
                base.Dispose(disposing);
            }
        }
    }
}
