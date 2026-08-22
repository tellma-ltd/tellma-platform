// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using Tellma.Connector.MarminAe.Tests.Infrastructure;

namespace Tellma.Connector.MarminAe.Tests.Responses
{
    /// <summary>Listings, downloads, and the business profile.</summary>
    public class MarminAeReadSurfaceTests
    {
        [Fact]
        public async Task Reads_a_page_of_documents_and_its_paging()
        {
            Vector vector = Vectors.Load("Documents/page-sales-invoices");

            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, vector.Content));

            MarminAeResponse<MarminAePage<MarminAeDocument>> page = await harness.Client.ListDocumentsAsync(
                MarminAeDocumentKind.SalesInvoice,
                new MarminAeDocumentQuery { Page = 0, Size = 5 },
                TestContext.Current.CancellationToken);

            Assert.Equal(0, page.Value.PageNumber);
            Assert.NotNull(page.Value.Content);
            Assert.NotEmpty(page.Value.Content);
            Assert.All(
                page.Value.Content,
                document => Assert.Equal(MarminAeDocumentKind.SalesInvoice, document.Kind));
        }

        [Fact]
        public async Task Reads_a_page_of_identifiers()
        {
            Vector vector = Vectors.Load("Documents/page-sales-invoice-ids");

            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, vector.Content));

            MarminAeResponse<MarminAePage<string>> page = await harness.Client.ListDocumentIdsAsync(
                MarminAeDocumentKind.SalesInvoice,
                new DateOnly(2026, 8, 22),
                page: 0,
                size: 10,
                TestContext.Current.CancellationToken);

            Assert.NotEmpty(page.Value.Content!);
            Assert.All(page.Value.Content!, id => Assert.True(Guid.TryParse(id, out _)));

            // The page holds at most what was asked for; the total counts everything the query
            // matched, which is why the two are not the same number.
            Assert.True(page.Value.TotalElements >= page.Value.Content!.Count);
            Assert.Equal(0, page.Value.PageNumber);
        }

        [Fact]
        public async Task Reads_an_empty_page_as_an_empty_page()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, MarminAeOperations.PageBody));

            MarminAeResponse<MarminAePage<MarminAeDocument>> page = await harness.Client.ListDocumentsAsync(
                MarminAeDocumentKind.PurchaseInvoice,
                new MarminAeDocumentQuery(),
                TestContext.Current.CancellationToken);

            Assert.Empty(page.Value.Content!);
            Assert.Equal(0, page.Value.TotalElements);
        }

        [Fact]
        public async Task Reads_the_business_profile()
        {
            Vector vector = Vectors.Load("Documents/business-profile");

            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, vector.Content));

            MarminAeResponse<MarminAeBusinessProfile> response = await harness.Client.GetBusinessProfileAsync(
                "MBP-EXAMPLE0000000", TestContext.Current.CancellationToken);

            MarminAeBusinessProfile profile = response.Value;
            Assert.Equal("MBP-EXAMPLE0000000", profile.ProfileId);
            Assert.Equal("COMPLETED", profile.Status);
            Assert.Equal("0235", profile.EndpointSchemeId);
            Assert.Equal("VAT", profile.PartyTaxScheme?.TaxScheme);
            Assert.Equal("DXB", profile.PostalAddress?.CountrySubentity);
            Assert.NotNull(profile.PostalAddress?.Id);
            Assert.True(Guid.TryParse(profile.OrgId, out _));

            // Timestamps arrive as Unix seconds on this endpoint, not milliseconds.
            Assert.NotNull(profile.CreatedAt);
            Assert.Equal(profile.CreatedAtSeconds, profile.CreatedAt?.ToUnixTimeSeconds());

            // Empty rather than absent, which is why these are strings and not URIs.
            Assert.Equal(string.Empty, profile.LogoUrl);
        }

        [Fact]
        public async Task Reads_the_token_response_the_vendor_actually_sends()
        {
            // The vendor's reference documents an expires_at; the deployment sends expires_in. The
            // recording is here so that discrepancy stays visible rather than becoming folklore.
            Vector vector = Vectors.Load("Documents/token-response");

            using var harness = MarminAeHarness.Create();
            harness.OnToken((_, _) => Task.FromResult(
                MarminAeResponses.Json(HttpStatusCode.OK, vector.Content)));

            string token = await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);

            Assert.StartsWith("eyJ", token, StringComparison.Ordinal);

            // A day's lifetime, so nothing refreshes for most of one.
            harness.Time.Advance(TimeSpan.FromHours(20));
            await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, harness.TokenRequestCount);
        }

        [Fact]
        public async Task Returns_a_download_with_its_media_type_and_name()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Binary(
                HttpStatusCode.OK, "%PDF-2.0"u8.ToArray(), "application/pdf", "invoice.pdf"));

            using MarminAeDownload download = await harness.Client.DownloadPdfAsync(
                MarminAeDocumentKind.SalesInvoice,
                MarminAeOperations.DocumentId,
                TestContext.Current.CancellationToken);

            Assert.Equal("application/pdf", download.ContentType);
            Assert.Equal("invoice.pdf", download.FileName);
            Assert.Equal(8, download.ContentLength);
        }

        [Fact]
        public async Task Prefers_the_vendors_own_file_name_header()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Binary(
                HttpStatusCode.OK,
                [1, 2, 3],
                "image/png",
                "scan.png",
                suggestedFileNameHeader: true));

            using MarminAeDownload download = await harness.Client.DownloadAttachmentAsync(
                MarminAeDocumentKind.SalesInvoice,
                MarminAeOperations.DocumentId,
                MarminAeOperations.AttachmentId,
                TestContext.Current.CancellationToken);

            Assert.Equal("scan.png", download.FileName);
        }

        [Fact]
        public async Task Returns_the_bytes_exactly_including_ones_that_are_not_text()
        {
            byte[] content = [0x25, 0x50, 0x44, 0x46, 0xFF, 0xFE, 0x00, 0x80];

            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Binary(HttpStatusCode.OK, content, "application/pdf"));

            using MarminAeDownload download = await harness.Client.DownloadXmlAsync(
                MarminAeDocumentKind.SalesInvoice,
                MarminAeOperations.DocumentId,
                TestContext.Current.CancellationToken);

            using MemoryStream buffer = new();
            await download.Content.CopyToAsync(buffer, TestContext.Current.CancellationToken);

            // A client that round-tripped the body through a string would have replaced these.
            Assert.Equal(content, buffer.ToArray());
        }

        [Fact]
        public async Task Closes_the_response_when_the_download_is_disposed()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Binary(
                HttpStatusCode.OK, "%PDF"u8.ToArray(), "application/pdf"));

            MarminAeDownload download = await harness.Client.DownloadPdfAsync(
                MarminAeDocumentKind.SalesInvoice,
                MarminAeOperations.DocumentId,
                TestContext.Current.CancellationToken);

            Stream content = download.Content;
            download.Dispose();
            download.Dispose();

            // Disposing twice is a no-op, and the stream behind it really is closed.
            await Assert.ThrowsAnyAsync<ObjectDisposedException>(
                async () => await content.CopyToAsync(Stream.Null, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Reports_a_refused_download_as_an_error_rather_than_a_stream_of_it()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(
                HttpStatusCode.BadRequest,
                /*lang=json,strict*/ """{"errors":{"document_status":"PDF download is only allowed for documents with APPROVED status."}}"""));

            MarminAeRequestException exception = await Assert.ThrowsAsync<MarminAeRequestException>(
                () => harness.Client.DownloadPdfAsync(
                    MarminAeDocumentKind.SalesInvoice,
                    MarminAeOperations.DocumentId,
                    TestContext.Current.CancellationToken));

            Assert.Equal(400, exception.StatusCode);
            Assert.Contains(
                exception.Detail!.Errors,
                error => error.Field == "document_status");
        }

        [Fact]
        public async Task Falls_back_to_a_generic_media_type_when_the_vendor_names_none()
        {
            using var harness = MarminAeHarness.Create();
            HttpResponseMessage response = new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2]) };
            response.Content.Headers.ContentType = null;
            harness.Enqueue(response);

            using MarminAeDownload download = await harness.Client.DownloadXmlAsync(
                MarminAeDocumentKind.SalesInvoice,
                MarminAeOperations.DocumentId,
                TestContext.Current.CancellationToken);

            Assert.Null(download.ContentType);
            Assert.Null(download.FileName);
        }
    }
}
