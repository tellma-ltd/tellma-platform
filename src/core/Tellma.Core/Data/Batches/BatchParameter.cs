// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Data;

namespace Tellma.Core.Data.Batches
{
    /// <summary>
    ///     One parameter a batch's command carries. A <see cref="SqlParameter" /> belongs to one
    ///     command, so every attempt creates its own from this description.
    /// </summary>
    internal abstract class BatchParameter
    {
        /// <summary>Creates the parameter's description.</summary>
        /// <param name="name">The parameter name, with its leading <c>@</c>.</param>
        protected BatchParameter(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            Name = name;
        }

        /// <summary>The parameter name, with its leading <c>@</c>.</summary>
        public string Name { get; }

        /// <summary>The rows the parameter binds when it is table-valued; zero for a scalar.</summary>
        public virtual int Rows => 0;

        /// <summary>Creates the parameter for one command.</summary>
        /// <returns>The parameter.</returns>
        public abstract SqlParameter Create();

        /// <summary>Whether this parameter binds the same value as another of the same name.</summary>
        /// <param name="other">The other parameter.</param>
        /// <returns>True when binding either is binding both.</returns>
        public abstract bool BindsSame(BatchParameter other);
    }

    /// <summary>A scalar parameter, typed when it is described.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="type">The SQL type.</param>
    /// <param name="value">The value, or null for SQL <c>NULL</c>.</param>
    /// <param name="size">The size of a sized type, or null for the provider's.</param>
    internal sealed class ScalarParameter(string name, SqlDbType type, object? value, int? size = null) : BatchParameter(name)
    {
        /// <summary>The SQL type.</summary>
        public SqlDbType Type { get; } = type;

        /// <summary>The value, or null for SQL <c>NULL</c>.</summary>
        public object? Value { get; } = value;

        /// <summary>The size of a sized type, or null for the provider's.</summary>
        public int? Size { get; } = size;

        /// <inheritdoc />
        public override SqlParameter Create()
        {
            SqlParameter parameter = new(Name, Type) { Value = Value ?? DBNull.Value };
            if (Size is int size)
            {
                parameter.Size = size;
            }

            return parameter;
        }

        /// <inheritdoc />
        public override bool BindsSame(BatchParameter other)
        {
            return other is ScalarParameter scalar
                && scalar.Type == Type
                && scalar.Size == Size
                && ScalarValues.AreEqual(scalar.Value, Value);
        }
    }

    /// <summary>A table-valued parameter: rows of a table type, streamed through the binder on every attempt.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="rows">The row list, by reference.</param>
    /// <param name="rowCount">How many rows the list holds.</param>
    /// <param name="factory">Creates the bound parameter; it enumerates the rows anew each time it is sent.</param>
    internal sealed class TableParameter(string name, object rows, int rowCount, Func<string, SqlParameter> factory)
        : BatchParameter(name)
    {
        /// <inheritdoc />
        public override int Rows => rowCount;

        /// <inheritdoc />
        public override SqlParameter Create()
        {
            return factory(Name);
        }

        /// <inheritdoc />
        public override bool BindsSame(BatchParameter other)
        {
            // One list bound twice is one binding; equal rows in two lists are two.
            return other is TableParameter table && ReferenceEquals(table.RowList, RowList);
        }

        /// <summary>The bound list, compared by reference.</summary>
        private object RowList => rows;
    }
}
