// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     Makes an entity a whole-table reference list shared by every reader: small, read-mostly data
    ///     that pickers read on most screens, such as countries, currencies and units. Its rows are
    ///     cached on the server and the client, validated by a version tag that every write to the table
    ///     bumps.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Users, roles and anything with row-level security are never cacheable: a cached list is
    ///         shared by every reader, so a row-level filter on it could only be silently ignored.
    ///     </para>
    ///     <para>Inherited by a leaf.</para>
    /// </remarks>
    /// <param name="maxRows">
    ///     The most rows the table may hold and stay cacheable; 0 takes the platform's configured
    ///     default for cacheable entities.
    /// </param>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class CacheableAttribute(int maxRows = 0) : Attribute
    {
        /// <summary>The most rows the table may hold and stay cacheable; 0 for the configured default.</summary>
        public int MaxRows { get; set; } = maxRows;
    }
}
