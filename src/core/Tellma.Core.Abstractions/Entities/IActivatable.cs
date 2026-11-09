// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     The gate of an entity whose rows can be deactivated: a one-property capability that gives its
    ///     stack the activate and deactivate actions and the active-only filter.
    /// </summary>
    /// <remarks>
    ///     <see cref="IsActive" /> is server-owned by derivation, because attributes on interface members
    ///     do not reach implementing properties. Every row is created active: the insert writes the
    ///     column's default (1) and ignores the payload, and the column changes only through the
    ///     activate and deactivate actions and the platform's own fixed statements. A client value on a
    ///     save or an import is overwritten silently.
    /// </remarks>
    public interface IActivatable
    {
        /// <summary>Whether the row is active; server-owned, <c>bit NOT NULL</c> defaulting to 1.</summary>
        bool IsActive { get; set; }
    }
}
