// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Data.Batches;
using Tellma.Core.Tenancy;

namespace Tellma.Testing.Entities
{
    /// <summary>
    ///     A fixture host bound to one fixture database as tenant <see cref="TenantId" />: the fixture
    ///     composition with the tenant connection seams pointed at the database, and scopes whose
    ///     request context binds the tenant for the platform's own principal, so a scope's
    ///     <see cref="ITenantDatabase" /> runs batches against the fixture database.
    /// </summary>
    public sealed class FixtureTenant : IAsyncDisposable
    {
        /// <summary>The fixture tenant's id.</summary>
        public const int TenantId = 1;

        /// <summary>The composed services.</summary>
        private readonly ServiceProvider _provider;

        /// <summary>Creates the host.</summary>
        /// <param name="provider">The composed services.</param>
        /// <param name="info">The tenant.</param>
        private FixtureTenant(ServiceProvider provider, TenantInfo info)
        {
            _provider = provider;
            Info = info;
        }

        /// <summary>The fixture database's connection string.</summary>
        public string ConnectionString => Info.ConnectionString;

        /// <summary>The running model's storage fingerprint.</summary>
        public string Fingerprint => _provider.GetRequiredService<IEntityMetadataProvider>().StorageFingerprint;

        /// <summary>The composed services.</summary>
        public IServiceProvider Services => _provider;

        /// <summary>The tenant, as the registry would describe it.</summary>
        internal TenantInfo Info { get; }

        /// <summary>
        ///     Composes a fixture host and creates its database: migrated, change-tracked, and listing
        ///     the host's fingerprint.
        /// </summary>
        /// <param name="masterConnectionString">A connection string to the server's <c>master</c> database.</param>
        /// <param name="databaseName">The new database's name; the caller drops it.</param>
        /// <param name="configureServices">Adds services after the composition, such as batch contributors.</param>
        /// <param name="audit">Whether every batch's writes are checked against change tracking.</param>
        /// <param name="readCommittedSnapshot">Whether the database reads committed rows from row versions, as provisioning sets it.</param>
        /// <param name="cancellationToken">Cancels the creation.</param>
        /// <returns>The host.</returns>
        public static async Task<FixtureTenant> CreateAsync(
            string masterConnectionString,
            string databaseName,
            Action<IServiceCollection>? configureServices = null,
            bool audit = true,
            bool readCommittedSnapshot = true,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(masterConnectionString);
            ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

            string connectionString = new SqlConnectionStringBuilder(masterConnectionString) { InitialCatalog = databaseName }.ConnectionString;
            SqlConnectionStringBuilder server = new(masterConnectionString);
            TenantInfo info = new(
                new TenantDescriptor(TenantId, "Fixture", null, null, ["en"], TenantCategory.Live, TenantState.Active, null),
                new TenantLocation(server.DataSource, databaseName, "default"),
                connectionString,
                null,
                Guid.Empty);

            // --- The composition, with the tenant connection seams on the fixture database ---
            ServiceCollection services = [];
            _ = services.AddLogging();
            _ = FixtureComposition.Compose(services);
            _ = services.Replace(ServiceDescriptor.Singleton<ITenantConnectionFactory>(new FixtureConnectionFactory(info)));
            _ = services.Replace(ServiceDescriptor.Scoped<ITenantConnectionProvider>(_ => new FixtureConnectionProvider(info)));
            if (audit)
            {
                _ = services.AddSingleton<IDataBatchAudit>(new ChangeTrackingAudit(connectionString));
            }

            configureServices?.Invoke(services);
            ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

            try
            {
                FixtureTenant tenant = new(provider, info);
                _ = await FixtureDatabase.CreateAsync(masterConnectionString, databaseName, tenant.Fingerprint, readCommittedSnapshot, cancellationToken).ConfigureAwait(false);
                return tenant;
            }
            catch
            {
                await provider.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        ///     Creates a scope bound to the fixture tenant for the platform's own principal, as the
        ///     tenant scope factory binds a system job's scope.
        /// </summary>
        /// <param name="userId">
        ///     The user the scope's writes are stamped with, such as the system user; none by default,
        ///     as a system job's scope has none until it names one.
        /// </param>
        /// <returns>The scope; the caller disposes it.</returns>
        public AsyncServiceScope CreateScope(int? userId = null)
        {
            AsyncServiceScope scope = _provider.CreateAsyncScope();
            IRequestContextHolder holder = scope.ServiceProvider.GetRequiredService<IRequestContextHolder>();
            holder.Set(holder.Current with { Tenant = Info.Descriptor, Kind = PrincipalKind.System, Subject = "system", UserId = userId });
            return scope;
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _provider.DisposeAsync();
        }

        /// <summary>Opens connections to the fixture database for the fixture tenant, outside any scope.</summary>
        /// <param name="info">The tenant.</param>
        private sealed class FixtureConnectionFactory(TenantInfo info) : ITenantConnectionFactory
        {
            /// <inheritdoc />
            public string GetConnectionString(int tenantId)
            {
                return tenantId == info.Id
                    ? info.ConnectionString
                    : throw new InvalidOperationException($"The fixture host serves tenant {info.Id} only, not {tenantId}.");
            }

            /// <inheritdoc />
            public async Task<SqlConnection> OpenAsync(int tenantId, CancellationToken cancellationToken = default)
            {
                SqlConnection connection = new(GetConnectionString(tenantId));
                try
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                    return connection;
                }
                catch
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            /// <inheritdoc />
            public Task<SqlConnection> OpenCatalogAsync(CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException("A fixture host has no catalog.");
            }
        }

        /// <summary>Opens connections to the fixture database within a scope.</summary>
        /// <param name="info">The tenant.</param>
        private sealed class FixtureConnectionProvider(TenantInfo info) : ITenantConnectionProvider
        {
            /// <inheritdoc />
            public TenantInfo Tenant => info;

            /// <inheritdoc />
            public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken = default)
            {
                SqlConnection connection = new(info.ConnectionString);
                try
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                    return connection;
                }
                catch
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }
        }
    }
}
