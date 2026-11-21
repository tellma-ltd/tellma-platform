// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     Leaves the property's column out of its class's table type: the column stays in the table
    ///     but no table-valued parameter carries it.
    /// </summary>
    /// <remarks>
    ///     Inherited by a leaf. A key column cannot be excluded, and no exclusion may touch a column a
    ///     platform base or capability interface declares; the tree node and the temporal period
    ///     columns are excluded by the platform itself.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class ExcludeFromTableTypeAttribute : Attribute;
}
