// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;
using System.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Data.Conventions;
using Tellma.Core.EntityFrameworkCore.TableTypes;

namespace Tellma.Core.Data.TableTypes
{
    /// <summary>
    ///     How the rows of one CLR type bind to one table type: the type's physical name, the
    ///     <see cref="SqlMetaData" /> of its columns in the definition's order, and one compiled
    ///     accessor per column, matched by column name. Built once by <see cref="TableTypeBinder" /> and
    ///     shared; immutable.
    /// </summary>
    /// <param name="rowType">The CLR type of the rows.</param>
    /// <param name="definition">The table type.</param>
    /// <param name="metadata">The columns' metadata, in the definition's order.</param>
    /// <param name="accessors">Each column's accessor, in the same order: the provider value of a row's column, or null.</param>
    [TableTypeBinder]
    internal sealed class TableTypeBinding(Type rowType, TableTypeDefinition definition, SqlMetaData[] metadata, Func<object, object?>[] accessors)
    {
        private readonly SqlMetaData[] _metadata = metadata;
        private readonly Func<object, object?>[] _accessors = accessors;

        /// <summary>The CLR type of the rows.</summary>
        public Type RowType { get; } = rowType;

        /// <summary>The table type.</summary>
        public TableTypeDefinition Definition { get; } = definition;

        /// <summary>The bracket-delimited, schema-qualified physical name the parameter's type is declared by.</summary>
        public string TypeName { get; } = definition.Schema is null
            ? ConventionModel.Delimit(definition.PhysicalName)
            : $"{ConventionModel.Delimit(definition.Schema)}.{ConventionModel.Delimit(definition.PhysicalName)}";

        /// <summary>The columns' metadata, in the definition's order.</summary>
        public IReadOnlyList<SqlMetaData> Metadata => _metadata;

        /// <summary>
        ///     The rows as records, created lazily: nothing is read from a row until the driver asks for
        ///     it, so a retry that sends the command again enumerates the rows again. One record per
        ///     enumeration is filled per row, as the driver reads each before asking for the next.
        /// </summary>
        /// <param name="rows">The rows, every one of <see cref="RowType" /> or a type derived from it.</param>
        /// <returns>The records.</returns>
        public IEnumerable<SqlDataRecord> Records(IEnumerable<object> rows)
        {
            ArgumentNullException.ThrowIfNull(rows);
            return Enumerate(rows);
        }

        /// <summary>
        ///     A structured parameter carrying the rows. An empty list binds as no value, which SQL
        ///     Server reads as an empty table: the driver refuses an empty enumeration of records.
        /// </summary>
        /// <param name="name">The parameter's name, with its leading <c>@</c>.</param>
        /// <param name="rows">The rows.</param>
        /// <returns>The parameter.</returns>
        public SqlParameter Parameter(string name, IReadOnlyCollection<object> rows)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            ArgumentNullException.ThrowIfNull(rows);

            return new SqlParameter(name, SqlDbType.Structured)
            {
                TypeName = TypeName,
                Value = rows.Count == 0 ? null : Records(rows),
            };
        }

        /// <summary>The lazy iterator behind <see cref="Records" />.</summary>
        /// <param name="rows">The rows.</param>
        /// <returns>The records.</returns>
        private IEnumerable<SqlDataRecord> Enumerate(IEnumerable<object> rows)
        {
            SqlDataRecord record = new(_metadata);
            foreach (object row in rows)
            {
                if (row is null || !RowType.IsInstanceOfType(row))
                {
                    throw new InvalidOperationException(
                        $"A row bound to table type {Definition.DisplayName} is {(row is null ? "null" : $"of type {row.GetType().Name}")}; every row must be a {RowType.Name}.");
                }

                // Every field is written for every row, so nothing of the previous row survives.
                for (int ordinal = 0; ordinal < _accessors.Length; ordinal++)
                {
                    object? value = _accessors[ordinal](row);
                    if (value is null)
                    {
                        record.SetDBNull(ordinal);
                    }
                    else
                    {
                        record.SetValue(ordinal, value);
                    }
                }

                yield return record;
            }
        }
    }
}
