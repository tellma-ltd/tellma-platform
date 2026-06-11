// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.EntityFrameworkCore.TableTypes
{
    /// <summary>
    ///     Excludes this property's column from the entity's table type (UDTT).
    /// </summary>
    /// <remarks>
    ///     The attribute is inherited along with <see cref="TableTypeAttribute" />. Fluent
    ///     configuration takes precedence: a derived entity can re-include an attribute-excluded
    ///     column with <c>IncludeInTableType()</c>. Primary-key columns cannot be excluded.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property, Inherited = true)]
    public sealed class ExcludeFromTableTypeAttribute : Attribute
    {
    }
}
