// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Csrf;
using Tellma.Core.AspNetCore.Tests.Infrastructure;

namespace Tellma.Core.AspNetCore.Tests.Csrf
{
    /// <summary>
    ///     The CSRF control: each rule on a cookie-authenticated write, the provenance rules alone on
    ///     safe methods and the hub, and the exemptions of bearer and anonymous endpoints.
    /// </summary>
    public sealed class CsrfTests(CsrfTests.Host fixture) : IClassFixture<CsrfTests.Host>
    {
        /// <summary>
        ///     A write's variations: the header to change (removed when the value is null), and the
        ///     status and rule expected; no rule for a request the control passes.
        /// </summary>
        public static TheoryData<string, string?, int, string?> Writes()
        {
            return new TheoryData<string, string?, int, string?>
            {
                { "Sec-Fetch-Site", "cross-site", 403, CsrfRules.SecFetchSite },
                { "Sec-Fetch-Site", "same-site", 403, CsrfRules.SecFetchSite },
                { "Sec-Fetch-Site", "none", 200, null },
                { "Sec-Fetch-Site", null, 200, null },
                { "Origin", "https://evil.example", 403, CsrfRules.Origin },
                { "Origin", "https://acme.app.tellma.com.evil.example", 403, CsrfRules.Origin },
                { "Origin", "http://acme.app.tellma.com", 403, CsrfRules.Origin },
                { "Origin", "null", 403, CsrfRules.Origin },
                { "Origin", "https://ACME.app.tellma.com:443", 200, null },
                { "Origin", null, 200, null },
                { "Tellma-Client", null, 403, CsrfRules.Header },
                { "Tellma-Client", "web", 403, CsrfRules.Header },
                { "Tellma-Client", "web/", 403, CsrfRules.Header },
                { "Tellma-Client", "/1.0", 403, CsrfRules.Header },
                { "Tellma-Client", "excel/1.0", 403, CsrfRules.Header },
                { "Tellma-Client", "Web/1.0", 403, CsrfRules.Header },
                { "Tellma-Client", "web/1.0 beta", 403, CsrfRules.Header },
                { "Tellma-Client", "web/2.3.4-rc.1+build", 200, null },
            };
        }

        [Theory]
        [MemberData(nameof(Writes))]
        public async Task A_cookie_write_answers_each_rule(string header, string? value, int status, string? rule)
        {
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Post, "/1/api/web/probe", fixture.Cookie, "{}");
            _ = request.Headers.Remove(header);
            if (value is not null)
            {
                request.Headers.TryAddWithoutValidation(header, value);
            }

            await AssertAsync(request, status, rule);
        }

        [Fact]
        public async Task A_referrer_from_another_origin_is_refused_when_no_origin_is_sent()
        {
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Post, "/1/api/web/probe", fixture.Cookie, "{}");
            _ = request.Headers.Remove("Origin");
            request.Headers.Referrer = new Uri("https://evil.example/page");

            await AssertAsync(request, 403, CsrfRules.Origin);
        }

        [Fact]
        public async Task A_referrer_from_the_public_origin_passes()
        {
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Post, "/1/api/web/probe", fixture.Cookie, "{}");
            _ = request.Headers.Remove("Origin");
            request.Headers.Referrer = new Uri(TellmaTestHost.PublicOrigin + "/1/settings");

            await AssertAsync(request, 200, null);
        }

        [Fact]
        public async Task Two_client_headers_are_refused()
        {
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Post, "/1/api/web/probe", fixture.Cookie, "{}");
            request.Headers.Add(TellmaAuthentication.CsrfHeaderName, "web/2.0.0");

            await AssertAsync(request, 403, CsrfRules.Header);
        }

        [Theory]
        [InlineData("text/plain", 415, CsrfRules.ContentType)]
        [InlineData("application/x-www-form-urlencoded", 415, CsrfRules.ContentType)]
        [InlineData("multipart/form-data; boundary=x", 415, CsrfRules.ContentType)]
        [InlineData("application/json; charset=utf-8", 200, null)]
        [InlineData("APPLICATION/JSON", 200, null)]
        public async Task A_body_must_be_json(string contentType, int status, string? rule)
        {
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Post, "/1/api/web/probe", fixture.Cookie);
            request.Content = new StringContent("{}");
            _ = request.Content.Headers.Remove("Content-Type");
            _ = request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);

            await AssertAsync(request, status, rule);
        }

        [Fact]
        public async Task A_body_without_a_media_type_is_refused()
        {
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Post, "/1/api/web/probe", fixture.Cookie);
            request.Content = new ByteArrayContent([1, 2, 3]);

            await AssertAsync(request, 415, CsrfRules.ContentType);
        }

        [Fact]
        public async Task A_write_without_a_body_needs_no_media_type()
        {
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Post, "/1/api/web/probe", fixture.Cookie);
            request.Content = new ByteArrayContent([]);

            await AssertAsync(request, 200, null);
        }

        [Theory]
        [InlineData("application/octet-stream", 200, null)]
        [InlineData("image/png", 200, null)]
        public async Task An_endpoint_that_accepts_binary_takes_any_declared_media_type(string contentType, int status, string? rule)
        {
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Post, "/1/api/web/upload", fixture.Cookie);
            request.Content = new ByteArrayContent([1, 2, 3]);
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

            await AssertAsync(request, status, rule);
        }

        [Fact]
        public async Task An_endpoint_that_accepts_binary_still_requires_a_declared_media_type_and_the_header()
        {
            using HttpRequestMessage undeclared = TellmaTestHost.SpaRequest(HttpMethod.Post, "/1/api/web/upload", fixture.Cookie);
            undeclared.Content = new ByteArrayContent([1, 2, 3]);
            await AssertAsync(undeclared, 415, CsrfRules.ContentType);

            using HttpRequestMessage headerless = TellmaTestHost.SpaRequest(HttpMethod.Post, "/1/api/web/upload", fixture.Cookie);
            _ = headerless.Headers.Remove(TellmaAuthentication.CsrfHeaderName);
            headerless.Content = new ByteArrayContent([1, 2, 3]);
            headerless.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            await AssertAsync(headerless, 403, CsrfRules.Header);
        }

        [Fact]
        public async Task A_cookie_get_needs_no_header_but_answers_to_the_provenance_rules()
        {
            using HttpRequestMessage plain = new(HttpMethod.Get, "/1/blobs/photo/7");
            plain.Headers.Add("Cookie", fixture.Cookie);
            await AssertAsync(plain, 200, null);

            using HttpRequestMessage crossSite = new(HttpMethod.Get, "/1/blobs/photo/7");
            crossSite.Headers.Add("Cookie", fixture.Cookie);
            crossSite.Headers.Add("Sec-Fetch-Site", "cross-site");
            await AssertAsync(crossSite, 403, CsrfRules.SecFetchSite);

            using HttpRequestMessage foreign = new(HttpMethod.Get, "/1/blobs/photo/7");
            foreign.Headers.Add("Cookie", fixture.Cookie);
            foreign.Headers.Add("Origin", "https://evil.example");
            await AssertAsync(foreign, 403, CsrfRules.Origin);
        }

        [Fact]
        public async Task The_hub_negotiate_needs_no_header_but_answers_to_the_provenance_rules()
        {
            using HttpRequestMessage plain = new(HttpMethod.Post, "/1/hub/negotiate");
            plain.Headers.Add("Cookie", fixture.Cookie);
            plain.Content = new StringContent("negotiate");
            await AssertAsync(plain, 200, null);

            using HttpRequestMessage crossSite = new(HttpMethod.Post, "/1/hub/negotiate");
            crossSite.Headers.Add("Cookie", fixture.Cookie);
            crossSite.Headers.Add("Sec-Fetch-Site", "cross-site");
            await AssertAsync(crossSite, 403, CsrfRules.SecFetchSite);
        }

        [Fact]
        public async Task A_tenantless_endpoint_on_the_session_fallback_is_controlled_too()
        {
            using HttpRequestMessage refused = new(HttpMethod.Post, "/api/echo");
            refused.Headers.Add("Cookie", fixture.Cookie);
            refused.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            await AssertAsync(refused, 403, CsrfRules.Header);

            using HttpRequestMessage passed = TellmaTestHost.SpaRequest(HttpMethod.Post, "/api/echo", fixture.Cookie, "{}");
            await AssertAsync(passed, 200, null);
        }

        [Fact]
        public async Task Bearer_and_anonymous_endpoints_are_exempt()
        {
            using HttpRequestMessage bearer = TellmaTestHost.BearerRequest(HttpMethod.Post, "/1/api/v1/probe", TellmaTestHost.UserToken());
            bearer.Headers.Add("Sec-Fetch-Site", "cross-site");
            bearer.Headers.Add("Origin", "https://evil.example");
            bearer.Content = new StringContent("x", System.Text.Encoding.UTF8, "text/plain");
            await AssertAsync(bearer, 200, null);

            using HttpRequestMessage anonymous = new(HttpMethod.Post, "/api/open");
            anonymous.Headers.Add("Sec-Fetch-Site", "cross-site");
            anonymous.Content = new StringContent("x", System.Text.Encoding.UTF8, "text/plain");
            await AssertAsync(anonymous, 200, null);
        }

        [Fact]
        public async Task A_refusal_is_counted_by_rule_before_any_tenant_is_resolved()
        {
            using MetricCollector<long> collector = new(
                fixture.Services.GetRequiredService<IMeterFactory>(), HostTelemetryNames.MeterName, HostTelemetryNames.CsrfRejections);

            // Tenant 99 does not exist: the refusal comes first, so it never learns that.
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Post, "/99/api/web/probe", fixture.Cookie, "{}");
            request.Headers.Remove("Sec-Fetch-Site");
            request.Headers.Add("Sec-Fetch-Site", "cross-site");
            await AssertAsync(request, 403, CsrfRules.SecFetchSite);

            CollectedMeasurement<long> measurement = Assert.Single(collector.GetMeasurementSnapshot());
            Assert.Equal(CsrfRules.SecFetchSite, measurement.Tags[HostTelemetryNames.RuleTag]);
        }

        /// <summary>Sends a request and asserts its answer.</summary>
        /// <param name="request">The request.</param>
        /// <param name="status">The status expected.</param>
        /// <param name="rule">The rule a refusal names; null for a request the control passes.</param>
        /// <returns>A task that completes when the answer is checked.</returns>
        private async Task AssertAsync(HttpRequestMessage request, int status, string? rule)
        {
            using HttpResponseMessage response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(status, (int)response.StatusCode);
            if (rule is not null)
            {
                Problem problem = await Problem.ReadAsync(response);
                Assert.Equal("csrf-rejected", problem.Code);
                Assert.Equal(rule, problem.Argument("rule"));
            }
        }

        /// <summary>One host for the suite: the control keeps no state between requests.</summary>
        public sealed class Host : IAsyncLifetime
        {
            private TellmaTestHost? _host;

            /// <summary>The host's client.</summary>
            internal HttpClient Client => Started.Client;

            /// <summary>The host's services.</summary>
            internal IServiceProvider Services => Started.Services;

            /// <summary>A member's session cookie.</summary>
            internal string Cookie { get; private set; } = string.Empty;

            /// <summary>The started host.</summary>
            private TellmaTestHost Started => _host ?? throw new InvalidOperationException("The host has not started.");

            /// <inheritdoc />
            public async ValueTask InitializeAsync()
            {
                _host = await TellmaTestHost.StartAsync();
                Cookie = _host.Cookie();
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
