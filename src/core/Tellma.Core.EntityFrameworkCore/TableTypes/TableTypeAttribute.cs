// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.EntityFrameworkCore.TableTypes
{
    /// <summary>
    ///     Opts the entity's table into having a paired SQL Server table type (UDTT), derived as a
    ///     row image of the table and kept in sync by EF Core migrations.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The attribute is inherited: under leaf-only mapping, a distribution leaf class that
    ///         extends a pack's default entity inherits the pack's opt-in. Fluent configuration
    ///         always takes precedence — a leaf can override an inherited attribute with
    ///         <c>HasNoTableType()</c>.
    ///     </para>
    ///     <para>
    ///         By default the type is named <c>[&lt;TableSchema&gt;].[&lt;TableName&gt;List]</c>, e.g.
    ///         <c>[gl].[InvoicesList]</c>; <see cref="Name" /> and <see cref="Schema" /> override that.
    ///     </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public sealed class TableTypeAttribute : Attribute
    {
        /// <summary>The table type's name; defaults to <c>&lt;TableName&gt;List</c> when omitted.</summary>
        public string? Name { get; set; }

        /// <summary>The table type's schema; defaults to the table's own schema when omitted.</summary>
        public string? Schema { get; set; }
    }
}
