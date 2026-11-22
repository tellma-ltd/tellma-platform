// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tellma.Core.Abstractions.Api;

namespace Tellma.Core.Mcp.Rendering
{
    /// <summary>One record a get read.</summary>
    /// <param name="Id">The record's id.</param>
    /// <param name="Entity">The record, as the service loaded it.</param>
    internal sealed record StackRecord(long Id, object Entity);

    /// <summary>The records a get read, as the service answered them.</summary>
    /// <param name="Ids">The ids of the records read, in request order.</param>
    /// <param name="Entities">The records, in the service's order.</param>
    /// <param name="Related">The records they point at, if any.</param>
    /// <param name="Extras">The extras asked for, if any.</param>
    internal sealed record StackRecords(
        IReadOnlyList<long> Ids, IReadOnlyList<StackRecord> Entities, object? Related, IReadOnlyDictionary<string, JsonElement>? Extras);

    /// <summary>
    ///     Renders whole records as one text block of the wire's JSON: each record's header first, then
    ///     each child collection cut to a window, with the collection's total and whether rows were
    ///     left out under <c>$collections</c>; then the related rows and the extras. Records are added
    ///     one by one under the character cap. The first record narrows its windows until it fits, so
    ///     a document whose lines alone pass the cap still returns its header.
    /// </summary>
    /// <remarks>
    ///     Every collection is cut on the loaded records, at every depth, before any JSON is written,
    ///     and each piece of JSON is written only into the room left for it: a record holding ten
    ///     thousand lines costs the rows its window keeps, never the rows it holds. The records are the
    ///     tool's own, so they are cut in place.
    /// </remarks>
    internal static class EntityRendering
    {
        /// <summary>The member of a rendered record that describes its windows.</summary>
        public const string CollectionsMember = "$collections";

        /// <summary>The most characters held back for the trailing lines; half the cap when the cap is small.</summary>
        public const int TrailerReserve = 1500;

        /// <summary>The characters a trailing line's list of ids may take before the rest are counted.</summary>
        public const int IdListChars = 500;

        /// <summary>The options the records are written with: the wire's, unindented.</summary>
        private static readonly JsonSerializerOptions Options = TellmaJson.Options;

        /// <summary>Renders records read by id.</summary>
        /// <param name="records">The records the service answered.</param>
        /// <param name="requested">The ids the call named, in order.</param>
        /// <param name="shape">The records' child collections.</param>
        /// <param name="top">The rows each collection keeps, at every depth.</param>
        /// <param name="maxChars">The cap on the text's length.</param>
        /// <returns>The text.</returns>
        public static RenderedText Records(StackRecords records, IReadOnlyList<long> requested, RecordShape shape, int top, int maxChars)
        {
            ArgumentNullException.ThrowIfNull(records);
            ArgumentNullException.ThrowIfNull(requested);
            ArgumentNullException.ThrowIfNull(shape);
            int budget = Math.Max(0, maxChars - Math.Min(TrailerReserve, maxChars / 2));
            RecordWindows windows = new();

            // --- The records, one by one under the budget; the first narrows its windows to fit ---
            StringBuilder text = new("{\"entities\":[");
            List<long> returned = [];
            bool narrowedAny = false;
            foreach (StackRecord record in records.Entities)
            {
                // The room leaves the separator and the closing brackets.
                string? rendered = Fit(record.Entity, shape, top, windows, budget - text.Length - 3, narrow: returned.Count == 0, out bool narrowed);
                if (rendered is null)
                {
                    break;
                }

                narrowedAny |= narrowed;
                _ = text.Append(returned.Count == 0 ? string.Empty : ",").Append(rendered);
                returned.Add(record.Id);
            }

            _ = text.Append(']');

            // --- The related rows and the extras, each when it still fits ---
            bool omitted = false;
            foreach ((string member, object? value, Type? type) in (ReadOnlySpan<(string, object?, Type?)>)[("related", records.Related, records.Related?.GetType()), ("extras", records.Extras, typeof(IReadOnlyDictionary<string, JsonElement>))])
            {
                if (value is null || type is null)
                {
                    continue;
                }

                string? json = BoundedJson.TryWrite(value, type, budget - text.Length - member.Length - 5);
                if (json is null)
                {
                    omitted = true;
                    continue;
                }

                _ = text.Append(",\"").Append(member).Append("\":").Append(json);
            }

            _ = text.Append('}');

            // --- The trailing lines, their id lists shortened until they fit what the cap leaves ---
            HashSet<long> read = [.. records.Ids];
            List<long> left = [.. requested.Where(id => read.Contains(id) && !returned.Contains(id)).Distinct()];
            List<long> absent = [.. requested.Where(id => !read.Contains(id)).Distinct()];
            int room = Math.Max(0, maxChars - text.Length);
            string trailer = Trailer(returned.Count, read.Count, left, absent, narrowedAny, omitted, IdListChars);
            for (int limit = IdListChars / 2; trailer.Length > room && limit > 0; limit /= 2)
            {
                trailer = Trailer(returned.Count, read.Count, left, absent, narrowedAny, omitted, limit);
            }

            if (trailer.Length > room)
            {
                trailer = Trailer(returned.Count, read.Count, left, absent, narrowedAny, omitted, 0);
            }

            return new RenderedText(text.Append(trailer).ToString(), narrowedAny || left.Count > 0 || omitted);
        }

        /// <summary>Renders one window of one record's child collection.</summary>
        /// <param name="records">The records the service answered: the one record, or none.</param>
        /// <param name="id">The record's id.</param>
        /// <param name="collection">The collection.</param>
        /// <param name="skip">The collection's rows to skip.</param>
        /// <param name="top">The collection's rows to return, and the rows each of their own collections keeps.</param>
        /// <param name="maxChars">The cap on the text's length.</param>
        /// <returns>The text.</returns>
        public static RenderedText Collection(StackRecords records, long id, CollectionShape collection, int skip, int top, int maxChars)
        {
            ArgumentNullException.ThrowIfNull(records);
            ArgumentNullException.ThrowIfNull(collection);
            CultureInfo invariant = CultureInfo.InvariantCulture;
            if (records.Entities.Count == 0)
            {
                return new RenderedText(string.Create(invariant, $"absent: {id} — no record has this id, or you may not see it."), false);
            }

            // --- The window's rows, one by one under the budget, each written only into the room left ---
            IList rows = collection.Read(records.Entities[0].Entity) ?? Array.Empty<object>();
            int total = rows.Count;
            int budget = Math.Max(0, maxChars - RowRendering.FooterReserve);
            RecordWindows windows = new();
            StringBuilder text = new();
            _ = text.Append(invariant, $"{{\"id\":{JsonValue.Create(id).ToJsonString()},\"collection\":{JsonValue.Create(collection.Name).ToJsonString(Options)},\"total\":{total},\"skip\":{skip},\"rows\":[");
            int returned = 0;
            for (int index = skip; index < total && returned < top; index++)
            {
                string? row = rows[index] is object value ? Render(value, collection.Rows, top, windows, budget - text.Length - 3) : "null";
                if (row is null || text.Length + row.Length + 3 > budget)
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

        /// <summary>The trailing lines: what the cap left out, and the ids that named no record.</summary>
        /// <param name="returned">The records returned.</param>
        /// <param name="read">The records read.</param>
        /// <param name="left">The ids of the records read but left out.</param>
        /// <param name="absent">The ids that named no record the caller may see.</param>
        /// <param name="narrowed">Whether a record's windows were narrowed to fit.</param>
        /// <param name="omitted">Whether the related rows or the extras were left out.</param>
        /// <param name="idChars">The characters each list of ids may take.</param>
        /// <returns>The lines, each starting with a line break.</returns>
        internal static string Trailer(int returned, int read, IReadOnlyList<long> left, IReadOnlyList<long> absent, bool narrowed, bool omitted, int idChars)
        {
            CultureInfo invariant = CultureInfo.InvariantCulture;
            StringBuilder text = new();
            if (left.Count > 0)
            {
                _ = returned == 0
                    ? text.Append(invariant, $"\ntruncated: true — record {left[0]} alone passes the size limit; read its fields with {McpToolNames.Query} and a select.")
                    : text.Append(invariant, $"\ntruncated: true — {returned} of {read} records fit the size limit; get the rest by id: {Ids(left, idChars)}.");
                if (returned == 0 && left.Count > 1)
                {
                    _ = text.Append(invariant, $" Get the rest by id: {Ids([.. left.Skip(1)], idChars)}.");
                }
            }
            else if (narrowed)
            {
                _ = text.Append("\ntruncated: true — a record's collections were cut short to fit the size limit; page a collection with collection, skip and top.");
            }

            if (omitted)
            {
                _ = text.Append("\nThe related rows or the extras were left out to fit the size limit; get fewer ids to see them.");
            }

            if (absent.Count > 0)
            {
                _ = text.Append(invariant, $"\nabsent: {Ids(absent, idChars)} — no record has these ids, or you may not see them.");
            }

            return text.ToString();
        }

        /// <summary>A list of ids, as many as fit the characters given, then the count of the rest.</summary>
        /// <param name="ids">The ids.</param>
        /// <param name="maxChars">The characters the listed ids may take.</param>
        /// <returns>The list.</returns>
        internal static string Ids(IReadOnlyList<long> ids, int maxChars)
        {
            CultureInfo invariant = CultureInfo.InvariantCulture;
            StringBuilder text = new();
            int listed = 0;
            foreach (long id in ids)
            {
                string piece = listed == 0 ? id.ToString(invariant) : ", " + id.ToString(invariant);
                if (text.Length + piece.Length > maxChars)
                {
                    break;
                }

                _ = text.Append(piece);
                listed++;
            }

            int rest = ids.Count - listed;
            return rest == 0 ? text.ToString()
                : listed == 0 ? string.Create(invariant, $"{rest} ids")
                : string.Create(invariant, $"{text} and {rest} more");
        }

        /// <summary>A record rendered with its windows, narrowed until it fits when it may be.</summary>
        /// <param name="entity">The record.</param>
        /// <param name="shape">Its child collections.</param>
        /// <param name="top">The rows each collection keeps at first.</param>
        /// <param name="windows">The windows cut so far.</param>
        /// <param name="room">The characters left for it.</param>
        /// <param name="narrow">Whether its windows may be narrowed to fit: the first record's alone.</param>
        /// <param name="narrowed">Whether they were.</param>
        /// <returns>The record's JSON, or null when it does not fit.</returns>
        private static string? Fit(object entity, RecordShape shape, int top, RecordWindows windows, int room, bool narrow, out bool narrowed)
        {
            narrowed = false;
            int window = top;
            while (true)
            {
                if (Render(entity, shape, window, windows, room) is string rendered)
                {
                    return rendered;
                }

                // Halved until no rows are left; past the header alone, the record does not fit.
                if (!narrow || window == 0)
                {
                    return null;
                }

                narrowed = true;
                window /= 2;
            }
        }

        /// <summary>A record cut to its windows and written, when it fits the room.</summary>
        /// <param name="entity">The record.</param>
        /// <param name="shape">Its child collections.</param>
        /// <param name="top">The rows each collection keeps, at every depth.</param>
        /// <param name="windows">The windows cut so far.</param>
        /// <param name="room">The characters it may take.</param>
        /// <returns>The JSON, or null when it takes more.</returns>
        private static string? Render(object entity, RecordShape shape, int top, RecordWindows windows, int room)
        {
            windows.Cut(entity, shape, top);
            if (BoundedJson.TryWrite(entity, entity.GetType(), room) is not string json)
            {
                return null;
            }

            // The descriptions of the windows add their own characters, so the room is checked again.
            JsonObject node = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidOperationException("A record serialized to nothing.");
            windows.Describe(node, entity, shape);
            string described = node.ToJsonString(Options);
            return described.Length <= room ? described : null;
        }

        /// <summary>
        ///     The windows of the records rendered: every collection cut to its first rows, at every
        ///     depth, with the rows it was loaded with kept aside, so a narrower window cuts again from
        ///     the loaded rows and a description reports their total.
        /// </summary>
        internal sealed class RecordWindows
        {
            /// <summary>The rows each collection was loaded with, by record and collection.</summary>
            private readonly Dictionary<(object Record, CollectionShape Collection), IList?> _loaded = new(RecordComparer.Instance);

            /// <summary>Cuts each collection of a record, and of each row it keeps, to its first rows.</summary>
            /// <param name="record">The record.</param>
            /// <param name="shape">Its child collections.</param>
            /// <param name="top">The rows each collection keeps.</param>
            public void Cut(object record, RecordShape shape, int top)
            {
                ArgumentNullException.ThrowIfNull(record);
                ArgumentNullException.ThrowIfNull(shape);
                foreach (CollectionShape collection in shape.Collections)
                {
                    if (Loaded(record, collection) is not IList loaded)
                    {
                        continue;
                    }

                    List<object?> kept = [];
                    for (int index = 0; index < loaded.Count && index < top; index++)
                    {
                        kept.Add(loaded[index]);
                    }

                    collection.Write(record, kept);
                    foreach (object? row in kept)
                    {
                        if (row is not null)
                        {
                            Cut(row, collection.Rows, top);
                        }
                    }
                }
            }

            /// <summary>
            ///     Writes a cut record's JSON in its reading order: its header, then the description of
            ///     its windows under <c>$collections</c>, then each collection; and each kept row's the
            ///     same way.
            /// </summary>
            /// <param name="json">The record's JSON.</param>
            /// <param name="record">The record, cut.</param>
            /// <param name="shape">Its child collections.</param>
            public void Describe(JsonObject json, object record, RecordShape shape)
            {
                ArgumentNullException.ThrowIfNull(json);
                ArgumentNullException.ThrowIfNull(record);
                ArgumentNullException.ThrowIfNull(shape);
                List<CollectionShape> present = [.. shape.Collections.Where(collection => json[collection.Name] is JsonArray)];
                if (present.Count == 0)
                {
                    return;
                }

                // --- The members detached in order, then the header put back first ---
                List<KeyValuePair<string, JsonNode?>> members = [.. json];
                json.Clear();
                foreach ((string name, JsonNode? value) in members)
                {
                    if (!present.Exists(collection => string.Equals(collection.Name, name, StringComparison.Ordinal)))
                    {
                        json[name] = value;
                    }
                }

                // --- Each window's total, the rows kept, and whether any were left out ---
                JsonObject description = [];
                foreach (CollectionShape collection in present)
                {
                    int kept = ((JsonArray)members.First(member => string.Equals(member.Key, collection.Name, StringComparison.Ordinal)).Value!).Count;
                    int total = Loaded(record, collection)?.Count ?? kept;
                    description[collection.Name] = new JsonObject { ["total"] = total, ["returned"] = kept, ["truncated"] = kept < total };
                }

                json[CollectionsMember] = description;

                // --- The collections, each kept row described in turn ---
                foreach (CollectionShape collection in present)
                {
                    var rows = (JsonArray)members.First(member => string.Equals(member.Key, collection.Name, StringComparison.Ordinal)).Value!;
                    json[collection.Name] = rows;
                    IList cut = collection.Read(record) ?? Array.Empty<object>();
                    for (int index = 0; index < rows.Count && index < cut.Count; index++)
                    {
                        if (rows[index] is JsonObject row && cut[index] is object value)
                        {
                            Describe(row, value, collection.Rows);
                        }
                    }
                }
            }

            /// <summary>The rows a collection of a record was loaded with, kept aside the first time it is cut.</summary>
            /// <param name="record">The record.</param>
            /// <param name="collection">The collection.</param>
            /// <returns>The rows, or null when the record carries none.</returns>
            private IList? Loaded(object record, CollectionShape collection)
            {
                if (!_loaded.TryGetValue((record, collection), out IList? loaded))
                {
                    loaded = collection.Read(record);
                    _loaded[(record, collection)] = loaded;
                }

                return loaded;
            }
        }

        /// <summary>Compares a record and a collection by reference: records are compared as the objects they are.</summary>
        private sealed class RecordComparer : IEqualityComparer<(object Record, CollectionShape Collection)>
        {
            /// <summary>The comparer.</summary>
            public static readonly RecordComparer Instance = new();

            /// <inheritdoc />
            public bool Equals((object Record, CollectionShape Collection) x, (object Record, CollectionShape Collection) y)
            {
                return ReferenceEquals(x.Record, y.Record) && ReferenceEquals(x.Collection, y.Collection);
            }

            /// <inheritdoc />
            public int GetHashCode((object Record, CollectionShape Collection) obj)
            {
                return HashCode.Combine(RuntimeHelpers.GetHashCode(obj.Record), RuntimeHelpers.GetHashCode(obj.Collection));
            }
        }
    }
}
