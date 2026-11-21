// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.EntityFrameworkCore.TableTypes;

namespace Tellma.Core.Data.Metadata
{
    /// <summary>
    ///     The two fingerprints of the tenant model: an entity's schema fingerprint, over the shape an
    ///     import may write, and the storage fingerprint, over the relational model every tenant database
    ///     must carry. Both are lower-case hex SHA-256 of an unambiguous token stream, so equal inputs give
    ///     equal values on every operating system and process.
    /// </summary>
    internal static class Fingerprints
    {
        /// <summary>
        ///     The schema fingerprint of an entity: for every editable and write-once property, in property
        ///     order, its name, column, store type, nullability and ownership; then, for every child
        ///     collection, its name and the child's own schema fingerprint, which covers the child's shape
        ///     and, recursively, its children's.
        /// </summary>
        /// <param name="entity">The entity's metadata; its children are read, so they must be built.</param>
        /// <param name="storeTypeOf">The store type of each of the entity's properties, by property name.</param>
        /// <returns>The fingerprint.</returns>
        public static string Schema(EntityMetadata entity, IReadOnlyDictionary<string, string> storeTypeOf)
        {
            using FingerprintWriter writer = new();

            // --- The entity's own writable columns ---
            foreach (PropertyMetadata property in entity.Properties)
            {
                if (property.Ownership is not (PropertyOwnership.Editable or PropertyOwnership.WriteOnce))
                {
                    continue;
                }

                writer.Add("property")
                    .Add(property.Name)
                    .Add(property.Column)
                    .Add(storeTypeOf[property.Name])
                    .Add(property.IsNullable)
                    .Add(property.Ownership.ToString());
            }

            // --- Each child collection's shape, through the child's own fingerprint ---
            foreach (ChildCollectionMetadata collection in entity.Children)
            {
                writer.Add("children").Add(collection.Property).Add(collection.Child.SchemaFingerprint);
            }

            return writer.Finish();
        }

        /// <summary>
        ///     The storage fingerprint of a model: every table by schema and name with, for each column, its
        ///     name, store type, nullability, default and computed definition, and each index's name; every
        ///     sequence by schema and name; and every table type by its physical name. Each list is sorted
        ///     ordinally, so the value depends on what the database must hold, never on the order classes,
        ///     properties or contributions were declared in.
        /// </summary>
        /// <remarks>
        ///     Table types are included because a version of a type the running model binds that the
        ///     database lacks fails every save of its entity, exactly like a missing column; computed
        ///     definitions because a column that stopped being computed is written by the running model.
        /// </remarks>
        /// <param name="model">The runtime model.</param>
        /// <returns>The fingerprint.</returns>
        public static string Storage(IModel model)
        {
            using FingerprintWriter writer = new();
            IRelationalModel relational = model.GetRelationalModel();

            // --- Tables, their columns and their indexes ---
            foreach (ITable table in relational.Tables
                .OrderBy(static table => table.Schema, StringComparer.Ordinal)
                .ThenBy(static table => table.Name, StringComparer.Ordinal))
            {
                _ = writer.Add("table").Add(table.Schema).Add(table.Name);

                foreach (IColumn column in table.Columns.OrderBy(static column => column.Name, StringComparer.Ordinal))
                {
                    _ = writer.Add("column")
                        .Add(column.Name)
                        .Add(column.StoreType)
                        .Add(column.IsNullable)
                        .Add(DefaultOf(column))
                        .Add(column.ComputedColumnSql)
                        .Add(column.ComputedColumnSql is null ? null : column.IsStored?.ToString(CultureInfo.InvariantCulture));
                }

                foreach (ITableIndex index in table.Indexes.OrderBy(static index => index.Name, StringComparer.Ordinal))
                {
                    _ = writer.Add("index").Add(index.Name);
                }
            }

            // --- Sequences ---
            foreach (ISequence sequence in relational.Sequences
                .OrderBy(static sequence => sequence.Schema, StringComparer.Ordinal)
                .ThenBy(static sequence => sequence.Name, StringComparer.Ordinal))
            {
                _ = writer.Add("sequence").Add(sequence.Schema).Add(sequence.Name);
            }

            // --- Table types, by the versioned name a binding addresses ---
            foreach (string tableType in model.GetTableTypes()
                .Select(static type => $"{type.Schema}.{type.PhysicalName}")
                .Order(StringComparer.Ordinal))
            {
                _ = writer.Add("type").Add(tableType);
            }

            return writer.Finish();
        }

        /// <summary>
        ///     A column's default as one token: the SQL default's text, a constant's type and invariant text,
        ///     or <see langword="null" /> when the column has no default.
        /// </summary>
        /// <param name="column">The column.</param>
        /// <returns>The token.</returns>
        private static string? DefaultOf(IColumn column)
        {
            return column.DefaultValueSql is string sql
                ? $"sql:{sql}"
                : column.TryGetDefaultValue(out object? value)
                    ? value is null ? "value:null" : $"value:{value.GetType().FullName}:{Convert.ToString(value, CultureInfo.InvariantCulture)}"
                    : null;
        }
    }

    /// <summary>
    ///     Writes tokens into a SHA-256 hash unambiguously: each as its UTF-8 length, four bytes little
    ///     endian, and its bytes; a null token as the length -1. No two token sequences share a stream.
    /// </summary>
    internal sealed class FingerprintWriter : IDisposable
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        /// <summary>Adds a text token, or a null one.</summary>
        /// <param name="token">The token.</param>
        /// <returns>This writer.</returns>
        public FingerprintWriter Add(string? token)
        {
            Span<byte> length = stackalloc byte[4];
            if (token is null)
            {
                BinaryPrimitives.WriteInt32LittleEndian(length, -1);
                _hash.AppendData(length);
                return this;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(token);
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            _hash.AppendData(length);
            _hash.AppendData(bytes);
            return this;
        }

        /// <summary>Adds a flag as the token <c>1</c> or <c>0</c>.</summary>
        /// <param name="flag">The flag.</param>
        /// <returns>This writer.</returns>
        public FingerprintWriter Add(bool flag)
        {
            return Add(flag ? "1" : "0");
        }

        /// <summary>The hash of every token added, as lower-case hex.</summary>
        /// <returns>The fingerprint.</returns>
        public string Finish()
        {
            return Convert.ToHexStringLower(_hash.GetHashAndReset());
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _hash.Dispose();
        }
    }
}
