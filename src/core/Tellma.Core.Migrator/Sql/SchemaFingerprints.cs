// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Data;
using Tellma.Core.Provisioning;

namespace Tellma.Core.Migrator.Sql
{
    /// <summary>One row of a tenant database's schema fingerprint table.</summary>
    /// <param name="Fingerprint">The model's storage fingerprint.</param>
    /// <param name="MigrationId">The migrator's last migration when it recorded the row.</param>
    /// <param name="PlatformVersion">The platform version that recorded it.</param>
    /// <param name="AppliedAt">When it was recorded.</param>
    internal sealed record SchemaFingerprintRow(string Fingerprint, string MigrationId, string PlatformVersion, DateTimeOffset AppliedAt);

    /// <summary>
    ///     Records a tenant database's schema fingerprint after a successful migration:
    ///     only when the database's history ends at the migrator's own last migration, the
    ///     model's fingerprint is inserted when absent and every row but the two newest is deleted. A
    ///     database whose history ends elsewhere is ahead of the migrator and gets no row.
    /// </summary>
    /// <remarks>
    ///     The statement runs as a plain command, never as an executor batch: those check the very
    ///     fingerprint this writes, as their first statement.
    /// </remarks>
    internal static class SchemaFingerprints
    {
        /// <summary>
        ///     The record, in one transaction: the history's last migration decides; the row is
        ///     inserted when absent; the rows other than it and the newest other one are deleted. The
        ///     result is 1 when the history ends at the migrator's last migration, 0 when it is ahead.
        /// </summary>
        private const string RecordStatement = """
            SET XACT_ABORT ON; BEGIN TRAN;
            DECLARE @last nvarchar(150) = (SELECT MAX([MigrationId]) FROM [dbo].[__EFMigrationsHistory]);
            IF @last = @migrationId
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM [dbo].[__TellmaSchema] WITH (UPDLOCK, HOLDLOCK) WHERE [Fingerprint] = @fingerprint)
                    INSERT INTO [dbo].[__TellmaSchema] ([Fingerprint], [MigrationId], [PlatformVersion], [AppliedAt])
                    VALUES (@fingerprint, @migrationId, @platformVersion, SYSDATETIMEOFFSET());
                DELETE FROM [dbo].[__TellmaSchema]
                 WHERE [Fingerprint] <> @fingerprint
                   AND [Fingerprint] NOT IN (SELECT TOP (1) o.[Fingerprint] FROM [dbo].[__TellmaSchema] AS o
                                              WHERE o.[Fingerprint] <> @fingerprint ORDER BY o.[AppliedAt] DESC, o.[Fingerprint] DESC);
            END
            COMMIT;
            SELECT CASE WHEN @last = @migrationId THEN 1 ELSE 0 END;
            """;

        /// <summary>Every recorded row, newest first.</summary>
        private const string ReadStatement = """
            SELECT [Fingerprint], [MigrationId], [PlatformVersion], [AppliedAt] FROM [dbo].[__TellmaSchema] ORDER BY [AppliedAt] DESC, [Fingerprint] DESC;
            """;

        /// <summary>Records the model's fingerprint in a migrated tenant database.</summary>
        /// <param name="connection">An open connection to the tenant database.</param>
        /// <param name="fingerprint">The model's storage fingerprint.</param>
        /// <param name="migrationId">The migrator's own last migration.</param>
        /// <param name="platformVersion">The running platform version; cut to the column's length.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>True when the database's history ends at <paramref name="migrationId" /> and the row is in place; false when the database is ahead.</returns>
        public static async Task<bool> RecordAsync(
            SqlConnection connection, string fingerprint, string migrationId, string platformVersion, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
            ArgumentException.ThrowIfNullOrWhiteSpace(migrationId);
            ArgumentNullException.ThrowIfNull(platformVersion);

            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = RecordStatement;
            _ = command.Parameters.Add(new SqlParameter("@fingerprint", SqlDbType.Char, 64) { Value = fingerprint });
            _ = command.Parameters.Add(new SqlParameter("@migrationId", SqlDbType.NVarChar, 150) { Value = migrationId });
            _ = command.Parameters.Add(new SqlParameter("@platformVersion", SqlDbType.NVarChar, ProvisioningRecords.MaxPlatformVersionLength)
            {
                Value = ProvisioningRecords.Fit(platformVersion),
            });

            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) == 1;
        }

        /// <summary>Reads every recorded fingerprint, newest first.</summary>
        /// <param name="connection">An open connection to the tenant database.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The rows.</returns>
        public static async Task<IReadOnlyList<SchemaFingerprintRow>> ReadAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);

            List<SchemaFingerprintRow> rows = [];
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = ReadStatement;
            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(new SchemaFingerprintRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetDateTimeOffset(3)));
            }

            return rows;
        }
    }
}
