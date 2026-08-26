// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;

namespace Tellma.Connector.MarminAe.Tests.Infrastructure
{
    /// <summary>One call the client can make, with a response that satisfies it.</summary>
    /// <param name="Name">What the matrix calls it.</param>
    /// <param name="Invoke">Makes the call.</param>
    /// <param name="Response">Builds a response the call will accept.</param>
    internal sealed record MarminAeOperation(
        string Name,
        Func<MarminAeHarness, CancellationToken, Task> Invoke,
        Func<HttpResponseMessage> Response);

    /// <summary>Every call the client can make.</summary>
    /// <remarks>
    ///     <para>
    ///         The properties that hold for all of them — the version header, the bearer, one
    ///         re-authentication and no more — are asserted over this list rather than method by
    ///         method, so a method added later is covered the moment it is added here, and a
    ///         reviewer can see when it was not.
    ///     </para>
    ///     <para>
    ///         Theories take the name and look the operation up, rather than taking the operation
    ///         itself, because a delegate cannot be serialized into a test case identity and a
    ///         suite whose cases cannot be named individually is a suite nobody can re-run one test
    ///         of.
    ///     </para>
    /// </remarks>
    internal static class MarminAeOperations
    {
        /// <summary>The document the matrix reads and writes.</summary>
        internal const string DocumentId = "11111111-1111-1111-1111-111111111111";

        /// <summary>The attachment the matrix downloads.</summary>
        internal const string AttachmentId = "22222222-2222-2222-2222-222222222222";

        /// <summary>A document response body with nothing but an identifier on it.</summary>
        internal const string DocumentBody = /*lang=json,strict*/ """{"id":"11111111-1111-1111-1111-111111111111"}""";

        /// <summary>An empty page.</summary>
        internal const string PageBody = /*lang=json,strict*/ """{"content":[],"page_number":0,"total_pages":0,"total_elements":0}""";

        /// <summary>The names of every operation, for a theory to run over.</summary>
        /// <returns>The names.</returns>
        internal static TheoryData<string> Names()
        {
            TheoryData<string> data = [];
            foreach (MarminAeOperation operation in Build())
            {
                data.Add(operation.Name);
            }

            return data;
        }

        /// <summary>Looks one operation up by name.</summary>
        /// <param name="name">Its name.</param>
        /// <returns>The operation.</returns>
        internal static MarminAeOperation Get(string name)
        {
            return Build().Single(
                operation => string.Equals(operation.Name, name, StringComparison.Ordinal));
        }

        private static IEnumerable<MarminAeOperation> Build()
        {
            yield return new MarminAeOperation(
                "create sales invoice",
                (harness, token) => harness.Client.CreateSalesInvoiceAsync(
                    MarminAeHarness.DefaultProfileId, MarminAeSamples.MinimalInvoice(), token),
                () => MarminAeResponses.Json(HttpStatusCode.Created, DocumentBody));

            yield return new MarminAeOperation(
                "resubmit sales invoice",
                (harness, token) => harness.Client.ResubmitSalesInvoiceAsync(
                    MarminAeHarness.DefaultProfileId, DocumentId, MarminAeSamples.MinimalInvoice(), token),
                () => MarminAeResponses.Json(HttpStatusCode.OK, DocumentBody));

            yield return new MarminAeOperation(
                "create sales credit note",
                (harness, token) => harness.Client.CreateSalesCreditNoteAsync(
                    MarminAeHarness.DefaultProfileId, MarminAeSamples.MinimalCreditNote(), token),
                () => MarminAeResponses.Json(HttpStatusCode.Created, DocumentBody));

            yield return new MarminAeOperation(
                "resubmit sales credit note",
                (harness, token) => harness.Client.ResubmitSalesCreditNoteAsync(
                    MarminAeHarness.DefaultProfileId, DocumentId, MarminAeSamples.MinimalCreditNote(), token),
                () => MarminAeResponses.Json(HttpStatusCode.OK, DocumentBody));

            yield return new MarminAeOperation(
                "get document",
                (harness, token) => harness.Client.GetDocumentAsync(
                    MarminAeDocumentKind.PurchaseInvoice, DocumentId, token),
                () => MarminAeResponses.Json(HttpStatusCode.OK, DocumentBody));

            yield return new MarminAeOperation(
                "list documents",
                (harness, token) => harness.Client.ListDocumentsAsync(
                    MarminAeDocumentKind.SalesInvoice, new MarminAeDocumentQuery(), token),
                () => MarminAeResponses.Json(HttpStatusCode.OK, PageBody));

            yield return new MarminAeOperation(
                "list document ids",
                (harness, token) => harness.Client.ListDocumentIdsAsync(
                    MarminAeDocumentKind.SalesInvoice, new DateOnly(2026, 5, 7), null, null, token),
                () => MarminAeResponses.Json(HttpStatusCode.OK, PageBody));

            yield return new MarminAeOperation(
                "get peppol status",
                (harness, token) => harness.Client.GetPeppolStatusAsync(
                    MarminAeDocumentKind.SalesInvoice, DocumentId, token),
                () => MarminAeResponses.Json(HttpStatusCode.OK, "{}"));

            yield return new MarminAeOperation(
                "get peppol status logs",
                (harness, token) => harness.Client.GetPeppolStatusLogsAsync(
                    MarminAeDocumentKind.SalesInvoice, DocumentId, token),
                () => MarminAeResponses.Json(HttpStatusCode.OK, "[]"));

            yield return new MarminAeOperation(
                "get business profile",
                (harness, token) => harness.Client.GetBusinessProfileAsync(
                    MarminAeHarness.DefaultProfileId, token),
                () => MarminAeResponses.Json(HttpStatusCode.OK, "{}"));

            yield return new MarminAeOperation(
                "download xml",
                (harness, token) => DisposeAfter(harness.Client.DownloadXmlAsync(
                    MarminAeDocumentKind.SalesInvoice, DocumentId, token)),
                () => MarminAeResponses.Binary(HttpStatusCode.OK, "<Invoice/>"u8.ToArray(), "application/xml"));

            yield return new MarminAeOperation(
                "download pdf",
                (harness, token) => DisposeAfter(harness.Client.DownloadPdfAsync(
                    MarminAeDocumentKind.SalesInvoice, DocumentId, token)),
                () => MarminAeResponses.Binary(HttpStatusCode.OK, "%PDF-1.7"u8.ToArray(), "application/pdf"));

            yield return new MarminAeOperation(
                "download attachment",
                (harness, token) => DisposeAfter(harness.Client.DownloadAttachmentAsync(
                    MarminAeDocumentKind.SalesInvoice, DocumentId, AttachmentId, token)),
                () => MarminAeResponses.Binary(HttpStatusCode.OK, [1, 2, 3], "image/png"));
        }

        private static async Task DisposeAfter(Task<MarminAeDownload> download)
        {
            using MarminAeDownload result = await download;
        }
    }
}
