// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Tenancy;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Access
{
    /// <summary>
    ///     Two tenant databases served by one composition, with no prologue stand-in: the real connect
    ///     prologue, caches and runner over both. The caches are the process's, so the two tenants
    ///     share every cache instance and must never share an entry. Each test seeds its own users.
    /// </summary>
    /// <param name="sql">The SQL Server the databases are created on.</param>
    public sealed class ConnectDatabase(SqlServerFixture sql) : IAsyncLifetime
    {
        /// <summary>The first tenant.</summary>
        public const int TenantA = 1;

        /// <summary>The second tenant, whose database reuses the first one's ids.</summary>
        public const int TenantB = 2;

        /// <summary>The next user id a test seeds, above the reserved band; the two databases share the sequence.</summary>
        private int _nextUserId = 1000;

        /// <summary>The tenants, by id.</summary>
        private Dictionary<int, TenantInfo>? _tenants;

        /// <summary>The composition over both databases.</summary>
        private ServiceProvider? _provider;

        /// <summary>The composition over both databases.</summary>
        public IServiceProvider Services => _provider ?? throw new InvalidOperationException("The databases are not created yet.");

        /// <summary>A tenant's descriptor, in a state.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="state">The state the request sees it in.</param>
        /// <returns>The descriptor.</returns>
        public static TenantDescriptor Descriptor(int tenantId, TenantState state = TenantState.Active)
        {
            return new TenantDescriptor(tenantId, string.Create(CultureInfo.InvariantCulture, $"Tenant {tenantId}"), null, null, ["en"], TenantCategory.Live, state, null);
        }

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            // --- The two tenants' locations ---
            SqlConnectionStringBuilder server = new(sql.MasterConnectionString);
            Dictionary<int, TenantInfo> tenants = [];
            foreach (int tenantId in new[] { TenantA, TenantB })
            {
                string name = sql.NewDatabaseName("fx_connect");
                string connectionString = new SqlConnectionStringBuilder(sql.MasterConnectionString) { InitialCatalog = name }.ConnectionString;
                tenants[tenantId] = new TenantInfo(Descriptor(tenantId), new TenantLocation(server.DataSource, name, "default"), connectionString, null, Guid.Empty);
            }

            _tenants = tenants;

            // --- The composition, with both tenants behind the connection seams ---
            ServiceCollection services = [];
            _ = services.AddLogging();
            _ = FixtureComposition.Compose(services);
            _ = services.Replace(ServiceDescriptor.Singleton<ITenantConnectionFactory>(new Factory(tenants)));
            _ = services.Replace(ServiceDescriptor.Scoped<ITenantConnectionProvider>(provider => new Provider(tenants, provider.GetRequiredService<IRequestContextAccessor>())));
            _provider = services.BuildServiceProvider(validateScopes: true);

            // --- The databases: migrated, the settings completed and every registered tag seeded ---
            string fingerprint = _provider.GetRequiredService<IEntityMetadataProvider>().StorageFingerprint;
            string tags = string.Join(", ", _provider.GetRequiredService<IVersionTagRegistry>().Names.Select(static name => $"(N'{name}')"));
            foreach ((int tenantId, TenantInfo tenant) in tenants)
            {
                _ = await FixtureDatabase.CreateAsync(sql.MasterConnectionString, tenant.Location.Database, fingerprint, readCommittedSnapshot: true, TestContext.Current.CancellationToken);
                await SqlServerFixture.ExecuteAsync(
                    tenant.ConnectionString,
                    string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Settings] SET [TenantId] = {tenantId}, [Name] = N'Tenant {tenantId}';"),
                    $"INSERT INTO [core].[VersionTags] ([Name], [Tag]) SELECT n.[Name], NEWID() FROM (VALUES {tags}) AS n ([Name]) WHERE NOT EXISTS (SELECT 1 FROM [core].[VersionTags] AS t WHERE t.[Name] = n.[Name]);");
            }
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _provider?.DisposeAsync() ?? ValueTask.CompletedTask;
        }

        /// <summary>A tenant database's connection string.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The connection string.</returns>
        public string ConnectionString(int tenantId)
        {
            return (_tenants ?? throw new InvalidOperationException("The databases are not created yet."))[tenantId].ConnectionString;
        }

        /// <summary>Runs statements on a tenant database.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="statements">The statements, one batch each.</param>
        /// <returns>A task that completes when they ran.</returns>
        public Task ExecuteAsync(int tenantId, params string[] statements)
        {
            return SqlServerFixture.ExecuteAsync(ConnectionString(tenantId), statements);
        }

        /// <summary>Reads one value from a tenant database.</summary>
        /// <typeparam name="T">The value's type.</typeparam>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="query">The query.</param>
        /// <returns>The value; default when null.</returns>
        public Task<T?> ScalarAsync<T>(int tenantId, string query)
        {
            return SqlServerFixture.ScalarAsync<T>(ConnectionString(tenantId), query);
        }

        /// <summary>A fresh user id, unused in either database.</summary>
        /// <returns>The id.</returns>
        public int NextUserId()
        {
            return Interlocked.Increment(ref _nextUserId);
        }

        /// <summary>
        ///     Seeds an active user, with its stamp and one role whose one permission it holds. An
        ///     individual is invited, and joined unless asked otherwise; a service account has joined.
        /// </summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="userId">The user's id.</param>
        /// <param name="subject">The user's subject.</param>
        /// <param name="name">The user's name.</param>
        /// <param name="kind">The user's kind: an individual or a service account.</param>
        /// <param name="joined">Whether an individual has joined.</param>
        /// <returns>A task that completes when the user is seeded.</returns>
        public Task SeedUserAsync(int tenantId, int userId, string subject, string name, UserKind kind = UserKind.Individual, bool joined = true)
        {
            string email = kind == UserKind.Individual ? $"N'{subject}@example.com'" : "NULL";
            string invitedAt = kind == UserKind.Individual ? "SYSDATETIMEOFFSET()" : "NULL";
            string joinedAt = joined || kind != UserKind.Individual ? "SYSDATETIMEOFFSET()" : "NULL";
            int system = WellKnownIds.SystemUserId;
            return ExecuteAsync(
                tenantId,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"""
                    INSERT INTO [core].[Users] ([Id], [Kind], [Subject], [Email], [InvitedAt], [JoinedAt], [Name], [IsActive], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById])
                    VALUES ({userId}, '{kind}', '{subject}', {email}, {invitedAt}, {joinedAt}, N'{name}', 1, SYSDATETIMEOFFSET(), {system}, SYSDATETIMEOFFSET(), {system});
                    INSERT INTO [core].[UserStamps] ([UserId]) VALUES ({userId});
                    DECLARE @role int = NEXT VALUE FOR [core].[sq_Roles];
                    INSERT INTO [core].[Roles] ([Id], [Name], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]) VALUES (@role, N'Role of {userId}', SYSDATETIMEOFFSET(), {system}, SYSDATETIMEOFFSET(), {system});
                    INSERT INTO [core].[Permissions] ([Id], [RoleId], [Resource], [Action]) VALUES (NEXT VALUE FOR [core].[sq_Permissions], @role, 'core.Users', 'Read');
                    INSERT INTO [core].[RoleMemberships] ([Id], [UserId], [RoleId]) VALUES (NEXT VALUE FOR [core].[sq_RoleMemberships], {userId}, @role);
                    """));
        }

        /// <summary>The connection factory over both databases.</summary>
        /// <param name="tenants">The tenants.</param>
        private sealed class Factory(Dictionary<int, TenantInfo> tenants) : ITenantConnectionFactory
        {
            /// <inheritdoc />
            public string GetConnectionString(int tenantId)
            {
                return tenants.TryGetValue(tenantId, out TenantInfo? tenant)
                    ? tenant.ConnectionString
                    : throw new InvalidOperationException($"The connect fixture serves tenants {TenantA} and {TenantB} only, not {tenantId}.");
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
                throw new NotSupportedException("The connect fixture has no catalog.");
            }
        }

        /// <summary>The scope's connections: to the database of the tenant the scope's context names.</summary>
        /// <param name="tenants">The tenants.</param>
        /// <param name="accessor">The scope's context.</param>
        private sealed class Provider(Dictionary<int, TenantInfo> tenants, IRequestContextAccessor accessor) : ITenantConnectionProvider
        {
            /// <inheritdoc />
            public TenantInfo Tenant => tenants[accessor.Current.RequireTenant().Id];

            /// <inheritdoc />
            public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken = default)
            {
                SqlConnection connection = new(Tenant.ConnectionString);
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
