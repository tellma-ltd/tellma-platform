// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Hosting;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     The <c>CREATE DATABASE</c> statement the migrator runs, in the target server's
    ///     <c>master</c>, for a catalog or tenant database: always in the platform collation, and on
    ///     Azure SQL Database with the configured creation options and the catalog collation pinned to
    ///     the database's.
    /// </summary>
    /// <remarks>
    ///     EF never writes Azure's options or <c>CATALOG_COLLATION</c> when it creates a database, so
    ///     the migrator creates every database with this statement on every engine. A database's
    ///     collation is set here once and never altered.
    /// </remarks>
    public static class DatabaseCreation
    {
        /// <summary>The <c>SERVERPROPERTY('EngineEdition')</c> of Azure SQL Database.</summary>
        public const int AzureSqlDatabaseEngineEdition = 5;

        /// <summary>The query that reads the engine edition, run in <c>master</c> before the statement is chosen.</summary>
        public const string EngineEditionQuery = "SELECT CONVERT(int, SERVERPROPERTY('EngineEdition'));";

        /// <summary>
        ///     The creation statement: <c>CREATE DATABASE [name] COLLATE Latin1_General_100_CI_AS;</c>
        ///     on SQL Server and LocalDB; on Azure SQL Database, the options, when given, then
        ///     <c>WITH CATALOG_COLLATION = DATABASE_DEFAULT</c>.
        /// </summary>
        /// <param name="databaseName">The database: 1 to 128 characters, no <c>;</c>, <c>=</c>, <c>]</c> or control character.</param>
        /// <param name="engineEdition">The server's engine edition.</param>
        /// <param name="createDatabaseOptions">
        ///     <c>Tellma:Sql:CreateDatabaseOptions</c>, such as
        ///     <c>(SERVICE_OBJECTIVE = ELASTIC_POOL(name = pool1))</c>; used on Azure SQL Database only.
        /// </param>
        /// <returns>The statement, to run as a batch of its own.</returns>
        /// <exception cref="ArgumentException">The name is malformed, or the options hold <c>;</c> or a comment.</exception>
        public static string CreateStatement(string databaseName, int engineEdition, string? createDatabaseOptions)
        {
            string create = TenantLocations.IsValidDatabaseName(databaseName)
                ? $"CREATE DATABASE [{databaseName}] COLLATE {DatabaseCollations.Platform}"
                : throw new ArgumentException("A database name is 1 to 128 characters without ';', '=', ']' or a control character.", nameof(databaseName));
            if (engineEdition != AzureSqlDatabaseEngineEdition)
            {
                return create + ";";
            }

            // Azure SQL Database: the options sit between COLLATE and WITH, and a blank value is none.
            // Lines break with '\n' on every OS, so the statement is the same text everywhere.
            string? options = string.IsNullOrWhiteSpace(createDatabaseOptions) ? null : createDatabaseOptions.Trim();
            bool endsEarly = options is not null
                && (options.Contains(';', StringComparison.Ordinal)
                    || options.Contains("--", StringComparison.Ordinal)
                    || options.Contains("/*", StringComparison.Ordinal));

            return endsEarly
                ? throw new ArgumentException("The creation options must be one clause, without ';' or a comment.", nameof(createDatabaseOptions))
                : options is null
                    ? $"{create}\n    WITH CATALOG_COLLATION = DATABASE_DEFAULT;"
                    : $"{create}\n    {options}\n    WITH CATALOG_COLLATION = DATABASE_DEFAULT;";
        }
    }
}
