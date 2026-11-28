// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Data;

namespace Tellma.Core.Provisioning
{
    /// <summary>
    ///     Reads and writes a tenant database's record of completed provisioning steps,
    ///     <c>dbo.__TellmaProvisioning</c>: one row per step name with the version that last completed.
    /// </summary>
    /// <remarks>
    ///     The table is the migrator's alone, so these statements run as plain commands on a
    ///     connection the migrator opened, outside the data layer's batches.
    /// </remarks>
    internal static class ProvisioningRecords
    {
        /// <summary>The longest platform version the record holds.</summary>
        public const int MaxPlatformVersionLength = 64;

        /// <summary>Reads every recorded step and its completed version.</summary>
        private const string ReadStatement = """
            SELECT [Step], [Version] FROM [dbo].[__TellmaProvisioning];
            """;

        /// <summary>Records one completion: the step's row is updated, else inserted, in one transaction (no MERGE).</summary>
        private const string RecordStatement = """
            SET XACT_ABORT ON; BEGIN TRAN;
            UPDATE [dbo].[__TellmaProvisioning] WITH (UPDLOCK, HOLDLOCK)
               SET [Version] = @version, [CompletedAt] = SYSDATETIMEOFFSET(), [PlatformVersion] = @platformVersion
             WHERE [Step] = @step;
            IF @@ROWCOUNT = 0
                INSERT INTO [dbo].[__TellmaProvisioning] ([Step], [Version], [CompletedAt], [PlatformVersion])
                VALUES (@step, @version, SYSDATETIMEOFFSET(), @platformVersion);
            COMMIT;
            """;

        /// <summary>Reads every recorded step with its completed version.</summary>
        /// <param name="connection">An open connection to the tenant database.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The completed version of each recorded step, by name, compared without regard to case as the key column compares them.</returns>
        public static async Task<IReadOnlyDictionary<string, int>> ReadAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);

            Dictionary<string, int> recorded = new(StringComparer.OrdinalIgnoreCase);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = ReadStatement;
            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                recorded[reader.GetString(0)] = reader.GetInt32(1);
            }

            return recorded;
        }

        /// <summary>Records that a step completed at a version.</summary>
        /// <param name="connection">An open connection to the tenant database.</param>
        /// <param name="step">The step's name.</param>
        /// <param name="version">The version that completed.</param>
        /// <param name="platformVersion">The platform version that ran it; cut to the column's length.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>A task that completes when the row is committed.</returns>
        public static async Task RecordAsync(SqlConnection connection, string step, int version, string platformVersion, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentException.ThrowIfNullOrEmpty(step);
            ArgumentNullException.ThrowIfNull(platformVersion);

            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = RecordStatement;
            _ = command.Parameters.Add(new SqlParameter("@step", SqlDbType.NVarChar, ProvisioningStepNames.MaxLength) { Value = step });
            _ = command.Parameters.Add(new SqlParameter("@version", SqlDbType.Int) { Value = version });
            _ = command.Parameters.Add(new SqlParameter("@platformVersion", SqlDbType.NVarChar, MaxPlatformVersionLength) { Value = Fit(platformVersion) });
            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>A platform version cut to the column's length, which an unusually long build suffix could exceed.</summary>
        /// <param name="platformVersion">The platform version.</param>
        /// <returns>The version, at most <see cref="MaxPlatformVersionLength" /> characters.</returns>
        public static string Fit(string platformVersion)
        {
            ArgumentNullException.ThrowIfNull(platformVersion);
            return platformVersion.Length <= MaxPlatformVersionLength ? platformVersion : platformVersion[..MaxPlatformVersionLength];
        }
    }
}
