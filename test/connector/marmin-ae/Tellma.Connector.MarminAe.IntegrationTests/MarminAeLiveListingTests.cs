// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Connector.MarminAe.IntegrationTests
{
    /// <summary>That the read surface is uniform across the families, against the real API.</summary>
    [Trait("Category", "Integration")]
    [Trait("Live", "true")]
    public class MarminAeLiveListingTests
    {
        [Fact(
            Skip = MarminAeLiveEnvironment.SkipReason,
            SkipUnless = nameof(MarminAeLiveEnvironment.HasCredentials),
            SkipType = typeof(MarminAeLiveEnvironment))]
        public async Task Finds_the_invoice_it_just_issued_in_its_familys_listing()
        {
            MarminAeLiveEnvironment.Report();

            CancellationToken cancellation = TestContext.Current.CancellationToken;
            ITestOutputHelper? output = TestContext.Current.TestOutputHelper;
            using MarminAeLiveSession session = MarminAeLiveEnvironment.CreateSession();

            string documentNumber = MarminAeLiveDocuments.DocumentNumber("LIVE-LIST");
            MarminAeResponse<MarminAeDocument> created = await session.Client.CreateSalesInvoiceAsync(
                MarminAeLiveEnvironment.ProfileId,
                MarminAeLiveDocuments.Invoice(documentNumber),
                cancellation);

            Assert.Equal(201, created.StatusCode);

            // Filtered by the document number rather than paged through: the listing may well be
            // eventually consistent, and a bounded search for one known document is the difference
            // between a test that reports a real problem and one that reports load.
            MarminAeDocument? found = await FindAsync(session, documentNumber, output, cancellation);

            Assert.NotNull(found);
            Assert.Equal(created.Value.Id, found.Id);
            Assert.Equal(MarminAeDocumentKind.SalesInvoice, found.Kind);
        }

        [Fact(
            Skip = MarminAeLiveEnvironment.SkipReason,
            SkipUnless = nameof(MarminAeLiveEnvironment.HasCredentials),
            SkipType = typeof(MarminAeLiveEnvironment))]
        public async Task Lists_todays_sales_invoice_identifiers()
        {
            using MarminAeLiveSession session = MarminAeLiveEnvironment.CreateSession();
            session.Transcript.NextAttachmentName = "page-sales-invoice-ids.recorded.json";

            MarminAeResponse<MarminAePage<string>> page = await session.Client.ListDocumentIdsAsync(
                MarminAeDocumentKind.SalesInvoice,
                MarminAeLiveDocuments.Today,
                page: 0,
                size: 10,
                TestContext.Current.CancellationToken);

            Assert.Equal(200, page.StatusCode);
            Assert.NotNull(page.Value.Content);
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"{page.Value.TotalElements} identifiers were created today; the first page holds {page.Value.Content.Count}.");
        }

        [Theory(
            Skip = MarminAeLiveEnvironment.SkipReason,
            SkipUnless = nameof(MarminAeLiveEnvironment.HasCredentials),
            SkipType = typeof(MarminAeLiveEnvironment))]
        [InlineData(MarminAeDocumentKind.PurchaseInvoice)]
        [InlineData(MarminAeDocumentKind.PurchaseCreditNote)]
        [InlineData(MarminAeDocumentKind.SalesCreditNote)]
        public async Task Lists_a_family_this_suite_cannot_write_to(MarminAeDocumentKind kind)
        {
            // Inbound documents cannot be conjured from outside, so this asserts the shape of the
            // read and nothing about its contents. It is still worth having: the purchase families
            // otherwise rest entirely on offline vectors.
            using MarminAeLiveSession session = MarminAeLiveEnvironment.CreateSession();

            MarminAeResponse<MarminAePage<MarminAeDocument>> page = await session.Client.ListDocumentsAsync(
                kind, new MarminAeDocumentQuery { Page = 0, Size = 5 }, TestContext.Current.CancellationToken);

            Assert.Equal(200, page.StatusCode);
            Assert.NotNull(page.Value.Content);
            Assert.All(page.Value.Content, document => Assert.Equal(kind, document.Kind));
        }

        private static async Task<MarminAeDocument?> FindAsync(
            MarminAeLiveSession session,
            string documentNumber,
            ITestOutputHelper? output,
            CancellationToken cancellationToken)
        {
            var interval = TimeSpan.FromSeconds(2);
            const int attempts = 5;

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                // Named on every attempt, so the capture is the answer that had the document in it
                // rather than the first poll, which is the one most likely to have come back empty.
                session.Transcript.NextAttachmentName = "page-sales-invoices.recorded.json";

                MarminAeResponse<MarminAePage<MarminAeDocument>> page = await session.Client.ListDocumentsAsync(
                    MarminAeDocumentKind.SalesInvoice,
                    new MarminAeDocumentQuery { DocumentNumber = documentNumber, Page = 0, Size = 5 },
                    cancellationToken);

                MarminAeDocument? match = page.Value.Content?.FirstOrDefault(
                    document => string.Equals(document.DocumentNumber, documentNumber, StringComparison.Ordinal));

                if (match is not null)
                {
                    output?.WriteLine($"the listing had it after {(attempt - 1) * interval.TotalSeconds:0} seconds");

                    return match;
                }

                if (attempt < attempts)
                {
                    await Task.Delay(interval, cancellationToken);
                }
            }

            output?.WriteLine(
                $"'{documentNumber}' never appeared in the listing within {attempts * interval.TotalSeconds:0} seconds");

            return null;
        }
    }
}
