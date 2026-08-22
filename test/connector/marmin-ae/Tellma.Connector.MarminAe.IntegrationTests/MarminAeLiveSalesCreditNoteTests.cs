// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Connector.MarminAe.IntegrationTests
{
    /// <summary>That the credit-note-only fields satisfy the real validator.</summary>
    /// <remarks>
    ///     Self-contained: it issues the invoice it then credits, because a credit note must name a
    ///     document that exists and xUnit gives no ordering to lean on.
    /// </remarks>
    [Trait("Category", "Integration")]
    [Trait("Live", "true")]
    public class MarminAeLiveSalesCreditNoteTests
    {
        [Fact(
            Skip = MarminAeLiveEnvironment.SkipReason,
            SkipUnless = nameof(MarminAeLiveEnvironment.HasCredentials),
            SkipType = typeof(MarminAeLiveEnvironment))]
        public async Task Credits_an_invoice_it_issues_first()
        {
            MarminAeLiveEnvironment.Report();

            CancellationToken cancellation = TestContext.Current.CancellationToken;
            ITestOutputHelper? output = TestContext.Current.TestOutputHelper;
            using MarminAeLiveSession session = MarminAeLiveEnvironment.CreateSession();

            output?.WriteLine("stage: issue the invoice to be credited");
            string invoiceNumber = MarminAeLiveDocuments.DocumentNumber("LIVE-CN-SRC");
            MarminAeResponse<MarminAeDocument> invoice = await session.Client.CreateSalesInvoiceAsync(
                MarminAeLiveEnvironment.ProfileId,
                MarminAeLiveDocuments.Invoice(invoiceNumber),
                cancellation);

            Assert.Equal(201, invoice.StatusCode);
            output?.WriteLine($"stage: issue -> {invoice.Value.Id} ({invoiceNumber})");

            output?.WriteLine("stage: credit it");
            string creditNoteNumber = MarminAeLiveDocuments.DocumentNumber("LIVE-CN");
            session.Transcript.NextAttachmentName = "sales-credit-note.recorded.json";

            MarminAeResponse<MarminAeDocument> creditNote = await session.Client.CreateSalesCreditNoteAsync(
                MarminAeLiveEnvironment.ProfileId,
                MarminAeLiveDocuments.CreditNote(creditNoteNumber, invoiceNumber),
                cancellation);

            Assert.Equal(201, creditNote.StatusCode);
            Assert.Equal(MarminAeDocumentKind.SalesCreditNote, creditNote.Value.Kind);
            Assert.Equal(creditNoteNumber, creditNote.Value.DocumentNumber);

            // The three fields an invoice does not have, echoed back by the validator that accepted
            // them: the type code, the reason for the adjustment, and the document being adjusted.
            Assert.Equal("381", creditNote.Value.CreditNoteTypeCode);
            Assert.Equal("381", creditNote.Value.TypeCode);
            Assert.Equal("DL8.61.1.A", creditNote.Value.DiscrepancyResponse);

            IReadOnlyList<MarminAeDocumentReference> credited = creditNote.Value.BillingReference ?? [];
            Assert.NotEmpty(credited);
            Assert.Equal(invoiceNumber, credited[0].Id);

            output?.WriteLine("stage: retrieve it from its own family");
            MarminAeResponse<MarminAeDocument> retrieved = await session.Client.GetDocumentAsync(
                MarminAeDocumentKind.SalesCreditNote, creditNote.Value.Id!, cancellation);

            Assert.Equal(creditNote.Value.Id, retrieved.Value.Id);
            Assert.Equal(MarminAeLiveEnvironment.Marker, retrieved.Value.BuyerReference);
        }
    }
}
