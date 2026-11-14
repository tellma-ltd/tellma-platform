// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Data;

namespace Tellma.Core.Tenancy
{
    /// <summary>The registry's reads of the catalog, over the catalog connection.</summary>
    /// <param name="catalog">The catalog connection.</param>
    internal sealed class SqlCatalogReader(CatalogConnection catalog) : ICatalogReader
    {
        /// <inheritdoc />
        public async Task<CatalogHistoryState> ReadHistoryAsync(CancellationToken cancellationToken)
        {
            SqlConnection connection;
            try
            {
                connection = await catalog.OpenWithoutRetryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SqlException exception) when (HasError(exception, CatalogStatements.CannotOpenDatabase))
            {
                // A login to a missing database fails with two errors, 4060 and the login failure
                // 18456, and which one the exception reports first is not to be relied on.
                return CatalogHistoryState.MissingDatabase;
            }
            catch (SqlException exception) when (HasError(exception, CatalogStatements.LoginInterrupted))
            {
                // The server ended the login without the client reading why, which a missing
                // database can cause as well as a transient fault: the server itself decides which.
                if (!await DatabaseExistsAsync(cancellationToken).ConfigureAwait(false))
                {
                    return CatalogHistoryState.MissingDatabase;
                }

                throw;
            }

            await using (connection.ConfigureAwait(false))
            {
                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = CatalogStatements.History;
                _ = command.Parameters.Add(new SqlParameter("@latest", SqlDbType.NVarChar, 150) { Value = CatalogMigrations.Latest });

                object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) switch
                {
                    1 => CatalogHistoryState.Current,
                    0 => CatalogHistoryState.Behind,
                    _ => CatalogHistoryState.MissingSchema,
                };
            }
        }

        /// <inheritdoc />
        public async Task<Guid> ReadVersionAsync(CancellationToken cancellationToken)
        {
            SqlConnection connection = await catalog.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = CatalogStatements.Probe;
                object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return result is Guid version
                    ? version
                    : throw new InvalidOperationException("The catalog's state row is missing; the catalog's first migration seeds it.");
            }
        }

        /// <inheritdoc />
        public async Task<CatalogReload> ReadTenantsAsync(CancellationToken cancellationToken)
        {
            SqlConnection connection = await catalog.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = CatalogStatements.Reload;
                await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                // --- The version, read first ---
                Guid version = await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                    ? reader.GetGuid(0)
                    : throw new InvalidOperationException("The catalog's state row is missing; the catalog's first migration seeds it.");

                // --- Then every row ---
                List<TenantRow> rows = [];
                if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        rows.Add(TenantRow.Read(reader));
                    }
                }

                return new CatalogReload(version, rows.AsReadOnly());
            }
        }

        /// <summary>Whether an exception carries a SQL Server error number.</summary>
        /// <param name="exception">The exception.</param>
        /// <param name="number">The error number.</param>
        /// <returns>Whether any of the exception's errors has the number.</returns>
        private static bool HasError(SqlException exception, int number)
        {
            return exception.Errors.Cast<SqlError>().Any(error => error.Number == number);
        }

        /// <summary>Whether the catalog database exists, when the server can be asked; a failure to ask counts as yes.</summary>
        /// <param name="cancellationToken">Cancels the question.</param>
        /// <returns><see langword="false" /> only when the server says the database is missing.</returns>
        private async Task<bool> DatabaseExistsAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await catalog.DatabaseExistsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SqlException)
            {
                // The server cannot be asked either: the original failure stands.
                return true;
            }
        }
    }
}
