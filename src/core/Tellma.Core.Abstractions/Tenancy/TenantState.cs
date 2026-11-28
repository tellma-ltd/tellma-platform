// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     The lifecycle state of a tenant, which decides whether a request or background work may
    ///     run against it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The legal transitions are: <see cref="Active" /> to and from <see cref="ReadOnly" />;
    ///         <see cref="Active" />, <see cref="ReadOnly" /> or <see cref="Suspended" /> to
    ///         <see cref="Frozen" /> and back to any of the three; <see cref="Active" /> or
    ///         <see cref="ReadOnly" /> to <see cref="Suspended" />, and <see cref="Suspended" /> to
    ///         <see cref="Active" />; <see cref="Provisioning" /> to <see cref="Active" />; and any
    ///         state to <see cref="Retired" />, which is terminal.
    ///     </para>
    ///     <para>
    ///         The catalog stores the member name; the names are part of the catalog's schema and
    ///         never change.
    ///     </para>
    /// </remarks>
    public enum TenantState
    {
        /// <summary>
        ///     Registered and being set up by the migrator. A request answers as if the tenant did not
        ///     exist; only the migrator's system scope may open it.
        /// </summary>
        Provisioning,

        /// <summary>Serving reads and writes.</summary>
        Active,

        /// <summary>
        ///     The commercial soft lock: members read and export, the platform's bookkeeping writes go
        ///     on, and user-initiated changes are refused.
        /// </summary>
        ReadOnly,

        /// <summary>
        ///     An operator's technical freeze, for a relocation or a restore: reads are served against
        ///     a database set read-only, every write is refused and background work stops.
        /// </summary>
        Frozen,

        /// <summary>Every request is refused and background work stops.</summary>
        Suspended,

        /// <summary>Terminal: the tenant answers as if it did not exist, and its id is never reused.</summary>
        Retired,
    }
}
