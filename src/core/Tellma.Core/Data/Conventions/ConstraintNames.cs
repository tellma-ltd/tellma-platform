// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Data.Conventions
{
    /// <summary>
    ///     The names the conventions give every constraint, index, default and sequence:
    ///     <c>PK_&lt;Table&gt;</c>, <c>FK_&lt;Table&gt;_&lt;Column&gt;</c>, <c>IX_&lt;Table&gt;_&lt;Columns&gt;</c>,
    ///     <c>UX_&lt;Table&gt;_&lt;Columns&gt;</c>, <c>CK_&lt;Table&gt;_&lt;Name&gt;</c>,
    ///     <c>DF_&lt;Table&gt;_&lt;Column&gt;</c> and <c>sq_&lt;Table&gt;</c>. A distribution never names
    ///     one, and nothing relies on the provider's implicit naming, which can shift across releases.
    /// </summary>
    internal static class ConstraintNames
    {
        /// <summary>SQL Server's identifier length limit.</summary>
        private const int MaxIdentifierLength = 128;

        /// <summary>A primary key's name.</summary>
        /// <param name="table">The table.</param>
        /// <returns>The name.</returns>
        public static string PrimaryKey(string table)
        {
            return Checked($"PK_{table}");
        }

        /// <summary>A foreign key's name.</summary>
        /// <param name="table">The dependent table.</param>
        /// <param name="column">The foreign-key column.</param>
        /// <returns>The name.</returns>
        public static string ForeignKey(string table, string column)
        {
            return Checked($"FK_{table}_{column}");
        }

        /// <summary>A composite foreign key's name, its columns joined in key order.</summary>
        /// <param name="table">The dependent table.</param>
        /// <param name="columns">The foreign-key columns, in key order.</param>
        /// <returns>The name.</returns>
        public static string ForeignKey(string table, IEnumerable<string> columns)
        {
            return Checked($"FK_{table}_{string.Join('_', columns)}");
        }

        /// <summary>A non-unique index's name.</summary>
        /// <param name="table">The table.</param>
        /// <param name="columns">The indexed columns, in key order.</param>
        /// <returns>The name.</returns>
        public static string Index(string table, IEnumerable<string> columns)
        {
            return Checked($"IX_{table}_{string.Join('_', columns)}");
        }

        /// <summary>A unique index's name.</summary>
        /// <param name="table">The table.</param>
        /// <param name="columns">The indexed columns, in key order.</param>
        /// <returns>The name.</returns>
        public static string UniqueIndex(string table, IEnumerable<string> columns)
        {
            return Checked($"UX_{table}_{string.Join('_', columns)}");
        }

        /// <summary>A check constraint's name.</summary>
        /// <param name="table">The table.</param>
        /// <param name="name">The constraint's own name.</param>
        /// <returns>The name.</returns>
        public static string Check(string table, string name)
        {
            return Checked($"CK_{table}_{name}");
        }

        /// <summary>A default constraint's name.</summary>
        /// <param name="table">The table.</param>
        /// <param name="column">The column.</param>
        /// <returns>The name.</returns>
        public static string Default(string table, string column)
        {
            return Checked($"DF_{table}_{column}");
        }

        /// <summary>A table's sequence name, in the table's schema.</summary>
        /// <param name="table">The table.</param>
        /// <returns>The name.</returns>
        public static string Sequence(string table)
        {
            return Checked($"sq_{table}");
        }

        /// <summary>
        ///     Refuses a name SQL Server would refuse. Truncating instead could make two names collide,
        ///     and the constraint names are what statements and error mappings rely on.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>The same name.</returns>
        private static string Checked(string name)
        {
            return name.Length <= MaxIdentifierLength
                ? name
                : throw new InvalidOperationException(
                    $"The conventions would name a constraint '{name}', which is longer than SQL Server's {MaxIdentifierLength}-character limit. Shorten the table or column names it is built from.");
        }
    }
}
