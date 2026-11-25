// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tellma.Core.Abstractions.Api;

namespace Tellma.Core.AspNetCore.Observability
{
    /// <summary>Why the relay dropped an item.</summary>
    internal enum ClientItemDrop
    {
        /// <summary>The item is kept.</summary>
        None,

        /// <summary>Its kind is not <c>event</c>, <c>timing</c> or <c>error</c>.</summary>
        Kind,

        /// <summary>Its name breaks the grammar, or an error's is not the one error name.</summary>
        Name,

        /// <summary>Its time is more than a day before the batch's, or more than a minute after it.</summary>
        Time,
    }

    /// <summary>
    ///     The relay's checks: a batch that breaks one is refused whole, an item that breaks one is
    ///     dropped, and a property that breaks one is dropped from its item.
    /// </summary>
    internal static partial class ClientTelemetryRules
    {
        /// <summary>The largest body the relay reads, in bytes.</summary>
        public const int MaxBodyBytes = 32 * 1024;

        /// <summary>The most items a batch carries.</summary>
        public const int MaxItems = 100;

        /// <summary>The most properties an item keeps.</summary>
        public const int MaxProperties = 16;

        /// <summary>The longest an item's name is.</summary>
        public const int MaxNameLength = 64;

        /// <summary>The longest a text property is, but for a message or a stack.</summary>
        public const int MaxTextLength = 128;

        /// <summary>The longest a <c>message</c> property is.</summary>
        public const int MaxMessageLength = 256;

        /// <summary>The longest a <c>stack</c> property is.</summary>
        public const int MaxStackLength = 4096;

        /// <summary>The kind of an item that records an occurrence.</summary>
        public const string EventKind = "event";

        /// <summary>The kind of an item that records a duration.</summary>
        public const string TimingKind = "timing";

        /// <summary>The kind of an item that records an error.</summary>
        public const string ErrorKind = "error";

        /// <summary>The one name of every error item.</summary>
        public const string ErrorName = "tm.error.captured";

        /// <summary>The name of the item that reports a slow request.</summary>
        public const string SlowRequestName = "tm.api.slow";

        /// <summary>The property holding an error's message, which the masks run over.</summary>
        public const string MessageKey = "message";

        /// <summary>The property holding an error's stack, which the masks run over.</summary>
        public const string StackKey = "stack";

        /// <summary>How long before the batch's time an item's time may be.</summary>
        public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

        /// <summary>How long after the batch's time an item's time may be.</summary>
        public static readonly TimeSpan MaxLead = TimeSpan.FromMinutes(1);

        /// <summary>
        ///     The reason a batch is refused whole, or null when its envelope holds: a version equal to
        ///     the <c>Tellma-Client</c> header's, a tab id, a tenant id that is null or positive, a time,
        ///     and at most <see cref="MaxItems" /> items.
        /// </summary>
        /// <param name="batch">The batch; null when the body is the JSON <c>null</c>.</param>
        /// <param name="headerVersion">The version part of the request's <c>Tellma-Client</c> header; null when it has none.</param>
        /// <returns>A sentence on what is wrong, or null.</returns>
        public static string? Refusal(ClientTelemetryBatch? batch, string? headerVersion)
        {
            return batch switch
            {
                null => "The body is not a telemetry batch.",
                { Version: null } or { Version.Length: 0 } => "The batch states no version.",
                _ when !string.Equals(batch.Version, headerVersion, StringComparison.Ordinal) =>
                    "The batch's version is not the one the Tellma-Client header states.",
                { TabId: null } => "The batch states no tab id.",
                _ when !TabIdPattern().IsMatch(batch.TabId) =>
                    "The batch's tab id is not 8 to 40 letters, digits, underscores or hyphens.",
                { TenantId: < 1 } => "The batch's tenant id is not positive.",
                _ when batch.SentAt == default => "The batch states no time it was sent.",
                { Items: null } => "The batch carries no list of items.",
                { Items.Count: > MaxItems } => string.Create(
                    CultureInfo.InvariantCulture, $"The batch carries {batch.Items.Count} items; it carries at most {MaxItems}."),
                _ => null,
            };
        }

        /// <summary>Why an item is dropped, or <see cref="ClientItemDrop.None" /> when it is kept.</summary>
        /// <param name="item">The item; null for a <c>null</c> in the list.</param>
        /// <param name="sentAt">The batch's time.</param>
        /// <returns>The reason.</returns>
        public static ClientItemDrop Check(ClientTelemetryItem? item, DateTimeOffset sentAt)
        {
            // --- The kind, then the name the kind requires, then the time ---
            if (item?.Kind is not (EventKind or TimingKind or ErrorKind))
            {
                return ClientItemDrop.Kind;
            }

            if (item.Kind == ErrorKind ? item.Name != ErrorName : !IsName(item.Name))
            {
                return ClientItemDrop.Name;
            }

            // A difference of two times never overflows, unlike a time moved by a span.
            TimeSpan offset = item.At - sentAt;
            return offset >= -MaxAge && offset <= MaxLead ? ClientItemDrop.None : ClientItemDrop.Time;
        }

        /// <summary>Whether a name follows the grammar: lowercase, dotted, three to six segments, at most 64 characters.</summary>
        /// <param name="name">The name; null for none.</param>
        /// <returns>True when it does.</returns>
        public static bool IsName(string? name)
        {
            return name is { Length: > 0 and <= MaxNameLength } && NamePattern().IsMatch(name);
        }

        /// <summary>Whether a trace id is 32 lowercase hexadecimal digits, not all zeros.</summary>
        /// <param name="traceId">The trace id; null for none.</param>
        /// <returns>True when it is one.</returns>
        public static bool IsTraceId(string? traceId)
        {
            if (traceId is not { Length: 32 })
            {
                return false;
            }

            bool nonZero = false;
            foreach (char digit in traceId)
            {
                if (digit is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
                {
                    return false;
                }

                nonZero |= digit != '0';
            }

            return nonZero;
        }

        /// <summary>
        ///     The properties an item keeps, in the client's order: at most <see cref="MaxProperties" />,
        ///     each with a camelCase key of at most 32 characters and a value that is a number, a
        ///     boolean or a text short enough for its key. A message's or a stack's text is masked.
        /// </summary>
        /// <param name="properties">The item's properties; null for none.</param>
        /// <param name="kept">Where the kept properties go: a <see cref="long" />, a finite <see cref="double" />, a <see cref="bool" /> or a <see cref="string" /> each.</param>
        /// <returns>How many properties were dropped.</returns>
        public static int KeepProperties(IReadOnlyDictionary<string, JsonElement>? properties, IDictionary<string, object> kept)
        {
            ArgumentNullException.ThrowIfNull(kept);
            if (properties is null)
            {
                return 0;
            }

            int dropped = 0;
            foreach (KeyValuePair<string, JsonElement> property in properties)
            {
                if (kept.Count < MaxProperties && KeyPattern().IsMatch(property.Key) && Value(property.Key, property.Value) is object value)
                {
                    kept[property.Key] = value;
                }
                else
                {
                    dropped++;
                }
            }

            return dropped;
        }

        /// <summary>A property's value as the relay keeps it, or null when it is dropped.</summary>
        /// <param name="key">The property's key.</param>
        /// <param name="value">The property's value.</param>
        /// <returns>The value, or null.</returns>
        private static object? Value(string key, JsonElement value)
        {
            switch (value.ValueKind)
            {
                // --- A number: a whole one as a long, any other finite one as a double ---
                case JsonValueKind.Number:
                    if (value.TryGetInt64(out long whole))
                    {
                        return whole;
                    }

                    return value.TryGetDouble(out double real) && double.IsFinite(real) ? real : null;

                case JsonValueKind.True:
                    return true;

                case JsonValueKind.False:
                    return false;

                // --- A text within its key's length; a message or a stack masked, and dropped when the masks give up ---
                case JsonValueKind.String:
                    string text = value.GetString()!;
                    if (text.Length > MaxLength(key))
                    {
                        return null;
                    }

                    if (key is MessageKey or StackKey)
                    {
                        return ClientTelemetryMasks.TryMask(text, out string? masked) ? masked : null;
                    }

                    return text;

                // --- Null, an object or an array: nothing is nested, and a null says nothing ---
                case JsonValueKind.Null:
                case JsonValueKind.Object:
                case JsonValueKind.Array:
                case JsonValueKind.Undefined:
                default:
                    return null;
            }
        }

        /// <summary>The longest a text property is.</summary>
        /// <param name="key">The property's key.</param>
        /// <returns>The length.</returns>
        private static int MaxLength(string key)
        {
            return key switch
            {
                MessageKey => MaxMessageLength,
                StackKey => MaxStackLength,
                _ => MaxTextLength,
            };
        }

        /// <summary>A tab id: 8 to 40 letters, digits, underscores or hyphens.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"^[A-Za-z0-9_-]{8,40}\z", RegexOptions.CultureInvariant, 1000)]
        private static partial Regex TabIdPattern();

        /// <summary>A name: a lowercase scope, then two to five lowercase segments of letters, digits and hyphens.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"^[a-z][a-z0-9]*(?:\.[a-z0-9][a-z0-9-]*){2,5}\z", RegexOptions.CultureInvariant, 1000)]
        private static partial Regex NamePattern();

        /// <summary>A property key: camelCase, at most 32 characters.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"^[a-z][a-zA-Z0-9]{0,31}\z", RegexOptions.CultureInvariant, 1000)]
        private static partial Regex KeyPattern();
    }
}
