// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;

namespace Tellma.Core.Migrator.Sql
{
    /// <summary>
    ///     An exclusive application lock in the catalog, owned by a session of its own: the catalog's
    ///     while the catalog migrates and tenants register, a tenant's while that tenant migrates or
    ///     provisions, so overlapping runs never interleave on one database.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The lock lives on a connection outside the pool, opened for it alone, and is released
    ///         explicitly before that connection closes, so it never outlives the work it guards in a
    ///         pooled session.
    ///     </para>
    ///     <para>
    ///         The server releases a session lock when the session ends, for instance on a failover
    ///         or a reset connection, and nothing tells the holder. The work therefore calls
    ///         <see cref="EnsureHeldAsync" /> before each write that another run must not interleave
    ///         with, and stops when the lock is gone.
    ///     </para>
    /// </remarks>
    internal sealed class MigrationLock : IAsyncDisposable
    {
        /// <summary>The catalog's lock resource.</summary>
        public const string CatalogResource = "tellma:migrate:catalog";

        /// <summary>How long a run waits for a lock another run holds: ten minutes.</summary>
        public const int TimeoutMilliseconds = 600_000;

        private const string AcquireStatement = """
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = @resource, @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = @timeout;
            SELECT @result;
            """;

        private const string ReleaseStatement = "EXEC sp_releaseapplock @Resource = @resource, @LockOwner = N'Session';";

        private const string ModeStatement = "SELECT APPLOCK_MODE(N'public', @resource, N'Session');";

        private readonly SqlConnection _connection;

        /// <summary>Creates the handle of a held lock.</summary>
        /// <param name="connection">The connection that holds it.</param>
        /// <param name="resource">The resource.</param>
        private MigrationLock(SqlConnection connection, string resource)
        {
            _connection = connection;
            Resource = resource;
        }

        /// <summary>The locked resource.</summary>
        public string Resource { get; }

        /// <summary>A tenant's lock resource.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The resource.</returns>
        public static string TenantResource(int tenantId)
        {
            return string.Create(CultureInfo.InvariantCulture, $"tellma:migrate:tenant:{tenantId}");
        }

        /// <summary>Takes a lock, waiting up to <see cref="TimeoutMilliseconds" /> for another run to release it.</summary>
        /// <param name="catalogConnectionString">The catalog's connection string.</param>
        /// <param name="resource">The resource.</param>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <returns>The held lock; disposing it releases it.</returns>
        /// <exception cref="MigratorFailureException">The lock was not granted in time.</exception>
        public static async Task<MigrationLock> AcquireAsync(string catalogConnectionString, string resource, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(catalogConnectionString);
            ArgumentException.ThrowIfNullOrWhiteSpace(resource);

            // --- A session of its own, outside the pool ---
            SqlConnectionStringBuilder builder = new(catalogConnectionString) { Pooling = false };
            SqlConnection connection = new(builder.ConnectionString);
            try
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = AcquireStatement;

                // The statement waits up to the lock's own timeout; the command outlasts it.
                command.CommandTimeout = (TimeoutMilliseconds / 1000) + 60;
                _ = command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = resource });
                _ = command.Parameters.Add(new SqlParameter("@timeout", SqlDbType.Int) { Value = TimeoutMilliseconds });

                // --- 0 granted at once, 1 granted after a wait; anything negative is a refusal ---
                int result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
                return result >= 0
                    ? new MigrationLock(connection, resource)
                    : throw new MigratorFailureException(
                        $"The migration lock '{resource}' was not granted (sp_getapplock returned {result}): another migrator run holds it.");
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>Confirms that the lock's session still holds the lock exclusively.</summary>
        /// <param name="cancellationToken">Cancels the check.</param>
        /// <returns>A task that completes when the lock is confirmed.</returns>
        /// <exception cref="MigratorFailureException">The lock's session ended or no longer holds the lock.</exception>
        public async Task EnsureHeldAsync(CancellationToken cancellationToken)
        {
            string? mode;
            try
            {
                await using SqlCommand command = _connection.CreateCommand();
                command.CommandText = ModeStatement;
                _ = command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = Resource });
                mode = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
            }
            catch (Exception exception) when (exception is SqlException or InvalidOperationException)
            {
                // A killed or broken session fails the command, or has already closed the connection.
                throw Lost(exception);
            }

            if (!string.Equals(mode, "Exclusive", StringComparison.Ordinal))
            {
                throw Lost(inner: null);
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            try
            {
                // Closing the session releases a session lock too; releasing first states the intent
                // and frees the resource even if the close is delayed.
                if (_connection.State == ConnectionState.Open)
                {
                    await using SqlCommand command = _connection.CreateCommand();
                    command.CommandText = ReleaseStatement;
                    _ = command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = Resource });
                    _ = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
            catch (SqlException)
            {
                // The session is closing anyway, and its end releases the lock.
            }
            finally
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>The failure of a lock whose session no longer holds it.</summary>
        /// <param name="inner">What the check failed with, or null when the session answered without the lock.</param>
        /// <returns>The failure.</returns>
        private MigratorFailureException Lost(Exception? inner)
        {
            string message = $"The migration lock '{Resource}' was lost: its catalog session ended, so another migrator run may hold it now. Nothing more was written; rerun the migrator.";
            return inner is null ? new MigratorFailureException(message) : new MigratorFailureException(message, inner);
        }
    }
}
