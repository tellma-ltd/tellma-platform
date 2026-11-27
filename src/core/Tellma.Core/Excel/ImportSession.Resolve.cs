// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections;
using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud.Save;

namespace Tellma.Core.Excel
{
    // Spec 0019 §9 and §10: a chunk resolves in two passes. First each parent row's identity: new, or
    // the stored row its row key names (§9.2), every new row given the chunk's next temporary id (§9.6).
    // Then each row's entity: a new one, or a copy of the stored row with the sheet's columns laid over
    // it (§9.3), its stamp the sheet's or the stored one; its references bound within the chunk first,
    // then through the committed ids of an earlier chunk, then through the lookups (§9.6, §10.2-§10.3);
    // and each present child sheet's rows synchronised with the stored children by identity, by natural
    // key, or by replacing them (§9.5). Every input is copied, never changed, so a range resolves the
    // same way each time it is given the same rows.
    /// <summary>The resolution of a parsed import's chunks.</summary>
    internal sealed partial class ImportSession
    {
        /// <summary>The name a lookup's key set is restricted by; the row source binds its own.</summary>
        private const string LookupTable = "@keys";

        /// <summary>The property an activatable entity's rows are active by.</summary>
        private const string ActiveProperty = "IsActive";

        /// <summary>
        ///     Resolves a chunk: binds its lookups, overlays its stored rows, synchronises its children,
        ///     assigns its temporary ids, and places every payload path in the workbook.
        /// </summary>
        /// <param name="fromRow">The chunk's first position.</param>
        /// <param name="toRow">The chunk's last position.</param>
        /// <param name="lookupRows">Each of the chunk's lookups with the rows it read.</param>
        /// <param name="hydrated">The stored rows of the chunk's hydration ids, each with its children at every depth.</param>
        /// <param name="committedNewIds">The ids an earlier chunk's new rows were given, by their negative sheet identity.</param>
        /// <returns>The chunk.</returns>
        public ImportChunk Resolve(
            int fromRow,
            int toRow,
            IReadOnlyDictionary<ExcelQuery, IReadOnlyList<IReadOnlyList<object?>>> lookupRows,
            IReadOnlyList<object> hydrated,
            IReadOnlyDictionary<long, long> committedNewIds)
        {
            CheckRange(fromRow, toRow);
            ArgumentNullException.ThrowIfNull(lookupRows);
            ArgumentNullException.ThrowIfNull(hydrated);
            ArgumentNullException.ThrowIfNull(committedNewIds);

            // --- The stored rows by id ---
            Dictionary<long, object> stored = [];
            foreach (object row in hydrated)
            {
                stored[IdOf(Shape.Entity, row)] = row;
            }

            // --- First each row's identity, then each row's entity ---
            ChunkBuilder builder = new(this, new Lookups(lookupRows), committedNewIds, fromRow, toRow);
            for (int position = fromRow; position <= toRow; position++)
            {
                builder.Identify(Order[position], stored);
            }

            builder.RefuseCycles(Cycles);

            List<object> entities = [];
            List<ImportChunkRow> rows = [];
            for (int position = fromRow; position <= toRow; position++)
            {
                (object entity, ImportChunkRow facts) = builder.Build(Order[position]);
                entities.Add(entity);
                rows.Add(facts);
            }

            return new ImportChunk(entities, builder.Map, builder.Errors, builder.Facts(rows));
        }

        /// <summary>The cells a chunk looks up: each reference cell it does not bind itself, and each natural row key's cell.</summary>
        /// <param name="fromRow">The chunk's first position.</param>
        /// <param name="toRow">The chunk's last position.</param>
        /// <returns>Each cell's target, key and normal value.</returns>
        private IEnumerable<(EntityMetadata Target, PropertyMetadata Key, object Value)> LookupValues(int fromRow, int toRow)
        {
            for (int position = fromRow; position <= toRow; position++)
            {
                ImportRow parent = Order[position];

                // --- The natural row key of an update or upsert ---
                if (Shape is { Mode: not ImportMode.Insert, RowKey: not ImportPaths.Id, KeyColumn: ImportColumn key }
                    && ImportKeys.Normalize(parent[key]) is object keyValue)
                {
                    yield return (Shape.Entity, key.Path.Property, keyValue);
                }

                // --- Every reference cell of the row and the rows under it ---
                foreach (ImportRow row in Family(parent))
                {
                    foreach (ImportColumn column in row.Sheet.Columns.Where(static column => column.Path.Kind == ImportPathKind.Reference))
                    {
                        if (LookedUp(row, column, fromRow, toRow) is object value)
                        {
                            yield return (column.Path.Reference!.Target, column.Path.Key!, value);
                        }
                    }
                }
            }
        }

        /// <summary>
        ///     The value a reference cell is looked up by: its normal value, unless it is blank, names a
        ///     row of the chunk, or is an identity that names no stored row (zero, negative, or beyond
        ///     its key's type).
        /// </summary>
        /// <param name="row">The row.</param>
        /// <param name="column">The reference column.</param>
        /// <param name="fromRow">The chunk's first position.</param>
        /// <param name="toRow">The chunk's last position.</param>
        /// <returns>The value; null when it is not looked up.</returns>
        private static object? LookedUp(ImportRow row, ImportColumn column, int fromRow, int toRow)
        {
            object? value = ImportKeys.Normalize(row[column]);
            return value is null || InChunk(row, column, fromRow, toRow) is not null
                ? null
                : value is long number && ((IsTargetKey(column) && number <= 0) || !ImportKeys.Fits(column.Path.Key!.ClrType, number))
                ? null
                : value;
        }

        /// <summary>The row of the chunk a reference cell names, matched within the workbook; null for none.</summary>
        /// <param name="row">The row.</param>
        /// <param name="column">The reference column.</param>
        /// <param name="fromRow">The chunk's first position.</param>
        /// <param name="toRow">The chunk's last position.</param>
        /// <returns>The parent row named.</returns>
        private static ImportRow? InChunk(ImportRow row, ImportColumn column, int fromRow, int toRow)
        {
            return row.InSheetTargets?.GetValueOrDefault(column.Slot) is ImportRow target && target.Position >= fromRow && target.Position <= toRow
                ? target
                : null;
        }

        /// <summary>Whether a reference column is in its target's surrogate key.</summary>
        /// <param name="column">The column.</param>
        /// <returns>True when it is.</returns>
        private static bool IsTargetKey(ImportColumn column)
        {
            return column.Path.Reference is ReferenceMetadata reference && column.Path.Key?.Name == reference.Target.Key.Name;
        }

        /// <summary>The stored row a parent row names by its row key; null for a new row, an insert, or one naming none.</summary>
        /// <param name="row">The parent row.</param>
        /// <param name="lookups">The chunk's lookups.</param>
        /// <returns>The id.</returns>
        private long? StoredId(ImportRow row, Lookups lookups)
        {
            // An insert overlays nothing; an Id key names the stored row itself; a natural key names it through its lookup.
            return Shape is not { Mode: not ImportMode.Insert, KeyColumn: ImportColumn key }
                ? null
                : Shape.RowKey == ImportPaths.Id
                    ? row.Id is long id and > 0 ? id : null
                    : ImportKeys.Normalize(row[key]) is object value && lookups.Find(Shape.Entity, key.Path.Property, value) is [long found] ? found : null;
        }

        /// <summary>A row's id, as a whole number.</summary>
        /// <param name="entity">The row's entity.</param>
        /// <param name="row">The row.</param>
        /// <returns>The id.</returns>
        private static long IdOf(EntityMetadata entity, object row)
        {
            return Convert.ToInt64(entity.Key.Getter(row) ?? 0L, CultureInfo.InvariantCulture);
        }

        /// <summary>A value as a property holds it: an id as the key type, a name as its enum, a number as its width.</summary>
        /// <param name="value">The value; null for none.</param>
        /// <param name="clrType">The property's CLR type.</param>
        /// <returns>The value converted.</returns>
        private static object? As(object? value, Type clrType)
        {
            Type type = Nullable.GetUnderlyingType(clrType) ?? clrType;
            return value is null || type.IsInstanceOfType(value)
                ? value
                : type.IsEnum
                ? value is string name ? Enum.Parse(type, name, ignoreCase: true) : Enum.ToObject(type, value)
                : Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }

        /// <summary>The scalar values of a stored row copied onto a new instance, its collections left for the chunk to set.</summary>
        /// <param name="entity">The row's entity.</param>
        /// <param name="source">The stored row.</param>
        /// <returns>The copy.</returns>
        private static object Copy(EntityMetadata entity, object source)
        {
            object copy = Activator.CreateInstance(source.GetType())!;
            foreach (PropertyMetadata property in entity.Properties)
            {
                property.Setter(copy, property.Getter(source));
            }

            return copy;
        }

        /// <summary>A new row of an entity, active when the entity is activatable, its id the given one.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="id">The id: a temporary id, or zero.</param>
        /// <returns>The row.</returns>
        private static object Create(EntityMetadata entity, long id)
        {
            object row = Activator.CreateInstance(entity.ClrType)!;
            if (id != 0)
            {
                entity.Key.Setter(row, As(id, entity.Key.ClrType));
            }

            // Spec 0019 §9.1: every imported row is created active; deactivation is the action, never the sheet.
            if (entity.IsActivatable && entity.Properties.FirstOrDefault(static property => property.Name == ActiveProperty) is PropertyMetadata active)
            {
                active.Setter(row, true);
            }

            return row;
        }

        /// <summary>A typed list of child rows, as a collection property holds them.</summary>
        /// <param name="collection">The collection.</param>
        /// <param name="rows">The rows.</param>
        /// <returns>The list.</returns>
        private static IList TypedList(ChildCollectionMetadata collection, IEnumerable<object> rows)
        {
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(collection.Child.ClrType))!;
            foreach (object row in rows)
            {
                _ = list.Add(row);
            }

            return list;
        }

        /// <summary>A chunk's lookups as their rows answered them, by target and key.</summary>
        private sealed class Lookups
        {
            /// <summary>Each target and key's ids, by normal value.</summary>
            private readonly Dictionary<(string Target, string Key), Dictionary<object, List<long>>> _found = [];

            /// <summary>
            ///     Each target and key's stored text values that matched no value the chunk asked for,
            ///     folded loosely: the rows a collation equivalence found that the codec cannot tell.
            /// </summary>
            private readonly Dictionary<(string Target, string Key), HashSet<string>> _unattributed = [];

            /// <summary>Reads the lookups' rows: each a key and an id, or an id alone for a lookup by identity.</summary>
            /// <param name="lookupRows">Each lookup with the rows it read.</param>
            public Lookups(IReadOnlyDictionary<ExcelQuery, IReadOnlyList<IReadOnlyList<object?>>> lookupRows)
            {
                // Each query is read by its content, so the map need not hold the session's own instances.
                foreach ((ExcelQuery query, IReadOnlyList<IReadOnlyList<object?>> rows) in lookupRows)
                {
                    if (query.SelectPaths.Count == 0 || query.RestrictionValues is null || rows is null)
                    {
                        continue;
                    }

                    (string, string) name = (query.RootEntity, query.SelectPaths[0]);
                    HashSet<object> asked = new(query.RestrictionValues.Select(ImportKeys.Normalize).OfType<object>(), ImportKeys.Comparer);
                    if (!_found.TryGetValue(name, out Dictionary<object, List<long>>? found))
                    {
                        found = new Dictionary<object, List<long>>(ImportKeys.Comparer);
                        _found[name] = found;
                    }

                    foreach (IReadOnlyList<object?> row in rows)
                    {
                        if (row is not { Count: > 0 } || row[^1] is not object stored)
                        {
                            continue;
                        }

                        // --- A stored row is matched to the value it was asked for, after the same normalisation ---
                        long id = Convert.ToInt64(stored, CultureInfo.InvariantCulture);
                        object? value = row.Count == 1 ? id : ImportKeys.Normalize(row[0]);
                        if (value is null || !asked.Contains(value))
                        {
                            // Only text has a looser form to attribute it by; another value hints at nothing.
                            if (value is string text)
                            {
                                if (!_unattributed.TryGetValue(name, out HashSet<string>? loose))
                                {
                                    loose = new HashSet<string>(StringComparer.Ordinal);
                                    _unattributed[name] = loose;
                                }

                                _ = loose.Add(Fold(text));
                            }

                            continue;
                        }

                        if (!found.TryGetValue(value, out List<long>? ids))
                        {
                            ids = [];
                            found[value] = ids;
                        }

                        if (!ids.Contains(id))
                        {
                            ids.Add(id);
                        }
                    }
                }
            }

            /// <summary>The ids a value names.</summary>
            /// <param name="target">The target entity.</param>
            /// <param name="key">The key property.</param>
            /// <param name="value">The normal value.</param>
            /// <returns>The ids; empty for none.</returns>
            public List<long> Find(EntityMetadata target, PropertyMetadata key, object value)
            {
                return _found.TryGetValue((target.Name, key.Name), out Dictionary<object, List<long>>? found) && found.TryGetValue(value, out List<long>? ids)
                    ? ids
                    : [];
            }

            /// <summary>
            ///     Whether a stored row of a target and key that matched no value asked for matches this
            ///     one loosely: a collation equivalence the codec cannot tell, so the value hints at it.
            /// </summary>
            /// <param name="target">The target entity.</param>
            /// <param name="key">The key property.</param>
            /// <param name="value">The normal value the cell asked for.</param>
            /// <returns>True when one does.</returns>
            public bool Unattributed(EntityMetadata target, PropertyMetadata key, object value)
            {
                return value is string text
                    && _unattributed.TryGetValue((target.Name, key.Name), out HashSet<string>? loose)
                    && loose.Contains(Fold(text));
            }

            /// <summary>
            ///     A text's loose form, as a case- and accent-insensitive collation compares it: the
            ///     compatibility decomposition without its combining marks, upper-cased invariantly.
            /// </summary>
            /// <param name="text">The text.</param>
            /// <returns>The loose form.</returns>
            private static string Fold(string text)
            {
                string decomposed = text.Normalize(NormalizationForm.FormKD);
                StringBuilder kept = new(decomposed.Length);
                foreach (char character in decomposed)
                {
                    if (CharUnicodeInfo.GetUnicodeCategory(character) is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark))
                    {
                        _ = kept.Append(character);
                    }
                }

                return kept.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
            }
        }

        /// <summary>The values one lookup restricts on, distinct after normalisation, in first-seen order.</summary>
        /// <param name="target">The target entity.</param>
        /// <param name="key">The key property.</param>
        private sealed class LookupGroup(EntityMetadata target, PropertyMetadata key)
        {
            /// <summary>The target entity.</summary>
            public EntityMetadata Target { get; } = target;

            /// <summary>The key property.</summary>
            public PropertyMetadata Key { get; } = key;

            /// <summary>The values, in first-seen order.</summary>
            public List<object> Values { get; } = [];

            /// <summary>The values seen.</summary>
            public HashSet<object> Seen { get; } = new(ImportKeys.Comparer);
        }

        /// <summary>A parent row's identity in a chunk.</summary>
        /// <param name="IsNew">Whether the row is inserted.</param>
        /// <param name="Id">The id the payload carries: the stored id, or the temporary id of a new row.</param>
        /// <param name="Stored">The stored row an existing row overlays; null for a new row.</param>
        private readonly record struct Identity(bool IsNew, long Id, object? Stored);

        /// <summary>The state of one chunk's resolution: its identities, its payload paths' cells, its errors and its counts.</summary>
        /// <param name="session">The session.</param>
        /// <param name="lookups">The chunk's lookups.</param>
        /// <param name="committedNewIds">The ids an earlier chunk's new rows were given.</param>
        /// <param name="fromRow">The chunk's first position.</param>
        /// <param name="toRow">The chunk's last position.</param>
        private sealed class ChunkBuilder(ImportSession session, Lookups lookups, IReadOnlyDictionary<long, long> committedNewIds, int fromRow, int toRow)
        {
            /// <summary>Each parent row's identity, by position.</summary>
            private readonly Dictionary<int, Identity> _identities = [];

            /// <summary>The stored ids the chunk's rows claimed, so two rows naming one are refused.</summary>
            private readonly HashSet<long> _claimed = [];

            /// <summary>The child sheets already reported incomplete.</summary>
            private readonly HashSet<string> _incomplete = new(StringComparer.Ordinal);

            /// <summary>The next temporary id, counting down from -1.</summary>
            private long _temporary;

            /// <summary>The counts of the chunk's child rows inserted, updated and dropped.</summary>
            private int _childrenInserted;

            /// <summary>The child rows updated.</summary>
            private int _childrenUpdated;

            /// <summary>The existing child rows dropped.</summary>
            private int _childrenDeleted;

            /// <summary>Each payload path's cell.</summary>
            public Dictionary<ValidationPath, ExcelCell> Map { get; } = [];

            /// <summary>The errors found.</summary>
            public List<ImportError> Errors { get; } = [];

            /// <summary>Settles a parent row's identity: new under the chunk's next temporary id, or the stored row its key names.</summary>
            /// <param name="row">The parent row.</param>
            /// <param name="stored">The stored rows, by id.</param>
            public void Identify(ImportRow row, Dictionary<long, object> stored)
            {
                ImportSessionShape shape = session.Shape;
                Identity identity = New();
                if (shape is { Mode: not ImportMode.Insert, KeyColumn: ImportColumn key })
                {
                    identity = shape.RowKey == ImportPaths.Id ? ById(row, key, stored) : ByNaturalKey(row, key, stored);
                }

                // --- One stored row, one sheet row ---
                if (!identity.IsNew && !_claimed.Add(identity.Id))
                {
                    Raise(row, key: shape.KeyColumn, ExcelErrorCodes.ImportDuplicateRowKey, new() { ["value"] = ImportKeys.Shown(shape.KeyColumn is null ? null : row[shape.KeyColumn]) });
                    identity = New();
                }

                _identities[row.Position] = identity;
            }

            /// <summary>
            ///     Refuses each deferred cycle the chunk holds whole when every row on it is new: a
            ///     stored row on it already has its id, so the new rows bind to it in any order.
            /// </summary>
            /// <param name="cycles">The session's deferred cycles.</param>
            public void RefuseCycles(IReadOnlyList<ImportCycle> cycles)
            {
                // A cycle split by a chunk boundary is checked in no chunk. The chunking ends a chunk
                // only where no row names a row after it, so it keeps a cycle whole unless the save's
                // ceilings cannot hold it; a split cycle of new rows then fails on its forward reference.
                foreach (ImportCycle cycle in cycles)
                {
                    if (cycle.Rows.Any(row => row.Position < fromRow || row.Position > toRow) || cycle.Rows.Any(row => !_identities[row.Position].IsNew))
                    {
                        continue;
                    }

                    long[] cycleRows = [.. cycle.Rows.Select(static row => (long)row.Row)];
                    for (int i = 0; i < cycle.Rows.Count; i++)
                    {
                        Raise(cycle.Referrers[i], cycle.Columns[i], ExcelErrorCodes.ImportParentCycle, new()
                        {
                            ["rows"] = cycleRows,
                            ["path"] = cycle.Columns[i].Path.Text,
                        });
                    }
                }
            }

            /// <summary>Builds a parent row's entity and the cells of its payload paths.</summary>
            /// <param name="row">The parent row.</param>
            /// <returns>The entity and the row's facts.</returns>
            public (object Entity, ImportChunkRow Facts) Build(ImportRow row)
            {
                ImportSessionShape shape = session.Shape;
                Identity identity = _identities[row.Position];
                EntityMetadata entity = shape.Entity;
                ValidationPath path = ValidationPath.Root.Index(row.Position - fromRow);
                object target = identity.Stored is object stored ? Copy(entity, stored) : Create(entity, identity.Id);

                // --- The expected stamp: the sheet's when it is the one used and the cell is filled, else the stored one ---
                bool sheetStamp = false;
                ImportColumn? stamp = row.Sheet.Stamp;
                if (identity.Stored is not null && shape.UseStamps && stamp is not null && row[stamp] is DateTimeOffset expected)
                {
                    stamp.Path.Property.Setter(target, As(expected, stamp.Path.Property.ClrType));
                    sheetStamp = true;
                }

                Overlay(row, target, identity.Stored, path);
                SetChildren(row, target, identity.Stored, path);
                return (target, new ImportChunkRow(row.Position, row.Sheet.Name, row.Row, identity.Id, identity.IsNew, sheetStamp, shape.KeyColumn?.Letter, stamp?.Letter));
            }

            /// <summary>The facts of the chunk's save.</summary>
            /// <param name="rows">The parent rows' facts.</param>
            /// <returns>The facts.</returns>
            public ImportChunkFacts Facts(List<ImportChunkRow> rows)
            {
                return new ImportChunkFacts(rows, rows.Count(static row => row.IsNew), rows.Count(static row => !row.IsNew), _childrenInserted, _childrenUpdated, _childrenDeleted);
            }

            /// <summary>A new row's identity, with the chunk's next temporary id.</summary>
            /// <returns>The identity.</returns>
            private Identity New()
            {
                return new Identity(IsNew: true, --_temporary, Stored: null);
            }

            /// <summary>
            ///     A row keyed by its identity: a positive one names a stored row, which must have been
            ///     read; a blank or negative one is new in an upsert and names nothing in an update.
            /// </summary>
            /// <param name="row">The parent row.</param>
            /// <param name="key">The identity column.</param>
            /// <param name="stored">The stored rows.</param>
            /// <returns>The identity.</returns>
            private Identity ById(ImportRow row, ImportColumn key, Dictionary<long, object> stored)
            {
                long? id = row.Id;
                if (id is > 0 && stored.TryGetValue(id.Value, out object? found))
                {
                    return new Identity(IsNew: false, id.Value, found);
                }

                // A vanished row is never inserted again (spec 0019 §9.2).
                if (id is > 0 || session.Shape.Mode == ImportMode.Update)
                {
                    NotFound(row, key);
                }

                return New();
            }

            /// <summary>
            ///     A row keyed by a natural key: the one stored row its lookup found, which must have been
            ///     read; none is new in an upsert and names nothing in an update; several are ambiguous.
            /// </summary>
            /// <param name="row">The parent row.</param>
            /// <param name="key">The natural key's column.</param>
            /// <param name="stored">The stored rows.</param>
            /// <returns>The identity.</returns>
            private Identity ByNaturalKey(ImportRow row, ImportColumn key, Dictionary<long, object> stored)
            {
                object? value = ImportKeys.Normalize(row[key]);
                List<long> ids = value is null ? [] : lookups.Find(session.Shape.Entity, key.Path.Property, value);
                switch (ids.Count)
                {
                    case 1 when stored.TryGetValue(ids[0], out object? found):
                        return new Identity(IsNew: false, ids[0], found);
                    case 0 when session.Shape.Mode == ImportMode.Upsert:
                        return New();
                    case > 1:
                        Raise(row, key, ExcelErrorCodes.ImportReferenceAmbiguous, new()
                        {
                            ["entity"] = session.Shape.Entity.Name,
                            ["key"] = key.Path.Property.Name,
                            ["value"] = ImportKeys.Shown(row[key]),
                            ["count"] = ids.Count,
                        });
                        return New();
                    default:
                        NotFound(row, key);
                        return New();
                }
            }

            /// <summary>Raises a row its key names nothing readable for.</summary>
            /// <param name="row">The row.</param>
            /// <param name="key">The key's column.</param>
            private void NotFound(ImportRow row, ImportColumn key)
            {
                Raise(row, key, ExcelErrorCodes.ImportRowNotFound, new()
                {
                    ["key"] = key.Path.Kind == ImportPathKind.Id ? ImportPaths.Id : key.Path.Property.Name,
                    ["value"] = ImportKeys.Shown(row[key]),
                });
            }

            /// <summary>
            ///     Lays a row's columns over its entity, placing each: the scalars and references it maps,
            ///     a write-once value of a stored row refused when it changes.
            /// </summary>
            /// <param name="row">The row.</param>
            /// <param name="target">The row's entity.</param>
            /// <param name="stored">The stored row it overlays; null for a new row.</param>
            /// <param name="path">The row's payload path.</param>
            private void Overlay(ImportRow row, object target, object? stored, ValidationPath path)
            {
                Map[path] = new ExcelCell(row.Sheet.Name, row.Row, Column: null);
                foreach (ImportColumn column in row.Sheet.Columns)
                {
                    // --- The column's cell, under the property it writes and, for a reference, its navigation ---
                    ExcelCell cell = new(row.Sheet.Name, row.Row, column.Letter);
                    Map[path.Property(column.Path.Property.Name)] = cell;
                    if (column.Path.Reference?.Navigation is string navigation && navigation != column.Path.Property.Name)
                    {
                        Map[path.Property(navigation)] = cell;
                    }

                    if (column.Path.Kind is not (ImportPathKind.Property or ImportPathKind.Reference))
                    {
                        continue;
                    }

                    // --- The value: the decoded cell, or the id a reference binds to ---
                    PropertyMetadata property = column.Path.Property;
                    if (!TryValue(row, column, out object? value))
                    {
                        continue;
                    }

                    object? converted = As(value, property.ClrType);
                    if (converted is null && Nullable.GetUnderlyingType(property.ClrType) is null && property.ClrType.IsValueType)
                    {
                        // A blank cell is null, which a value of this type cannot hold: the save's own refusal, raised here.
                        Raise(row, column, ValidationCodes.Required, new() { ["property"] = property.Name });
                        continue;
                    }

                    // Spec 0019 §9.3: a write-once value may be restated but never changed.
                    if (stored is not null && property.Ownership == PropertyOwnership.WriteOnce && !Equals(property.Getter(stored), converted))
                    {
                        Raise(row, column, ExcelErrorCodes.ImportWriteOnceChanged, new() { ["path"] = column.Path.Text });
                        continue;
                    }

                    property.Setter(target, converted);
                }
            }

            /// <summary>A column's value for its row: the decoded scalar, or the id a reference cell binds to.</summary>
            /// <param name="row">The row.</param>
            /// <param name="column">The column.</param>
            /// <param name="value">The value; null for a blank cell.</param>
            /// <returns>False when the cell binds to nothing, which is raised.</returns>
            private bool TryValue(ImportRow row, ImportColumn column, out object? value)
            {
                value = row[column];
                if (column.Path.Kind != ImportPathKind.Reference || value is null)
                {
                    return true;
                }

                // --- A row of the chunk, by the id it carries in the payload ---
                if (InChunk(row, column, fromRow, toRow) is ImportRow named)
                {
                    value = _identities.TryGetValue(named.Position, out Identity identity) ? identity.Id : null;
                    return value is not null;
                }

                // --- A new row an earlier chunk committed, named by its negative sheet identity ---
                EntityMetadata targetEntity = column.Path.Reference!.Target;
                PropertyMetadata key = column.Path.Key!;
                object? normal = ImportKeys.Normalize(value);
                if (IsTargetKey(column) && normal is long number && number <= 0)
                {
                    if (column.ToStackEntity && committedNewIds.TryGetValue(number, out long committed))
                    {
                        value = committed;
                        return true;
                    }

                    ReferenceNotFound(row, column, collation: false);
                    return false;
                }

                // --- Else the lookup: exactly one row binds ---
                object? asked = LookedUp(row, column, fromRow, toRow);
                List<long> ids = asked is not null ? lookups.Find(targetEntity, key, asked) : [];
                switch (ids.Count)
                {
                    case 1:
                        value = ids[0];
                        return true;
                    case 0:
                        ReferenceNotFound(row, column, asked is not null && lookups.Unattributed(targetEntity, key, asked));
                        return false;
                    default:
                        Raise(row, column, ExcelErrorCodes.ImportReferenceAmbiguous, new()
                        {
                            ["entity"] = targetEntity.Name,
                            ["key"] = key.Name,
                            ["value"] = ImportKeys.Shown(row[column]),
                            ["count"] = ids.Count,
                        });
                        return false;
                }
            }

            /// <summary>Raises a reference cell that names no readable row.</summary>
            /// <param name="row">The row.</param>
            /// <param name="column">The reference column.</param>
            /// <param name="collation">Whether a stored row that matched no value asked for matches this one loosely, which hints at a collation equivalence.</param>
            private void ReferenceNotFound(ImportRow row, ImportColumn column, bool collation)
            {
                Dictionary<string, object?> arguments = new(StringComparer.Ordinal)
                {
                    ["entity"] = column.Path.Reference!.Target.Name,
                    ["key"] = column.Path.Key!.Name,
                    ["value"] = ImportKeys.Shown(row[column]),
                    ["collation"] = collation,
                };

                Raise(row, column, ExcelErrorCodes.ImportReferenceNotFound, arguments);
            }

            /// <summary>
            ///     Sets each of a row's collections: null when its sheet is absent, else its sheet's rows
            ///     under the row, each matched to a stored child by identity, by natural key, or not at
            ///     all, the collection then replaced.
            /// </summary>
            /// <param name="row">The owner row.</param>
            /// <param name="target">The owner's entity.</param>
            /// <param name="stored">The stored owner; null for a new one.</param>
            /// <param name="path">The owner's payload path.</param>
            private void SetChildren(ImportRow row, object target, object? stored, ValidationPath path)
            {
                foreach (ChildCollectionMetadata collection in row.Sheet.Entity.Children)
                {
                    // --- An absent sheet leaves the collection untouched ---
                    string collectionPath = row.Sheet.Path.Length == 0 ? collection.Property : $"{row.Sheet.Path}.{collection.Property}";
                    ImportSheet? sheet = row.Sheet.Children.FirstOrDefault(child => string.Equals(child.Path, collectionPath, StringComparison.Ordinal));
                    if (sheet is null)
                    {
                        RowRules.SetCollection(target, collection, null);
                        continue;
                    }

                    // --- How the sheet's rows find the stored children: identity, natural key, or none ---
                    IReadOnlyList<object> storedChildren = stored is null ? [] : collection.Getter(stored) ?? [];
                    bool byId = stored is not null && session.Shape is { SameSource: true, Mode: not ImportMode.Insert } && sheet.Id is not null;
                    ImportColumn? natural = stored is null || byId ? null : NaturalKeyColumn(sheet);
                    Dictionary<long, object> storedById = [];
                    Dictionary<object, object> storedByKey = new(ImportKeys.Comparer);
                    foreach (object child in storedChildren)
                    {
                        storedById[IdOf(collection.Child, child)] = child;
                        if (natural is not null && ImportKeys.Normalize(natural.Path.Property.Getter(child)) is object key)
                        {
                            _ = storedByKey.TryAdd(key, child);
                        }
                    }

                    if (stored is not null && !byId && natural is null && storedChildren.Count > 0)
                    {
                        CheckComplete(sheet);
                    }

                    // --- Each sheet row: a matched child overlaid like a parent, else a new one ---
                    List<object> children = [];
                    HashSet<object> kept = new(ReferenceEqualityComparer.Instance);
                    foreach (ImportRow childRow in row.ChildrenOn(sheet))
                    {
                        object? match = null;
                        if (byId && childRow.Id is long id and > 0)
                        {
                            if (!storedById.TryGetValue(id, out match))
                            {
                                NotFound(childRow, sheet.Id!);
                            }
                        }
                        else if (natural is not null && ImportKeys.Normalize(childRow[natural]) is object key && storedByKey.TryGetValue(key, out object? named) && !kept.Contains(named))
                        {
                            match = named;
                        }

                        if (match is not null && !kept.Add(match))
                        {
                            match = null;
                        }

                        ValidationPath childPath = path.Property(collection.Property).Index(children.Count);
                        object entity = match is null ? Create(collection.Child, 0) : Copy(collection.Child, match);
                        Overlay(childRow, entity, match, childPath);
                        SetChildren(childRow, entity, match, childPath);
                        children.Add(entity);
                        if (match is null)
                        {
                            _childrenInserted++;
                        }
                        else
                        {
                            _childrenUpdated++;
                        }
                    }

                    // An unmatched stored child is dropped, and deleted with its descendants, which are not counted.
                    _childrenDeleted += storedChildren.Count(child => !kept.Contains(child));
                    RowRules.SetCollection(target, collection, TypedList(collection, children));
                }
            }

            /// <summary>The column of a child sheet that holds its entity's natural key, any slot of a multilingual one; null for none.</summary>
            /// <param name="sheet">The child sheet.</param>
            /// <returns>The column.</returns>
            private static ImportColumn? NaturalKeyColumn(ImportSheet sheet)
            {
                return sheet.Entity.NaturalKeys.Count == 0
                    ? null
                    : sheet.Columns.FirstOrDefault(column => column.Path.Kind == ImportPathKind.Property
                        && (column.Path.Property.Name == sheet.Entity.NaturalKeys[0].Property.Name
                            || (ImportPaths.Slot(sheet.Entity, column.Path.Property.Name) is (MultilingualGroup group, _) && group.Primary.Name == sheet.Entity.NaturalKeys[0].Property.Name)));
            }

            /// <summary>
            ///     Refuses a child sheet whose collection is replaced while it lacks an editable column of
            ///     the child, or a descendant collection's sheet: replacing deletes what the sheet cannot
            ///     restate. Raised once per sheet.
            /// </summary>
            /// <param name="sheet">The child sheet.</param>
            private void CheckComplete(ImportSheet sheet)
            {
                if (_incomplete.Contains(sheet.Path))
                {
                    return;
                }

                // --- Every editable column of the child, a multilingual group's in each tenant language's slot ---
                EntityMetadata child = sheet.Entity;
                ChildCollectionMetadata collection = sheet.Collection!.Collection;
                int languages = session.Shape.Context.TenantLanguages.Count;
                HashSet<string> written = new(sheet.Columns.Select(static column => column.Path.Property.Name), StringComparer.Ordinal);
                List<string> missingColumns = [];
                foreach (PropertyMetadata property in child.Properties)
                {
                    if (!ExportPlanner.IsEditable(child, property, collection) || (ImportPaths.Slot(child, property.Name) is (_, int slot) && slot > languages))
                    {
                        continue;
                    }

                    if (!written.Contains(property.Name))
                    {
                        missingColumns.Add(child.References.FirstOrDefault(reference => reference.ForeignKey.Name == property.Name)?.Navigation ?? property.Name);
                    }
                }

                // --- Every collection below it, each with its sheet ---
                string prefix = sheet.Path + ".";
                List<string> missingCollections = [.. ImportPaths.Collections(session.Shape.Entity)
                    .Where(candidate => candidate.Path.StartsWith(prefix, StringComparison.Ordinal))
                    .Where(candidate => !session.Sheets.Any(present => string.Equals(present.Path, candidate.Path, StringComparison.Ordinal)))
                    .Select(static candidate => candidate.Path)];

                if (missingColumns.Count == 0 && missingCollections.Count == 0)
                {
                    return;
                }

                _ = _incomplete.Add(sheet.Path);
                Errors.Add(new ImportError(sheet.Name, Row: null, Column: null, Header: null, Property: null, ExcelErrorCodes.ImportIncompleteChildSheet, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["collection"] = sheet.Path,
                    ["missingColumns"] = missingColumns.ToArray(),
                    ["missingCollections"] = missingCollections.ToArray(),
                }));
            }

            /// <summary>Raises an error at a row's cell.</summary>
            /// <param name="row">The row.</param>
            /// <param name="key">The column; null for the row alone.</param>
            /// <param name="code">The code.</param>
            /// <param name="arguments">The arguments.</param>
            private void Raise(ImportRow row, ImportColumn? key, string code, Dictionary<string, object?> arguments)
            {
                Errors.Add(new ImportError(row.Sheet.Name, row.Row, key?.Letter, key?.Header, key?.Path.Property.Name, code, new Dictionary<string, object?>(arguments, StringComparer.Ordinal)));
            }
        }
    }
}
