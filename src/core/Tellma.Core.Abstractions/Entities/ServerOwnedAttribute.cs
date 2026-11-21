// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     Makes a property server-owned: a client never sets it. On update the stored value wins
    ///     silently; on insert the column is written from a freshly constructed instance's value.
    /// </summary>
    /// <remarks>
    ///     The key, the audit columns, the tree counts and the capability columns
    ///     (<see cref="IActivatable.IsActive" />, <see cref="IJobEntity.JobId" />) are server-owned by
    ///     derivation and need no declaration. A property carries at most one ownership attribute, and
    ///     none on a parent key or a database-computed column.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class ServerOwnedAttribute : Attribute;
}
