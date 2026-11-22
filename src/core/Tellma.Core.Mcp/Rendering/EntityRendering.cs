// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tellma.Core.Abstractions.Api;

namespace Tellma.Core.Mcp.Rendering
{
    /// <summary>How a get windows each record's child collections.</summary>
    /// <param name="Collections">The JSON names of the stack's child collections.</param>
    /// <param name="Top">The rows of each collection a record carries.</param>
    internal sealed record ChildWindows(IReadOnlyList<string> Collections, int Top);

    /// <summary>
    ///     Renders whole records as one text block of the wire's JSON: each record's header first, then
    ///     each child collection cut to a window, with the collection's total and whether rows were
    ///     left out under <c>$collections</c>; then the related rows and the extras. Records are added
    ///     one by one under the character cap. The first record always appears, its windows narrowed
    ///     until it fits, so a document whose lines alone pass the cap still returns its header.
    /// </summary>
    internal static class EntityRendering
    {
        /// <summary>The member of a rendered record that describes its windows.</summary>
        public const string CollectionsMember = "$collections";

        /// <summary>The characters held back for the trailing lines.</summary>
        public const int TrailerReserve = 1500;

        /// <summary>The options the records are written with: the wire's, unindented.</summary>
        private static readonly JsonSerializerOptions Options = TellmaJson.Options;

        /// <summary>Renders records read by id.</summary>
        /// <param name="result">The entities result, in the wire's JSON.</param>
        /// <param name="requested">The ids the call named, in order.</param>
        /// <param name="windows">How each record's collections are windowed.</param>
        /// <param name="maxChars">The cap on the text's length.</param>
        /// <returns>The text.</returns>
        public static RenderedText Records(JsonObject result, IReadOnlyList<long> requested, ChildWindows windows, int maxChars)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(requested);
            ArgumentNullException.ThrowIfNull(windows);
            JsonArray entities = result["entities"] as JsonArray ?? [];
            int budget = Math.Max(0, maxChars - TrailerReserve);

            // --- The records, one by one under the budget; the first narrows its windows to fit ---
            StringBuilder text = new("{\"entities\":[");
            List<long> returned = [];
            bool truncated = false;
            for (int index = 0; index < entities.Count; index++)
            {
                if (entities[index] is not JsonObject entity)
                {
                    continue;
                }

                string? rendered = Fit(entity, windows, budget - text.Length - 1, narrow: returned.Count == 0, out bool narrowed);
                if (rendered is null)
                {
                    truncated = true;
                    break;
                }

                truncated |= narrowed;
                _ = text.Append(returned.Count == 0 ? string.Empty : ",").Append(rendered);
                if (IdOf(entity) is long id)
                {
                    returned.Add(id);
                }
            }

            _ = text.Append(']');

            // --- The related rows and the extras, each when it still fits ---
            bool omitted = false;
            foreach (string member in (string[])["related", "extras"])
            {
                if (result[member] is JsonNode node)
                {
                    string value = node.ToJsonString(Options);
                    if (text.Length + member.Length + value.Length + 5 <= budget)
                    {
                        _ = text.Append(",\"").Append(member).Append("\":").Append(value);
                    }
                    else
                    {
                        omitted = true;
                    }
                }
            }

            _ = text.Append('}');

            // --- The trailing lines: what the cap left out, and the ids that named no record ---
            HashSet<long> read = [.. IdsOf(result)];
            List<long> left = [.. requested.Where(id => read.Contains(id) && !returned.Contains(id)).Distinct()];
            List<long> absent = [.. requested.Where(id => !read.Contains(id)).Distinct()];
            if (left.Count > 0)
            {
                _ = text.Append(CultureInfo.InvariantCulture, $"\ntruncated: true — {returned.Count} of {read.Count} records fit the size limit; get the rest by id: {string.Join(", ", left)}.");
            }
            else if (truncated)
            {
                _ = text.Append("\ntruncated: true — a record's collections were cut short to fit the size limit; page a collection with collection, skip and top.");
            }

            if (omitted)
            {
                _ = text.Append("\nThe related rows or the extras were left out to fit the size limit; get fewer ids to see them.");
            }

            if (absent.Count > 0)
            {
                _ = text.Append(CultureInfo.InvariantCulture, $"\nabsent: {string.Join(", ", absent)} — no record has these ids, or you may not see them.");
            }

            return new RenderedText(text.ToString(), truncated || left.Count > 0 || omitted);
        }

        /// <summary>Renders one window of one record's child collection.</summary>
        /// <param name="result">The entities result of the one record, in the wire's JSON.</param>
        /// <param name="id">The record's id.</param>
        /// <param name="collection">The collection's JSON name.</param>
        /// <param name="skip">The collection's rows to skip.</param>
        /// <param name="top">The collection's rows to return.</param>
        /// <param name="maxChars">The cap on the text's length.</param>
        /// <returns>The text.</returns>
        public static RenderedText Collection(JsonObject result, long id, string collection, int skip, int top, int maxChars)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(collection);
            CultureInfo invariant = CultureInfo.InvariantCulture;
            if ((result["entities"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault() is not JsonObject entity)
            {
                return new RenderedText(string.Create(invariant, $"absent: {id} — no record has this id, or you may not see it."), false);
            }

            // --- The window's rows, one by one under the budget ---
            JsonArray rows = entity[collection] as JsonArray ?? [];
            int total = rows.Count;
            int budget = Math.Max(0, maxChars - RowRendering.FooterReserve);
            StringBuilder text = new();
            _ = text.Append(invariant, $"{{\"id\":{JsonValue.Create(id).ToJsonString()},\"collection\":{JsonValue.Create(collection).ToJsonString(Options)},\"total\":{total},\"skip\":{skip},\"rows\":[");
            int returned = 0;
            for (int index = skip; index < total && returned < top; index++)
            {
                string row = rows[index]?.ToJsonString(Options) ?? "null";
                if (text.Length + row.Length + 3 > budget)
                {
                    break;
                }

                _ = text.Append(returned == 0 ? string.Empty : ",").Append(row);
                returned++;
            }

            _ = text.Append("]}");

            // --- The footer: what came back, and how to go on ---
            int next = skip + returned;
            bool truncated = returned < Math.Min(top, Math.Max(0, total - skip));
            _ = text.Append(invariant, $"\n\nreturned: {returned} rows from skip {skip} of {total}");
            if (truncated)
            {
                _ = text.Append(invariant, $"\ntruncated: true — the result reached its size limit; page with skip={next} and a smaller top.");
            }
            else if (next < total)
            {
                _ = text.Append(invariant, $"\nMore rows follow: page with skip={next}.");
            }

            return new RenderedText(text.ToString(), truncated);
        }

        /// <summary>A record rendered with its windows, narrowed until it fits when it may be.</summary>
        /// <param name="entity">The record.</param>
        /// <param name="windows">How its collections are windowed.</param>
        /// <param name="room">The characters left for it.</param>
        /// <param name="narrow">Whether its windows may be narrowed to fit: the first record's alone.</param>
        /// <param name="narrowed">Whether they were.</param>
        /// <returns>The record's JSON, or null when it does not fit and may not be narrowed.</returns>
        private static string? Fit(JsonObject entity, ChildWindows windows, int room, bool narrow, out bool narrowed)
        {
            narrowed = false;
            int top = windows.Top;
            while (true)
            {
                string rendered = Window(entity, windows.Collections, top).ToJsonString(Options);
                if (rendered.Length <= room)
                {
                    return rendered;
                }

                if (!narrow)
                {
                    return null;
                }

                // Halved until no rows are left; then the header alone, whatever its size.
                narrowed = true;
                if (top == 0)
                {
                    return rendered;
                }

                top /= 2;
            }
        }

        /// <summary>A record with its header first, then its windows' description, then each collection cut to its window.</summary>
        /// <param name="entity">The record.</param>
        /// <param name="collections">The JSON names of the stack's child collections.</param>
        /// <param name="top">The rows of each collection to keep.</param>
        /// <returns>The windowed record.</returns>
        internal static JsonObject Window(JsonObject entity, IReadOnlyList<string> collections, int top)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(collections);

            // --- The header: every member that is not a child collection, in the record's order ---
            JsonObject windowed = [];
            foreach ((string name, JsonNode? value) in entity)
            {
                if (!collections.Contains(name, StringComparer.Ordinal))
                {
                    windowed[name] = value?.DeepClone();
                }
            }

            // --- The windows: each collection's total, the rows kept, and whether any were left out ---
            List<(string Name, JsonArray Rows)> present = [.. collections
                .Where(entity.ContainsKey)
                .Select(name => (name, entity[name] as JsonArray))
                .Where(static pair => pair.Item2 is not null)
                .Select(static pair => (pair.name, pair.Item2!))];
            if (present.Count > 0)
            {
                JsonObject description = [];
                foreach ((string name, JsonArray rows) in present)
                {
                    int kept = Math.Min(top, rows.Count);
                    description[name] = new JsonObject
                    {
                        ["total"] = rows.Count,
                        ["returned"] = kept,
                        ["truncated"] = kept < rows.Count,
                    };
                }

                windowed[CollectionsMember] = description;
                foreach ((string name, JsonArray rows) in present)
                {
                    windowed[name] = new JsonArray([.. rows.Take(top).Select(static row => row?.DeepClone())]);
                }
            }

            return windowed;
        }

        /// <summary>The ids an entities result reports.</summary>
        /// <param name="result">The result.</param>
        /// <returns>The ids.</returns>
        private static IEnumerable<long> IdsOf(JsonObject result)
        {
            return (result["ids"] as JsonArray ?? []).Select(Long).OfType<long>();
        }

        /// <summary>A record's id.</summary>
        /// <param name="entity">The record.</param>
        /// <returns>The id, or null when it carries none.</returns>
        private static long? IdOf(JsonObject entity)
        {
            return Long(entity["id"]);
        }

        /// <summary>An id as the wire writes it: a number, or a long's digits as a string.</summary>
        /// <param name="node">The node.</param>
        /// <returns>The id, or null.</returns>
        private static long? Long(JsonNode? node)
        {
            return node is JsonValue value
                ? value.TryGetValue(out long number) ? number
                : value.TryGetValue(out string? text) && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed) ? parsed
                : null
                : null;
        }
    }
}
