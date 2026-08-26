// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Net;
using Tellma.Connector.MarminAe.Tests.Infrastructure;

namespace Tellma.Connector.MarminAe.Tests.Errors
{
    /// <summary>What the client reports about the caller's remaining quota.</summary>
    /// <remarks>
    ///     Throttling is signal, not something to absorb: nothing here is retried, and the reading
    ///     rides on successful calls as much as on refusals, because by the time a refusal arrives
    ///     the caller has already been throttled.
    /// </remarks>
    public class MarminAeRateLimitTests
    {
        [Fact]
        public async Task Surfaces_a_throttling_refusal_without_a_second_attempt()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.WithRateLimit(
                MarminAeResponses.Json(HttpStatusCode.TooManyRequests, /*lang=json,strict*/ """{"message":"API rate limit exceeded"}"""),
                limit: 60,
                remaining: 0,
                retryAfterSeconds: 47));

            MarminAeRequestException exception = await Assert.ThrowsAsync<MarminAeRequestException>(
                () => harness.Client.GetDocumentAsync(
                    MarminAeDocumentKind.SalesInvoice,
                    MarminAeOperations.DocumentId,
                    TestContext.Current.CancellationToken));

            Assert.True(exception.IsRateLimited);
            Assert.Equal(60, exception.RateLimit.Limit);
            Assert.Equal(0, exception.RateLimit.Remaining);
            Assert.Equal(TimeSpan.FromSeconds(47), exception.RateLimit.RetryAfter);
            Assert.Single(harness.DataRequests);
        }

        [Fact]
        public async Task Reports_the_quota_on_a_successful_call_too()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.WithRateLimit(
                MarminAeResponses.Json(HttpStatusCode.OK, MarminAeOperations.DocumentBody),
                limit: 60,
                remaining: 57));

            MarminAeResponse<MarminAeDocument> response = await harness.Client.GetDocumentAsync(
                MarminAeDocumentKind.SalesInvoice,
                MarminAeOperations.DocumentId,
                TestContext.Current.CancellationToken);

            Assert.Equal(60, response.RateLimit.Limit);
            Assert.Equal(57, response.RateLimit.Remaining);
        }

        public static TheoryData<string, string, string> HeaderSpellings()
        {
            // The vendor's reference documents one set of names and its deployment sends two
            // others. Reading only the documented ones yields a reading that is silently always
            // absent, which a caller cannot tell apart from head-room.
            return new TheoryData<string, string, string>
            {
                {
                    MarminAeClient.RateLimitLimitHeaderName,
                    MarminAeClient.RateLimitRemainingHeaderName,
                    MarminAeClient.RateLimitResetHeaderName
                },
                {
                    MarminAeClient.StandardRateLimitLimitHeaderName,
                    MarminAeClient.StandardRateLimitRemainingHeaderName,
                    MarminAeClient.StandardRateLimitResetHeaderName
                },
                {
                    MarminAeClient.RateLimitLimitPerMinuteHeaderName,
                    MarminAeClient.RateLimitRemainingPerMinuteHeaderName,
                    MarminAeClient.StandardRateLimitResetHeaderName
                },
            };
        }

        [Theory]
        [MemberData(nameof(HeaderSpellings))]
        public async Task Reads_the_quota_under_every_spelling_the_vendor_uses(
            string limitHeader, string remainingHeader, string resetHeader)
        {
            using var harness = MarminAeHarness.Create();
            HttpResponseMessage response = MarminAeResponses.Json(
                HttpStatusCode.OK, MarminAeOperations.DocumentBody);
            response.Headers.TryAddWithoutValidation(limitHeader, "60");
            response.Headers.TryAddWithoutValidation(remainingHeader, "42");
            response.Headers.TryAddWithoutValidation(resetHeader, "47");
            harness.Enqueue(response);

            MarminAeResponse<MarminAeDocument> read = await harness.Client.GetDocumentAsync(
                MarminAeDocumentKind.SalesInvoice,
                MarminAeOperations.DocumentId,
                TestContext.Current.CancellationToken);

            Assert.Equal(60, read.RateLimit.Limit);
            Assert.Equal(42, read.RateLimit.Remaining);
            Assert.Equal(harness.Time.GetUtcNow().AddSeconds(47), read.RateLimit.ResetsAt);
        }

        [Fact]
        public async Task Reads_a_reset_given_as_an_instant_as_one()
        {
            DateTimeOffset resetsAt = new(2026, 5, 7, 9, 5, 0, TimeSpan.Zero);

            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.WithRateLimit(
                MarminAeResponses.Json(HttpStatusCode.OK, MarminAeOperations.DocumentBody),
                resetsAt: resetsAt));

            MarminAeResponse<MarminAeDocument> response = await harness.Client.GetDocumentAsync(
                MarminAeDocumentKind.SalesInvoice,
                MarminAeOperations.DocumentId,
                TestContext.Current.CancellationToken);

            Assert.Equal(resetsAt, response.RateLimit.ResetsAt);
        }

        [Fact]
        public async Task Reads_a_retry_hint_given_as_a_date_as_a_wait()
        {
            using var harness = MarminAeHarness.Create();
            HttpResponseMessage response = MarminAeResponses.Json(HttpStatusCode.TooManyRequests, "{}");
            response.Headers.TryAddWithoutValidation(
                "Retry-After",
                harness.Time.GetUtcNow().AddSeconds(30).ToString("R", CultureInfo.InvariantCulture));
            harness.Enqueue(response);

            MarminAeRequestException exception = await Assert.ThrowsAsync<MarminAeRequestException>(
                () => harness.Client.GetDocumentAsync(
                    MarminAeDocumentKind.SalesInvoice,
                    MarminAeOperations.DocumentId,
                    TestContext.Current.CancellationToken));

            Assert.NotNull(exception.RateLimit.RetryAfter);
            Assert.InRange(exception.RateLimit.RetryAfter.Value, TimeSpan.FromSeconds(29), TimeSpan.FromSeconds(31));
        }

        [Fact]
        public async Task Never_reports_a_negative_wait_when_the_clocks_disagree()
        {
            using var harness = MarminAeHarness.Create();
            HttpResponseMessage response = MarminAeResponses.Json(HttpStatusCode.TooManyRequests, "{}");
            response.Headers.TryAddWithoutValidation(
                "Retry-After",
                harness.Time.GetUtcNow().AddMinutes(-5).ToString("R", CultureInfo.InvariantCulture));
            harness.Enqueue(response);

            MarminAeRequestException exception = await Assert.ThrowsAsync<MarminAeRequestException>(
                () => harness.Client.GetDocumentAsync(
                    MarminAeDocumentKind.SalesInvoice,
                    MarminAeOperations.DocumentId,
                    TestContext.Current.CancellationToken));

            // A caller that multiplies a negative wait by a backoff factor busy-loops.
            Assert.Equal(TimeSpan.Zero, exception.RateLimit.RetryAfter);
        }

        [Fact]
        public async Task Reports_no_quota_at_all_when_the_response_carried_none()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, MarminAeOperations.DocumentBody));

            MarminAeResponse<MarminAeDocument> response = await harness.Client.GetDocumentAsync(
                MarminAeDocumentKind.SalesInvoice,
                MarminAeOperations.DocumentId,
                TestContext.Current.CancellationToken);

            Assert.Same(MarminAeRateLimit.None, response.RateLimit);
            Assert.False(response.RateLimit.HasValue);
        }

        [Theory]
        [InlineData("not-a-number")]
        [InlineData("")]
        [InlineData("60,60")]
        public async Task Tolerates_a_quota_header_it_cannot_read(string value)
        {
            using var harness = MarminAeHarness.Create();
            HttpResponseMessage response = MarminAeResponses.Json(
                HttpStatusCode.OK, MarminAeOperations.DocumentBody);
            response.Headers.TryAddWithoutValidation(MarminAeClient.RateLimitLimitHeaderName, value);
            response.Headers.TryAddWithoutValidation(MarminAeClient.RateLimitRemainingHeaderName, "7");
            harness.Enqueue(response);

            MarminAeResponse<MarminAeDocument> read = await harness.Client.GetDocumentAsync(
                MarminAeDocumentKind.SalesInvoice,
                MarminAeOperations.DocumentId,
                TestContext.Current.CancellationToken);

            Assert.Null(read.RateLimit.Limit);
            Assert.Equal(7, read.RateLimit.Remaining);
        }

        [Fact]
        public async Task Reads_the_quota_headers_however_they_are_cased()
        {
            using var harness = MarminAeHarness.Create();
            HttpResponseMessage response = MarminAeResponses.Json(
                HttpStatusCode.OK, MarminAeOperations.DocumentBody);
            response.Headers.TryAddWithoutValidation("x-ratelimit-remaining", "13");
            harness.Enqueue(response);

            MarminAeResponse<MarminAeDocument> read = await harness.Client.GetDocumentAsync(
                MarminAeDocumentKind.SalesInvoice,
                MarminAeOperations.DocumentId,
                TestContext.Current.CancellationToken);

            Assert.Equal(13, read.RateLimit.Remaining);
        }
    }
}
