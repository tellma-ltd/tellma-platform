// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;

namespace Tellma.Connector.MarminAe.Tests.Webhook
{
    /// <summary>What a notification has to carry to be worth acting on.</summary>
    /// <remarks>
    ///     The payload the vendor documents is the payload this parses. Everything it does not
    ///     document is either ignored, when it is extra, or a refusal, when it is one of the three
    ///     fields without which the notification says nothing a handler could act on.
    /// </remarks>
    public class MarminAeWebhookEventParserTests
    {
        private const string Documented = /*lang=json,strict*/ """
            {
              "org_id": "80d3cd7d-25ff-4d49-a60c-5d30a0026692",
              "event_type": "sale.invoice.update",
              "profile_id": "MBP-46ETA0X3DI",
              "resource_id": "15dd7668-5462-4998-8813-c71f1395c869",
              "resource_url": "https://api-sandbox.ae.marmin.ai/api/sales-invoices/15dd7668-5462-4998-8813-c71f1395c869",
              "event_timestamp": "2026-04-14T04:31:31.064Z",
              "webhook_event_id": "d3f1bfa2-b445-4b38-b0db-67dcd9b23baa"
            }
            """;

        public static TheoryData<string> KnownEventTypes()
        {
            return
            [
                MarminAeWebhookEventTypes.SaleInvoiceCreate,
                MarminAeWebhookEventTypes.SaleInvoiceUpdate,
                MarminAeWebhookEventTypes.SaleCreditNoteCreate,
                MarminAeWebhookEventTypes.SaleCreditNoteUpdate,
                MarminAeWebhookEventTypes.PurchaseInvoiceCreate,
                MarminAeWebhookEventTypes.PurchaseInvoiceUpdate,
                MarminAeWebhookEventTypes.PurchaseCreditNoteCreate,
                MarminAeWebhookEventTypes.PurchaseCreditNoteUpdate,
            ];
        }

        [Fact]
        public void Reads_every_field_the_documented_payload_carries()
        {
            Assert.True(MarminAeWebhookEventParser.TryParse(Utf8(Documented), out MarminAeWebhookEvent? notification));

            Assert.Equal(Guid.Parse("80d3cd7d-25ff-4d49-a60c-5d30a0026692"), notification.OrgId);
            Assert.Equal("sale.invoice.update", notification.EventType);
            Assert.Equal("MBP-46ETA0X3DI", notification.ProfileId);
            Assert.Equal(Guid.Parse("15dd7668-5462-4998-8813-c71f1395c869"), notification.ResourceId);
            Assert.Equal(
                "https://api-sandbox.ae.marmin.ai/api/sales-invoices/15dd7668-5462-4998-8813-c71f1395c869",
                notification.ResourceUrl.AbsoluteUri);
            Assert.Equal(
                new DateTimeOffset(2026, 4, 14, 4, 31, 31, 64, TimeSpan.Zero),
                notification.EventTimestamp);
            Assert.Equal(Guid.Parse("d3f1bfa2-b445-4b38-b0db-67dcd9b23baa"), notification.WebhookEventId);
        }

        [Theory]
        [MemberData(nameof(KnownEventTypes))]
        public void Reads_a_payload_built_from_each_published_event_name(string eventType)
        {
            // Run over the constants rather than over literals, so a typo in one of them fails here
            // rather than routing quietly to nothing in production.
            Assert.True(MarminAeWebhookEventParser.TryParse(
                Utf8(Documented.Replace("sale.invoice.update", eventType, StringComparison.Ordinal)),
                out MarminAeWebhookEvent? notification));

            Assert.Equal(eventType, notification.EventType);
        }

        [Fact]
        public void Passes_an_event_name_it_has_never_seen_through_verbatim()
        {
            Assert.True(MarminAeWebhookEventParser.TryParse(
                Utf8(Documented.Replace("sale.invoice.update", "sale.invoice.cancelled", StringComparison.Ordinal)),
                out MarminAeWebhookEvent? notification));

            Assert.Equal("sale.invoice.cancelled", notification.EventType);
        }

        [Fact]
        public void Ignores_fields_it_has_never_seen()
        {
            string extended = Documented.Replace(
                "\"org_id\"", "\"future_field\": {\"nested\": [1, 2]}, \"org_id\"", StringComparison.Ordinal);

            Assert.True(MarminAeWebhookEventParser.TryParse(Utf8(extended), out MarminAeWebhookEvent? notification));
            Assert.Equal("MBP-46ETA0X3DI", notification.ProfileId);
        }

        [Fact]
        public void Reads_a_payload_that_arrived_with_a_byte_order_mark()
        {
            byte[] withMark = [.. Encoding.UTF8.Preamble, .. Utf8(Documented)];

            Assert.True(MarminAeWebhookEventParser.TryParse(withMark, out _));
        }

        [Fact]
        public void Reads_a_payload_whose_fields_arrive_in_another_order()
        {
            const string reordered = /*lang=json,strict*/ """
                {
                  "webhook_event_id": "d3f1bfa2-b445-4b38-b0db-67dcd9b23baa",
                  "event_timestamp": "2026-04-14T04:31:31.064Z",
                  "resource_url": "https://api-sandbox.ae.marmin.ai/api/sales-invoices/1",
                  "resource_id": "15dd7668-5462-4998-8813-c71f1395c869",
                  "profile_id": "MBP-46ETA0X3DI",
                  "event_type": "purchase.credit_note.create",
                  "org_id": "80d3cd7d-25ff-4d49-a60c-5d30a0026692"
                }
                """;

            Assert.True(MarminAeWebhookEventParser.TryParse(Utf8(reordered), out MarminAeWebhookEvent? notification));
            Assert.Equal("purchase.credit_note.create", notification.EventType);
        }

        [Theory]
        [InlineData("event_type")]
        [InlineData("org_id")]
        [InlineData("resource_id")]
        [InlineData("resource_url")]
        [InlineData("event_timestamp")]
        [InlineData("webhook_event_id")]
        public void Refuses_a_payload_missing_a_field_it_cannot_act_without(string field)
        {
            string without = RemoveField(Documented, field);

            Assert.False(MarminAeWebhookEventParser.TryParse(Utf8(without), out MarminAeWebhookEvent? notification));
            Assert.Null(notification);
        }

        [Fact]
        public void Tolerates_a_missing_profile_because_the_document_can_still_be_fetched()
        {
            string without = RemoveField(Documented, "profile_id");

            Assert.True(MarminAeWebhookEventParser.TryParse(Utf8(without), out MarminAeWebhookEvent? notification));
            Assert.Equal(string.Empty, notification.ProfileId);
        }

        [Theory]
        [InlineData("\"org_id\": \"not-a-guid\"")]
        [InlineData("\"resource_id\": \"\"")]
        [InlineData("\"webhook_event_id\": 12345")]
        [InlineData("\"resource_url\": \"/api/sales-invoices/1\"")]
        [InlineData("\"event_timestamp\": \"the fourteenth\"")]
        public void Refuses_a_payload_whose_field_is_not_what_it_claims(string replacement)
        {
            string field = replacement[1..replacement.IndexOf("\":", StringComparison.Ordinal)];
            string mangled = ReplaceField(Documented, field, replacement);

            Assert.False(MarminAeWebhookEventParser.TryParse(Utf8(mangled), out _));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("\"a string\"")]
        [InlineData("42")]
        [InlineData("{\"org_id\":")]
        [InlineData("<html>not json at all</html>")]
        public void Refuses_a_body_that_is_not_a_notification(string body)
        {
            Assert.False(MarminAeWebhookEventParser.TryParse(Utf8(body), out _));
        }

        [Fact]
        public void Refuses_an_empty_body()
        {
            Assert.False(MarminAeWebhookEventParser.TryParse(default, out _));
        }

        [Fact]
        public void Publishes_exactly_the_names_the_vendor_documents()
        {
            string[] published = [.. KnownEventTypes().Select(static row => row.Data).Order(StringComparer.Ordinal)];

            Assert.Equal(
                [
                    "purchase.credit_note.create",
                    "purchase.credit_note.update",
                    "purchase.invoice.create",
                    "purchase.invoice.update",
                    "sale.credit_note.create",
                    "sale.credit_note.update",
                    "sale.invoice.create",
                    "sale.invoice.update",
                ],
                published);
        }

        private static string RemoveField(string payload, string field)
        {
            string[] lines = [.. payload.Split('\n').Where(line => !line.Contains($"\"{field}\"", StringComparison.Ordinal))];
            string joined = string.Join('\n', lines);

            // Removing the last field leaves a trailing comma behind it.
            return joined.Replace(",\n}", "\n}", StringComparison.Ordinal);
        }

        private static string ReplaceField(string payload, string field, string replacement)
        {
            string[] lines = [.. payload.Split('\n').Select(line =>
                line.Contains($"\"{field}\"", StringComparison.Ordinal)
                    ? "  " + replacement + (line.TrimEnd().EndsWith(',') ? "," : "")
                    : line)];

            return string.Join('\n', lines);
        }

        private static byte[] Utf8(string value)
        {
            return Encoding.UTF8.GetBytes(value);
        }
    }
}
