// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using Tellma.Connector.MarminAe.Tests.Infrastructure;

namespace Tellma.Connector.MarminAe.Tests.Responses
{
    /// <summary>Reading a document back, against bodies the vendor actually sent.</summary>
    public class MarminAeDocumentResponseTests
    {
        public static TheoryData<MarminAeDocumentKind> Families()
        {
            return
            [
                MarminAeDocumentKind.SalesInvoice,
                MarminAeDocumentKind.SalesCreditNote,
                MarminAeDocumentKind.PurchaseInvoice,
                MarminAeDocumentKind.PurchaseCreditNote,
            ];
        }

        [Fact]
        public async Task Reads_the_identifiers_and_totals_off_a_recorded_invoice()
        {
            MarminAeDocument invoice = await ReadAsync("Documents/sales-invoice", MarminAeDocumentKind.SalesInvoice);

            Assert.Equal("2026-08-22", invoice.IssueDate?.ToString("yyyy-MM-dd", null));
            Assert.Equal("380", invoice.InvoiceTypeCode);
            Assert.Equal("380", invoice.TypeCode);
            Assert.Equal("AED", invoice.DocumentCurrencyCode);
            Assert.Equal(200m, invoice.LineExtensionAmount);
            Assert.Equal(200m, invoice.TaxExclusiveAmount);
            Assert.Equal(10m, invoice.TaxAmount);
            Assert.Equal(210m, invoice.TaxInclusiveAmount);
            Assert.Equal(210m, invoice.PayableAmount);
            Assert.Equal(210m, invoice.PayableAmountInAed);
            Assert.True(invoice.IsPhase2Document);
        }

        [Fact]
        public async Task Reads_a_time_the_vendor_echoed_without_its_seconds()
        {
            // The vendor documents times to the second and returns them to the minute. A strict
            // reader would fail the whole document over it.
            MarminAeDocument invoice = await ReadAsync("Documents/sales-invoice", MarminAeDocumentKind.SalesInvoice);

            Assert.Equal(new TimeOnly(10, 30, 0), invoice.IssueTime);
        }

        [Fact]
        public async Task Reads_the_unit_price_the_vendor_renamed_on_the_way_back()
        {
            // Sent as base_amount, returned as price_amount. Reading only the documented name would
            // produce a null that reads as a free line.
            MarminAeDocument invoice = await ReadAsync("Documents/sales-invoice", MarminAeDocumentKind.SalesInvoice);

            MarminAeDocumentLine line = Assert.Single(invoice.DocumentLines!);
            Assert.Equal(100m, line.Price?.PriceAmount);
            Assert.Equal(100m, line.Price?.Amount);
            Assert.Equal(1m, line.Price?.BaseQuantity);
            Assert.Equal(2m, line.Quantity);
            Assert.Equal(200m, line.NetAmount);
            Assert.Equal(210m, line.TotalAmount);
            Assert.Equal(10m, line.TaxAmount);
        }

        [Fact]
        public async Task Reads_the_tax_breakdown_under_the_names_the_document_routes_use()
        {
            MarminAeDocument invoice = await ReadAsync("Documents/sales-invoice", MarminAeDocumentKind.SalesInvoice);

            MarminAeTaxBreakdownItem item = Assert.Single(invoice.TaxBreakdown!);
            Assert.Equal("S", item.TaxCategoryCode);
            Assert.Equal("S", item.TaxCategory);
            Assert.Equal(5m, item.TaxPercentage);
            Assert.Equal(5m, item.Rate);
            Assert.Equal(200m, item.TaxableAmount);
            Assert.Equal(10m, item.TaxAmount);
        }

        [Fact]
        public void Reads_the_tax_breakdown_under_the_names_the_retrieve_documentation_uses()
        {
            // The other spelling of the same thing, which the vendor's reference shows on a
            // different page. Both are read so neither endpoint can produce a silent null.
            const string body = /*lang=json,strict*/ """
                {"tax_breakdown":[{"tax_category_id":"Z","percent":0.0,"taxable_amount":50.0,"tax_amount":0.0}]}
                """;

            MarminAeDocument document = Deserialize(body);

            MarminAeTaxBreakdownItem item = Assert.Single(document.TaxBreakdown!);
            Assert.Equal("Z", item.TaxCategory);
            Assert.Equal(0m, item.Rate);
        }

        [Fact]
        public async Task Reads_the_supplier_the_vendor_substituted()
        {
            MarminAeDocument invoice = await ReadAsync("Documents/sales-invoice", MarminAeDocumentKind.SalesInvoice);

            MarminAeParty supplier = Assert.IsType<MarminAeParty>(invoice.AccountingSupplierParty);
            Assert.Equal("MBP-EXAMPLE0000000", supplier.ProfileId);
            Assert.Equal("0235", supplier.EndpointSchemeId);
            Assert.Equal("Dubai", supplier.PostalAddress?.CityName);

            // Empty rather than absent, which is why these are strings and not URIs.
            Assert.Equal(string.Empty, supplier.LogoUrl);
        }

        [Fact]
        public async Task Reads_the_fields_the_vendor_returns_but_does_not_document()
        {
            MarminAeDocument invoice = await ReadAsync("Documents/sales-invoice", MarminAeDocumentKind.SalesInvoice);

            Assert.Equal("ACTIVE", invoice.DocumentStatus);
            Assert.Equal(new DateOnly(2026, 8, 22), invoice.EffectiveTaxPointDate);
        }

        [Fact]
        public async Task Reads_the_credit_note_fields_an_invoice_does_not_carry()
        {
            MarminAeDocument creditNote = await ReadAsync(
                "Documents/sales-credit-note", MarminAeDocumentKind.SalesCreditNote);

            Assert.Equal("381", creditNote.CreditNoteTypeCode);
            Assert.Equal("381", creditNote.TypeCode);
            Assert.Null(creditNote.InvoiceTypeCode);
            Assert.Equal("DL8.61.1.A", creditNote.DiscrepancyResponse);
            Assert.NotEmpty(creditNote.BillingReference!);
            Assert.Equal(MarminAeDocumentKind.SalesCreditNote, creditNote.Kind);
        }

        [Theory]
        [MemberData(nameof(Families))]
        public async Task Stamps_the_family_it_was_asked_for_onto_the_document(MarminAeDocumentKind kind)
        {
            // The wire carries no discriminator, so a caller holding a bare document would otherwise
            // have no way to route it back to the right endpoint.
            MarminAeDocument document = await ReadAsync("Documents/sales-invoice", kind);

            Assert.Equal(kind, document.Kind);
        }

        [Fact]
        public void Ignores_fields_it_has_never_seen()
        {
            const string body = /*lang=json,strict*/ """
                {"id":"1","a_field_from_a_later_release":{"nested":[1,2,3]},"payable_amount":9.5}
                """;

            MarminAeDocument document = Deserialize(body);

            Assert.Equal("1", document.Id);
            Assert.Equal(9.5m, document.PayableAmount);
        }

        [Fact]
        public void Reads_a_document_whose_optional_blocks_are_all_null()
        {
            const string body = /*lang=json,strict*/ """
                {"id":"1","meta_info":null,"document_lines":null,"accounting_supplier_party":null,
                 "tax_breakdown":null,"attachments":null,"payment_means":null,"delivery":null}
                """;

            MarminAeDocument document = Deserialize(body);

            Assert.Equal("1", document.Id);
            Assert.Null(document.DocumentLines);
            Assert.Null(document.MetaInfo);
        }

        [Fact]
        public void Reads_a_document_that_is_nothing_but_an_empty_object()
        {
            MarminAeDocument document = Deserialize("{}");

            Assert.Null(document.Id);
        }

        [Fact]
        public void Reads_an_amount_the_vendor_quoted_as_a_string()
        {
            MarminAeDocument document = Deserialize(/*lang=json,strict*/ """{"payable_amount":"1234.5678"}""");

            Assert.Equal(1234.5678m, document.PayableAmount);
        }

        [Fact]
        public void Keeps_the_scale_of_an_amount_that_carries_more_than_expected()
        {
            MarminAeDocument document = Deserialize(/*lang=json,strict*/ """{"payable_amount":1.234567890123}""");

            Assert.Equal(1.234567890123m, document.PayableAmount);
        }

        [Fact]
        public void Reads_a_date_the_vendor_returned_as_a_full_timestamp()
        {
            MarminAeDocument document = Deserialize(/*lang=json,strict*/ """{"issue_date":"2026-05-07T00:00:00+04:00"}""");

            Assert.Equal(new DateOnly(2026, 5, 7), document.IssueDate);
        }

        [Theory]
        [InlineData("\"not a date\"")]
        [InlineData("42")]
        [InlineData("null")]
        public void Yields_null_rather_than_failing_over_a_date_it_cannot_read(string value)
        {
            MarminAeDocument document = Deserialize($$"""{"id":"1","issue_date":{{value}}}""");

            Assert.Equal("1", document.Id);
            Assert.Null(document.IssueDate);
        }

        [Theory]
        [InlineData("\"25:99\"")]
        [InlineData("1030")]
        [InlineData("null")]
        public void Yields_null_rather_than_failing_over_a_time_it_cannot_read(string value)
        {
            MarminAeDocument document = Deserialize($$"""{"id":"1","issue_time":{{value}}}""");

            Assert.Equal("1", document.Id);
            Assert.Null(document.IssueTime);
        }

        [Fact]
        public async Task Reads_the_attachments_the_vendor_holds_against_a_document()
        {
            MarminAeDocument invoice = await ReadAsync("Documents/sales-invoice", MarminAeDocumentKind.SalesInvoice);

            MarminAeAttachment attachment = Assert.Single(invoice.Attachments!);
            Assert.Equal("invoice.pdf", attachment.FileName);
            Assert.Equal("application/pdf", attachment.FileType);
            Assert.False(string.IsNullOrWhiteSpace(attachment.Id));
        }

        [Fact]
        public void Reads_the_transmission_summary_carried_on_a_document()
        {
            Vector vector = Vectors.Load("PeppolStatus/meta-info-peppol-status");

            MarminAeDocument document = Deserialize(vector.Content);

            MarminAeDocumentPeppolStatus status =
                Assert.IsType<MarminAeDocumentPeppolStatus>(document.MetaInfo?.PeppolStatus);
            Assert.Equal(MarminAePeppolStatus.Approved, status.ParticipantStatus);
            Assert.Equal(MarminAePeppolStatus.Pending, status.FtaStatus);
            Assert.Equal(MarminAePeppolStatus.Pending, status.OverallStatus);
            Assert.Empty(status.ValidationResults!);
        }

        [Fact]
        public void Reads_what_validation_objected_to()
        {
            Vector vector = Vectors.Load("PeppolStatus/meta-info-validation-failed");

            MarminAeDocument document = Deserialize(vector.Content);

            MarminAeDocumentPeppolStatus status =
                Assert.IsType<MarminAeDocumentPeppolStatus>(document.MetaInfo?.PeppolStatus);
            Assert.Equal(MarminAePeppolStatus.ValidationFailed, status.OverallStatus);
            Assert.Null(status.ParticipantStatus);

            // Kept as raw JSON: the vendor publishes no shape for these, and on a validation failure
            // the part nobody anticipated is exactly the part somebody needs.
            System.Text.Json.JsonElement objection = Assert.Single(status.ValidationResults!);
            Assert.Equal("IBR-141-AE", objection.GetProperty("reasonCode").GetString());
        }

        [Fact]
        public void Passes_an_overall_status_it_has_never_seen_through_verbatim()
        {
            const string body = /*lang=json,strict*/ """
                {"meta_info":{"peppol_status":{"overall_status":"QUARANTINED_PENDING_REVIEW"}}}
                """;

            MarminAeDocument document = Deserialize(body);

            Assert.Equal(
                "QUARANTINED_PENDING_REVIEW", document.MetaInfo?.PeppolStatus?.OverallStatus);
        }

        [Fact]
        public void Publishes_the_statuses_the_vendor_documents()
        {
            Assert.Equal("VALIDATION_FAILED", MarminAePeppolStatus.ValidationFailed);
            Assert.Equal("PENDING", MarminAePeppolStatus.Pending);
            Assert.Equal("APPROVED", MarminAePeppolStatus.Approved);
            Assert.Equal("REJECTED", MarminAePeppolStatus.Rejected);
        }

        private static async Task<MarminAeDocument> ReadAsync(string logicalName, MarminAeDocumentKind kind)
        {
            Vector vector = Vectors.Load(logicalName);

            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, vector.Content));

            MarminAeResponse<MarminAeDocument> response = await harness.Client.GetDocumentAsync(
                kind, MarminAeOperations.DocumentId, TestContext.Current.CancellationToken);

            return response.Value;
        }

        private static MarminAeDocument Deserialize(string body)
        {
            return System.Text.Json.JsonSerializer.Deserialize(
                body, MarminAeJsonContext.Default.MarminAeDocument)
                ?? throw new InvalidOperationException("The body read as null.");
        }
    }
}
