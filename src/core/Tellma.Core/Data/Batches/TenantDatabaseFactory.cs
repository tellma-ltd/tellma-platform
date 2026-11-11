// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Queryex;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Data.Batches
{
    /// <summary>Opens tenant databases outside any tenant scope, each holding one connection.</summary>
    /// <param name="executor">The executor.</param>
    /// <param name="connections">The tenant connection factory.</param>
    internal sealed class TenantDatabaseFactory(DataExecutor executor, ITenantConnectionFactory connections) : ITenantDatabaseFactory
    {
        /// <inheritdoc />
        public async Task<ITenantDatabaseHandle> OpenAsync(int tenantId, IServiceProvider services, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(services);

            SqlConnection connection = await connections.OpenAsync(tenantId, cancellationToken).ConfigureAwait(false);
            return new TenantDatabaseHandle(executor, services, tenantId, connection);
        }
    }

    /// <summary>
    ///     A tenant database bound to one tenant and one connection, for maintenance batches only. Its
    ///     batches take the connection one at a time.
    /// </summary>
    internal sealed class TenantDatabaseHandle : ITenantDatabaseHandle, IBatchConnection
    {
        /// <summary>The executor.</summary>
        private readonly DataExecutor _executor;

        /// <summary>The caller's scope.</summary>
        private readonly IServiceProvider _services;

        /// <summary>The handle's connection.</summary>
        private readonly SqlConnection _connection;

        /// <summary>Admits one round trip at a time to the connection.</summary>
        private readonly SemaphoreSlim _gate = new(1, 1);

        /// <summary>Whether the handle is disposed.</summary>
        private bool _disposed;

        /// <summary>Creates the handle.</summary>
        /// <param name="executor">The executor.</param>
        /// <param name="services">The caller's scope.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="connection">The open connection, which the handle owns.</param>
        public TenantDatabaseHandle(DataExecutor executor, IServiceProvider services, int tenantId, SqlConnection connection)
        {
            ArgumentNullException.ThrowIfNull(executor);
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(connection);

            _executor = executor;
            _services = services;
            TenantId = tenantId;
            _connection = connection;
        }

        /// <inheritdoc />
        public int TenantId { get; }

        /// <inheritdoc />
        public QueryexSchema Schema => throw NoRequestContext(nameof(Schema));

        /// <inheritdoc />
        public QueryexContextValues Context => throw NoRequestContext(nameof(Context));

        /// <inheritdoc />
        public IDataBatch CreateBatch(BatchPurpose purpose)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return purpose == BatchPurpose.Maintenance
                ? new DataBatch(_executor, this, _services, purpose)
                : throw new InvalidOperationException(
                    $"A tenant database opened outside a tenant scope creates maintenance batches only; a {purpose} batch runs in a tenant scope, on its scoped tenant database.");
        }

        /// <inheritdoc />
        public async ValueTask<SqlConnection> AcquireAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                // A severe error closes the connection; the next batch reopens it.
                if (_connection.State != System.Data.ConnectionState.Open)
                {
                    await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                }

                return _connection;
            }
            catch
            {
                _ = _gate.Release();
                throw;
            }
        }

        /// <inheritdoc />
        public ValueTask ReleaseAsync(SqlConnection connection)
        {
            ArgumentNullException.ThrowIfNull(connection);
            _ = _gate.Release();
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            // Waits for a running round trip, then closes the connection for good.
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                _disposed = true;
                await _connection.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _ = _gate.Release();
            }
        }

        /// <summary>The refusal of what needs a request context, which a handle has none of.</summary>
        /// <param name="member">The member.</param>
        /// <returns>The exception.</returns>
        private static InvalidOperationException NoRequestContext(string member)
        {
            return new InvalidOperationException(
                $"A tenant database opened outside a tenant scope has no request context, so it has no {member}. Its batches carry raw statements only.");
        }
    }
}
