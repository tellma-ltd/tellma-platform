// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     The platform's catalog migrations, as a host that does not carry them knows them: the id of
    ///     the last one. A web host checks the catalog's history against it at startup and refuses a
    ///     catalog that lacks it, naming the migrator command that brings the catalog up to date.
    /// </summary>
    public static class CatalogMigrations
    {
        /// <summary>
        ///     The id of the last catalog migration in <c>Tellma.Core.Migrator</c>. A test keeps it equal
        ///     to that assembly's last migration, so a new migration must move it.
        /// </summary>
        public const string Latest = "20261109071357_Initial";
    }
}
