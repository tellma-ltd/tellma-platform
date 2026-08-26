// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using Tellma.Connector.MarminAe.Tests.Infrastructure;

namespace Tellma.Connector.MarminAe.Tests.Requests
{
    /// <summary>Where every call is addressed.</summary>
    /// <remarks>
    ///     Routes are the part of a client with the shortest path from a typo to a silent
    ///     misbehaviour: a wrong segment answers 404, but two identifiers the wrong way round
    ///     answers something plausible for the wrong document.
    /// </remarks>
    public class MarminAeRouteTests
    {
        private const string DocumentId = MarminAeOperations.DocumentId;

        public static TheoryData<MarminAeDocumentKind, string> Families()
        {
            return new TheoryData<MarminAeDocumentKind, string>
            {
                { MarminAeDocumentKind.SalesInvoice, "sales-invoices" },
                { MarminAeDocumentKind.SalesCreditNote, "sales-credit-notes" },
                { MarminAeDocumentKind.PurchaseInvoice, "purchase-invoices" },
                { MarminAeDocumentKind.PurchaseCreditNote, "purchase-credit-notes" },
            };
        }

        [Fact]
        public async Task Addresses_a_sales_invoice_submission_at_the_profile()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.Created, MarminAeOperations.DocumentBody));

            await harness.Client.CreateSalesInvoiceAsync(
                "MBP-1234567890", MarminAeSamples.MinimalInvoice(), TestContext.Current.CancellationToken);

            Assert.Equal(HttpMethod.Post, harness.SingleDataRequest.Method);
            Assert.Equal("/api/sales-invoices/MBP-1234567890", harness.SingleDataRequest.RequestUri?.AbsolutePath);
        }

        [Fact]
        public async Task Addresses_a_sales_credit_note_submission_at_the_profile()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.Created, MarminAeOperations.DocumentBody));

            await harness.Client.CreateSalesCreditNoteAsync(
                "MBP-1234567890", MarminAeSamples.MinimalCreditNote(), TestContext.Current.CancellationToken);

            Assert.Equal(
                "/api/sales-credit-notes/MBP-1234567890", harness.SingleDataRequest.RequestUri?.AbsolutePath);
        }

        [Fact]
        public async Task Puts_the_document_before_the_profile_on_a_resubmission()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, MarminAeOperations.DocumentBody));

            await harness.Client.ResubmitSalesInvoiceAsync(
                "MBP-1234567890",
                DocumentId,
                MarminAeSamples.MinimalInvoice(),
                TestContext.Current.CancellationToken);

            // The vendor's order here is the reverse of the method's, which is exactly why it is
            // pinned: swapping the two produces a route that looks entirely reasonable.
            Assert.Equal(HttpMethod.Put, harness.SingleDataRequest.Method);
            Assert.Equal(
                "/api/sales-invoices/" + DocumentId + "/MBP-1234567890",
                harness.SingleDataRequest.RequestUri?.AbsolutePath);
        }

        [Theory]
        [MemberData(nameof(Families))]
        public async Task Reads_a_document_from_its_own_family(MarminAeDocumentKind kind, string segment)
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, MarminAeOperations.DocumentBody));

            await harness.Client.GetDocumentAsync(kind, DocumentId, TestContext.Current.CancellationToken);

            Assert.Equal(
                "/api/" + segment + "/" + DocumentId, harness.SingleDataRequest.RequestUri?.AbsolutePath);
        }

        [Theory]
        [MemberData(nameof(Families))]
        public async Task Lists_a_family_at_its_own_route(MarminAeDocumentKind kind, string segment)
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, MarminAeOperations.PageBody));

            await harness.Client.ListDocumentsAsync(
                kind, new MarminAeDocumentQuery(), TestContext.Current.CancellationToken);

            Assert.Equal("/api/" + segment, harness.SingleDataRequest.RequestUri?.AbsolutePath);
        }

        [Theory]
        [MemberData(nameof(Families))]
        public async Task Lists_a_familys_identifiers_at_its_own_route(MarminAeDocumentKind kind, string segment)
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, MarminAeOperations.PageBody));

            await harness.Client.ListDocumentIdsAsync(
                kind, new DateOnly(2026, 5, 7), null, null, TestContext.Current.CancellationToken);

            Assert.Equal("/api/" + segment + "/uuids", harness.SingleDataRequest.RequestUri?.AbsolutePath);
        }

        [Theory]
        [InlineData("peppol-status")]
        [InlineData("peppol-status-logs")]
        [InlineData("xml")]
        [InlineData("download-pdf")]
        public async Task Hangs_each_document_sub_resource_off_the_document(string suffix)
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, suffix == "peppol-status-logs" ? "[]" : "{}"));

            CancellationToken cancellation = TestContext.Current.CancellationToken;
            Task call = suffix switch
            {
                "peppol-status" => harness.Client.GetPeppolStatusAsync(
                    MarminAeDocumentKind.SalesInvoice, DocumentId, cancellation),
                "peppol-status-logs" => harness.Client.GetPeppolStatusLogsAsync(
                    MarminAeDocumentKind.SalesInvoice, DocumentId, cancellation),
                "xml" => Download(harness.Client.DownloadXmlAsync(
                    MarminAeDocumentKind.SalesInvoice, DocumentId, cancellation)),
                _ => Download(harness.Client.DownloadPdfAsync(
                    MarminAeDocumentKind.SalesInvoice, DocumentId, cancellation)),
            };

            await call;

            Assert.Equal(
                "/api/sales-invoices/" + DocumentId + "/" + suffix,
                harness.SingleDataRequest.RequestUri?.AbsolutePath);
        }

        [Fact]
        public async Task Addresses_an_attachment_under_its_document()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Binary(HttpStatusCode.OK, [1], "application/pdf"));

            using (await harness.Client.DownloadAttachmentAsync(
                MarminAeDocumentKind.SalesCreditNote,
                DocumentId,
                MarminAeOperations.AttachmentId,
                TestContext.Current.CancellationToken))
            {
            }

            Assert.Equal(
                "/api/sales-credit-notes/" + DocumentId + "/attachments/" + MarminAeOperations.AttachmentId + "/download",
                harness.SingleDataRequest.RequestUri?.AbsolutePath);
        }

        [Fact]
        public async Task Addresses_a_business_profile_outside_the_document_families()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, "{}"));

            await harness.Client.GetBusinessProfileAsync("MBP-00001", TestContext.Current.CancellationToken);

            Assert.Equal("/api/business-profiles/MBP-00001", harness.SingleDataRequest.RequestUri?.AbsolutePath);
        }

        [Theory]
        [InlineData("with/slash")]
        [InlineData("with space")]
        [InlineData("with#hash")]
        [InlineData("with?question")]
        [InlineData("with%percent")]
        public async Task Escapes_an_identifier_that_would_otherwise_change_the_route(string documentId)
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, MarminAeOperations.DocumentBody));

            await harness.Client.GetDocumentAsync(
                MarminAeDocumentKind.SalesInvoice, documentId, TestContext.Current.CancellationToken);

            Uri requestUri = Assert.IsType<Uri>(harness.SingleDataRequest.RequestUri);
            Assert.Empty(requestUri.Query);
            Assert.Equal("/api/sales-invoices/" + documentId, Uri.UnescapeDataString(requestUri.AbsolutePath));
        }

        [Fact]
        public async Task Keeps_the_path_prefix_a_base_address_carries()
        {
            // A gateway deployment lives under a prefix, and composing an absolute path against the
            // base address would silently throw the prefix away. No live test would ever catch it:
            // the vendor's own host has no prefix.
            using var harness = MarminAeHarness.Create(
                baseAddress: new Uri("https://gateway.example.com/marmin/"));
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, MarminAeOperations.DocumentBody));

            await harness.Client.GetDocumentAsync(
                MarminAeDocumentKind.SalesInvoice, DocumentId, TestContext.Current.CancellationToken);

            Assert.Equal(
                "https://gateway.example.com/marmin/api/sales-invoices/" + DocumentId,
                harness.SingleDataRequest.RequestUri?.AbsoluteUri);
            Assert.Equal(
                "https://gateway.example.com/marmin/auth/token",
                harness.TokenRequests[0].RequestUri?.GetLeftPart(UriPartial.Path));
        }

        [Fact]
        public async Task Adds_the_trailing_slash_a_base_address_left_off()
        {
            using var harness = MarminAeHarness.Create(
                baseAddress: new Uri("https://api-sandbox.ae.marmin.ai"));
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, MarminAeOperations.DocumentBody));

            await harness.Client.GetDocumentAsync(
                MarminAeDocumentKind.SalesInvoice, DocumentId, TestContext.Current.CancellationToken);

            Assert.Equal(
                "https://api-sandbox.ae.marmin.ai/api/sales-invoices/" + DocumentId,
                harness.SingleDataRequest.RequestUri?.AbsoluteUri);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Refuses_a_blank_identifier_without_touching_the_wire(string documentId)
        {
            using var harness = MarminAeHarness.Create();

            await Assert.ThrowsAsync<ArgumentException>(
                () => harness.Client.GetDocumentAsync(
                    MarminAeDocumentKind.SalesInvoice, documentId, TestContext.Current.CancellationToken));

            Assert.Empty(harness.Exchanges);
        }

        [Fact]
        public async Task Refuses_a_blank_profile_without_touching_the_wire()
        {
            using var harness = MarminAeHarness.Create();

            await Assert.ThrowsAsync<ArgumentException>(
                () => harness.Client.CreateSalesInvoiceAsync(
                    "  ", MarminAeSamples.MinimalInvoice(), TestContext.Current.CancellationToken));

            Assert.Empty(harness.Exchanges);
        }

        [Fact]
        public void Refuses_to_name_a_family_that_does_not_exist()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => MarminAeRoutes.Segment((MarminAeDocumentKind)99));
        }

        private static async Task Download(Task<MarminAeDownload> pending)
        {
            using MarminAeDownload download = await pending;
        }
    }
}
