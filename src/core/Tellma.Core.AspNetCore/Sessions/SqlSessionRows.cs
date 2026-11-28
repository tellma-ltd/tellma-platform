// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Data;
using Tellma.Core.Tenancy;

namespace Tellma.Core.AspNetCore.Sessions
{
    /// <summary>The session rows in the catalog's session table, one parameterised statement per operation.</summary>
    /// <param name="connections">Opens the catalog connection.</param>
    internal sealed class SqlSessionRows(ITenantConnectionFactory connections) : ISessionRows
    {
        /// <summary>The most rows one sweep statement deletes.</summary>
        public const int SweepBatchSize = 1000;

        /// <summary>The insert of a new session.</summary>
        internal const string Insert = """
            INSERT INTO [catalog].[Sessions] ([Key], [Handle], [Subject], [Sid], [Ticket], [TokensVersion], [CreatedAt], [RenewedAt], [ExpiresAt])
            VALUES (@key, @handle, @subject, @sid, @ticket, 0, SYSUTCDATETIME(), SYSUTCDATETIME(), @expiresAt);
            """;

        /// <summary>The primary-key read of a live session.</summary>
        internal const string Read = """
            SELECT [Subject], [Sid], [Ticket], [TokensVersion], [ExpiresAt] FROM [catalog].[Sessions]
             WHERE [Key] = @key AND [ExpiresAt] > SYSUTCDATETIME();
            """;

        /// <summary>The renewal, guarded by the tokens version.</summary>
        internal const string Renew = """
            UPDATE [catalog].[Sessions] SET [Ticket] = @ticket, [ExpiresAt] = @expiresAt, [RenewedAt] = SYSUTCDATETIME()
             WHERE [Key] = @key AND [TokensVersion] = @tokensVersion;
            """;

        /// <summary>The delete of one session.</summary>
        internal const string Delete = """
            DELETE FROM [catalog].[Sessions] WHERE [Key] = @key;
            """;

        /// <summary>The refresh's compare-and-set, which increments the tokens version.</summary>
        internal const string Replace = """
            UPDATE [catalog].[Sessions] SET [Ticket] = @ticket, [TokensVersion] = [TokensVersion] + 1, [ExpiresAt] = @expiresAt, [RenewedAt] = SYSUTCDATETIME()
            OUTPUT inserted.[TokensVersion]
             WHERE [Key] = @key AND [TokensVersion] = @expectedVersion;
            """;

        /// <summary>The revocation of an identity server session's sessions.</summary>
        internal const string DeleteBySid = """
            DELETE FROM [catalog].[Sessions] OUTPUT deleted.[Key], deleted.[Handle], deleted.[Subject] WHERE [Sid] = @sid;
            """;

        /// <summary>The revocation of a subject's sessions.</summary>
        internal const string DeleteBySubject = """
            DELETE FROM [catalog].[Sessions] OUTPUT deleted.[Key], deleted.[Handle], deleted.[Subject] WHERE [Subject] = @subject;
            """;

        /// <summary>One batch of the sweep: sessions that expired more than a day ago.</summary>
        internal const string Sweep = """
            DELETE TOP (1000) FROM [catalog].[Sessions] WHERE [ExpiresAt] < DATEADD(day, -1, SYSUTCDATETIME());
            """;

        /// <inheritdoc />
        public async Task InsertAsync(string key, string handle, string subject, string? sid, byte[] ticket, DateTimeOffset expiresAt, CancellationToken cancellationToken)
        {
            SqlConnection connection = await connections.OpenCatalogAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                await using SqlCommand command = Command(connection, Insert);
                AddKey(command, key);
                _ = command.Parameters.Add(new SqlParameter("@handle", SqlDbType.Char, SessionKeys.HandleLength) { Value = handle });
                _ = command.Parameters.Add(new SqlParameter("@subject", SqlDbType.VarChar, 255) { Value = subject });
                _ = command.Parameters.Add(new SqlParameter("@sid", SqlDbType.NVarChar, 128) { Value = (object?)sid ?? DBNull.Value });
                AddTicket(command, ticket, expiresAt);
                _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async Task<SessionRow?> ReadAsync(string key, CancellationToken cancellationToken)
        {
            SqlConnection connection = await connections.OpenCatalogAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                await using SqlCommand command = Command(connection, Read);
                AddKey(command, key);
                await using SqlDataReader reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
                return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                    ? new SessionRow(
                        reader.GetString(0),
                        reader.IsDBNull(1) ? null : reader.GetString(1),
                        (byte[])reader.GetValue(2),
                        reader.GetInt32(3),
                        reader.GetDateTimeOffset(4))
                    : null;
            }
        }

        /// <inheritdoc />
        public async Task<bool> RenewAsync(string key, byte[] ticket, DateTimeOffset expiresAt, int tokensVersion, CancellationToken cancellationToken)
        {
            SqlConnection connection = await connections.OpenCatalogAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                await using SqlCommand command = Command(connection, Renew);
                AddKey(command, key);
                AddTicket(command, ticket, expiresAt);
                _ = command.Parameters.Add(new SqlParameter("@tokensVersion", SqlDbType.Int) { Value = tokensVersion });
                return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
            }
        }

        /// <inheritdoc />
        public async Task<int?> ReplaceAsync(string key, byte[] ticket, DateTimeOffset expiresAt, int expectedVersion, CancellationToken cancellationToken)
        {
            SqlConnection connection = await connections.OpenCatalogAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                await using SqlCommand command = Command(connection, Replace);
                AddKey(command, key);
                AddTicket(command, ticket, expiresAt);
                _ = command.Parameters.Add(new SqlParameter("@expectedVersion", SqlDbType.Int) { Value = expectedVersion });

                // The OUTPUT clause answers the new version, or no row when the compare-and-set lost.
                object? version = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return version is int written ? written : null;
            }
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken)
        {
            SqlConnection connection = await connections.OpenCatalogAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                await using SqlCommand command = Command(connection, Delete);
                AddKey(command, key);
                return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
            }
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<EndedSession>> DeleteBySidAsync(string sid, CancellationToken cancellationToken)
        {
            return DeleteManyAsync(DeleteBySid, new SqlParameter("@sid", SqlDbType.NVarChar, 128) { Value = sid }, cancellationToken);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<EndedSession>> DeleteBySubjectAsync(string subject, CancellationToken cancellationToken)
        {
            return DeleteManyAsync(DeleteBySubject, new SqlParameter("@subject", SqlDbType.VarChar, 255) { Value = subject }, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<int> SweepBatchAsync(CancellationToken cancellationToken)
        {
            SqlConnection connection = await connections.OpenCatalogAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                await using SqlCommand command = Command(connection, Sweep);
                return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>A command over a statement.</summary>
        /// <param name="connection">The open catalog connection.</param>
        /// <param name="statement">The statement.</param>
        /// <returns>The command; the caller disposes it.</returns>
        private static SqlCommand Command(SqlConnection connection, string statement)
        {
            SqlCommand command = connection.CreateCommand();
            command.CommandText = statement;
            return command;
        }

        /// <summary>Binds the key, typed as the column is so the read seeks the primary key.</summary>
        /// <param name="command">The command.</param>
        /// <param name="key">The key.</param>
        private static void AddKey(SqlCommand command, string key)
        {
            _ = command.Parameters.Add(new SqlParameter("@key", SqlDbType.VarChar, 64) { Value = key });
        }

        /// <summary>Binds the protected ticket and the expiry.</summary>
        /// <param name="command">The command.</param>
        /// <param name="ticket">The protected ticket.</param>
        /// <param name="expiresAt">The expiry.</param>
        private static void AddTicket(SqlCommand command, byte[] ticket, DateTimeOffset expiresAt)
        {
            _ = command.Parameters.Add(new SqlParameter("@ticket", SqlDbType.VarBinary, -1) { Value = ticket });
            _ = command.Parameters.Add(new SqlParameter("@expiresAt", SqlDbType.DateTimeOffset) { Scale = 3, Value = expiresAt });
        }

        /// <summary>Runs a revocation, which answers the sessions it deleted.</summary>
        /// <param name="statement">The statement.</param>
        /// <param name="parameter">Its one parameter.</param>
        /// <param name="cancellationToken">Cancels the delete.</param>
        /// <returns>The sessions deleted.</returns>
        private async Task<IReadOnlyList<EndedSession>> DeleteManyAsync(string statement, SqlParameter parameter, CancellationToken cancellationToken)
        {
            List<EndedSession> ended = [];
            SqlConnection connection = await connections.OpenCatalogAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                await using SqlCommand command = Command(connection, statement);
                _ = command.Parameters.Add(parameter);
                await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    ended.Add(new EndedSession(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
                }
            }

            return ended.AsReadOnly();
        }
    }
}
