// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Tellma.Core.Abstractions.Hosting;

namespace Tellma.Core.Migrator
{
    /// <summary>
    ///     Creates the application role in every database a design-time context connects to, before
    ///     any migration runs there: the table-type migrations grant <c>EXECUTE</c> to the role
    ///     unconditionally, and a database <c>dotnet ef database update</c> creates has no role yet.
    /// </summary>
    /// <remarks>
    ///     <c>master</c>, which EF connects to when it creates the database, is left alone. The
    ///     statement is idempotent, so the many connections a command opens each find the role there.
    ///     Only design-time factories install it: the migrator creates the role itself, as its first
    ///     step in a fresh database.
    /// </remarks>
    internal sealed class ApplicationRoleInterceptor : DbConnectionInterceptor
    {
        /// <summary>The statement: create the role unless the database has it, or is <c>master</c>.</summary>
        public const string EnsureRole =
            $"IF DB_NAME() <> N'master' AND DATABASE_PRINCIPAL_ID(N'{DatabaseRoles.Application}') IS NULL CREATE ROLE [{DatabaseRoles.Application}];";

        /// <inheritdoc />
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            ArgumentNullException.ThrowIfNull(connection);

            using DbCommand command = connection.CreateCommand();
            command.CommandText = EnsureRole;
            _ = command.ExecuteNonQuery();
        }

        /// <inheritdoc />
        public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(connection);

            DbCommand command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = EnsureRole;
                _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
