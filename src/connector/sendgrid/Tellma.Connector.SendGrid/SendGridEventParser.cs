// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Tellma.Connector.SendGrid
{
    /// <summary>Parses an event-webhook batch.</summary>
    /// <remarks>
    ///     Hand-rolled over <see cref="JsonDocument" /> rather than source-generated, because the
    ///     interesting part of an event — a message's custom arguments — arrives as arbitrary
    ///     top-level fields. The natural model for that is extension data, which does not work
    ///     reliably with init accessors under source generation; reading the object directly avoids
    ///     the whole question and is tolerant of every field SendGrid adds later.
    /// </remarks>
    public static class SendGridEventParser
    {
        private const string EventProperty = "event";
        private const string EmailProperty = "email";
        private const string TimestampProperty = "timestamp";
        private const string EventIdProperty = "sg_event_id";
        private const string MessageIdProperty = "sg_message_id";
        private const string ReasonProperty = "reason";

        /// <summary>Parses a webhook body into events.</summary>
        /// <param name="body">The raw request body.</param>
        /// <param name="events">The parsed events, or an empty list when parsing failed.</param>
        /// <returns>True when the body was a JSON array of objects.</returns>
        /// <remarks>
        ///     Takes memory rather than a span so the document can be parsed over the caller's buffer
        ///     instead of a copy of it — a webhook body runs to the fronting's megabyte-scale cap, and
        ///     copying it whole before reading a single token is the most expensive thing on this
        ///     path. <see cref="JsonDocument" /> then borrows that buffer for its lifetime, which is
        ///     safe here because the document does not outlive this call and everything read out of
        ///     it is a copied string.
        /// </remarks>
        public static bool TryParse(ReadOnlyMemory<byte> body, out IReadOnlyList<SendGridEvent> events)
        {
            events = [];

            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                List<SendGridEvent> parsed = [];
                foreach (JsonElement element in document.RootElement.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        return false;
                    }

                    if (TryParseEvent(element, out SendGridEvent? @event))
                    {
                        parsed.Add(@event);
                    }
                }

                events = parsed;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool TryParseEvent(JsonElement element, [NotNullWhen(true)] out SendGridEvent? @event)
        {
            @event = null;

            string? eventName = ReadString(element, EventProperty);
            string? eventId = ReadString(element, EventIdProperty);
            if (string.IsNullOrEmpty(eventName) || string.IsNullOrEmpty(eventId))
            {
                // Without a name there is nothing to classify, and without an id a handler cannot
                // deduplicate; either way the entry is unusable rather than merely unknown.
                return false;
            }

            // Null, not the epoch, when the field is missing or is not a number. A substituted epoch
            // reads downstream as fifty-odd years of arrival lag, which poisons the lag histogram
            // and trips its alert off a single malformed entry — and it would reach a handler as a
            // 1970 timestamp worth persisting.
            DateTimeOffset? timestamp = element.TryGetProperty(TimestampProperty, out JsonElement timestampElement)
                && timestampElement.ValueKind == JsonValueKind.Number
                && timestampElement.TryGetInt64(out long unixSeconds)
                    ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
                    : null;

            Dictionary<string, string> customArgs = new(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String || IsMappedProperty(property.Name))
                {
                    continue;
                }

                customArgs[property.Name] = property.Value.GetString() ?? string.Empty;
            }

            @event = new SendGridEvent(
                eventName,
                ReadString(element, EmailProperty),
                timestamp,
                eventId,
                ReadString(element, MessageIdProperty),
                ReadString(element, ReasonProperty),
                customArgs);

            return true;
        }

        private static bool IsMappedProperty(string name)
        {
            return name is EventProperty or EmailProperty or TimestampProperty
                or EventIdProperty or MessageIdProperty or ReasonProperty;
        }

        private static string? ReadString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
        }
    }
}
