// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>Who writes a property's column, which decides where the save statements carry it.</summary>
    public enum PropertyOwnership
    {
        /// <summary>The client: in every <c>SET</c> list and every <c>INSERT</c> column list.</summary>
        Editable,

        /// <summary>
        ///     The client, on insert only: written on <c>INSERT</c> from the payload and never in a
        ///     <c>SET</c> list; a changed value on update is a validation error.
        /// </summary>
        WriteOnce,

        /// <summary>
        ///     The server: never in a <c>SET</c> list; the before image wins on update and a fresh
        ///     instance's value (or the statement's stamp, for the audit columns) on insert. The key, the
        ///     audit columns, the subtree counts, <c>IsActive</c> and <c>JobId</c> are server-owned by
        ///     derivation.
        /// </summary>
        ServerOwned,

        /// <summary>
        ///     The pipeline's hooks: the client's value is discarded before them, and the value they leave
        ///     is written on insert and update.
        /// </summary>
        Derived,

        /// <summary>
        ///     The database: a computed column, never in an <c>INSERT</c> column list, a <c>SET</c> list or
        ///     the unchanged-row comparison, and absent from the table type; the read-back returns it.
        /// </summary>
        DatabaseOwned,
    }
}
