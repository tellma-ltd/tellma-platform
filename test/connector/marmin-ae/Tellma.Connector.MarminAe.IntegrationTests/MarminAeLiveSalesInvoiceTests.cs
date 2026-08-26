// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;

namespace Tellma.Connector.MarminAe.IntegrationTests
{
    /// <summary>An invoice, end to end, against the real API.</summary>
    /// <remarks>
    ///     One test rather than six. The later stages need the document the earlier ones created,
    ///     xUnit does not order tests, and the alternative — a class fixture — would be constructed
    ///     even on a run with no credentials, which is exactly what attribute-level skipping buys.
    ///     Each stage announces itself first, so a failure names the stage without a debugger.
    /// </remarks>
    [Trait("Category", "Integration")]
    [Trait("Live", "true")]
    public class MarminAeLiveSalesInvoiceTests
    {
        [Fact(
            Skip = MarminAeLiveEnvironment.SkipReason,
            SkipUnless = nameof(MarminAeLiveEnvironment.HasCredentials),
            SkipType = typeof(MarminAeLiveEnvironment))]
        public async Task Issues_retrieves_and_downloads_a_minimal_invoice()
        {
            MarminAeLiveEnvironment.Report();

            CancellationToken cancellation = TestContext.Current.CancellationToken;
            ITestOutputHelper? output = TestContext.Current.TestOutputHelper;
            using MarminAeLiveSession session = MarminAeLiveEnvironment.CreateSession();

            output?.WriteLine("stage: submit");
            string documentNumber = MarminAeLiveDocuments.DocumentNumber("LIVE-INV");
            session.Transcript.NextAttachmentName = "sales-invoice.recorded.json";

            MarminAeResponse<MarminAeDocument> created = await session.Client.CreateSalesInvoiceAsync(
                MarminAeLiveEnvironment.ProfileId,
                MarminAeLiveDocuments.Invoice(documentNumber),
                cancellation);

            Assert.Equal(201, created.StatusCode);
            MarminAeDocument invoice = created.Value;
            Assert.False(string.IsNullOrWhiteSpace(invoice.Id), "The vendor assigned no identifier.");
            Assert.Equal(MarminAeDocumentKind.SalesInvoice, invoice.Kind);
            Assert.Equal(documentNumber, invoice.DocumentNumber);
            output?.WriteLine($"stage: submit -> {invoice.Id} ({invoice.DocumentNumber})");

            output?.WriteLine("stage: totals");
            AssertTotalsReconcile(invoice);

            output?.WriteLine("stage: retrieve");
            MarminAeResponse<MarminAeDocument> retrieved = await session.Client.GetDocumentAsync(
                MarminAeDocumentKind.SalesInvoice, invoice.Id!, cancellation);

            Assert.Equal(200, retrieved.StatusCode);
            Assert.Equal(invoice.Id, retrieved.Value.Id);
            Assert.Equal(documentNumber, retrieved.Value.DocumentNumber);
            Assert.Equal(MarminAeLiveEnvironment.Marker, retrieved.Value.BuyerReference);
            Assert.Equal(MarminAeLiveDocuments.Today, retrieved.Value.IssueDate);
            Assert.Equal("AED", retrieved.Value.DocumentCurrencyCode);

            // The supplier is the vendor's to fill in, and it does so from the profile in the route.
            Assert.Equal(MarminAeLiveEnvironment.ProfileId, retrieved.Value.AccountingSupplierParty?.ProfileId);

            output?.WriteLine("stage: peppol status");
            session.Transcript.NextAttachmentName = "status-not-ready.recorded.json";
            await AssertPeppolStatusShapeAsync(session, invoice.Id!, cancellation);

            output?.WriteLine("stage: peppol status log");
            session.Transcript.NextAttachmentName = "status-logs.recorded.json";
            MarminAeResponse<IReadOnlyList<MarminAePeppolStatusLogEntry>> logs =
                await session.Client.GetPeppolStatusLogsAsync(
                    MarminAeDocumentKind.SalesInvoice, invoice.Id!, cancellation);

            Assert.Equal(200, logs.StatusCode);
            output?.WriteLine(
                $"stage: peppol status log -> {logs.Value.Count} entries: {string.Join(", ", logs.Value.Select(static entry => entry.Event))}");

            // The vendor renders a PDF of every document it accepts and holds it as an attachment.
            // That is the artifact a caller can actually have straight away, so it is the one this
            // asserts on; the two dedicated download routes are gated on approval (below).
            output?.WriteLine("stage: rendered pdf");
            IReadOnlyList<MarminAeAttachment> attachments = retrieved.Value.Attachments ?? [];
            Assert.NotEmpty(attachments);

            MarminAeAttachment rendered = attachments[0];
            Assert.Equal("application/pdf", rendered.FileType);
            Assert.False(string.IsNullOrWhiteSpace(rendered.Id), "The attachment carried no identifier.");

            using (MarminAeDownload pdf = await session.Client.DownloadAttachmentAsync(
                MarminAeDocumentKind.SalesInvoice, invoice.Id!, rendered.Id!, cancellation))
            {
                byte[] bytes = await ReadAsync(pdf, cancellation);
                Assert.Equal("%PDF"u8.ToArray(), bytes[..4]);
                Assert.Equal("application/pdf", pdf.ContentType);
                Assert.Equal(rendered.FileName, pdf.FileName);

                // The quota reading rides on every response, and a caller pacing itself needs it
                // from the successful ones.
                Assert.NotNull(pdf.RateLimit.Remaining);
                output?.WriteLine(
                    $"  quota after the download: {pdf.RateLimit.Remaining} of {pdf.RateLimit.Limit} left");
            }

            output?.WriteLine("stage: gated artifacts");
            await AssertGatedOrDeliveredAsync(
                "xml",
                () => session.Client.DownloadXmlAsync(MarminAeDocumentKind.SalesInvoice, invoice.Id!, cancellation),
                static bytes => Assert.StartsWith("<?xml", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal),
                cancellation);

            await AssertGatedOrDeliveredAsync(
                "pdf",
                () => session.Client.DownloadPdfAsync(MarminAeDocumentKind.SalesInvoice, invoice.Id!, cancellation),
                static bytes => Assert.Equal("%PDF"u8.ToArray(), bytes[..4]),
                cancellation);
        }

        private static async Task AssertGatedOrDeliveredAsync(
            string what,
            Func<Task<MarminAeDownload>> download,
            Action<byte[]> assertContent,
            CancellationToken cancellationToken)
        {
            // Both of these are gated on the document reaching a terminal transmission status, and
            // the legs that decide that owe this suite nothing by the time it asks. So either the
            // artifact is there and is what it claims to be, or the refusal explains itself — which
            // is the property the client is actually responsible for.
            try
            {
                using MarminAeDownload artifact = await download();
                byte[] bytes = await ReadAsync(artifact, cancellationToken);
                Assert.NotEmpty(bytes);
                assertContent(bytes);
            }
            catch (MarminAeRequestException exception)
            {
                TestContext.Current.TestOutputHelper?.WriteLine(
                    $"  {what} is not available yet: {exception.StatusCode} {exception.Detail?.Describe()}");

                Assert.True(
                    exception.StatusCode is 400 or 404,
                    $"Expected the vendor to explain why {what} is unavailable, but it answered {exception.StatusCode}.");
                Assert.False(
                    string.IsNullOrWhiteSpace(exception.Detail?.Describe()),
                    $"The vendor refused {what} without saying why, and nothing could be read out of the body.");
            }
        }

        private static async Task AssertPeppolStatusShapeAsync(
            MarminAeLiveSession session, string documentId, CancellationToken cancellationToken)
        {
            // Shape only. The delivery and reporting legs run asynchronously and owe this suite
            // nothing by the time it asks; asserting an outcome here would be asserting a race.
            try
            {
                MarminAeResponse<MarminAePeppolStatusSnapshot> status = await session.Client.GetPeppolStatusAsync(
                    MarminAeDocumentKind.SalesInvoice, documentId, cancellationToken);

                Assert.Equal(200, status.StatusCode);
                TestContext.Current.TestOutputHelper?.WriteLine(
                    $"stage: peppol status -> c3 {status.Value.ToC3?.Status ?? "-"}, c5 {status.Value.ToC5?.Status ?? "-"}");
            }
            catch (MarminAeRequestException exception) when (exception.StatusCode is 404)
            {
                // A document the network has not started carrying yet has no snapshot, which is a
                // state of the world rather than a failure of the client.
                TestContext.Current.TestOutputHelper?.WriteLine(
                    $"stage: peppol status -> none yet ({exception.Detail?.Describe()})");
            }
        }

        private static void AssertTotalsReconcile(MarminAeDocument invoice)
        {
            // Two units at a hundred, at five per cent: the arithmetic is the point, not the
            // numbers. If the vendor ever stops computing these, a null here says so immediately.
            Assert.Equal(200m, Round(invoice.LineExtensionAmount));
            Assert.Equal(200m, Round(invoice.TaxExclusiveAmount));
            Assert.Equal(10m, Round(invoice.TaxAmount));
            Assert.Equal(210m, Round(invoice.TaxInclusiveAmount));
            Assert.Equal(210m, Round(invoice.PayableAmount));
        }

        private static decimal Round(decimal? value)
        {
            Assert.NotNull(value);

            return Math.Round(value.Value, 2, MidpointRounding.AwayFromZero);
        }

        private static async Task<byte[]> ReadAsync(MarminAeDownload download, CancellationToken cancellationToken)
        {
            using MemoryStream buffer = new();
            await download.Content.CopyToAsync(buffer, cancellationToken);

            TestContext.Current.TestOutputHelper?.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"  downloaded {buffer.Length} bytes of {download.ContentType} named {download.FileName ?? "(unnamed)"}"));

            return buffer.ToArray();
        }
    }
}
