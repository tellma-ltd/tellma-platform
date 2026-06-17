// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.EntityFrameworkCore.TableTypes
{
    /// <summary>
    ///     Names of the model annotations used by the table-types extension. The configuration-level
    ///     values are strings or booleans so the stock EF Core snapshot generator round-trips them as
    ///     <c>.HasAnnotation(...)</c> literals; the derived <em>definition</em> annotations are instead
    ///     rendered as readable <c>HasTableTypeDefinition(...)</c> calls by
    ///     <c>TableTypesCSharpSnapshotGenerator</c> and filtered out of the generic annotation output.
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

        /// <summary>
        ///     Entity-type or standalone-config flag (<see cref="bool" />): when <see langword="true" />,
        ///     the type stays in the model and metadata API (so the context binds it at runtime) but is
        ///     <b>not</b> created or swept by this context's migrations — another context owns the
        ///     physical type (spec 0001 §3 → scoping; mirrors EF Core's table-level
        ///     <c>ExcludeFromMigrations</c>). For standalone types this rides on the
        ///     <see cref="StandaloneTableTypeConfiguration" />; for table-derived types it is this
        ///     entity-type annotation.
        /// </summary>
        public const string ExcludeFromMigrations = "Tellma:TableType:ExcludeFromMigrations";

        /// <summary>
        ///     Model-level annotation (<see cref="string" />): JSON array of the definition keys
        ///     (<c>&lt;schema&gt;.&lt;name&gt;</c>) the convention resolved as excluded from this context's
        ///     migrations. The differ reads it to skip those creates and omit them from the cleanup
        ///     keep-list; the metadata API ignores it (the definitions remain bindable). Live-model
        ///     only — filtered from snapshots, since the source side never needs it.
        /// </summary>
        public const string ExcludedKeys = "Tellma:TableTypes:ExcludedKeys";
    }
}
