// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Tests.Infrastructure;
using Tellma.Core.Tenancy;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.AspNetCore.Tests.Authentication
{
    /// <summary>
    ///     Each surface authenticates its own scheme alone: a cookie never reaches a bearer surface, a
    ///     token never reaches a cookie surface, and a bearer surface checks its scope and audience.
    /// </summary>
    public sealed class SchemeIsolationTests(SchemeIsolationTests.Host fixture) : IClassFixture<SchemeIsolationTests.Host>
    {
        [Fact]
        public async Task A_token_does_not_authenticate_the_web_surface()
        {
            using HttpRequestMessage request = TellmaTestHost.BearerRequest(HttpMethod.Get, "/1/api/web/probe", TellmaTestHost.UserToken());
            await AssertStatusAsync(request, HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task A_cookie_does_not_authenticate_the_public_api()
        {
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/v1/probe", fixture.Cookie);
            await AssertStatusAsync(request, HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task A_cookie_does_not_authenticate_the_control_plane()
        {
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Get, "/api/admin/v1/tenants", fixture.Cookie);
            await AssertStatusAsync(request, HttpStatusCode.Unauthorized);
        }

        [Theory]
        [InlineData("tellma_api", TellmaTestHost.PublicOrigin, HttpStatusCode.OK)]
        [InlineData("openid tellma_api profile", TellmaTestHost.PublicOrigin, HttpStatusCode.OK)]
        [InlineData("openid", TellmaTestHost.PublicOrigin, HttpStatusCode.Forbidden)]
        [InlineData("tellma_control_plane", TellmaTestHost.PublicOrigin, HttpStatusCode.Forbidden)]
        [InlineData("tellma_api", "https://other.app.tellma.com", HttpStatusCode.Forbidden)]
        [InlineData("tellma_api", TellmaTestHost.PublicOrigin + "/1/mcp", HttpStatusCode.Forbidden)]
        public async Task The_public_api_requires_its_scope_and_the_public_origin_audience(string scope, string audience, HttpStatusCode status)
        {
            using HttpRequestMessage request = TellmaTestHost.BearerRequest(HttpMethod.Get, "/1/api/v1/probe", TellmaTestHost.UserToken(audience: audience, scope: scope));
            await AssertStatusAsync(request, status);
        }

        [Theory]
        [InlineData(TellmaTestHost.PublicOrigin + "/1/mcp", HttpStatusCode.OK)]
        [InlineData(TellmaTestHost.PublicOrigin + "/2/mcp", HttpStatusCode.Forbidden)]
        [InlineData(TellmaTestHost.PublicOrigin, HttpStatusCode.Forbidden)]
        public async Task Mcp_requires_the_audience_of_the_tenant_the_route_names(string audience, HttpStatusCode status)
        {
            using HttpRequestMessage request = TellmaTestHost.BearerRequest(HttpMethod.Get, "/1/mcp", TellmaTestHost.UserToken(audience: audience));
            await AssertStatusAsync(request, status);
        }

        [Theory]
        [InlineData("tellma_control_plane", HttpStatusCode.OK)]
        [InlineData("tellma_api", HttpStatusCode.Forbidden)]
        public async Task The_control_plane_requires_its_own_scope(string scope, HttpStatusCode status)
        {
            using HttpRequestMessage request = TellmaTestHost.BearerRequest(HttpMethod.Get, "/api/admin/v1/tenants", TellmaTestHost.ServiceToken("control-plane", scope: scope));
            await AssertStatusAsync(request, status);
        }

        [Fact]
        public async Task A_token_of_another_type_is_refused()
        {
            string idToken = TellmaTestHost.Token(
                new Dictionary<string, object> { ["sub"] = "sub-1", ["aud"] = TellmaTestHost.PublicOrigin, ["scope"] = "tellma_api" },
                type: "JWT");
            using HttpRequestMessage request = TellmaTestHost.BearerRequest(HttpMethod.Get, "/1/api/v1/probe", idToken);
            await AssertStatusAsync(request, HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task A_bearer_request_binds_the_principal_its_token_names()
        {
            using HttpRequestMessage user = TellmaTestHost.BearerRequest(HttpMethod.Get, "/1/api/v1/probe", TellmaTestHost.UserToken("sub-9"));
            using HttpResponseMessage userResponse = await fixture.Client.SendAsync(user, TestContext.Current.CancellationToken);
            ProbeResult userProbe = await ProbeResult.ReadAsync(userResponse);
            Assert.Equal(nameof(PrincipalKind.User), userProbe.Kind);
            Assert.Equal("sub-9", userProbe.Subject);
            Assert.Equal("cli", userProbe.ClientId);
            Assert.Null(userProbe.SessionHandle);

            using HttpRequestMessage service = TellmaTestHost.BearerRequest(HttpMethod.Get, "/1/api/v1/probe", TellmaTestHost.ServiceToken("svc-1"));
            using HttpResponseMessage serviceResponse = await fixture.Client.SendAsync(service, TestContext.Current.CancellationToken);
            ProbeResult serviceProbe = await ProbeResult.ReadAsync(serviceResponse);
            Assert.Equal(nameof(PrincipalKind.ServiceAccount), serviceProbe.Kind);
            Assert.Equal("svc-1", serviceProbe.Subject);
        }

        [Fact]
        public async Task Mcp_requests_carry_the_mcp_client_name()
        {
            using HttpRequestMessage request = TellmaTestHost.BearerRequest(HttpMethod.Get, "/1/mcp", TellmaTestHost.UserToken(audience: TellmaTestHost.PublicOrigin + "/1/mcp"));
            using HttpResponseMessage response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal("mcp", (await ProbeResult.ReadAsync(response)).Client);
        }

        /// <summary>Sends a request and asserts its status.</summary>
        /// <param name="request">The request.</param>
        /// <param name="status">The status expected.</param>
        /// <returns>A task that completes when the status is checked.</returns>
        private async Task AssertStatusAsync(HttpRequestMessage request, HttpStatusCode status)
        {
            using HttpResponseMessage response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(status, response.StatusCode);
        }

        /// <summary>One host for the suite.</summary>
        public sealed class Host : IAsyncLifetime
        {
            private TellmaTestHost? _host;

            /// <summary>The host's client.</summary>
            internal HttpClient Client => (_host ?? throw new InvalidOperationException("The host has not started.")).Client;

            /// <summary>A member's session cookie.</summary>
            internal string Cookie { get; private set; } = string.Empty;

            /// <inheritdoc />
            public async ValueTask InitializeAsync()
            {
                // The control plane lists tenants from the catalog's write side, held in memory here.
                FakeTenantCatalog catalog = new();
                catalog.Add(1);
                _host = await TellmaTestHost.StartAsync(options =>
                {
                    options.Rows.Add(FakeCatalogReader.Row(2));
                    options.ConfigureServices = services => services.Replace(ServiceDescriptor.Singleton<ITenantCatalog>(catalog));
                });
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
