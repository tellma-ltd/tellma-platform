// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Net;
using Tellma.Core.AspNetCore.Limits;
using Tellma.Testing.WebHost;

namespace Tellma.Core.AspNetCore.Tests.Limits
{
    /// <summary>
    ///     Response compression and output caching through the real pipeline: JSON and problem
    ///     bodies alone are compressed, Brotli first, never a short declared body or a blob surface
    ///     response, and configuration turns it off; the tenant-independent public documents are
    ///     served from one cached copy whoever asks.
    /// </summary>
    /// <param name="fixture">The host the cases share.</param>
    public sealed class CompressionAndCachingTests(CompressionAndCachingTests.Host fixture) : IClassFixture<CompressionAndCachingTests.Host>
    {
        /// <summary>A body well past the compression minimum.</summary>
        private static readonly string Large = new('x', 4096);

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public void Only_json_and_problem_bodies_are_compressed()
        {
            Assert.Equal(["application/json", "application/problem+json"], JsonResponseCompressionProvider.MimeTypes);
            Assert.Equal(1024, JsonResponseCompressionProvider.MinimumBytes);
        }

        [Theory]
        [InlineData("br, gzip", "br")]
        [InlineData("gzip", "gzip")]
        public async Task A_large_json_body_is_compressed_brotli_first(string accepted, string encoding)
        {
            TellmaTestHost host = fixture.Started;

            using HttpResponseMessage response = await SendAsync(host, "/1/api/web/large-json", accepted);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal([encoding], response.Content.Headers.ContentEncoding);
        }

        [Fact]
        public async Task A_body_of_another_type_is_not_compressed()
        {
            TellmaTestHost host = fixture.Started;

            using HttpResponseMessage response = await SendAsync(host, "/1/api/web/large-text", "br");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(response.Content.Headers.ContentEncoding);
        }

        [Fact]
        public async Task A_declared_body_under_a_kilobyte_is_not_compressed()
        {
            TellmaTestHost host = fixture.Started;

            // The problem writer declares its length; this one is far under the minimum.
            using HttpResponseMessage response = await SendAsync(host, "/99/api/web/probe", "br");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Empty(response.Content.Headers.ContentEncoding);
        }

        [Fact]
        public async Task A_blob_surface_response_is_never_compressed()
        {
            TellmaTestHost host = fixture.Started;

            using HttpResponseMessage response = await SendAsync(host, "/1/blobs/large/1", "br");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(response.Content.Headers.ContentEncoding);
        }

        [Fact]
        public async Task Configuration_turns_compression_off()
        {
            await using TellmaTestHost host = await StartAsync(static options => options.Settings["Tellma:Api:EnableCompression"] = "false");

            using HttpResponseMessage response = await SendAsync(host, "/1/api/web/large-json", "br");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(response.Content.Headers.ContentEncoding);
        }

        [Fact]
        public async Task The_information_document_is_served_from_one_copy_whoever_asks()
        {
            await using TellmaTestHost host = await StartAsync();

            // --- Stored by the first request; a signed-in caller and a query string share the copy ---
            using HttpResponseMessage first = await host.Client.GetAsync("/api/distribution-info", Token);
            using HttpRequestMessage signedIn = new(HttpMethod.Get, "/api/distribution-info?x=1");
            signedIn.Headers.Add("Cookie", host.Cookie());
            using HttpResponseMessage second = await host.Client.SendAsync(signedIn, Token);

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.Null(first.Headers.Age);
            Assert.NotNull(second.Headers.Age);
            Assert.Equal(await first.Content.ReadAsStringAsync(Token), await second.Content.ReadAsStringAsync(Token));
        }

        [Fact]
        public async Task A_tenant_response_is_never_served_from_the_cache()
        {
            TellmaTestHost host = fixture.Started;
            string cookie = host.Cookie();

            using HttpResponseMessage first = await host.Client.SendAsync(TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/probe", cookie), Token);
            using HttpResponseMessage second = await host.Client.SendAsync(TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/probe", cookie), Token);

            Assert.Null(second.Headers.Age);
            Assert.Equal("no-store", second.Headers.CacheControl?.ToString());
        }

        /// <summary>Starts a host with the large-body endpoints.</summary>
        /// <param name="configure">Changes to the defaults.</param>
        /// <returns>The host.</returns>
        private static Task<TellmaTestHost> StartAsync(Action<TestHostOptions>? configure = null)
        {
            return TellmaTestHost.StartAsync(options =>
            {
                configure?.Invoke(options);
                options.Map = static (app, endpoints) =>
                {
                    _ = endpoints.Web.MapGet("/large-json", static () => Results.Json(new { text = Large })).WithMutation(false).AllowMember();
                    _ = endpoints.Web.MapGet("/large-text", static () => Results.Text(Large)).WithMutation(false).AllowMember();
                    _ = endpoints.Blobs.MapGet("/large/{id:int}", static () => Results.Json(new { text = Large })).WithMutation(false).AllowMember();
                };
            });
        }

        /// <summary>Sends a GET as the SPA does, accepting the given encodings.</summary>
        /// <param name="host">The host.</param>
        /// <param name="path">The path.</param>
        /// <param name="accepted">The <c>Accept-Encoding</c> header.</param>
        /// <returns>The response.</returns>
        private static async Task<HttpResponseMessage> SendAsync(TellmaTestHost host, string path, string accepted)
        {
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Get, path, host.Cookie());
            request.Headers.Add("Accept-Encoding", accepted);
            return await host.Client.SendAsync(request, Token);
        }

        /// <summary>
        ///     One host for the cases that send a request and read only its answer's encoding and
        ///     caching headers. The case that watches the output cache store its first copy, and the
        ///     case that turns compression off, start hosts of their own.
        /// </summary>
        public sealed class Host : IAsyncLifetime
        {
            /// <summary>The host, once started.</summary>
            private TellmaTestHost? _host;

            /// <summary>The started host.</summary>
            internal TellmaTestHost Started => _host ?? throw new InvalidOperationException("The host has not started.");

            /// <inheritdoc />
            public async ValueTask InitializeAsync()
            {
                _host = await StartAsync();
            }

            /// <inheritdoc />
            public async ValueTask DisposeAsync()
            {
                if (_host is not null)
                {
                    await _host.DisposeAsync();
                }
            }
        }
    }
}
