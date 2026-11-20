// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Localization
{
    /// <summary>
    ///     The ICU arguments of a message stored as a JSON object: each top-level member by its JSON
    ///     kind, as a notification's text and a job's progress and error render them.
    /// </summary>
    /// <remarks>
    ///     A string renders as text, an ISO 8601 date-time with an offset as an instant, an ISO 8601
    ///     date as a calendar date, a number as a number, a boolean as <c>true</c> or <c>false</c> (so
    ///     a <c>select</c> on it matches those keys, as on the client), null as nothing, a localized
    ///     text as the reader's content slot, and anything else as its JSON text.
    /// </remarks>
    internal static partial class IcuJsonArguments
    {
        /// <summary>The elements of a localized text: the three content slots.</summary>
        private const int LocalizedTextSlots = 3;

        /// <summary>Reads the arguments; null when the text is not one JSON object.</summary>
        /// <param name="argumentsJson">The stored arguments.</param>
        /// <param name="contentLanguageIndex">The reader's content slot, which a localized text renders in.</param>
        /// <param name="failure">The parse failure when the text is not JSON; null when it parsed.</param>
        /// <returns>The arguments by name, a repeated name keeping its last value; null when the text is not a JSON object.</returns>
        public static Dictionary<string, object?>? Read(string argumentsJson, int contentLanguageIndex, out JsonException? failure)
        {
            ArgumentNullException.ThrowIfNull(argumentsJson);

            // --- The text parses as one JSON object, or there are no arguments ---
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(argumentsJson);
            }
            catch (JsonException exception)
            {
                failure = exception;
                return null;
            }

            failure = null;
            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                // --- Each top-level member by its kind ---
                Dictionary<string, object?> arguments = new(StringComparer.Ordinal);
                foreach (JsonProperty member in document.RootElement.EnumerateObject())
                {
                    arguments[member.Name] = Value(member.Value, contentLanguageIndex);
                }

                return arguments;
            }
        }

        /// <summary>One argument's value by its JSON kind.</summary>
        /// <param name="element">The member's value.</param>
        /// <param name="contentLanguageIndex">The reader's content slot.</param>
        /// <returns>The value the message formats.</returns>
        private static object? Value(JsonElement element, int contentLanguageIndex)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => Text(element.GetString()!),
                JsonValueKind.Number => Number(element),
                // ICU's own spelling, which a select's keys name; the client's formatter reads it so too.
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                JsonValueKind.Array => LocalizedTextOf(element) is LocalizedText text ? text.In(contentLanguageIndex) : element.GetRawText(),
                JsonValueKind.Object or JsonValueKind.Undefined => element.GetRawText(),
                _ => element.GetRawText(),
            };
        }

        /// <summary>A string argument: an instant or a date when it is one in ISO 8601, else the text.</summary>
        /// <param name="text">The string.</param>
        /// <returns>The value.</returns>
        private static object Text(string text)
        {
            // An instant needs its offset: a wall-clock time without one names no instant to convert
            // to the reader's zone, so it stays text. A bare date is a calendar date, never shifted.
            return InstantPattern().IsMatch(text)
                && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset instant)
                ? instant
                : DatePattern().IsMatch(text) && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
                    ? date
                    : text;
        }

        /// <summary>A number argument, as the narrowest exact type that holds it.</summary>
        /// <param name="element">The number.</param>
        /// <returns>A long, else a decimal, else a double.</returns>
        private static object Number(JsonElement element)
        {
            return element.TryGetInt64(out long integer)
                ? integer
                : element.TryGetDecimal(out decimal exact) ? exact : element.GetDouble();
        }

        /// <summary>
        ///     The localized text an array holds when it has a localized text's shape: exactly three
        ///     elements, the first a string and the others strings or null.
        /// </summary>
        /// <param name="element">The array.</param>
        /// <returns>The localized text; null for any other array.</returns>
        private static LocalizedText? LocalizedTextOf(JsonElement element)
        {
            if (element.GetArrayLength() != LocalizedTextSlots)
            {
                return null;
            }

            JsonElement first = element[0];
            JsonElement second = element[1];
            JsonElement third = element[2];
            return first.ValueKind == JsonValueKind.String && IsSlot(second) && IsSlot(third)
                ? new LocalizedText(first.GetString()!, second.GetString(), third.GetString())
                : null;
        }

        /// <summary>Whether an element may fill a localized text's second or third slot.</summary>
        /// <param name="element">The element.</param>
        /// <returns>True for a string or null.</returns>
        private static bool IsSlot(JsonElement element)
        {
            return element.ValueKind is JsonValueKind.String or JsonValueKind.Null;
        }

        /// <summary>An ISO 8601 date-time with seconds optional, a fraction optional, and a zone designator or offset required.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}(:[0-9]{2}(\.[0-9]{1,7})?)?(Z|[+-][0-9]{2}:[0-9]{2})\z", RegexOptions.CultureInvariant)]
        private static partial Regex InstantPattern();

        /// <summary>An ISO 8601 calendar date.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}\z", RegexOptions.CultureInvariant)]
        private static partial Regex DatePattern();
    }
}
