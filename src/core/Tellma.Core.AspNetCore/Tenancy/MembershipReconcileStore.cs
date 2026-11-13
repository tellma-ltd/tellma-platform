// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Data;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Tenancy;

namespace Tellma.Core.AspNetCore.Tenancy
{
    /// <summary>
    ///     What the membership reconciliation reads and writes: the leader's lock on the catalog, a
    ///     tenant's users, and the tenant's hints. A seam so the timer's scheduling and leadership run
    ///     without a database in the unit suite.
    /// </summary>
    internal interface IMembershipReconcileStore
    {
        /// <summary>Takes the reconciliation's lock without waiting, so exactly one instance runs.</summary>
        /// <param name="cancellationToken">Cancels the attempt.</param>
        /// <returns>The lock, released when disposed; null when another instance holds it.</returns>
        Task<IAsyncDisposable?> TryLeadAsync(CancellationToken cancellationToken);

        /// <summary>Reads a tenant's users that have a subject: who should have a hint, and whether active.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The hints the tenant should have.</returns>
        Task<IReadOnlyList<TenantMembershipRecord>> ReadUsersAsync(int tenantId, CancellationToken cancellationToken);

        /// <summary>Rebuilds a tenant's hints in one catalog transaction.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="records">The hints it should have.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>How many hint rows changed.</returns>
        Task<int> ReconcileAsync(int tenantId, IReadOnlyList<TenantMembershipRecord> records, CancellationToken cancellationToken);
    }

    /// <summary>The reconciliation's reads and writes against the catalog and the tenants' databases.</summary>
    /// <param name="connections">Opens the catalog's and the tenants' connections.</param>
    /// <param name="directory">The membership hints.</param>
    internal sealed class SqlMembershipReconcileStore(ITenantConnectionFactory connections, TenantMembershipDirectory directory) : IMembershipReconcileStore
    {
        /// <summary>The application lock's resource on the catalog.</summary>
        public const string LockResource = "tellma:membership-reconcile";

        /// <summary>Takes the lock for the connection's session, without waiting; answers the procedure's result.</summary>
        internal const string AcquireLock = """
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0;
            SELECT @result;
            """;

        /// <summary>Releases the session's lock.</summary>
        internal const string ReleaseLock = """
            EXEC sp_releaseapplock @Resource = @resource, @LockOwner = 'Session';
            """;

        /// <summary>A tenant's users with a subject.</summary>
        internal const string ReadUsers = """
            SELECT [Subject], [IsActive] FROM [core].[Users] WHERE [Subject] IS NOT NULL;
            """;

        /// <inheritdoc />
        public async Task<IAsyncDisposable?> TryLeadAsync(CancellationToken cancellationToken)
        {
            // The lock lives as long as the connection's session, which the lease holds open.
            SqlConnection connection = await connections.OpenCatalogAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = AcquireLock;
                _ = command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = LockResource });
                object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                if (result is int code && code >= 0)
                {
                    return new Lease(connection);
                }
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            await connection.DisposeAsync().ConfigureAwait(false);
            return null;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<TenantMembershipRecord>> ReadUsersAsync(int tenantId, CancellationToken cancellationToken)
        {
            List<TenantMembershipRecord> records = [];
            SqlConnection connection = await connections.OpenAsync(tenantId, cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = ReadUsers;
                await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    records.Add(new TenantMembershipRecord { TenantId = tenantId, Subject = reader.GetString(0), IsActive = reader.GetBoolean(1) });
                }
            }

            return records.AsReadOnly();
        }

        /// <inheritdoc />
        public Task<int> ReconcileAsync(int tenantId, IReadOnlyList<TenantMembershipRecord> records, CancellationToken cancellationToken)
        {
            return directory.ReconcileAsync(tenantId, records, cancellationToken);
        }

        /// <summary>The held lock: released, then its connection closed.</summary>
        /// <param name="connection">The connection whose session holds the lock.</param>
        private sealed class Lease(SqlConnection connection) : IAsyncDisposable
        {
            /// <inheritdoc />
            public async ValueTask DisposeAsync()
            {
                try
                {
                    await using SqlCommand command = connection.CreateCommand();
                    command.CommandText = ReleaseLock;
                    _ = command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = LockResource });
                    _ = await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (SqlException)
                {
                    // A broken connection released the lock with its session.
                }
                finally
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
