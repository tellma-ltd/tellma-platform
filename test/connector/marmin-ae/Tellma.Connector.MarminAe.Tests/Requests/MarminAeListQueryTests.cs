// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using Tellma.Connector.MarminAe.Tests.Infrastructure;

namespace Tellma.Connector.MarminAe.Tests.Requests
{
    /// <summary>How a listing's filters reach the wire.</summary>
    /// <remarks>
    ///     Asserted by parsing the query rather than by comparing whole URLs: the order parameters
    ///     come out in is an implementation detail the vendor has no opinion about, and pinning it
    ///     would make an unrelated edit look like a regression.
    /// </remarks>
    public class MarminAeListQueryTests
    {
        [Fact]
        public async Task Sends_no_query_at_all_when_nothing_was_asked_for()
        {
            Dictionary<string, string> query = await ListAsync(new MarminAeDocumentQuery());

            Assert.Empty(query);
        }

        [Fact]
        public async Task Composes_every_filter_the_vendor_accepts()
        {
            Dictionary<string, string> query = await ListAsync(new MarminAeDocumentQuery
            {
                DocumentNumber = "INV-001",
                BilledByName = "Seller Business",
                BilledToName = "Buyer Business",
                BilledToVat = "100000000000003",
                BilledByVat = "100000000000004",
                Status = "APPROVED",
                DocumentDate = new DateOnly(2026, 5, 7),
                Page = 2,
                Size = 50,
            });

            Assert.Equal("INV-001", query["document_number"]);
            Assert.Equal("Seller Business", query["billed_by_name"]);
            Assert.Equal("Buyer Business", query["billed_to_name"]);
            Assert.Equal("100000000000003", query["billed_to_vat"]);
            Assert.Equal("100000000000004", query["billed_by_vat"]);
            Assert.Equal("APPROVED", query["status"]);
            Assert.Equal("2026-05-07", query["document_date"]);
            Assert.Equal("2", query["page"]);
            Assert.Equal("50", query["size"]);
        }

        [Fact]
        public async Task Omits_the_filters_that_were_left_unset()
        {
            Dictionary<string, string> query = await ListAsync(new MarminAeDocumentQuery
            {
                DocumentNumber = "INV-001",
                Page = 0,
            });

            Assert.Equal(["document_number", "page"], query.Keys.Order(StringComparer.Ordinal));
        }

        [Fact]
        public async Task Sends_a_zero_page_rather_than_treating_it_as_absent()
        {
            // The vendor pages from zero, so the first page is a value a caller may well set
            // explicitly and must not be silently dropped.
            Dictionary<string, string> query = await ListAsync(new MarminAeDocumentQuery { Page = 0 });

            Assert.Equal("0", query["page"]);
        }

        [Fact]
        public async Task Escapes_a_filter_value_that_would_otherwise_break_the_query()
        {
            Dictionary<string, string> query = await ListAsync(new MarminAeDocumentQuery
            {
                BilledToName = "Smith & Sons + Co / Dubai?",
            });

            Assert.Single(query);
            Assert.Equal("Smith & Sons + Co / Dubai?", query["billed_to_name"]);
        }

        [Fact]
        public async Task Sends_the_mandatory_creation_date_on_an_identifier_listing()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, MarminAeOperations.PageBody));

            await harness.Client.ListDocumentIdsAsync(
                MarminAeDocumentKind.SalesInvoice,
                new DateOnly(2026, 4, 17),
                page: 1,
                size: 25,
                TestContext.Current.CancellationToken);

            Dictionary<string, string> query = ParseQuery(harness.SingleDataRequest.RequestUri);
            Assert.Equal("2026-04-17", query["created_on"]);
            Assert.Equal("1", query["page"]);
            Assert.Equal("25", query["size"]);
        }

        [Fact]
        public async Task Sends_only_the_creation_date_when_paging_is_left_to_the_vendor()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, MarminAeOperations.PageBody));

            await harness.Client.ListDocumentIdsAsync(
                MarminAeDocumentKind.SalesInvoice,
                new DateOnly(2026, 4, 17),
                page: null,
                size: null,
                TestContext.Current.CancellationToken);

            Assert.Equal(["created_on"], ParseQuery(harness.SingleDataRequest.RequestUri).Keys);
        }

        [Fact]
        public async Task Refuses_a_listing_with_no_query_object()
        {
            using var harness = MarminAeHarness.Create();

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => harness.Client.ListDocumentsAsync(
                    MarminAeDocumentKind.SalesInvoice, null!, TestContext.Current.CancellationToken));

            Assert.Empty(harness.Exchanges);
        }

        private static async Task<Dictionary<string, string>> ListAsync(MarminAeDocumentQuery query)
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, MarminAeOperations.PageBody));

            await harness.Client.ListDocumentsAsync(
                MarminAeDocumentKind.SalesInvoice, query, TestContext.Current.CancellationToken);

            return ParseQuery(harness.SingleDataRequest.RequestUri);
        }

        private static Dictionary<string, string> ParseQuery(Uri? requestUri)
        {
            string query = requestUri?.Query.TrimStart('?') ?? string.Empty;
            Dictionary<string, string> parsed = new(StringComparer.Ordinal);

            if (query.Length == 0)
            {
                return parsed;
            }

            foreach (string pair in query.Split('&'))
            {
                int separator = pair.IndexOf('=');
                Assert.True(separator > 0, $"'{pair}' is not a name=value pair.");
                parsed.Add(
                    Uri.UnescapeDataString(pair[..separator]),
                    Uri.UnescapeDataString(pair[(separator + 1)..]));
            }

            return parsed;
        }
    }
}
