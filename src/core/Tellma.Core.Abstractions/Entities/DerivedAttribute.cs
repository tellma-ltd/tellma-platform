// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     Makes a property derived by the service from the payload itself, such as a document total
    ///     summed over its lines or a normalised code. The client's value is discarded before the
    ///     service's hooks run, and the column is written on insert and update from the value the hooks
    ///     left.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A value derivable from the row's own columns alone is a persisted computed column
    ///         instead, owned by the database. Server ownership cannot express a derived value (the
    ///         stored value would win), and leaving it editable would leave it to the client.
    ///     </para>
    ///     <para>A property carries at most one ownership attribute, and none on a parent key or a database-computed column.</para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class DerivedAttribute : Attribute;
}
