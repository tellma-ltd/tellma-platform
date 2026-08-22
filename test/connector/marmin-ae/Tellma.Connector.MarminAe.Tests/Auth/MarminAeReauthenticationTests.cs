// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using Tellma.Connector.MarminAe.Tests.Infrastructure;
using Tellma.Testing.Support.Http;

namespace Tellma.Connector.MarminAe.Tests.Auth
{
    /// <summary>The one retry this client performs, and everything it declines to retry.</summary>
    /// <remarks>
    ///     A refusal to authenticate means the vendor did no work, whatever the verb, so repeating
    ///     the request cannot duplicate a document. That is the whole justification, and it is why
    ///     no other status gets the same treatment.
    /// </remarks>
    public class MarminAeReauthenticationTests
    {
        private const string DocumentBody = /*lang=json,strict*/ """{"id":"11111111-1111-1111-1111-111111111111"}""";

        [Fact]
        public async Task Repeats_a_read_once_with_a_freshly_obtained_token()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.Unauthorized, /*lang=json,strict*/ """{"message":"expired"}"""))
                   .Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, DocumentBody));

            MarminAeResponse<MarminAeDocument> response = await harness.Client.GetDocumentAsync(
                MarminAeDocumentKind.SalesInvoice, "doc-1", TestContext.Current.CancellationToken);

            Assert.Equal("11111111-1111-1111-1111-111111111111", response.Value.Id);
            Assert.Equal(2, harness.TokenRequestCount);

            IReadOnlyList<RecordedHttpRequest> attempts = harness.DataRequests;
            Assert.Equal(2, attempts.Count);
            Assert.Equal("Bearer token-1", attempts[0].Header("Authorization"));
            Assert.Equal("Bearer token-2", attempts[1].Header("Authorization"));
        }

        [Fact]
        public async Task Replays_the_submitted_body_byte_for_byte_on_the_second_attempt()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.Unauthorized, "{}"))
                   .Enqueue(MarminAeResponses.Json(HttpStatusCode.Created, DocumentBody));

            await harness.Client.CreateSalesInvoiceAsync(
                MarminAeHarness.DefaultProfileId,
                MarminAeSamples.FullInvoice(),
                TestContext.Current.CancellationToken);

            // A request message cannot be sent twice, so the naive retry either throws or sends an
            // empty body. Both would pass a test that only counted attempts.
            IReadOnlyList<RecordedHttpRequest> attempts = harness.DataRequests;
            Assert.Equal(2, attempts.Count);
            Assert.NotEmpty(attempts[0].Body);
            Assert.Equal(attempts[0].Body, attempts[1].Body);
            Assert.Equal(attempts[0].RequestUri, attempts[1].RequestUri);
            Assert.Equal(attempts[0].Method, attempts[1].Method);
            Assert.Equal(
                attempts[0].Header(MarminAeClient.VersionHeaderName),
                attempts[1].Header(MarminAeClient.VersionHeaderName));
            Assert.Equal("application/json", attempts[1].ContentHeader("Content-Type"));
        }

        [Fact]
        public async Task Surfaces_a_second_refusal_without_a_third_attempt()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.Unauthorized, "{}"))
                   .Enqueue(MarminAeResponses.Json(HttpStatusCode.Unauthorized, /*lang=json,strict*/ """{"message":"revoked"}"""));

            MarminAeRequestException exception = await Assert.ThrowsAsync<MarminAeRequestException>(
                () => harness.Client.GetDocumentAsync(
                    MarminAeDocumentKind.SalesInvoice, "doc-1", TestContext.Current.CancellationToken));

            Assert.Equal(401, exception.StatusCode);
            Assert.Equal("revoked", exception.Detail?.Message);
            Assert.Equal(2, harness.DataRequests.Count);
        }

        [Fact]
        public async Task Discards_the_token_the_vendor_refused()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.Unauthorized, "{}"))
                   .Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, DocumentBody))
                   .Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, DocumentBody));

            await harness.Client.GetDocumentAsync(
                MarminAeDocumentKind.SalesInvoice, "doc-1", TestContext.Current.CancellationToken);
            await harness.Client.GetDocumentAsync(
                MarminAeDocumentKind.SalesInvoice, "doc-2", TestContext.Current.CancellationToken);

            // The call after the recovery reuses the replacement rather than going back for a third.
            Assert.Equal(2, harness.TokenRequestCount);
            Assert.Equal("Bearer token-2", harness.DataRequests[2].Header("Authorization"));
        }

        [Theory]
        [InlineData(HttpStatusCode.BadRequest)]
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData(HttpStatusCode.RequestEntityTooLarge)]
        [InlineData(HttpStatusCode.TooManyRequests)]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.BadGateway)]
        public async Task Does_not_re_authenticate_on_any_other_refusal(HttpStatusCode statusCode)
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(statusCode, "{}"));

            await Assert.ThrowsAsync<MarminAeRequestException>(
                () => harness.Client.GetDocumentAsync(
                    MarminAeDocumentKind.SalesInvoice, "doc-1", TestContext.Current.CancellationToken));

            Assert.Single(harness.DataRequests);
            Assert.Equal(1, harness.TokenRequestCount);
        }

        [Fact]
        public async Task Re_authenticates_a_download_too()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.Unauthorized, "{}"))
                   .Enqueue(MarminAeResponses.Binary(
                       HttpStatusCode.OK, "%PDF-1.7"u8.ToArray(), "application/pdf"));

            using MarminAeDownload download = await harness.Client.DownloadPdfAsync(
                MarminAeDocumentKind.SalesInvoice, "doc-1", TestContext.Current.CancellationToken);

            Assert.Equal("application/pdf", download.ContentType);
            Assert.Equal(2, harness.DataRequests.Count);
        }

        [Fact]
        public async Task Re_authenticates_a_resubmission_too()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.Unauthorized, "{}"))
                   .Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, DocumentBody));

            await harness.Client.ResubmitSalesCreditNoteAsync(
                MarminAeHarness.DefaultProfileId,
                "doc-1",
                MarminAeSamples.MinimalCreditNote(),
                TestContext.Current.CancellationToken);

            IReadOnlyList<RecordedHttpRequest> attempts = harness.DataRequests;
            Assert.Equal(2, attempts.Count);
            Assert.Equal(HttpMethod.Put, attempts[1].Method);
            Assert.Equal(attempts[0].Body, attempts[1].Body);
        }
    }
}
