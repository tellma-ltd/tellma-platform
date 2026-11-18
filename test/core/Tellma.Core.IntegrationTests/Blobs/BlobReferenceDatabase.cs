// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.IntegrationTests.Access;
using Tellma.Core.Testing.Blobs;

namespace Tellma.Core.IntegrationTests.Blobs
{
    /// <summary>
    ///     The two tenant databases of <see cref="ConnectDatabase" />, served by one instance with
    ///     in-memory blobs, and the helpers the blob-reference suites seed and read blob rows with.
    ///     Every blob row is seeded straight into <c>core.Blobs</c> with its id from the blob sequence
    ///     and its deadline relative to the database's clock, as the stage statement writes it. Each
    ///     test seeds its own callers, roles and rows.
    /// </summary>
    /// <param name="sql">The SQL Server the databases are created on.</param>
    public sealed class BlobReferenceDatabase(SqlServerFixture sql) : IAsyncLifetime
    {
        /// <summary>The root file kind of <c>fixture.BlobOwners</c>.</summary>
        public const string FileKind = "fixture-file";

        /// <summary>The root photo kind of <c>fixture.BlobOwners</c>.</summary>
        public const string PhotoKind = "fixture-photo";

        /// <summary>The child file kind of <c>fixture.BlobOwnerFiles</c>.</summary>
        public const string ChildFileKind = "fixture-child-file";

        /// <summary>The icon kind of the tree <c>fixture.BlobFolders</c>.</summary>
        public const string FolderIconKind = "fixture-folder-icon";

        /// <summary>A deadline an hour from the database's clock: a fresh staged row's.</summary>
        public const string InAnHour = "DATEADD(hour, 1, SYSUTCDATETIME())";

        /// <summary>The instance that serves the tests; null until initialised.</summary>
        private ServiceProvider? _instance;

        /// <summary>The two tenant databases and their composition.</summary>
        public ConnectDatabase Connect { get; } = new(sql);

        /// <summary>The instance, with in-memory blobs.</summary>
        public IServiceProvider Instance => _instance ?? throw new InvalidOperationException("The databases are not created yet.");

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            await Connect.InitializeAsync();
            _instance = Connect.CreateInstance(configure: static services => services.AddInMemoryBlobs());
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_instance is not null)
            {
                await _instance.DisposeAsync();
            }

            await Connect.DisposeAsync();
        }

        /// <summary>A request scope of a tenant on the instance, for a caller.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The scope.</returns>
        public Task<AsyncServiceScope> ScopeAsync(string subject, int tenantId = ConnectDatabase.TenantA)
        {
            return Connect.ScopeAsync(subject, tenantId: tenantId, services: Instance);
        }

        /// <summary>Seeds a caller of a tenant who is a member of one role holding the permissions.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="permissions">The role's permissions: a resource, an action and a filter, null for none.</param>
        /// <returns>The caller's id and subject.</returns>
        public async Task<(int UserId, string Subject)> SeedCallerAsync(int tenantId, params (string Resource, string Action, string? Filter)[] permissions)
        {
            int userId = Connect.NextUserId();
            string subject = string.Create(CultureInfo.InvariantCulture, $"sub-{userId}");
            await Connect.SeedUserAsync(tenantId, userId, subject, "Caller");
            int roleId = await Connect.SeedRoleAsync(tenantId, permissions);
            await Connect.SeedMembershipAsync(userId, roleId, tenantId: tenantId);
            if (tenantId == ConnectDatabase.TenantA)
            {
                await Connect.BumpPermissionsAsync(userId);
            }

            return (userId, subject);
        }

        /// <summary>Seeds a staged blob row, as the stage statement writes one.</summary>
        /// <param name="userId">The uploader.</param>
        /// <param name="kind">The kind.</param>
        /// <param name="expiresAt">The deadline, a SQL expression over the database's clock.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The blob's id.</returns>
        public Task<int> StageAsync(int userId, string kind, string expiresAt = InAnHour, int tenantId = ConnectDatabase.TenantA)
        {
            return SeedAsync(tenantId, kind, BlobState.Staged, userId, committedAt: "NULL", expiresAt);
        }

        /// <summary>Seeds a committed blob row, as a confirm leaves one, uploaded by the system user.</summary>
        /// <param name="kind">The kind.</param>
        /// <param name="committedAt">When it was committed, a SQL expression over the database's clock.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The blob's id.</returns>
        public Task<int> CommitAsync(string kind, string committedAt = "SYSUTCDATETIME()", int tenantId = ConnectDatabase.TenantA)
        {
            return SeedAsync(tenantId, kind, BlobState.Committed, WellKnownIds.SystemUserId, committedAt, expiresAt: "NULL");
        }

        /// <summary>
        ///     A blob row's state as <c>State|committed|expires</c>: the state, then <c>c</c> when it
        ///     carries a commit instant and <c>e</c> when it carries a deadline, <c>-</c> otherwise.
        /// </summary>
        /// <param name="id">The blob's id.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The state; null when no row holds the id.</returns>
        public Task<string?> StateAsync(int id, int tenantId = ConnectDatabase.TenantA)
        {
            return Connect.ScalarAsync<string>(tenantId, string.Create(CultureInfo.InvariantCulture, $"""
                SELECT CONCAT([State], N'|', IIF([CommittedAt] IS NULL, N'-', N'c'), N'|', IIF([ExpiresAt] IS NULL, N'-', N'e'))
                FROM [core].[Blobs] WHERE [Id] = {id};
                """));
        }

        /// <summary>Seeds one blob row with its id from the blob sequence and a fresh storage key.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="kind">The kind.</param>
        /// <param name="state">The state.</param>
        /// <param name="userId">The uploader.</param>
        /// <param name="committedAt">The commit instant, a SQL expression.</param>
        /// <param name="expiresAt">The deadline, a SQL expression.</param>
        /// <returns>The blob's id.</returns>
        private Task<int> SeedAsync(int tenantId, string kind, BlobState state, int userId, string committedAt, string expiresAt)
        {
            string key = Guid.NewGuid().ToString("N");
            string storageKey = BlobName.Primary(kind, key);
            return Connect.ScalarAsync<int>(tenantId, string.Create(CultureInfo.InvariantCulture, $"""
                DECLARE @id int = NEXT VALUE FOR [core].[sq_Blobs];
                INSERT INTO [core].[Blobs] ([Id], [Kind], [State], [ContentType], [FileName], [Size], [Variants], [CreatedAt], [CreatedById], [CommittedAt], [ExpiresAt], [Sha256], [StorageKey])
                VALUES (@id, N'{kind}', '{state}', N'{BlobContentTypes.Pdf}', N'file.pdf', 10, N'', SYSUTCDATETIME(), {userId}, {committedAt}, {expiresAt}, 0x{new string('0', 64)}, N'{storageKey}');
                SELECT @id;
                """));
        }
    }
}
