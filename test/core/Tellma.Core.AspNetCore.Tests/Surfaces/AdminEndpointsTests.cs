// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Text;
using System.Text.Json;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Routing;
using Tellma.Core.AspNetCore.Tests.Infrastructure;
using Tellma.Core.Tenancy;

namespace Tellma.Core.AspNetCore.Tests.Surfaces
{
    /// <summary>
    ///     The control plane's admin surface: what it lists, how it registers and moves tenants, and
    ///     what it refuses.
    /// </summary>
    public sealed class AdminEndpointsTests
    {
        /// <summary>The control plane's client.</summary>
        private const string ControlPlane = "control-plane";

        [Fact]
        public async Task Info_adds_the_tenant_counts_to_the_distribution_info()
        {
            FakeTenantCatalog catalog = new();
            catalog.Add(1);
            catalog.Add(2);
            catalog.Add(3, TenantState.Suspended);
            await using TellmaTestHost host = await StartAsync(catalog);

            using JsonDocument info = await SendJsonAsync(host, HttpMethod.Get, "/api/admin/v1/info", body: null, HttpStatusCode.OK);
            Assert.Equal(TellmaTestHost.Slug, info.RootElement.GetProperty("slug").GetString());
            JsonElement counts = info.RootElement.GetProperty("tenantCounts");
            Assert.Equal(2, counts.GetProperty("Active").GetInt32());
            Assert.Equal(1, counts.GetProperty("Suspended").GetInt32());
            Assert.Equal(0, counts.GetProperty("Retired").GetInt32());
        }

        [Fact]
        public async Task Every_route_sits_under_a_version_the_info_document_announces()
        {
            await using TellmaTestHost host = await StartAsync(new FakeTenantCatalog());
            using HttpResponseMessage response = await host.Client.GetAsync("/api/distribution-info", TestContext.Current.CancellationToken);
            using var info = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            int[] versions = [.. info.RootElement.GetProperty("adminVersions").EnumerateArray().Select(static version => version.GetInt32())];

            string[] routes = [.. host.Services.GetRequiredService<MappedEndpoints>().Endpoints()
                .OfType<RouteEndpoint>()
                .Select(static endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
                .Where(static route => route.StartsWith("/api/admin", StringComparison.Ordinal))];
            Assert.NotEmpty(routes);
            Assert.All(routes, route => Assert.Contains(versions, version => route.StartsWith($"/api/admin/v{version}/", StringComparison.Ordinal)));
        }

        [Fact]
        public async Task Tenants_lists_every_descriptor_in_id_order()
        {
            FakeTenantCatalog catalog = new();
            catalog.Add(7);
            catalog.Add(2, TenantState.ReadOnly);
            await using TellmaTestHost host = await StartAsync(catalog);

            using JsonDocument tenants = await SendJsonAsync(host, HttpMethod.Get, "/api/admin/v1/tenants", body: null, HttpStatusCode.OK);
            JsonElement[] list = [.. tenants.RootElement.EnumerateArray()];
            Assert.Equal([2, 7], list.Select(static tenant => tenant.GetProperty("id").GetInt32()));
            Assert.Equal("ReadOnly", list[0].GetProperty("state").GetString());

            // The descriptor alone: never the location or the connection string.
            Assert.DoesNotContain("Data Source", tenants.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sql01", tenants.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Register_answers_501_when_the_deployment_provisions_through_the_migrator()
        {
            FakeTenantCatalog catalog = new();
            await using TellmaTestHost host = await StartAsync(catalog);

            using HttpResponseMessage response = await SendAsync(host, HttpMethod.Post, "/api/admin/v1/tenants", /*lang=json,strict*/ """{"name":"Beta","category":"Live"}""");
            Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
            Assert.Equal("provisioning-not-configured", (await Problem.ReadAsync(response)).Code);
            Assert.Empty(catalog.Registrations);
        }

        [Fact]
        public async Task Register_registers_the_tenant_and_starts_its_provisioning()
        {
            FakeTenantCatalog catalog = new();
            RecordingTrigger trigger = new();
            await using TellmaTestHost host = await StartAsync(catalog, trigger);

            using JsonDocument descriptor = await SendJsonAsync(host, HttpMethod.Post, "/api/admin/v1/tenants", /*lang=json,strict*/ """{"name":"Beta","category":"Sandbox","liveTenantId":1}""", HttpStatusCode.Accepted);
            int id = descriptor.RootElement.GetProperty("id").GetInt32();
            Assert.Equal("Provisioning", descriptor.RootElement.GetProperty("state").GetString());

            (TenantRegistration registration, string actor, int? requestedId) = Assert.Single(catalog.Registrations);
            Assert.Equal(new TenantRegistration("Beta", TenantCategory.Sandbox, 1, null), registration);
            Assert.Equal("control-plane:" + ControlPlane, actor);
            Assert.Null(requestedId);
            Assert.Equal([id], trigger.Started);
        }

        [Theory]
        [InlineData(/*lang=json,strict*/ """{"category":"Live"}""")]
        [InlineData(/*lang=json,strict*/ """{"name":"  ","category":"Live"}""")]
        [InlineData(/*lang=json,strict*/ """{"name":"Beta"}""")]
        [InlineData(/*lang=json,strict*/ """{"name":"Beta","category":"Galaxy"}""")]
        [InlineData("not json")]
        public async Task Register_refuses_a_malformed_body(string body)
        {
            FakeTenantCatalog catalog = new();
            RecordingTrigger trigger = new();
            await using TellmaTestHost host = await StartAsync(catalog, trigger);

            using HttpResponseMessage response = await SendAsync(host, HttpMethod.Post, "/api/admin/v1/tenants", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("bad-request", (await Problem.ReadAsync(response)).Code);
            Assert.Empty(catalog.Registrations);
            Assert.Empty(trigger.Started);
        }

        [Fact]
        public async Task State_moves_the_tenant_and_says_when_every_instance_serves_it()
        {
            FakeTenantCatalog catalog = new();
            catalog.Add(1);
            await using TellmaTestHost host = await StartAsync(catalog);

            using JsonDocument moved = await SendJsonAsync(host, HttpMethod.Post, "/api/admin/v1/tenants/1/state", /*lang=json,strict*/ """{"state":"Suspended","reason":"Unpaid"}""", HttpStatusCode.OK);
            Assert.Equal("Suspended", moved.RootElement.GetProperty("state").GetString());
            Assert.Equal(15, moved.RootElement.GetProperty("effectiveWithinSeconds").GetInt32());
            Assert.Equal((1, TenantState.Suspended, "Unpaid", "control-plane:" + ControlPlane), Assert.Single(catalog.StateChanges));
        }

        [Theory]
        [InlineData(/*lang=json,strict*/ """{"state":"Frozen"}""")]
        [InlineData(/*lang=json,strict*/ """{"state":"Provisioning"}""")]
        [InlineData(/*lang=json,strict*/ """{"reason":"No state"}""")]
        [InlineData(/*lang=json,strict*/ """{"state":3}""")]
        public async Task State_refuses_a_state_the_control_plane_does_not_set(string body)
        {
            FakeTenantCatalog catalog = new();
            catalog.Add(1);
            await using TellmaTestHost host = await StartAsync(catalog);

            using HttpResponseMessage response = await SendAsync(host, HttpMethod.Post, "/api/admin/v1/tenants/1/state", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(catalog.StateChanges);
        }

        [Fact]
        public async Task State_maps_the_catalog_refusals()
        {
            FakeTenantCatalog catalog = new();
            catalog.Add(1, TenantState.Retired);
            await using TellmaTestHost host = await StartAsync(catalog);

            using HttpResponseMessage illegal = await SendAsync(host, HttpMethod.Post, "/api/admin/v1/tenants/1/state", /*lang=json,strict*/ """{"state":"Active"}""");
            Assert.Equal(HttpStatusCode.Conflict, illegal.StatusCode);
            Assert.Equal(TenantProblemCodes.IllegalTransition, (await Problem.ReadAsync(illegal)).Code);

            using HttpResponseMessage absent = await SendAsync(host, HttpMethod.Post, "/api/admin/v1/tenants/9/state", /*lang=json,strict*/ """{"state":"Active"}""");
            Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        }

        [Fact]
        public async Task Members_of_an_unknown_tenant_is_404()
        {
            await using TellmaTestHost host = await StartAsync(new FakeTenantCatalog());
            using HttpResponseMessage response = await SendAsync(host, HttpMethod.Get, "/api/admin/v1/tenants/9/members", body: null);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task A_control_plane_write_needs_no_csrf_headers()
        {
            // The bearer surface is exempt from the cookie defenses: no Origin, no Tellma-Client.
            FakeTenantCatalog catalog = new();
            catalog.Add(1);
            await using TellmaTestHost host = await StartAsync(catalog);

            using HttpRequestMessage request = TellmaTestHost.BearerRequest(HttpMethod.Post, "/api/admin/v1/tenants/1/state", Token());
            request.Content = new StringContent(/*lang=json,strict*/ """{"state":"ReadOnly"}""", Encoding.UTF8, "application/json");
            request.Headers.Add("Sec-Fetch-Site", "cross-site");
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        /// <summary>Starts a host whose catalog and provisioning are the fakes given.</summary>
        /// <param name="catalog">The catalog.</param>
        /// <param name="trigger">The provisioning trigger; none leaves provisioning not configured.</param>
        /// <returns>The host.</returns>
        private static Task<TellmaTestHost> StartAsync(FakeTenantCatalog catalog, ITenantProvisioningTrigger? trigger = null)
        {
            return TellmaTestHost.StartAsync(options => options.ConfigureServices = services =>
            {
                _ = services.Replace(ServiceDescriptor.Singleton<ITenantCatalog>(catalog));
                if (trigger is not null)
                {
                    _ = services.Replace(ServiceDescriptor.Singleton(trigger));
                }
            });
        }

        /// <summary>The control plane's token.</summary>
        /// <returns>The token.</returns>
        private static string Token()
        {
            return TellmaTestHost.ServiceToken(ControlPlane, scope: "tellma_control_plane");
        }

        /// <summary>Sends a control-plane request.</summary>
        /// <param name="host">The host.</param>
        /// <param name="method">The method.</param>
        /// <param name="path">The path.</param>
        /// <param name="body">A JSON body, if any.</param>
        /// <returns>The response.</returns>
        private static async Task<HttpResponseMessage> SendAsync(TellmaTestHost host, HttpMethod method, string path, string? body)
        {
            using HttpRequestMessage request = TellmaTestHost.BearerRequest(method, path, Token());
            if (body is not null)
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            return await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        /// <summary>Sends a control-plane request and reads its JSON answer.</summary>
        /// <param name="host">The host.</param>
        /// <param name="method">The method.</param>
        /// <param name="path">The path.</param>
        /// <param name="body">A JSON body, if any.</param>
        /// <param name="status">The status expected.</param>
        /// <returns>The answer.</returns>
        private static async Task<JsonDocument> SendJsonAsync(TellmaTestHost host, HttpMethod method, string path, string? body, HttpStatusCode status)
        {
            using HttpResponseMessage response = await SendAsync(host, method, path, body);
            string text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.StatusCode == status, $"{(int)response.StatusCode}: {text}");
            return JsonDocument.Parse(text);
        }

        /// <summary>A provisioning trigger that records the tenants it was asked to provision.</summary>
        private sealed class RecordingTrigger : ITenantProvisioningTrigger
        {
            /// <summary>The tenants started, in order.</summary>
            public List<int> Started { get; } = [];

            /// <inheritdoc />
            public Task StartAsync(int tenantId, CancellationToken cancellationToken = default)
            {
                Started.Add(tenantId);
                return Task.CompletedTask;
            }
        }
    }
}
