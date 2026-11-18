// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Caching;
using Tellma.Core.Composition;
using Tellma.Core.Queryex;
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

        /// <summary>
        ///     The next user id a test seeds, far above the ids the users' sequence hands a save through
        ///     the pipeline, so the two never meet; the two databases share the counter.
        /// </summary>
        private int _nextUserId = 1_000_000;

        /// <summary>The tenants, by id.</summary>
        private Dictionary<int, TenantInfo>? _tenants;

        /// <summary>The composition over both databases.</summary>
        private ServiceProvider? _provider;

        /// <summary>The composition over both databases.</summary>
        public IServiceProvider Services => _provider ?? throw new InvalidOperationException("The databases are not created yet.");

        /// <summary>The composition's test-only contributor that evicts cache entries while a batch executes.</summary>
        internal CacheEvictor Evictor => Services.GetRequiredService<CacheEvictor>();

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
            _provider = Compose(tenants);

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

        /// <summary>
        ///     Another instance of the platform over the same databases: its own caches and single
        ///     flights, as a second process has. The caller disposes it.
        /// </summary>
        /// <param name="configure">Adjusts the instance's services last, such as its options; nothing when null.</param>
        /// <param name="compose">Adds features beside the fixture's, such as a test's contributors; nothing when null.</param>
        /// <returns>The instance's composition.</returns>
        public ServiceProvider CreateInstance(Action<IServiceCollection>? configure = null, Action<TellmaBuilder>? compose = null)
        {
            return Compose(_tenants ?? throw new InvalidOperationException("The databases are not created yet."), configure, compose);
        }

        /// <summary>
        ///     A scope of a tenant whose context names a caller, completed by the initializers as a
        ///     request's is: the connection at 100, then the locale negotiation.
        /// </summary>
        /// <param name="subject">The caller's subject; null for a background scope that carries a user id.</param>
        /// <param name="userId">The run-as user of a background scope; null for a request.</param>
        /// <param name="kind">The principal's kind.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="state">The state the request sees the tenant in.</param>
        /// <param name="background">Whether the request is a background one that stamps no activity.</param>
        /// <param name="services">The instance the scope is of; this fixture's own when null.</param>
        /// <param name="assurance">How the caller's session was authenticated, which a sensitive securable's step-up bar reads; none when null.</param>
        /// <returns>The scope.</returns>
        public async Task<AsyncServiceScope> ScopeAsync(
            string? subject,
            int? userId = null,
            PrincipalKind kind = PrincipalKind.User,
            int tenantId = TenantA,
            TenantState state = TenantState.Active,
            bool background = false,
            IServiceProvider? services = null,
            AuthenticationAssurance? assurance = null)
        {
            AsyncServiceScope scope = (services ?? Services).CreateAsyncScope();
            try
            {
                IRequestContextHolder holder = scope.ServiceProvider.GetRequiredService<IRequestContextHolder>();
                RequestContext context = holder.Current with { Tenant = Descriptor(tenantId, state), Kind = kind, Subject = subject, UserId = userId, Assurance = assurance };
                holder.Set(context);
                _ = await RequestContextInitialization.RunAsync(
                    holder,
                    scope.ServiceProvider.GetServices<IRequestContextInitializer>(),
                    context,
                    new RequestContextInputs(null, null, null, null, null, null, SuppressActivityStamp: background),
                    TestContext.Current.CancellationToken);
                return scope;
            }
            catch
            {
                await scope.DisposeAsync();
                throw;
            }
        }

        /// <summary>Seeds a user of the first tenant under a fresh id and subject.</summary>
        /// <param name="name">The user's name.</param>
        /// <param name="joined">Whether the user has joined.</param>
        /// <returns>The id and the subject.</returns>
        public async Task<(int UserId, string Subject)> SeedCallerAsync(string name, bool joined = true)
        {
            int userId = NextUserId();
            string subject = string.Create(CultureInfo.InvariantCulture, $"sub-{userId}");
            await SeedUserAsync(TenantA, userId, subject, name, joined: joined);
            return (userId, subject);
        }

        /// <summary>
        ///     Makes a tenant database read-only, as a frozen tenant's is, or writable again. The
        ///     database's pooled connections are dropped, since the change rolls back their sessions.
        /// </summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="readOnly">Whether the database becomes read-only.</param>
        /// <returns>A task that completes when the database changed.</returns>
        public async Task SetReadOnlyAsync(int tenantId, bool readOnly)
        {
            string database = (_tenants ?? throw new InvalidOperationException("The databases are not created yet."))[tenantId].Location.Database;
            await SqlServerFixture.ExecuteAsync(
                sql.MasterConnectionString,
                $"ALTER DATABASE [{database}] SET {(readOnly ? "READ_ONLY" : "READ_WRITE")} WITH ROLLBACK IMMEDIATE;");
            using SqlConnection pooled = new(ConnectionString(tenantId));
            SqlConnection.ClearPool(pooled);
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

        /// <summary>Seeds an active, private role of the first tenant with permissions.</summary>
        /// <param name="permissions">The permissions: a resource, an action and a filter, null for none.</param>
        /// <returns>The role's id.</returns>
        public Task<int> SeedRoleAsync(params (string Resource, string Action, string? Filter)[] permissions)
        {
            return SeedRoleAsync(TenantA, permissions);
        }

        /// <summary>Seeds an active, private role of a tenant with permissions.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="permissions">The permissions: a resource, an action and a filter, null for none.</param>
        /// <returns>The role's id.</returns>
        public Task<int> SeedRoleAsync(int tenantId, params (string Resource, string Action, string? Filter)[] permissions)
        {
            // --- The role, named after its id, then each permission with its filter's version ---
            int system = WellKnownIds.SystemUserId;
            StringBuilder sql = new();
            _ = sql.Append(CultureInfo.InvariantCulture, $"""
                DECLARE @role int = NEXT VALUE FOR [core].[sq_Roles];
                INSERT INTO [core].[Roles] ([Id], [Name], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]) VALUES (@role, N'Role ' + CAST(@role AS nvarchar(11)), SYSDATETIMEOFFSET(), {system}, SYSDATETIMEOFFSET(), {system});

                """);
            foreach ((string resource, string action, string? filter) in permissions)
            {
                string text = filter is null ? "NULL" : $"N'{filter.Replace("'", "''", StringComparison.Ordinal)}'";
                string version = filter is null ? "NULL" : QueryexLanguage.Version.ToString(CultureInfo.InvariantCulture);
                _ = sql.Append(CultureInfo.InvariantCulture, $"""
                    INSERT INTO [core].[Permissions] ([Id], [RoleId], [Resource], [Action], [Filter], [FilterQueryexVersion]) VALUES (NEXT VALUE FOR [core].[sq_Permissions], @role, N'{resource}', N'{action}', {text}, {version});

                    """);
            }

            _ = sql.Append("SELECT @role;");
            return ScalarAsync<int>(tenantId, sql.ToString());
        }

        /// <summary>Makes a user a member of a role over a window.</summary>
        /// <param name="userId">The user.</param>
        /// <param name="roleId">The role.</param>
        /// <param name="startsAt">The window's start as a SQL expression, such as <c>DATEADD(day, 1, SYSDATETIMEOFFSET())</c>; null since always.</param>
        /// <param name="endsAt">The window's exclusive end as a SQL expression; null for no end.</param>
        /// <param name="tenantId">The tenant of the user and the role; the first when not given.</param>
        /// <returns>A task that completes when the membership is seeded.</returns>
        public Task SeedMembershipAsync(int userId, int roleId, string? startsAt = null, string? endsAt = null, int tenantId = TenantA)
        {
            return ExecuteAsync(
                tenantId,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"INSERT INTO [core].[RoleMemberships] ([Id], [UserId], [RoleId], [StartsAt], [EndsAt]) VALUES (NEXT VALUE FOR [core].[sq_RoleMemberships], {userId}, {roleId}, {startsAt ?? "NULL"}, {endsAt ?? "NULL"});"));
        }

        /// <summary>Moves the first tenant's permissions tag and a user's own, as a save of the user's memberships does.</summary>
        /// <param name="userId">The user.</param>
        /// <returns>A task that completes when the tags moved.</returns>
        public Task BumpPermissionsAsync(int userId)
        {
            return ExecuteAsync(
                TenantA,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"UPDATE [core].[VersionTags] SET [Tag] = NEWID() WHERE [Name] = N'permissions'; UPDATE [core].[UserStamps] SET [PermissionsTag] = NEWID() WHERE [UserId] = {userId};"));
        }

        /// <summary>A composition of the platform, with both tenants behind the connection seams.</summary>
        /// <param name="tenants">The tenants.</param>
        /// <param name="configure">Adjusts the services last; nothing when null.</param>
        /// <param name="compose">Adds features beside the fixture's; nothing when null.</param>
        /// <returns>The composition.</returns>
        private static ServiceProvider Compose(Dictionary<int, TenantInfo> tenants, Action<IServiceCollection>? configure = null, Action<TellmaBuilder>? compose = null)
        {
            ServiceCollection services = [];
            _ = services.AddLogging();
            _ = FixtureComposition.Compose(services, compose: compose);
            _ = services.Replace(ServiceDescriptor.Singleton<ITenantConnectionFactory>(new Factory(tenants)));
            _ = services.Replace(ServiceDescriptor.Scoped<ITenantConnectionProvider>(provider => new Provider(tenants, provider.GetRequiredService<IRequestContextAccessor>())));
            _ = services.AddSingleton<CacheEvictor>();
            _ = services.AddSingleton<IDataBatchContributor>(static provider => provider.GetRequiredService<CacheEvictor>());
            configure?.Invoke(services);
            return services.BuildServiceProvider(validateScopes: true);
        }

        /// <summary>
        ///     A test-only batch contributor: while armed, every batch that executes drops a user's
        ///     cached permission set or profile in its epilogue stage, which runs after a connector
        ///     built its premises from the caches and before it applies what the prologue read.
        /// </summary>
        /// <param name="permissions">The permission sets.</param>
        /// <param name="profiles">The profiles.</param>
        internal sealed class CacheEvictor(PermissionsCache permissions, ProfileCache profiles) : IDataBatchContributor
        {
            /// <summary>The entry to drop; none while disarmed.</summary>
            private TenantUserCacheKey? _key;

            /// <summary>Whether the profile is dropped rather than the permission set.</summary>
            private bool _profile;

            /// <inheritdoc />
            public int Order => 1000;

            /// <summary>The entries dropped since the last arming.</summary>
            public int Evictions { get; private set; }

            /// <summary>Arms the contributor for one user's entry.</summary>
            /// <param name="key">The user's key.</param>
            /// <param name="profile">Whether the profile is dropped rather than the permission set.</param>
            public void Arm(TenantUserCacheKey key, bool profile)
            {
                (_key, _profile, Evictions) = (key, profile, 0);
            }

            /// <summary>Disarms the contributor.</summary>
            public void Disarm()
            {
                _key = null;
            }

            /// <inheritdoc />
            public void Contribute(IDataBatch batch, DataBatchStage stage)
            {
                if (stage != DataBatchStage.Epilogue || _key is not TenantUserCacheKey key)
                {
                    return;
                }

                if (_profile)
                {
                    profiles.Remove(key);
                }
                else
                {
                    permissions.Remove(key);
                }

                Evictions++;
            }
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
