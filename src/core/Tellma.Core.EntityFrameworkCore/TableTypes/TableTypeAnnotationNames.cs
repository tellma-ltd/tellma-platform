// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.EntityFrameworkCore.TableTypes
{
    /// <summary>
    ///     Names of the model annotations used by the table-types extension. All values are
    ///     strings or booleans so that the stock EF Core model-snapshot generator round-trips
    ///     them as <c>.HasAnnotation(...)</c> literals with no custom snapshot code.
    /// </summary>
    /// <remarks>
    ///     Two layers of annotations exist:
    ///     <list type="bullet">
    ///         <item>
    ///             <description>
    ///                 <b>Configuration annotations</b> (entity-type, property, and model level) carry
    ///                 the raw opt-in input written by the fluent API. CLR attributes are not copied
    ///                 into annotations; they are read directly by the finalizing convention, and an
    ///                 explicit fluent annotation always wins over an attribute.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 <b>Derived definition annotations</b> (model level, one per table type, named
    ///                 <see cref="DefinitionPrefix" /> + <c>&lt;schema&gt;.&lt;name&gt;</c>) carry the full derived
    ///                 <see cref="TableTypeDefinition" /> as canonical JSON. They are the diffing
    ///                 contract: the migrations differ compares them verbatim, string to string,
    ///                 and never re-derives a definition from the model structure.
    ///             </description>
    ///         </item>
    ///     </list>
    /// </remarks>
    public static class TableTypeAnnotationNames
    {
        /// <summary>
        ///     Entity-type annotation (<see cref="bool" />): <see langword="true" /> when the entity opted in via the
        ///     fluent API, <see langword="false" /> when it explicitly opted out via <c>HasNoTableType()</c>
        ///     (overriding an inherited <see cref="TableTypeAttribute" />). Absent when only attributes apply.
        /// </summary>
        public const string Enabled = "Tellma:TableType:Enabled";

        /// <summary>
        ///     Entity-type annotation (<see cref="string" />): explicit table-type name override. When absent the
        ///     name defaults to <c>&lt;TableName&gt;List</c>.
        /// </summary>
        public const string Name = "Tellma:TableType:Name";

        /// <summary>
        ///     Entity-type annotation (<see cref="string" />): explicit table-type schema override. When absent the
        ///     schema defaults to the table's own schema.
        /// </summary>
        public const string Schema = "Tellma:TableType:Schema";

        /// <summary>
        ///     Entity-type annotation (<see cref="bool" />): when <see langword="true" />, the table's
        ///     rowversion/concurrency-token column is excluded from the table type instead of being
        ///     included as a nullable <c>binary(8)</c> column.
        /// </summary>
        public const string ExcludeRowVersion = "Tellma:TableType:ExcludeRowVersion";

        /// <summary>
        ///     Entity-type annotation (<see cref="bool" />): when <see langword="true" />, the table type is created
        ///     with <c>MEMORY_OPTIMIZED = ON</c> (In-Memory OLTP), guarded by a pre-flight support check.
        /// </summary>
        public const string MemoryOptimized = "Tellma:TableType:MemoryOptimized";

        /// <summary>
        ///     Entity-type annotation (<see cref="string" />): JSON array of database principals that receive
        ///     <c>GRANT EXECUTE ON TYPE</c> after every create/recreate of the table type.
        /// </summary>
        public const string Grants = "Tellma:TableType:Grants";

        /// <summary>
        ///     Property annotation (<see cref="bool" />): <see langword="true" /> when the property's column is
        ///     excluded from the table type, <see langword="false" /> when it is explicitly re-included
        ///     (overriding an inherited <see cref="ExcludeFromTableTypeAttribute" />).
        /// </summary>
        public const string Excluded = "Tellma:TableType:Excluded";

        /// <summary>
        ///     Prefix of the model-level standalone-type configuration annotations (spec 0001 §5). The
        ///     full annotation name is <c>Tellma:TableTypeStandalone:&lt;schema&gt;.&lt;name&gt;</c> and the value is
        ///     the canonical JSON of the <see cref="StandaloneTableTypeConfiguration" />.
        /// </summary>
        public const string StandalonePrefix = "Tellma:TableTypeStandalone:";

        /// <summary>
        ///     Prefix of the model-level derived definition annotations. The full annotation name is
        ///     <c>Tellma:TableTypeDefinition:&lt;schema&gt;.&lt;name&gt;</c> (empty schema when the type has none) and the
        ///     value is the canonical JSON of the <see cref="TableTypeDefinition" />.
        /// </summary>
        public const string DefinitionPrefix = "Tellma:TableTypeDefinition:";
    }
}
