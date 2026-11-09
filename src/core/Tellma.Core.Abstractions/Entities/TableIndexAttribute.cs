// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     Declares a non-unique index over the named properties' columns, in the order given: a pack's
    ///     composite index. The index is named <c>IX_&lt;Table&gt;_&lt;Column1&gt;_&lt;Column2&gt;…</c>.
    /// </summary>
    /// <remarks>
    ///     Repeatable, one index per declaration, and inherited by a leaf. Name the properties with
    ///     <c>nameof</c>, so a rename keeps the declaration in step.
    /// </remarks>
    /// <param name="properties">The indexed properties, in key order.</param>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class TableIndexAttribute(params string[] properties) : Attribute
    {
        /// <summary>The indexed properties, in key order.</summary>
        public IReadOnlyList<string> Properties { get; } = [.. properties ?? []];
    }
}
