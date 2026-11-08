// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Hosting
{
    /// <summary>
    ///     The collations every platform database is created in. One collation serves the catalog,
    ///     every tenant database, the identity database, and the development and test databases that
    ///     stand in for them.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A database's collation is set once, by <c>CREATE DATABASE</c>, and never altered:
    ///         Azure SQL Database cannot change it, and on SQL Server the existing columns would keep
    ///         the old one. Every platform EF model declares it through
    ///         <c>UseCollation(DatabaseCollations.Platform)</c>, so a database EF creates for a
    ///         development or test run is created in it too, and no migration ever carries a change
    ///         to it.
    ///     </para>
    ///     <para>
    ///         Every host that finds an existing database checks it against this value and refuses
    ///         any other, naming the value it expected and the value it found.
    ///     </para>
    /// </remarks>
    public static class DatabaseCollations
    {
        /// <summary>
        ///     The platform collation, <c>Latin1_General_100_CI_AS</c>: case-insensitive and
        ///     accent-sensitive, with the version-100 sort tables. The server default
        ///     <c>SQL_Latin1_General_CP1_CI_AS</c> defines no order on Amharic text, such as users'
        ///     names in the shared directory.
        /// </summary>
        public const string Platform = "Latin1_General_100_CI_AS";
    }
}
