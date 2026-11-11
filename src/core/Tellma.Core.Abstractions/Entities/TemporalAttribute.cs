// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     Makes the entity's table system-versioned: SQL Server keeps every prior version of each row
    ///     in <c>&lt;schema&gt;.&lt;Table&gt;History</c>, with the <c>ValidFrom</c> and <c>ValidTo</c>
    ///     period columns as shadow columns absent from the table type.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Inherited by a leaf. The children of a temporal root follow it unless they declare
    ///         <c>[Temporal(false)]</c>, and the save emitter skips rows that did not change, so the
    ///         history holds only real changes. A table-per-type root or leaf cannot be temporal.
    ///     </para>
    /// </remarks>
    /// <param name="enabled">
    ///     Whether the table is system-versioned; <see langword="false" /> opts a child out of its
    ///     temporal root.
    /// </param>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class TemporalAttribute(bool enabled = true) : Attribute
    {
        /// <summary>Whether the table is system-versioned.</summary>
        public bool Enabled { get; } = enabled;
    }
}
