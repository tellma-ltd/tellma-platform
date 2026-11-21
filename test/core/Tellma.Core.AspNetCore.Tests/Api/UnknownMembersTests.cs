// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.AspNetCore.Api;
using Tellma.Testing.WebHost;

namespace Tellma.Core.AspNetCore.Tests.Api
{
    /// <summary>
    ///     A request body member no property of its type declares is skipped as before and counted
    ///     by its type, at any depth, and the counting member is never written.
    /// </summary>
    public sealed class UnknownMembersTests
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task An_unknown_member_is_skipped_and_counted_by_its_type()
        {
            await using TellmaTestHost host = await StartAsync();
            using MetricCollector<long> unknown = new(host.Services.GetRequiredService<IMeterFactory>(), ApiTelemetryNames.MeterName, ApiTelemetryNames.UnknownMembers);

            using HttpRequestMessage request = TellmaTestHost.SpaRequest(
                HttpMethod.Post,
                "/1/api/web/echo-sample",
                host.Cookie(),
                /*lang=json,strict*/ """{"name":"a","extra":1,"inner":{"value":2,"stale":{"x":[1,2]},"gone":null}}""");
            using HttpResponseMessage response = await host.Client.SendAsync(request, Token);

            // --- The known members round-trip; the unknown ones are gone ---
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(/*lang=json,strict*/ """{"name":"a","inner":{"value":2}}""", await response.Content.ReadAsStringAsync(Token));

            // --- One count per skipped member, tagged by the type that skipped it ---
            var counts = unknown.GetMeasurementSnapshot()
                .GroupBy(static measurement => (string)measurement.Tags[ApiTelemetryNames.TypeTag]!)
                .ToDictionary(static group => group.Key, static group => group.Sum(static measurement => measurement.Value), StringComparer.Ordinal);
            Assert.Equal(new Dictionary<string, long>(StringComparer.Ordinal) { [nameof(SampleBody)] = 1, [nameof(SampleInner)] = 2 }, counts);
        }

        [Fact]
        public async Task A_body_without_unknown_members_counts_nothing()
        {
            await using TellmaTestHost host = await StartAsync();
            using MetricCollector<long> unknown = new(host.Services.GetRequiredService<IMeterFactory>(), ApiTelemetryNames.MeterName, ApiTelemetryNames.UnknownMembers);

            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Post, "/1/api/web/echo-sample", host.Cookie(), /*lang=json,strict*/ """{"name":"a"}""");
            using HttpResponseMessage response = await host.Client.SendAsync(request, Token);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(unknown.GetMeasurementSnapshot());
        }

        [Fact]
        public void A_type_that_refuses_unknown_members_or_keeps_its_own_is_left_alone()
        {
            JsonSerializerOptions options = new() { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
            using ServiceProvider services = new ServiceCollection().AddMetrics().BuildServiceProvider();
            UnknownMembers.Apply(options, new ApiMetrics(services.GetRequiredService<IMeterFactory>()));

            Assert.DoesNotContain(options.GetTypeInfo(typeof(StrictBody)).Properties, static property => property.IsExtensionData);
            Assert.Equal(nameof(KeepingBody.Rest), Assert.Single(options.GetTypeInfo(typeof(KeepingBody)).Properties, static property => property.IsExtensionData).Name);
            _ = Assert.Single(options.GetTypeInfo(typeof(SampleBody)).Properties, static property => property.IsExtensionData);
        }

        /// <summary>Starts a host with an endpoint that echoes its body.</summary>
        /// <returns>The host.</returns>
        private static Task<TellmaTestHost> StartAsync()
        {
            return TellmaTestHost.StartAsync(static options => options.Map = static (_, endpoints) =>
                endpoints.Web.MapPost("/echo-sample", static (SampleBody body) => Results.Json(body))
                    .WithMutation(true)
                    .AllowMember());
        }
    }

    /// <summary>A body with a nested object.</summary>
    /// <param name="Name">A name.</param>
    /// <param name="Inner">A nested object.</param>
    public sealed record SampleBody(string Name, SampleInner? Inner);

    /// <summary>The nested object.</summary>
    /// <param name="Value">A value.</param>
    public sealed record SampleInner(int Value);

    /// <summary>A body that refuses unknown members itself.</summary>
    /// <param name="Name">A name.</param>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record StrictBody(string Name);

    /// <summary>A body that keeps its unknown members.</summary>
    public sealed class KeepingBody
    {
        /// <summary>A name.</summary>
        public string? Name { get; set; }

        /// <summary>The unknown members.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Rest { get; set; }
    }
}
