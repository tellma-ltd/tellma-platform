// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     Gives a class a SQL Server user-defined table type, the row shape of a table-valued
    ///     parameter. On an entity it opts the table into a type derived as the table's row image; on a
    ///     plain class contributed as a standalone table type it names the type the class's properties
    ///     derive.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Inherited, so a distribution's leaf keeps its default's opt-in. An entity's type is named
    ///         <c>[&lt;schema&gt;].[&lt;Table&gt;List]</c> by default and a standalone type
    ///         <c>[dbo].[&lt;class name&gt;]</c>; the deployed name appends a hash of the definition, so
    ///         a changed shape deploys as a new version beside the old one.
    ///     </para>
    ///     <para>
    ///         Every type is bound at run time by column name from the model, never by ordinal.
    ///     </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class TableTypeAttribute : Attribute
    {
        /// <summary>The type's logical name, when it differs from the default.</summary>
        public string? Name { get; set; }

        /// <summary>The type's schema, when it differs from the default.</summary>
        public string? Schema { get; set; }
    }
}
