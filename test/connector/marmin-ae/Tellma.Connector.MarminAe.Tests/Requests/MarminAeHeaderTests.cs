// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using Tellma.Connector.MarminAe.Tests.Infrastructure;
using Tellma.Testing.Support.Http;

namespace Tellma.Connector.MarminAe.Tests.Requests
{
    /// <summary>What every request carries, whatever it is asking for.</summary>
    public class MarminAeHeaderTests
    {
        public static TheoryData<string> Operations()
        {
            return MarminAeOperations.Names();
        }

        [Theory]
        [MemberData(nameof(Operations))]
        public async Task Sends_the_version_and_the_bearer_on_every_call(string operationName)
        {
            MarminAeOperation operation = MarminAeOperations.Get(operationName);
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(operation.Response());

            await operation.Invoke(harness, TestContext.Current.CancellationToken);

            RecordedHttpRequest request = harness.SingleDataRequest;
            Assert.Equal(MarminAeClient.ApiVersion, request.Header(MarminAeClient.VersionHeaderName));
            Assert.Equal("Bearer token-1", request.Header("Authorization"));
        }

        [Fact]
        public void Pins_the_dated_wire_contract_the_client_was_written_against()
        {
            // The failure of this assertion is the point. The vendor versions its payload shapes by
            // date, so moving to a later one is a release with the live suite re-run behind it, not
            // an edit somebody makes in passing.
            Assert.Equal("20260507", MarminAeClient.ApiVersion);
            Assert.Equal("X-MARMIN-VERSION", MarminAeClient.VersionHeaderName);
        }

        [Fact]
        public async Task Sends_the_version_header_on_the_token_request_too()
        {
            using var harness = MarminAeHarness.Create();

            await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);

            Assert.Equal(
                MarminAeClient.ApiVersion,
                harness.TokenRequests[0].Header(MarminAeClient.VersionHeaderName));
        }

        [Theory]
        [InlineData("create sales invoice")]
        [InlineData("resubmit sales credit note")]
        public async Task Declares_a_json_body_on_a_submission(string operationName)
        {
            MarminAeOperation operation = MarminAeOperations.Get(operationName);
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(operation.Response());

            await operation.Invoke(harness, TestContext.Current.CancellationToken);

            Assert.Equal("application/json", harness.SingleDataRequest.ContentHeader("Content-Type"));
        }

        [Fact]
        public async Task Sends_no_body_at_all_on_a_read()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, MarminAeOperations.DocumentBody));

            await harness.Client.GetDocumentAsync(
                MarminAeDocumentKind.SalesInvoice,
                MarminAeOperations.DocumentId,
                TestContext.Current.CancellationToken);

            Assert.Equal(string.Empty, harness.SingleDataRequest.Body);
            Assert.Null(harness.SingleDataRequest.ContentHeader("Content-Type"));
        }

        [Fact]
        public async Task Leaves_the_transports_default_headers_alone()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, MarminAeOperations.DocumentBody));

            await harness.Client.GetDocumentAsync(
                MarminAeDocumentKind.SalesInvoice,
                MarminAeOperations.DocumentId,
                TestContext.Current.CancellationToken);

            // The transport is injected and may be shared. Anything left on it would outlive this
            // client, and a stale bearer parked there is how one organization ends up authenticated
            // as another.
            Assert.Null(harness.HttpClient.DefaultRequestHeaders.Authorization);
            Assert.Empty(harness.HttpClient.DefaultRequestHeaders);
        }

        [Fact]
        public async Task Asks_for_json_on_a_read_and_not_on_a_download()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, MarminAeOperations.DocumentBody))
                   .Enqueue(MarminAeResponses.Binary(HttpStatusCode.OK, "%PDF"u8.ToArray(), "application/pdf"));

            await harness.Client.GetDocumentAsync(
                MarminAeDocumentKind.SalesInvoice,
                MarminAeOperations.DocumentId,
                TestContext.Current.CancellationToken);
            using (await harness.Client.DownloadPdfAsync(
                MarminAeDocumentKind.SalesInvoice,
                MarminAeOperations.DocumentId,
                TestContext.Current.CancellationToken))
            {
            }

            Assert.Equal("application/json", harness.DataRequests[0].Header("Accept"));
            Assert.Null(harness.DataRequests[1].Header("Accept"));
        }
    }
}
