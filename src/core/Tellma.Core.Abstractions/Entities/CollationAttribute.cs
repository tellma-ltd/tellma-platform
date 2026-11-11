// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     Gives a string property's column an explicit collation instead of the database's.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         On an entity the table column takes it, and the table's row-image type copies it. On a
    ///         standalone table-type class the type's column takes it. Inherited by a leaf.
    ///     </para>
    ///     <para>
    ///         A string column of a table-valued parameter must declare the collation of any explicitly
    ///         collated column a statement compares it with: otherwise it takes the database collation
    ///         and SQL Server refuses the comparison (Msg 468). A key compared with a
    ///         <c>Latin1_General_100_BIN2</c> column, for example, declares
    ///         <c>[Collation("Latin1_General_100_BIN2")]</c>. Only <c>char</c>, <c>varchar</c>,
    ///         <c>nchar</c> and <c>nvarchar</c> columns take a collation.
    ///     </para>
    /// </remarks>
    /// <param name="name">The SQL Server collation name, such as <c>Latin1_General_100_BIN2</c>.</param>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class CollationAttribute(string name) : Attribute
    {
        /// <summary>The SQL Server collation name.</summary>
        public string Name { get; } = name;
    }
}
