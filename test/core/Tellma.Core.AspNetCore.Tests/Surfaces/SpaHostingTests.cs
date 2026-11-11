// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Surfaces;
using Tellma.Core.AspNetCore.Tests.Infrastructure;

namespace Tellma.Core.AspNetCore.Tests.Surfaces
{
    /// <summary>
    ///     The SPA's hosting: hashed files cached for good, everything else revalidated, index.html
    ///     with its security headers for a navigation the SPA owns, and a problem for anything else.
    /// </summary>
    public sealed class SpaHostingTests(SpaHostingTests.Host fixture) : IClassFixture<SpaHostingTests.Host>
    {
        /// <summary>The index page's text.</summary>
        private const string IndexText = "<!doctype html><title>Acme</title>";

        [Fact]
        public async Task A_hashed_file_is_cached_for_good()
        {
            using HttpResponseMessage response = await fixture.Test.Client.GetAsync("/main-ABCD2345.js", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(SpaHosting.ImmutableCacheControl, response.Headers.CacheControl?.ToString());
            Assert.False(response.Headers.Contains("Content-Security-Policy"));
        }

        [Fact]
        public async Task An_unhashed_file_is_revalidated()
        {
            using HttpResponseMessage response = await fixture.Test.Client.GetAsync("/favicon.ico", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(SpaHosting.NoCache, response.Headers.CacheControl?.ToString());
        }

        [Fact]
        public async Task Index_served_as_a_file_carries_the_security_headers()
        {
            using HttpResponseMessage response = await fixture.Test.Client.GetAsync("/index.html", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertIndex(response, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        [Theory]
        [InlineData("/")]
        [InlineData("/tenants")]
        [InlineData("/signed-out")]
        [InlineData("/1")]
        [InlineData("/1/documents/7")]
        [InlineData("/1/settings")]
        [InlineData("/12/apiary")]
        public async Task A_navigation_the_SPA_owns_gets_the_index(string path)
        {
            using HttpRequestMessage request = Navigation(path);
            using HttpResponseMessage response = await fixture.Test.Client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertIndex(response, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task An_older_browser_navigates_by_accepting_html()
        {
            using HttpRequestMessage request = new(HttpMethod.Get, "/1/documents");
            request.Headers.Add("Accept", "text/html,application/xhtml+xml;q=0.9,*/*;q=0.8");
            using HttpResponseMessage response = await fixture.Test.Client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Theory]
        [InlineData("/api/nothing", "not-found")]
        [InlineData("/bff/nothing", "not-found")]
        [InlineData("/health/other", "not-found")]
        [InlineData("/.well-known/other", "not-found")]
        [InlineData("/1/api/web/nothing", "route-not-found")]
        [InlineData("/1/hub/nothing", "route-not-found")]
        [InlineData("/1/blobs/x", "route-not-found")]
        [InlineData("/1/mcp/x", "route-not-found")]
        public async Task A_navigation_to_a_server_path_gets_a_problem(string path, string code)
        {
            using HttpRequestMessage request = Navigation(path);
            using HttpResponseMessage response = await fixture.Test.Client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(code, (await Problem.ReadAsync(response)).Code);
        }

        [Theory]
        [InlineData("GET", "/1/documents", "cors", "route-not-found")]
        [InlineData("GET", "/documents", "no-cors", "not-found")]
        [InlineData("POST", "/documents", "navigate", "not-found")]
        [InlineData("DELETE", "/1/documents", "navigate", "route-not-found")]
        public async Task A_request_that_is_not_a_navigation_gets_a_problem(string method, string path, string mode, string code)
        {
            using HttpRequestMessage request = new(new HttpMethod(method), path);
            request.Headers.Add("Sec-Fetch-Mode", mode);
            request.Headers.Add("Accept", "text/html");
            using HttpResponseMessage response = await fixture.Test.Client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(code, (await Problem.ReadAsync(response)).Code);
        }

        [Fact]
        public async Task A_host_without_the_SPA_answers_navigations_not_found()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options => options.ManifestPath = Path.Combine(Path.GetTempPath(), "no-such-manifest.json"));
            using HttpRequestMessage request = Navigation("/1/documents");
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("not-found", (await Problem.ReadAsync(response)).Code);
        }

        [Theory]
        [InlineData("main-ABCD2345.js", true)]
        [InlineData("assets/chunk-Z9Y8X7W6.js", true)]
        [InlineData("styles-ABCD2345.css.br", true)]
        [InlineData("styles-ABCD2345.css.gz", true)]
        [InlineData("favicon.ico", false)]
        [InlineData("index.html", false)]
        [InlineData("main-abcd2345.js", false)]
        [InlineData("main-ABC.js", false)]
        [InlineData("main-ABCD2345", false)]
        public void Hashed_names_are_recognized(string route, bool hashed)
        {
            Assert.Equal(hashed, SpaHosting.IsHashed(route));
        }

        [Fact]
        public void The_policy_admits_the_realtime_service_but_never_its_key()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Azure:SignalR:ConnectionString"] = "Endpoint=https://acme.service.signalr.net;AccessKey=c2VjcmV0;Version=1.0;",
                })
                .Build();
            string policy = SpaHosting.ContentSecurityPolicy(configuration, new IdentityHostOptions { Authority = TellmaTestHost.Issuer });

            Assert.Contains("connect-src 'self' https://acme.service.signalr.net wss://acme.service.signalr.net;", policy, StringComparison.Ordinal);
            Assert.DoesNotContain("c2VjcmV0", policy, StringComparison.Ordinal);
            Assert.Contains("form-action 'self' https://identity.test;", policy, StringComparison.Ordinal);
        }

        [Fact]
        public void The_policy_of_an_in_process_identity_server_posts_forms_to_itself_alone()
        {
            IConfiguration configuration = new ConfigurationBuilder().Build();
            string policy = SpaHosting.ContentSecurityPolicy(configuration, new IdentityHostOptions { Mode = IdentityMode.InProc });

            Assert.Contains("connect-src 'self';", policy, StringComparison.Ordinal);
            Assert.Contains("form-action 'self';", policy, StringComparison.Ordinal);
            Assert.Contains("frame-ancestors 'none'", policy, StringComparison.Ordinal);
            Assert.Contains("require-trusted-types-for 'script'", policy, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("Endpoint=http://localhost:8888;Port=8080;Version=1.0;", "http://localhost:8888", "ws://localhost:8888")]
        [InlineData("AccessKey=x;Endpoint=https://a.service.signalr.net", "https://a.service.signalr.net", "wss://a.service.signalr.net")]
        public void The_realtime_service_origins_come_from_its_endpoint(string connectionString, string https, string wss)
        {
            Assert.Equal((https, wss), SpaHosting.SignalROrigins(connectionString));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("AccessKey=x")]
        [InlineData("Endpoint=ftp://a.example")]
        [InlineData("Endpoint=not a url")]
        public void A_connection_string_without_a_web_endpoint_admits_nothing(string? connectionString)
        {
            Assert.Null(SpaHosting.SignalROrigins(connectionString));
        }

        /// <summary>A browser navigation.</summary>
        /// <param name="path">The path.</param>
        /// <returns>The request.</returns>
        private static HttpRequestMessage Navigation(string path)
        {
            HttpRequestMessage request = new(HttpMethod.Get, path);
            request.Headers.Add("Sec-Fetch-Mode", "navigate");
            request.Headers.Add("Sec-Fetch-Site", "none");
            request.Headers.Add("Accept", "text/html");
            return request;
        }

        /// <summary>Asserts a response is the index page with its headers.</summary>
        /// <param name="response">The response.</param>
        /// <param name="body">Its body.</param>
        private static void AssertIndex(HttpResponseMessage response, string body)
        {
            Assert.Equal(IndexText, body);
            Assert.Equal(SpaHosting.NoCache, response.Headers.CacheControl?.ToString());
            Assert.Equal("strict-origin-when-cross-origin", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
            Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
            Assert.Equal("same-origin", Assert.Single(response.Headers.GetValues("Cross-Origin-Opener-Policy")));
            Assert.Contains("camera=()", Assert.Single(response.Headers.GetValues("Permissions-Policy")), StringComparison.Ordinal);
            string policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
            Assert.StartsWith("default-src 'self'; script-src 'self';", policy, StringComparison.Ordinal);
            Assert.Contains("form-action 'self' https://identity.test;", policy, StringComparison.Ordinal);
        }

        /// <summary>One host for the suite, over a published SPA in a temporary web root.</summary>
        public sealed class Host : IAsyncLifetime
        {
            private readonly string _root = Path.Combine(Path.GetTempPath(), "tellma-spa-" + Guid.NewGuid().ToString("N"));
            private TellmaTestHost? _host;

            /// <summary>The started host.</summary>
            internal TellmaTestHost Test => _host ?? throw new InvalidOperationException("The host has not started.");

            /// <inheritdoc />
            public async ValueTask InitializeAsync()
            {
                string webRoot = Path.Combine(_root, "wwwroot");
                _ = Directory.CreateDirectory(webRoot);
                List<object> endpoints = [];
                foreach ((string name, string text, string type) in new[]
                {
                    ("index.html", IndexText, "text/html"),
                    ("main-ABCD2345.js", "console.log(1);", "text/javascript"),
                    ("favicon.ico", "icon", "image/x-icon"),
                })
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(text);
                    await File.WriteAllBytesAsync(Path.Combine(webRoot, name), bytes, TestContext.Current.CancellationToken);
                    endpoints.Add(Endpoint(name, bytes.Length, type));
                }

                // The manifest a publish writes: one endpoint per file, with its headers.
                string manifest = Path.Combine(_root, "spa.staticwebassets.endpoints.json");
                await File.WriteAllTextAsync(
                    manifest,
                    JsonSerializer.Serialize(new { Version = 1, ManifestType = "Publish", Endpoints = endpoints }),
                    TestContext.Current.CancellationToken);

                _host = await TellmaTestHost.StartAsync(options =>
                {
                    options.WebRoot = webRoot;
                    options.ManifestPath = manifest;
                });
            }

            /// <inheritdoc />
            public async ValueTask DisposeAsync()
            {
                if (_host is not null)
                {
                    await _host.DisposeAsync();
                }

                Directory.Delete(_root, recursive: true);
            }

            /// <summary>A manifest endpoint for a file at the web root.</summary>
            /// <param name="name">The file's name, which is also its route.</param>
            /// <param name="length">Its length in bytes.</param>
            /// <param name="type">Its media type.</param>
            /// <returns>The endpoint.</returns>
            private static object Endpoint(string name, int length, string type)
            {
                return new
                {
                    Route = name,
                    AssetFile = name,
                    Selectors = Array.Empty<object>(),
                    EndpointProperties = Array.Empty<object>(),
                    ResponseHeaders = new object[]
                    {
                        new { Name = "Content-Length", Value = length.ToString(CultureInfo.InvariantCulture) },
                        new { Name = "Content-Type", Value = type },
                        new { Name = "ETag", Value = $"\"{name.GetHashCode(StringComparison.Ordinal):x8}\"" },
                        new { Name = "Last-Modified", Value = "Thu, 01 Jan 2026 00:00:00 GMT" },
                        new { Name = "Cache-Control", Value = "max-age=3600" },
                    },
                };
            }
        }
    }
}
