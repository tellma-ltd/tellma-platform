// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;

namespace Tellma.Connector.SendGrid.Tests.Webhook
{
    /// <summary>
    ///     Event payloads are provider-shaped and grow new fields without warning, so the parser is
    ///     tested for tolerance as much as for correctness.
    /// </summary>
    public class SendGridEventParserTests
    {
        [Fact]
        public void Reads_the_documented_fields()
        {
            const string Payload = /*lang=json,strict*/ """
                [{
                  "email": "recipient@example.com",
                  "timestamp": 1767225600,
                  "event": "bounce",
                  "sg_event_id": "evt-1",
                  "sg_message_id": "msg-1",
                  "reason": "550 unknown user"
                }]
                """;

            Assert.True(SendGridEventParser.TryParse(Bytes(Payload), out IReadOnlyList<SendGridEvent> events));

            SendGridEvent @event = Assert.Single(events);
            Assert.Equal("bounce", @event.EventName);
            Assert.Equal("recipient@example.com", @event.Email);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1767225600), @event.Timestamp);
            Assert.Equal("evt-1", @event.EventId);
            Assert.Equal("msg-1", @event.MessageId);
            Assert.Equal("550 unknown user", @event.Reason);
        }

        [Fact]
        public void Reads_custom_arguments_from_the_top_level_where_sendgrid_puts_them()
        {
            const string Payload = /*lang=json,strict*/ """
                [{
                  "event": "delivered",
                  "sg_event_id": "evt-1",
                  "tellma_correlation": "etpharma:outbox:3:42",
                  "smtp-id": "<abc@example.com>"
                }]
                """;

            Assert.True(SendGridEventParser.TryParse(Bytes(Payload), out IReadOnlyList<SendGridEvent> events));

            SendGridEvent @event = Assert.Single(events);
            Assert.Equal("etpharma:outbox:3:42", @event.CustomArgs["tellma_correlation"]);

            // Unmapped provider fields land here too; the adapter looks up one key and ignores the rest.
            Assert.Equal("<abc@example.com>", @event.CustomArgs["smtp-id"]);
        }

        [Fact]
        public void Tolerates_a_field_it_has_never_seen()
        {
            const string Payload = /*lang=json,strict*/ """
                [{ "event": "open", "sg_event_id": "evt-1", "future_object": { "a": 1 }, "future_array": [1] }]
                """;

            Assert.True(SendGridEventParser.TryParse(Bytes(Payload), out IReadOnlyList<SendGridEvent> events));
            Assert.Equal("open", Assert.Single(events).EventName);
        }

        [Fact]
        public void Skips_an_entry_with_no_event_name_or_no_event_id()
        {
            // Without a name there is nothing to classify; without an id a handler cannot deduplicate.
            const string Payload = /*lang=json,strict*/ """
                [
                  { "sg_event_id": "evt-1" },
                  { "event": "delivered" },
                  { "event": "delivered", "sg_event_id": "evt-2" }
                ]
                """;

            Assert.True(SendGridEventParser.TryParse(Bytes(Payload), out IReadOnlyList<SendGridEvent> events));
            Assert.Equal("evt-2", Assert.Single(events).EventId);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("{}")]
        [InlineData("""[1, 2]""")]
        public void Refuses_a_payload_that_is_not_an_array_of_objects(string payload)
        {
            Assert.False(SendGridEventParser.TryParse(Bytes(payload), out IReadOnlyList<SendGridEvent> events));
            Assert.Empty(events);
        }

        [Fact]
        public void Reads_an_empty_batch_as_an_empty_batch()
        {
            Assert.True(SendGridEventParser.TryParse(Bytes("[]"), out IReadOnlyList<SendGridEvent> events));
            Assert.Empty(events);
        }

        private static byte[] Bytes(string payload)
        {
            return Encoding.UTF8.GetBytes(payload);
        }
    }
}
