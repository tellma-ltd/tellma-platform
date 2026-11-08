// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.IntegrationTests.Infrastructure;

namespace Tellma.Identity.IntegrationTests.Api
{
    /// <summary>
    ///     The management API's routes live under a version segment: every route answers under
    ///     <c>v1</c>, nothing answers without the segment or under an unmapped version, and each
    ///     call is counted on the <c>Tellma.Identity</c> meter.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class ManagementApiRouteTests(SqlServerFixture fixture)
    {
        /// <summary>
        ///     Turns off the Development-only static-asset fallback (<c>{**path:file}</c>), which
        ///     otherwise answers 405 to a POST on any unmapped path. No host outside Development
        ///     maps it, and with it in place a removed route could not be told from a missing one.
        /// </summary>
        private static readonly Dictionary<string, string?> WithoutDevelopmentFallback = new()
        {
            ["DisableStaticAssetNotFoundRuntimeFallback"] = "true",
        };

        /// <summary>A delivery-status body naming a subject nobody invited.</summary>
        private static readonly string[] UnknownSubject = ["unknown-sub"];

        /// <summary>Every management route, relative to the version segment.</summary>
        private static readonly (HttpMethod Method, string Path, bool OperatorOnly)[] Routes =
        [
            (HttpMethod.Post, "invitations", false),
            (HttpMethod.Post, "invitations/delivery-status", false),
            (HttpMethod.Post, "service-accounts", false),
            (HttpMethod.Get, "service-accounts/some-client", false),
            (HttpMethod.Delete, "service-accounts/some-client", false),
            (HttpMethod.Get, "users/some-sub", true),
            (HttpMethod.Post, "users/some-sub/temporary-access-passes", true),
        ];

        [Fact]
        public async Task Every_route_answers_under_v1_and_404_without_the_segment_standalone()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(
                fixture, "idroutes", WithoutDevelopmentFallback);
            using HttpClient client = factory.CreateClient();

            foreach ((HttpMethod method, string path, bool _) in Routes)
            {
                // The route exists: an anonymous call draws the authentication challenge.
                await AssertStatusAsync(client, method, $"/api/identity/v1/{path}", HttpStatusCode.Unauthorized);

                // The unversioned route is gone, and an unmapped version was never there.
                await AssertStatusAsync(client, method, $"/api/identity/{path}", HttpStatusCode.NotFound);
                await AssertStatusAsync(client, method, $"/api/identity/v2/{path}", HttpStatusCode.NotFound);
            }
        }

        [Fact]
        public async Task Every_distribution_route_answers_under_v1_and_404_without_the_segment_in_proc()
        {
            using InProcFactory factory = DatabaseBackedFactory.CreateInProc(
                fixture, "idroutesip", WithoutDevelopmentFallback);
            using HttpClient client = factory.CreateClient();

            foreach ((HttpMethod method, string path, bool operatorOnly) in Routes)
            {
                // In-proc the API lives under the engine's path base; the operator surface does
                // not exist at all there.
                await AssertStatusAsync(
                    client,
                    method,
                    $"/id/api/identity/v1/{path}",
                    operatorOnly ? HttpStatusCode.NotFound : HttpStatusCode.Unauthorized);
                await AssertStatusAsync(client, method, $"/id/api/identity/{path}", HttpStatusCode.NotFound);
                await AssertStatusAsync(client, method, $"/api/identity/v1/{path}", HttpStatusCode.NotFound);
            }
        }

        [Fact]
        public async Task A_call_is_counted_on_the_identity_meter_with_its_version_and_endpoint()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idapicalls");
            string token = await TestData.GetIdentityScopeTokenAsync(factory);

            // Collected from the host's own meter factory, which the engine's metrics are built on.
            using MetricCollector<long> calls = new(
                factory.Services.GetRequiredService<IMeterFactory>(),
                IdentityMetrics.MeterName,
                IdentityMetrics.ApiCallsInstrumentName);

            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri("/api/identity/v1/invitations/delivery-status", UriKind.Relative),
                new { subs = UnknownSubject },
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            CollectedMeasurement<long> measurement = Assert.Single(calls.GetMeasurementSnapshot());
            Assert.Equal(1, measurement.Value);
            Assert.Equal("v1", measurement.Tags[IdentityMetrics.ApiVersionTag]);
            Assert.Equal(ManagementApiEndpoints.DeliveryStatus, measurement.Tags[IdentityMetrics.ApiEndpointTag]);

            // An anonymous call never reaches the API, so it is not counted.
            using HttpClient anonymous = factory.CreateClient();
            using HttpResponseMessage refused = await anonymous.PostAsJsonAsync(
                new Uri("/api/identity/v1/invitations/delivery-status", UriKind.Relative),
                new { subs = UnknownSubject },
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
            Assert.Single(calls.GetMeasurementSnapshot());
        }

        /// <summary>Sends an anonymous request and asserts its status, naming the request on failure.</summary>
        private static async Task AssertStatusAsync(
            HttpClient client, HttpMethod method, string path, HttpStatusCode expected)
        {
            HttpStatusCode actual = await SendAsync(client, method, path);
            Assert.True(actual == expected, $"{method} {path} answered {(int)actual} {actual}, expected {(int)expected} {expected}.");
        }

        /// <summary>Sends an anonymous request with an empty JSON body and returns its status.</summary>
        private static async Task<HttpStatusCode> SendAsync(HttpClient client, HttpMethod method, string path)
        {
            using HttpRequestMessage request = new(method, new Uri(path, UriKind.Relative));
            if (method != HttpMethod.Get && method != HttpMethod.Delete)
            {
                request.Content = JsonContent.Create(new { });
            }

            using HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            return response.StatusCode;
        }
    }
}
