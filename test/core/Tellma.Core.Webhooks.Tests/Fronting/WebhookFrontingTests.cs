// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using System.Text;
using Tellma.Core.Abstractions.Webhooks;
using Tellma.Core.Webhooks.Tests.Infrastructure;

namespace Tellma.Core.Webhooks.Tests.Fronting
{
    /// <summary>
    ///     The status code the fronting returns is what drives a provider's redelivery behaviour, so
    ///     the whole outcome map is pinned — including the paths a receiver never sees.
    /// </summary>
    public class WebhookFrontingTests
    {
        [Theory]
        [InlineData(WebhookOutcome.Accepted, HttpStatusCode.OK)]
        [InlineData(WebhookOutcome.Unauthorized, HttpStatusCode.Unauthorized)]
        [InlineData(WebhookOutcome.Invalid, HttpStatusCode.BadRequest)]
        [InlineData(WebhookOutcome.TransientFailure, HttpStatusCode.ServiceUnavailable)]
        public async Task Maps_each_outcome_to_the_status_code_that_drives_redelivery(
            WebhookOutcome outcome, HttpStatusCode expected)
        {
            StubWebhookReceiver receiver = new("stub", _ => new WebhookResult(outcome, "detail"));
            await using WebhookTestHost host = await WebhookTestHost.StartAsync([receiver]);

            using HttpResponseMessage response = await Post(host, "stub", "{}");

            Assert.Equal(expected, response.StatusCode);
        }

        [Fact]
        public async Task Never_sends_the_receivers_detail_to_the_caller()
        {
            StubWebhookReceiver receiver = new(
                "stub", _ => new WebhookResult(WebhookOutcome.Unauthorized, "the third key did not match"));
            await using WebhookTestHost host = await WebhookTestHost.StartAsync([receiver]);

            using HttpResponseMessage response = await Post(host, "stub", "{}");

            // The detail is for our logs; a forger must learn nothing from the response.
            Assert.Empty(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Passes_a_challenge_echo_through_with_its_content_type()
        {
            StubWebhookReceiver receiver = new("stub", _ => new WebhookResult(
                WebhookOutcome.Accepted,
                ResponseBody: Encoding.UTF8.GetBytes(/*lang=json,strict*/ "{\"validationResponse\":\"abc\"}"),
                ResponseContentType: "application/json"));

            await using WebhookTestHost host = await WebhookTestHost.StartAsync([receiver]);

            using HttpResponseMessage response = await Post(host, "stub", "{}");

            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(
                                     /*lang=json,strict*/
                                     "{\"validationResponse\":\"abc\"}",
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Drops_a_response_body_that_carries_no_content_type()
        {
            // Providers are strict about the content type of a challenge echo, and an unlabeled body
            // would fail their verification in a way that is hard to diagnose from their side.
            StubWebhookReceiver receiver = new("stub", _ => new WebhookResult(
                WebhookOutcome.Accepted, ResponseBody: Encoding.UTF8.GetBytes("abc")));

            await using WebhookTestHost host = await WebhookTestHost.StartAsync([receiver]);

            using HttpResponseMessage response = await Post(host, "stub", "{}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Answers_404_for_an_unknown_key_without_a_body()
        {
            await using WebhookTestHost host = await WebhookTestHost.StartAsync([new StubWebhookReceiver("stub")]);

            using HttpResponseMessage response = await Post(host, "not-a-receiver", "{}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Answers_500_when_a_receiver_throws_so_the_provider_redelivers()
        {
            StubWebhookReceiver receiver = new("stub", _ => throw new InvalidOperationException("boom"));
            await using WebhookTestHost host = await WebhookTestHost.StartAsync([receiver]);

            using HttpResponseMessage response = await Post(host, "stub", "{}");

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        [Fact]
        public async Task Hands_the_receiver_the_exact_wire_bytes()
        {
            StubWebhookReceiver receiver = new("stub");
            await using WebhookTestHost host = await WebhookTestHost.StartAsync([receiver]);

            // Signature verification is over these bytes, so nothing may normalize them.
            const string Body = /*lang=json,strict*/ "[{\"event\":\"delivered\",\"trailing\":\"  space  \"}]\n";
            using HttpResponseMessage response = await Post(host, "stub", Body);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            WebhookRequest request = Assert.Single(receiver.Requests);
            Assert.Equal(Body, Encoding.UTF8.GetString(request.Body.Span));
            Assert.Equal("POST", request.Method);
        }

        [Fact]
        public async Task Gives_the_receiver_case_insensitive_headers_and_query_parameters()
        {
            StubWebhookReceiver receiver = new("stub");
            await using WebhookTestHost host = await WebhookTestHost.StartAsync([receiver]);

            using HttpRequestMessage request = new(HttpMethod.Post, "/api/webhooks/stub?Token=secret&token=second")
            {
                Content = new StringContent("{}"),
            };

            request.Headers.TryAddWithoutValidation("X-Custom-Signature", "sig");
            using HttpResponseMessage response = await host.Client.SendAsync(
                request, TestContext.Current.CancellationToken);

            WebhookRequest received = Assert.Single(receiver.Requests);
            Assert.True(received.Headers.TryGetValue("x-custom-signature", out IReadOnlyList<string>? header));
            Assert.Equal("sig", Assert.Single(header));

            // Repeated query keys arrive together, whatever case they were written in.
            Assert.True(received.QueryParams.TryGetValue("TOKEN", out IReadOnlyList<string>? tokens));
            Assert.Equal(["secret", "second"], tokens);
        }

        [Fact]
        public async Task Accepts_a_get_so_a_provider_can_run_its_registration_handshake()
        {
            StubWebhookReceiver receiver = new("stub");
            await using WebhookTestHost host = await WebhookTestHost.StartAsync([receiver]);

            using HttpResponseMessage response = await host.Client.GetAsync(
                new Uri("/api/webhooks/stub", UriKind.Relative), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("GET", Assert.Single(receiver.Requests).Method);
        }

        [Fact]
        public async Task Answers_413_and_never_dispatches_a_body_over_the_cap()
        {
            StubWebhookReceiver receiver = new("stub");
            await using WebhookTestHost host = await WebhookTestHost.StartAsync(
                [receiver],
                new Dictionary<string, string?> { ["Webhooks:MaxRequestBodyBytes"] = "1024" });

            using HttpResponseMessage response = await Post(host, "stub", new string('x', 2048));

            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
            Assert.Empty(receiver.Requests);
        }

        [Fact]
        public async Task Accepts_a_body_exactly_at_the_cap()
        {
            StubWebhookReceiver receiver = new("stub");
            await using WebhookTestHost host = await WebhookTestHost.StartAsync(
                [receiver],
                new Dictionary<string, string?> { ["Webhooks:MaxRequestBodyBytes"] = "1024" });

            using HttpResponseMessage response = await Post(host, "stub", new string('x', 1024));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(1024, Assert.Single(receiver.Requests).Body.Length);
        }

        private static async Task<HttpResponseMessage> Post(WebhookTestHost host, string key, string body)
        {
            return await host.Client.PostAsync(
                new Uri($"/api/webhooks/{key}", UriKind.Relative),
                new StringContent(body),
                TestContext.Current.CancellationToken);
        }
    }
}
