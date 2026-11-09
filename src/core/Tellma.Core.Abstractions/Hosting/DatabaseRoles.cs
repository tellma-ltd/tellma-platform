// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Hosting
{
    /// <summary>
    ///     The database roles every platform database carries. Each name is a platform constant, so
    ///     no migration ever carries an environment-specific principal.
    /// </summary>
    public static class DatabaseRoles
    {
        /// <summary>
        ///     The application role, <c>tellma_app</c>: the web host's and the workers' principal is a
        ///     member of it, and it holds data access on every mapped schema, the sequences and
        ///     <c>EXECUTE</c> on every table type. The migrator creates it before a database's first
        ///     migration, because the table-type migrations grant it.
        /// </summary>
        public const string Application = "tellma_app";
    }
}
