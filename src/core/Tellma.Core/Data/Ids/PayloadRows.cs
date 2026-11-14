// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Data.Ids
{
    /// <summary>One row of a save payload, with the row and collection that hold it.</summary>
    /// <param name="Entity">The row's entity.</param>
    /// <param name="Row">The row.</param>
    /// <param name="Owner">The row whose collection holds it; null for a root.</param>
    /// <param name="OwnerEntity">The owner's entity; null for a root.</param>
    /// <param name="Collection">The collection that holds it; null for a root.</param>
    internal readonly record struct PayloadRow(EntityMetadata Entity, object Row, object? Owner, EntityMetadata? OwnerEntity, ChildCollectionMetadata? Collection)
    {
        /// <summary>The row's id, widened to a <see cref="long" />.</summary>
        public long Id => PayloadRows.KeyOf(Entity, Row);
    }

    /// <summary>
    ///     Walks a save payload in topological order: the roots, then each child collection in
    ///     metadata order, every collection's rows followed by their own collections, recursively.
    /// </summary>
    internal static class PayloadRows
    {
        /// <summary>Every row of a payload, in topological order. A null collection is not supplied and holds nothing.</summary>
        /// <param name="root">The roots' entity.</param>
        /// <param name="rows">The roots.</param>
        /// <returns>The rows.</returns>
        /// <exception cref="ArgumentException">A row is null or not an instance of its entity.</exception>
        public static List<PayloadRow> Enumerate(EntityMetadata root, IReadOnlyList<object> rows)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(rows);

            List<PayloadRow> result = [];
            List<PayloadRow> roots = new(rows.Count);
            foreach (object? row in rows)
            {
                roots.Add(new PayloadRow(root, Checked(root, row), Owner: null, OwnerEntity: null, Collection: null));
            }

            result.AddRange(roots);
            Descend(root, roots, result);
            return result;
        }

        /// <summary>A row's id, widened to a <see cref="long" />.</summary>
        /// <param name="entity">The row's entity.</param>
        /// <param name="row">The row.</param>
        /// <returns>The id.</returns>
        public static long KeyOf(EntityMetadata entity, object row)
        {
            return Convert.ToInt64(entity.Key.Getter(row), CultureInfo.InvariantCulture);
        }

        /// <summary>Writes an id into a key or foreign-key property, converted to the property's type.</summary>
        /// <param name="property">The property, of an <see cref="int" /> or <see cref="long" /> type, nullable or not.</param>
        /// <param name="row">The row.</param>
        /// <param name="id">The id.</param>
        public static void SetId(PropertyMetadata property, object row, long id)
        {
            Type type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            property.Setter(row, Convert.ChangeType(id, type, CultureInfo.InvariantCulture));
        }

        /// <summary>The value of a foreign-key property, widened, or null when it holds none.</summary>
        /// <param name="property">The property.</param>
        /// <param name="row">The row.</param>
        /// <returns>The value.</returns>
        public static long? ValueOf(PropertyMetadata property, object row)
        {
            return property.Getter(row) is object value ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : null;
        }

        /// <summary>Adds the rows of each collection of a level of owners, then theirs, recursively.</summary>
        /// <param name="entity">The owners' entity.</param>
        /// <param name="owners">The owners.</param>
        /// <param name="result">The rows walked so far.</param>
        private static void Descend(EntityMetadata entity, List<PayloadRow> owners, List<PayloadRow> result)
        {
            foreach (ChildCollectionMetadata collection in entity.Children)
            {
                List<PayloadRow> children = [];
                foreach (PayloadRow owner in owners)
                {
                    if (collection.Getter(owner.Row) is not IReadOnlyList<object> list)
                    {
                        continue;
                    }

                    foreach (object? child in list)
                    {
                        children.Add(new PayloadRow(collection.Child, Checked(collection.Child, child), owner.Row, entity, collection));
                    }
                }

                result.AddRange(children);
                Descend(collection.Child, children, result);
            }
        }

        /// <summary>Refuses a null row or a row of another type.</summary>
        /// <param name="entity">The row's expected entity.</param>
        /// <param name="row">The row.</param>
        /// <returns>The row.</returns>
        private static object Checked(EntityMetadata entity, object? row)
        {
            return row is null
                ? throw new ArgumentException($"A save payload of {entity.Name} holds a null row.", nameof(row))
                : entity.ClrType.IsInstanceOfType(row)
                    ? row
                    : throw new ArgumentException($"A save payload holds a {row.GetType().Name} where a {entity.ClrType.Name} row of {entity.Name} belongs.", nameof(row));
        }
    }
}
