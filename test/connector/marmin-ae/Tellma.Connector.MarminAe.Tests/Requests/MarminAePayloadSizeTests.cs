// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Net;
using Tellma.Connector.MarminAe.Tests.Infrastructure;

namespace Tellma.Connector.MarminAe.Tests.Requests
{
    /// <summary>The submission the client refuses to send.</summary>
    /// <remarks>
    ///     The vendor states its limit as "8MB", which is ambiguous between eight million bytes and
    ///     eight binary megabytes. Probing it settled the question — 8,099,998 bytes reach
    ///     validation, 8,499,998 are refused — so the cap here is the same number the vendor
    ///     enforces rather than a conservative guess at it.
    /// </remarks>
    public class MarminAePayloadSizeTests
    {
        [Fact]
        public void Accepts_a_payload_exactly_at_the_cap()
        {
            // The boundary asserted against the real constant. The comparison being strictly greater
            // than, rather than greater than or equal, is the whole content of this test.
            MarminAeClient.ThrowIfOversized(MarminAeClient.MaxRequestPayloadBytes);
        }

        [Fact]
        public void Refuses_a_payload_one_byte_over_the_cap()
        {
            MarminAeRequestException exception = Assert.Throws<MarminAeRequestException>(
                () => MarminAeClient.ThrowIfOversized(MarminAeClient.MaxRequestPayloadBytes + 1));

            Assert.Equal(0, exception.StatusCode);
        }

        [Fact]
        public void Names_both_byte_counts_in_the_refusal()
        {
            int over = MarminAeClient.MaxRequestPayloadBytes + 1;

            MarminAeRequestException exception = Assert.Throws<MarminAeRequestException>(
                () => MarminAeClient.ThrowIfOversized(over));

            // Whoever reads this has to decide what to remove, which needs both numbers.
            Assert.Contains(over.ToString(CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
            Assert.Contains(
                MarminAeClient.MaxRequestPayloadBytes.ToString(CultureInfo.InvariantCulture),
                exception.Message,
                StringComparison.Ordinal);
        }

        [Fact]
        public void Reads_the_vendors_megabyte_as_a_binary_one()
        {
            // Not a preference: 8,099,998 bytes reach the vendor's validator and 8,499,998 are
            // refused, which puts the boundary here and nowhere near eight million.
            Assert.Equal(8 * 1024 * 1024, MarminAeClient.MaxRequestPayloadBytes);
            Assert.True(MarminAeClient.MaxRequestPayloadBytes > 8_099_998);
            Assert.True(MarminAeClient.MaxRequestPayloadBytes < 8_499_998);
        }

        [Fact]
        public async Task Refuses_an_oversized_attachment_before_touching_the_wire()
        {
            MarminAeSalesInvoiceRequest invoice = MarminAeSamples.MinimalInvoice() with
            {
                Attachments =
                [
                    new MarminAeAttachmentRequest
                    {
                        FileName = "scan.pdf",
                        FileType = "application/pdf",

                        // The vendor counts a file after encoding, which is where a document that
                        // looked comfortable on disk stops being one.
                        FileContent = new string('A', 9 * 1024 * 1024),
                    },
                ],
            };

            using var harness = MarminAeHarness.Create();

            await Assert.ThrowsAsync<MarminAeRequestException>(
                () => harness.Client.CreateSalesInvoiceAsync(
                    MarminAeHarness.DefaultProfileId, invoice, TestContext.Current.CancellationToken));

            // Not merely "no submission": no token request either. An implementation that
            // authenticates and only then measures burns a request and a unit of the caller's quota
            // on every oversized document.
            Assert.Empty(harness.Exchanges);
        }

        [Fact]
        public async Task Measures_the_serialized_bytes_rather_than_the_character_count()
        {
            // Every character here costs six bytes once written out, so the document is over the cap
            // while its character count is nowhere near it. A client measuring the model instead of
            // the payload sends this happily.
            const int characters = 1_500_000;
            MarminAeSalesInvoiceRequest invoice = MarminAeSamples.MinimalInvoice() with
            {
                Note = new string('中', characters),
            };

            Assert.True(characters < MarminAeClient.MaxRequestPayloadBytes);

            using var harness = MarminAeHarness.Create();

            await Assert.ThrowsAsync<MarminAeRequestException>(
                () => harness.Client.CreateSalesInvoiceAsync(
                    MarminAeHarness.DefaultProfileId, invoice, TestContext.Current.CancellationToken));

            Assert.Empty(harness.Exchanges);
        }

        [Fact]
        public async Task Applies_the_cap_to_a_replacement_as_well_as_a_submission()
        {
            MarminAeSalesCreditNoteRequest creditNote = MarminAeSamples.MinimalCreditNote() with
            {
                Note = new string('中', 1_500_000),
            };

            using var harness = MarminAeHarness.Create();

            await Assert.ThrowsAsync<MarminAeRequestException>(
                () => harness.Client.ResubmitSalesCreditNoteAsync(
                    MarminAeHarness.DefaultProfileId,
                    MarminAeOperations.DocumentId,
                    creditNote,
                    TestContext.Current.CancellationToken));

            Assert.Empty(harness.Exchanges);
        }

        [Fact]
        public async Task Sends_a_document_that_is_merely_large()
        {
            MarminAeSalesInvoiceRequest invoice = MarminAeSamples.MinimalInvoice() with
            {
                Attachments =
                [
                    new MarminAeAttachmentRequest
                    {
                        FileName = "scan.pdf",
                        FileType = "application/pdf",
                        FileContent = new string('A', 1024 * 1024),
                    },
                ],
            };

            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.Created, MarminAeOperations.DocumentBody));

            await harness.Client.CreateSalesInvoiceAsync(
                MarminAeHarness.DefaultProfileId, invoice, TestContext.Current.CancellationToken);

            Assert.Single(harness.DataRequests);
        }

        [Fact]
        public async Task Applies_no_cap_to_a_read()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(
                HttpStatusCode.OK,
                $$"""{"id":"1","note":"{{new string('x', 200_000)}}"}"""));

            MarminAeResponse<MarminAeDocument> response = await harness.Client.GetDocumentAsync(
                MarminAeDocumentKind.SalesInvoice,
                MarminAeOperations.DocumentId,
                TestContext.Current.CancellationToken);

            Assert.Equal(200_000, response.Value.Note?.Length);
        }
    }
}
