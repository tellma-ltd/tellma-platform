// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Analyzers.Entities
{
    /// <summary>
    ///     A table's schema and name, compared ignoring case as SQL Server's default collation
    ///     compares object names.
    /// </summary>
    /// <param name="schema">The schema.</param>
    /// <param name="name">The table's name.</param>
    internal sealed class TableKey(string schema, string name) : IEquatable<TableKey>
    {
        /// <summary>The schema.</summary>
        public string Schema { get; } = schema;

        /// <summary>The table's name.</summary>
        public string Name { get; } = name;

        /// <inheritdoc />
        public bool Equals(TableKey? other)
        {
            return other is not null
                && StringComparer.OrdinalIgnoreCase.Equals(Schema, other.Schema)
                && StringComparer.OrdinalIgnoreCase.Equals(Name, other.Name);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return Equals(obj as TableKey);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return unchecked((StringComparer.OrdinalIgnoreCase.GetHashCode(Schema) * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(Name));
        }

        /// <summary>The bracket-quoted name, as messages show it.</summary>
        /// <returns>The text.</returns>
        public override string ToString()
        {
            return $"[{Schema}].[{Name}]";
        }
    }
}
