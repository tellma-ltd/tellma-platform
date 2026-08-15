// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tellma.Core.Webhooks.Tests.Infrastructure;

namespace Tellma.Core.Webhooks.Tests.Fronting
{
    /// <summary>
    ///     A receiver key is part of its connector's public surface — operators configure provider
    ///     dashboards against it — so a duplicate or malformed key must fail at startup, not on the
    ///     first callback.
    /// </summary>
    public class WebhookStartupValidationTests
    {
        [Fact]
        public async Task Refuses_two_receivers_with_the_same_key()
        {
            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => WebhookTestHost.StartAsync(
                    [new StubWebhookReceiver("sendgrid-events"), new StubWebhookReceiver("sendgrid-events")]));

            Assert.Contains("More than one webhook receiver", failure.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("SendGrid-Events")]
        [InlineData("sendgrid_events")]
        [InlineData("")]
        public async Task Refuses_a_key_that_is_not_lowercase_kebab_case(string key)
        {
            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => WebhookTestHost.StartAsync([new StubWebhookReceiver(key)]));

            Assert.Contains("lowercase kebab-case", failure.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("64")]
        public async Task Refuses_a_body_cap_too_small_to_accept_any_real_payload(string cap)
        {
            // A cap of zero answers every inbound webhook 413 — silently, because the provider sees
            // only a status code and the deployment sees only a metric nobody is looking at.
            OptionsValidationException failure = await Assert.ThrowsAsync<OptionsValidationException>(
                () => WebhookTestHost.StartAsync(
                    [new StubWebhookReceiver("stub")],
                    new Dictionary<string, string?> { ["Webhooks:MaxRequestBodyBytes"] = cap }));

            Assert.Contains(failure.Failures, static f => f.Contains("MaxRequestBodyBytes", StringComparison.Ordinal));
        }

        [Fact]
        public void Refuses_to_map_the_endpoint_without_the_service_registration()
        {
            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
            WebApplication app = builder.Build();

            // Mapping without AddTellmaWebhooks would otherwise fail on the first request instead of
            // at composition, where the mistake is visible.
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
                app.MapTellmaWebhooks);

            Assert.Contains(nameof(TellmaWebhooksServiceCollectionExtensions.AddTellmaWebhooks), failure.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Marks_the_endpoint_so_tenant_resolution_can_skip_it()
        {
            await using WebhookTestHost host = await WebhookTestHost.StartAsync([new StubWebhookReceiver("stub")]);

            EndpointDataSource endpoints = host.Services.GetRequiredService<EndpointDataSource>();
            Endpoint endpoint = Assert.Single(endpoints.Endpoints);

            // A webhook belongs to the deployable, not to a tenant: the caller presents no tenant,
            // and the tenant is reached later through the correlation in the payload.
            Assert.NotNull(endpoint.Metadata.GetMetadata<WebhookEndpointMetadata>());
        }
    }
}
