// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.EntityFrameworkCore.TableTypes
{
    /// <summary>
    ///     The built-in primitive table types the library can create alongside the table-derived
    ///     ones. The "0 or 1 type per table" rule blocks per-operation shapes, so these generic
    ///     single-column types serve bulk delete / bulk lookup scenarios.
    /// </summary>
    /// <remarks>
    ///     Opted into per model via
    ///     <see cref="TableTypesModelBuilderExtensions.HasBuiltInTableTypes" />; created and dropped
    ///     by the same migration operations as table-derived types.
    /// </remarks>
    [Flags]
    public enum BuiltInTableTypes
    {
        /// <summary>No built-in types.</summary>
        None = 0,

        /// <summary>A <c>[IdList]</c> type with a single <c>[Id] int</c> column.</summary>
        IdList = 1,

        /// <summary>A <c>[BigIdList]</c> type with a single <c>[Id] bigint</c> column.</summary>
        BigIdList = 2,

        /// <summary>A <c>[GuidList]</c> type with a single <c>[Id] uniqueidentifier</c> column.</summary>
        GuidList = 4,

        /// <summary>A <c>[StringList]</c> type with a single <c>[Id] nvarchar(450)</c> column.</summary>
        StringList = 8,

        /// <summary>All built-in types.</summary>
        All = IdList | BigIdList | GuidList | StringList,
    }

    /// <summary>
    ///     The model-level configuration of the built-in primitive table types, stored as canonical
    ///     JSON in the <see cref="TableTypeAnnotationNames.BuiltIn" /> model annotation.
    /// </summary>
    public sealed record BuiltInTableTypesConfiguration
    {
        /// <summary>Which built-in types the model opted into.</summary>
        public required BuiltInTableTypes Types { get; init; }

        /// <summary>The schema the built-in types are created in; defaults to <c>dbo</c>.</summary>
        public string? Schema { get; init; }

        /// <summary>
        ///     Database principals that receive <c>GRANT EXECUTE ON TYPE</c> on each built-in type
        ///     after every create/recreate.
        /// </summary>
        public IReadOnlyList<string> Grants { get; init; } = [];
    }
}
