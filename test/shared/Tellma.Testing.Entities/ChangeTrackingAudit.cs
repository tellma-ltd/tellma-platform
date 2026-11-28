// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data.Batches;

namespace Tellma.Testing.Entities
{
    /// <summary>
    ///     The fixture's audit of every executed batch: the tables change tracking saw written while
    ///     the batch ran must be among the batch's declared writes. It reads change tracking on
    ///     connections of its own, so it never adds to the batch's round trips.
    /// </summary>
    /// <remarks>
    ///     Change tracking versions are database-wide, so the audit is exact only while one batch at a
    ///     time writes the fixture database, which is how the fixture suites use it.
    /// </remarks>
    /// <param name="connectionString">The fixture database's connection string.</param>
    internal sealed class ChangeTrackingAudit(string connectionString) : IDataBatchAudit
    {
        /// <inheritdoc />
        public async ValueTask<object?> BeforeAsync(CancellationToken cancellationToken)
        {
            await using SqlConnection connection = new(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT CHANGE_TRACKING_CURRENT_VERSION();";
            object? version = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return version is long current ? current : 0L;
        }

        /// <inheritdoc />
        public async ValueTask AfterAsync(object? state, IReadOnlySet<TableName> writtenTables, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(writtenTables);
            long since = state is long version ? version : 0L;

            IReadOnlyList<TableName> changed = await ChangedSinceAsync(since, cancellationToken).ConfigureAwait(false);
            List<TableName> undeclared = [.. changed.Where(table => !writtenTables.Contains(table))];
            if (undeclared.Count > 0)
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The batch wrote {Describe(undeclared)}, which it did not declare in its writes ({(writtenTables.Count == 0 ? "none" : Describe(writtenTables))})."));
            }
        }

        /// <summary>Tables by their delimited names, in order.</summary>
        /// <param name="tables">The tables.</param>
        /// <returns>The names, comma-separated.</returns>
        private static string Describe(IEnumerable<TableName> tables)
        {
            return string.Join(", ", tables.Select(static table => table.Delimited).Order(StringComparer.Ordinal));
        }

        /// <summary>The tracked tables with a change after a version.</summary>
        /// <param name="since">The version.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The tables.</returns>
        private async Task<IReadOnlyList<TableName>> ChangedSinceAsync(long since, CancellationToken cancellationToken)
        {
            await using SqlConnection connection = new(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // --- The tracked tables ---
            List<(string Schema, string Table)> tracked = [];
            await using (SqlCommand list = connection.CreateCommand())
            {
                list.CommandText = """
                    SELECT s.[name], t.[name]
                    FROM sys.change_tracking_tables AS c
                    JOIN sys.tables AS t ON t.[object_id] = c.[object_id]
                    JOIN sys.schemas AS s ON s.[schema_id] = t.[schema_id]
                    ORDER BY s.[name], t.[name];
                    """;
                await using SqlDataReader reader = await list.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    tracked.Add((reader.GetString(0), reader.GetString(1)));
                }
            }

            if (tracked.Count == 0)
            {
                return [];
            }

            // --- One probe per table, in one statement: the names of those with a change ---
            StringBuilder probe = new();
            foreach ((string schema, string table) in tracked)
            {
                if (probe.Length > 0)
                {
                    _ = probe.Append("\nUNION ALL\n");
                }

                _ = probe.Append(CultureInfo.InvariantCulture,
                    $"SELECT N'{schema}', N'{table}' WHERE EXISTS (SELECT 1 FROM CHANGETABLE(CHANGES [{schema}].[{table}], @since) AS c)");
            }

            List<TableName> changed = [];
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = probe.Append(';').ToString();
            _ = command.Parameters.Add(new SqlParameter("@since", SqlDbType.BigInt) { Value = since });
            await using SqlDataReader rows = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await rows.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                changed.Add(new TableName(rows.GetString(0), rows.GetString(1)));
            }

            return changed;
        }
    }
}
