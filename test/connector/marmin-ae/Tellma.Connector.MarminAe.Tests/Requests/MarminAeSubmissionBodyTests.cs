// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Net;
using System.Text.Json;
using Tellma.Connector.MarminAe.Tests.Infrastructure;

namespace Tellma.Connector.MarminAe.Tests.Requests
{
    /// <summary>What a submission actually puts on the wire.</summary>
    /// <remarks>
    ///     Asserted against the serialized body rather than against the model, because the model
    ///     lacking a field only says what is true today: a field added later would still fail these.
    /// </remarks>
    public class MarminAeSubmissionBodyTests
    {
        /// <summary>
        ///     The fields the vendor computes or derives, which a submission must never carry.
        /// </summary>
        public static TheoryData<string> ServerOwnedFields()
        {
            return
            [
                "accounting_supplier_party",
                "id",
                "org_id",
                "document_sequence",
                "meta_info",
                "is_phase2_document",
                "tax_breakdown",
                "allowance_total_amount",
                "charge_total_amount",
                "line_extension_amount",
                "tax_exclusive_amount",
                "tax_amount",
                "tax_amount_in_aed",
                "tax_inclusive_amount",
                "payable_amount",
                "payable_amount_in_aed",
                "total_item_allowances",
                "total_item_charges",
                "total_taxable_amount",
                "total_non_taxable_amount",
            ];
        }

        [Theory]
        [MemberData(nameof(ServerOwnedFields))]
        public async Task Sends_no_field_the_vendor_owns(string field)
        {
            JsonElement body = await SubmitAsync(MarminAeSamples.FullInvoice());

            Assert.False(
                body.TryGetProperty(field, out _),
                $"A submission must not carry '{field}': the vendor computes it and overwrites whatever it is sent.");
        }

        [Fact]
        public async Task Sends_only_the_required_fields_of_a_minimal_invoice()
        {
            JsonElement body = await SubmitAsync(MarminAeSamples.MinimalInvoice());

            string[] present = [.. body.EnumerateObject().Select(static property => property.Name).Order(StringComparer.Ordinal)];

            Assert.Equal(
                [
                    "accounting_customer_party",
                    "document_currency_code",
                    "document_lines",
                    "due_date",
                    "invoice_type_code",
                    "issue_date",
                    "profile_execution_id",
                ],
                present);
        }

        [Fact]
        public async Task Names_every_field_in_snake_case()
        {
            JsonElement body = await SubmitAsync(MarminAeSamples.FullInvoice());

            foreach (string name in AllPropertyNames(body))
            {
                Assert.Matches("^[a-z][a-z0-9_]*$", name);
            }
        }

        [Fact]
        public async Task Writes_dates_and_times_in_the_vendors_formats()
        {
            JsonElement body = await SubmitAsync(MarminAeSamples.FullInvoice());

            Assert.Equal("2026-05-07", body.GetProperty("issue_date").GetString());
            Assert.Equal("2026-06-06", body.GetProperty("due_date").GetString());
            Assert.Equal("2026-05-06", body.GetProperty("tax_point_date").GetString());
            Assert.Equal("10:30:00", body.GetProperty("issue_time").GetString());
        }

        [Fact]
        public async Task Truncates_a_time_the_vendor_would_refuse_for_being_too_precise()
        {
            // The obvious way to reach a time of day carries sub-second precision, and the vendor
            // accepts seconds and nothing longer.
            MarminAeSalesInvoiceRequest invoice = MarminAeSamples.MinimalInvoice() with
            {
                IssueTime = TimeOnly.FromDateTime(new DateTime(2026, 5, 7, 10, 30, 15, 123, DateTimeKind.Utc)),
            };

            JsonElement body = await SubmitAsync(invoice);

            Assert.Equal("10:30:15", body.GetProperty("issue_time").GetString());
        }

        [Fact]
        public async Task Writes_amounts_as_unquoted_numbers_with_their_scale_intact()
        {
            JsonElement body = await SubmitAsync(MarminAeSamples.FullInvoice());

            JsonElement price = body.GetProperty("document_lines")[0].GetProperty("price");
            Assert.Equal(JsonValueKind.Number, price.GetProperty("base_amount").ValueKind);
            Assert.Equal(100.125m, price.GetProperty("base_amount").GetDecimal());
            Assert.Equal(-0.02m, body.GetProperty("payable_rounding_amount").GetDecimal());
            Assert.Equal(3.6725m, body.GetProperty("tax_exchange_rate").GetProperty("calculation_rate").GetDecimal());
        }

        [Fact]
        public async Task Serializes_identically_under_a_culture_that_writes_decimals_with_commas()
        {
            CultureInfo original = CultureInfo.CurrentCulture;
            try
            {
                // Built rather than looked up: the property under test is the separator, and a
                // globalization-invariant image has no locale data to look one up in.
                var commaDecimal = (CultureInfo)CultureInfo.InvariantCulture.Clone();
                commaDecimal.NumberFormat.NumberDecimalSeparator = ",";
                commaDecimal.NumberFormat.NumberGroupSeparator = ".";
                CultureInfo.CurrentCulture = commaDecimal;
                JsonElement body = await SubmitAsync(MarminAeSamples.FullInvoice());

                Assert.Equal(
                    "100.125",
                    body.GetProperty("document_lines")[0].GetProperty("price").GetProperty("base_amount").GetRawText());
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Fact]
        public async Task Round_trips_text_outside_the_ascii_range()
        {
            MarminAeSalesInvoiceRequest invoice = MarminAeSamples.MinimalInvoice() with
            {
                Note = "\u0641\u0627\u062a\u0648\u0631\u0629 \u0636\u0631\u064a\u0628\u064a\u0629",
            };

            JsonElement body = await SubmitAsync(invoice);

            Assert.Equal(
                "\u0641\u0627\u062a\u0648\u0631\u0629 \u0636\u0631\u064a\u0628\u064a\u0629",
                body.GetProperty("note").GetString());
        }

        [Fact]
        public async Task Writes_an_attachment_as_base64_under_the_vendors_field_names()
        {
            JsonElement body = await SubmitAsync(MarminAeSamples.FullInvoice());

            JsonElement attachment = body.GetProperty("attachments")[0];
            Assert.Equal("delivery-note.pdf", attachment.GetProperty("file_name").GetString());
            Assert.Equal("application/pdf", attachment.GetProperty("file_type").GetString());
            Assert.Equal("SGVsbG8=", attachment.GetProperty("file_content").GetString());
        }

        [Fact]
        public async Task Sends_the_same_body_whether_submitting_or_replacing()
        {
            MarminAeSalesInvoiceRequest invoice = MarminAeSamples.FullInvoice();

            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.Created, MarminAeOperations.DocumentBody))
                   .Enqueue(MarminAeResponses.Json(HttpStatusCode.OK, MarminAeOperations.DocumentBody));

            await harness.Client.CreateSalesInvoiceAsync(
                MarminAeHarness.DefaultProfileId, invoice, TestContext.Current.CancellationToken);
            await harness.Client.ResubmitSalesInvoiceAsync(
                MarminAeHarness.DefaultProfileId,
                MarminAeOperations.DocumentId,
                invoice,
                TestContext.Current.CancellationToken);

            // Replacement is a whole-document operation at the vendor, so the two bodies being
            // identical is the contract rather than a coincidence — and the document identifier
            // stays in the route.
            Assert.Equal(harness.DataRequests[0].Body, harness.DataRequests[1].Body);
            Assert.False(Parse(harness.DataRequests[1].Body).TryGetProperty("id", out _));
        }

        [Fact]
        public async Task Sends_the_credit_note_fields_an_invoice_does_not_have()
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.Created, MarminAeOperations.DocumentBody));

            await harness.Client.CreateSalesCreditNoteAsync(
                MarminAeHarness.DefaultProfileId,
                MarminAeSamples.MinimalCreditNote(),
                TestContext.Current.CancellationToken);

            JsonElement body = Parse(harness.SingleDataRequest.Body);
            Assert.Equal("381", body.GetProperty("credit_note_type_code").GetString());
            Assert.Equal("DL8.61.1.A", body.GetProperty("discrepancy_response").GetString());
            Assert.Equal("INV-001", body.GetProperty("billing_reference")[0].GetProperty("id").GetString());
            Assert.False(body.TryGetProperty("invoice_type_code", out _));
            Assert.False(body.TryGetProperty("due_date", out _));
        }

        [Fact]
        public async Task Offers_a_credit_note_the_fields_the_vendors_own_example_sends()
        {
            // The vendor's field list omits these three and its own example sends them, so they are
            // offered rather than obliged: a caller who follows the list sends exactly the list.
            MarminAeSalesCreditNoteRequest creditNote = MarminAeSamples.MinimalCreditNote() with
            {
                DueDate = new DateOnly(2026, 6, 6),
                ProjectReference = new MarminAeProjectReference { Id = "PRJ-1" },
                PaymentMeans = [new MarminAePaymentMeans { PaymentMeansCode = "30" }],
            };

            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.Created, MarminAeOperations.DocumentBody));

            await harness.Client.CreateSalesCreditNoteAsync(
                MarminAeHarness.DefaultProfileId, creditNote, TestContext.Current.CancellationToken);

            JsonElement body = Parse(harness.SingleDataRequest.Body);
            Assert.Equal("2026-06-06", body.GetProperty("due_date").GetString());
            Assert.Equal("PRJ-1", body.GetProperty("project_reference").GetProperty("id").GetString());
            Assert.Equal("30", body.GetProperty("payment_means")[0].GetProperty("payment_means_code").GetString());
        }

        [Fact]
        public async Task Reads_the_identifiers_and_totals_the_vendor_assigned()
        {
            const string created = /*lang=json,strict*/ """
                {
                  "id": "52c2e50b-a76c-4422-8a2d-1552bc0a207f",
                  "org_id": "f7f8c445-c990-4683-9617-c66cc16a6ffd",
                  "document_number": "INV-12",
                  "document_sequence": 12,
                  "payable_amount": 252.0,
                  "tax_amount": 12.0
                }
                """;

            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.Created, created));

            MarminAeResponse<MarminAeDocument> response = await harness.Client.CreateSalesInvoiceAsync(
                MarminAeHarness.DefaultProfileId,
                MarminAeSamples.MinimalInvoice(),
                TestContext.Current.CancellationToken);

            Assert.Equal(201, response.StatusCode);
            Assert.Equal("52c2e50b-a76c-4422-8a2d-1552bc0a207f", response.Value.Id);
            Assert.Equal(12, response.Value.DocumentSequence);
            Assert.Equal(252.0m, response.Value.PayableAmount);
            Assert.Equal(MarminAeDocumentKind.SalesInvoice, response.Value.Kind);
        }

        private static async Task<JsonElement> SubmitAsync(MarminAeSalesInvoiceRequest invoice)
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.Created, MarminAeOperations.DocumentBody));

            await harness.Client.CreateSalesInvoiceAsync(
                MarminAeHarness.DefaultProfileId, invoice, TestContext.Current.CancellationToken);

            return Parse(harness.SingleDataRequest.Body);
        }

        private static JsonElement Parse(string body)
        {
            using var document = JsonDocument.Parse(body);

            return document.RootElement.Clone();
        }

        private static IEnumerable<string> AllPropertyNames(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    yield return property.Name;

                    foreach (string nested in AllPropertyNames(property.Value))
                    {
                        yield return nested;
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    foreach (string nested in AllPropertyNames(item))
                    {
                        yield return nested;
                    }
                }
            }
        }
    }
}
