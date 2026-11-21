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
            catch (SqlException exception)
            {
                // A failure the classification cannot call a missing database stands as it is.
                CatalogHistoryState? state = await ClassifyLoginFailureAsync(
                    exception.Errors.Cast<SqlError>().Select(static error => error.Number),
                    catalog.DatabaseExistsAsync,
                    cancellationToken).ConfigureAwait(false);
                if (state is not CatalogHistoryState missing)
                {
                    throw;
                }

                return missing;
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

        /// <summary>
        ///     Classifies a failed login to the catalog: <see cref="CatalogHistoryState.MissingDatabase" />
        ///     when the failure shows the database is missing, or <see langword="null" /> when the
        ///     failure must stand as a reachability fault.
        /// </summary>
        /// <param name="errorNumbers">The SQL Server error numbers the failure carries.</param>
        /// <param name="databaseExists">Asks the server whether the catalog database exists; <see langword="null" /> when it cannot tell.</param>
        /// <param name="cancellationToken">Cancels the question.</param>
        /// <returns>The state, or <see langword="null" /> to keep the failure.</returns>
        internal static async Task<CatalogHistoryState?> ClassifyLoginFailureAsync(
            IEnumerable<int> errorNumbers,
            Func<CancellationToken, Task<bool?>> databaseExists,
            CancellationToken cancellationToken)
        {
            int[] numbers = [.. errorNumbers];

            // --- Refused for the database: 4060, beside the login failure 18456 in either order ---
            if (numbers.Contains(CatalogStatements.CannotOpenDatabase))
            {
                return CatalogHistoryState.MissingDatabase;
            }

            // --- Cut off before saying why: a missing database or a transient fault ---
            if (!numbers.Contains(CatalogStatements.LoginInterrupted))
            {
                return null;
            }

            bool? exists;
            try
            {
                exists = await databaseExists(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The server cannot be asked either: the original failure stands.
                return null;
            }

            // Only a server that says the database is absent turns the failure into a missing one.
            return exists == false ? CatalogHistoryState.MissingDatabase : null;
        }
    }
}
