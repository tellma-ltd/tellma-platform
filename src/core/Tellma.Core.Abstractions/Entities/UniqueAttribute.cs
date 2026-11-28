// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     Makes a property's values unique: a unique index, a save validator that loads the clashing
    ///     keys, and a field error when the database refuses a duplicate after all.
    /// </summary>
    /// <remarks>
    ///     The index is <c>UX_&lt;Table&gt;_&lt;Column&gt;</c>, filtered to non-null values when the column
    ///     is nullable; on a child it is <c>UX_&lt;Table&gt;_&lt;ParentKey&gt;_&lt;Column&gt;</c>, unique
    ///     within one parent. The property may not be a JSON column or binary.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class UniqueAttribute : Attribute;
}
