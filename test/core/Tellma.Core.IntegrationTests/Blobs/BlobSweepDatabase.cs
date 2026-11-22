// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Blobs;
using Tellma.Core.Data.Batches;
using Tellma.Core.IntegrationTests.Jobs;
using Tellma.Core.Testing.Blobs;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Blobs
{
    /// <summary>
    ///     One fixture database for the sweep and reconcile suites, created once and reset before each
    ///     test: a fixture host with in-memory blobs, a sweep batch of three rows so a test spans
    ///     several rounds, and a log that keeps every record. Blob rows are seeded straight into
    ///     <c>core.Blobs</c> with their instants relative to the database's clock, the one clock the
    ///     sweep's claim reads, and their objects into the store.
    /// </summary>
    /// <param name="sql">The suite's server.</param>
    public sealed class BlobSweepDatabase(SqlServerFixture sql) : IAsyncLifetime
    {
        /// <summary>The rows one sweep round claims.</summary>
        public const int SweepBatchSize = 3;

        /// <summary>The variant a seeded row lists when it has a thumbnail.</summary>
        public const string Thumb = "thumb";

        /// <summary>The statements that return the database to its state after creation.</summary>
        private const string ResetText = """
            DELETE FROM [fixture].[BlobOwnerFiles];
            DELETE FROM [fixture].[BlobOwners];
            DELETE FROM [core].[Blobs];
            DELETE FROM [core].[Jobs];
            """;

        /// <summary>The host, once created.</summary>
        private FixtureTenant? _tenant;

        /// <summary>The fixture host over the database.</summary>
        public FixtureTenant Tenant => _tenant ?? throw new InvalidOperationException("The database is not created yet.");

        /// <summary>The database's connection string.</summary>
        public string ConnectionString => Tenant.ConnectionString;

        /// <summary>The host's blob store.</summary>
        public InMemoryBlobStore Store => Tenant.Services.GetRequiredService<InMemoryBlobStore>();

        /// <summary>The host's log, debug records included.</summary>
        public FakeLogCollector Log => Tenant.Services.GetFakeLogCollector();

        /// <summary>The audit of every batch's writes, which a test suspends while a progress renewal overlaps its batches.</summary>
        internal SuspendableAudit Audit { get; } = new();

        /// <summary>The cancellation token of the running test.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            _tenant = await FixtureTenant.CreateAsync(
                sql.MasterConnectionString,
                sql.NewDatabaseName("fx_blob_sweep"),
                services =>
                {
                    // --- In-memory blobs, a small sweep batch, and the log ---
                    _ = services.AddInMemoryBlobs();
                    _ = services.PostConfigure<BlobOptions>(static options => options.SweepBatchSize = SweepBatchSize);
                    _ = services.AddFakeLogging();
                    _ = services.AddLogging(static logging => logging.SetMinimumLevel(LogLevel.Debug));

                    // --- The fixture's audit, behind a switch a test turns off while batches overlap ---
                    Audit.Inner = services.LastOrDefault(static service => service.ServiceType == typeof(IDataBatchAudit))?.ImplementationInstance as IDataBatchAudit;
                    _ = services.Replace(ServiceDescriptor.Singleton<IDataBatchAudit>(Audit));
                },
                cancellationToken: Token);

            // --- The tenant's settings and version tags ---
            string tags = string.Join(", ", Tenant.Services.GetRequiredService<IVersionTagRegistry>().Names.Select(static name => $"(N'{name}')"));
            await SqlServerFixture.ExecuteAsync(
                ConnectionString,
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Settings] SET [TenantId] = {FixtureTenant.TenantId}, [Name] = N'Fixture';"),
                $"INSERT INTO [core].[VersionTags] ([Name], [Tag]) SELECT n.[Name], NEWID() FROM (VALUES {tags}) AS n ([Name]) WHERE NOT EXISTS (SELECT 1 FROM [core].[VersionTags] AS t WHERE t.[Name] = n.[Name]);");
        }

        /// <summary>Deletes every row and object a test may have written, and returns the tenant and the store to their defaults.</summary>
        /// <returns>A task that completes when the database is reset.</returns>
        public async Task ResetAsync()
        {
            await SqlServerFixture.ExecuteAsync(ConnectionString, ResetText);
            Store.RemoveTenant(FixtureTenant.TenantId);
            Store.FailDeletes = null;
            Store.FailPurges = null;
            Store.Deleting = null;
            Store.ClearPurges();
            Tenant.State = TenantState.Active;
            Audit.Suspended = false;
            Log.Clear();
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _tenant?.DisposeAsync() ?? ValueTask.CompletedTask;
        }

        /// <summary>Seeds a blob row of the file kind, uploaded by the system user, and its objects when asked.</summary>
        /// <param name="state">The row's state.</param>
        /// <param name="expiresAt">The row's deadline or reclaim instant, a SQL expression over the database's clock; NULL for a committed row.</param>
        /// <param name="committedAt">The commit instant, a SQL expression; NULL for none.</param>
        /// <param name="thumbnail">Whether the row lists a thumbnail beside its primary.</param>
        /// <param name="stored">Whether the row's objects are written to the store.</param>
        /// <param name="kind">The row's kind.</param>
        /// <param name="createdAt">The staging instant, a SQL expression.</param>
        /// <param name="storageKey">The row's primary name; null for a fresh one of the kind.</param>
        /// <returns>The row's id and its objects' names.</returns>
        public async Task<SeededBlob> SeedAsync(
            BlobState state,
            string expiresAt,
            string committedAt = "NULL",
            bool thumbnail = false,
            bool stored = true,
            string kind = BlobReferenceDatabase.FileKind,
            string createdAt = "SYSUTCDATETIME()",
            string? storageKey = null)
        {
            storageKey ??= BlobName.Primary(kind, Guid.NewGuid().ToString("N"));
            string variants = thumbnail ? Thumb : string.Empty;
            int id = await SqlServerFixture.ScalarAsync<int>(ConnectionString, string.Create(CultureInfo.InvariantCulture, $"""
                DECLARE @id int = NEXT VALUE FOR [core].[sq_Blobs];
                INSERT INTO [core].[Blobs] ([Id], [Kind], [State], [ContentType], [FileName], [Size], [Variants], [CreatedAt], [CreatedById], [CommittedAt], [ExpiresAt], [Sha256], [StorageKey])
                VALUES (@id, N'{kind}', '{state}', N'{BlobContentTypes.Pdf}', N'file.pdf', 10, N'{variants}', {createdAt}, {WellKnownIds.SystemUserId}, {committedAt}, {expiresAt}, 0x{new string('0', 64)}, N'{storageKey}');
                SELECT @id;
                """));

            string[] names = thumbnail ? [storageKey, BlobName.Variant(storageKey, Thumb)] : [storageKey];
            if (stored)
            {
                await WriteAsync(names);
            }

            return new SeededBlob(id, names);
        }

        /// <summary>Writes objects into the store, each a few bytes.</summary>
        /// <param name="names">The objects' names.</param>
        /// <returns>A task that completes when written.</returns>
        public Task WriteAsync(params string[] names)
        {
            byte[] bytes = [1, 2, 3];
            return Store.WriteAsync(
                FixtureTenant.TenantId,
                [.. names.Select(name => new BlobWrite(name, BlobContentTypes.Pdf, new MemoryStream(bytes), bytes.Length))],
                Token);
        }

        /// <summary>A blob row's state, or null when no row holds the id.</summary>
        /// <param name="id">The blob's id.</param>
        /// <returns>The state.</returns>
        public Task<string?> StateAsync(int id)
        {
            return SqlServerFixture.ScalarAsync<string?>(ConnectionString, string.Create(CultureInfo.InvariantCulture, $"SELECT [State] FROM [core].[Blobs] WHERE [Id] = {id};"));
        }

        /// <summary>How many minutes from the database's clock a blob row's deadline or reclaim instant lies, or null when it has none.</summary>
        /// <param name="id">The blob's id.</param>
        /// <returns>The minutes, rounded down.</returns>
        public Task<int?> MinutesToExpiryAsync(int id)
        {
            return SqlServerFixture.ScalarAsync<int?>(
                ConnectionString,
                string.Create(CultureInfo.InvariantCulture, $"SELECT DATEDIFF(second, SYSUTCDATETIME(), [ExpiresAt]) / 60 FROM [core].[Blobs] WHERE [Id] = {id};"));
        }

        /// <summary>The ids of every blob row, in id order.</summary>
        /// <returns>The ids.</returns>
        public async Task<IReadOnlyList<int>> BlobIdsAsync()
        {
            List<int> ids = [];
            await using SqlConnection connection = new(ConnectionString);
            await connection.OpenAsync(Token);
            await using SqlCommand command = new("SELECT [Id] FROM [core].[Blobs] ORDER BY [Id];", connection);
            await using SqlDataReader reader = await command.ExecuteReaderAsync(Token);
            while (await reader.ReadAsync(Token))
            {
                ids.Add(reader.GetInt32(0));
            }

            return ids;
        }

        /// <summary>Inserts a due job of a handler, as a built-in schedule would, run by the system user.</summary>
        /// <param name="handlerKey">The handler's key.</param>
        /// <returns>The job's id.</returns>
        public Task<int> JobAsync(string handlerKey)
        {
            return SqlServerFixture.ScalarAsync<int>(ConnectionString, string.Create(CultureInfo.InvariantCulture, $"""
                DECLARE @id int = NEXT VALUE FOR [core].[sq_Jobs];
                INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [Attempts], [RunAsUserId], [CreatedAt])
                VALUES (@id, N'{handlerKey}', 'Pending', DATEADD(second, -5, SYSUTCDATETIME()), 0, {WellKnownIds.SystemUserId}, SYSUTCDATETIME());
                SELECT @id;
                """));
        }

        /// <summary>A job's status.</summary>
        /// <param name="jobId">The job.</param>
        /// <returns>The status's name.</returns>
        public Task<string?> JobStatusAsync(int jobId)
        {
            return SqlServerFixture.ScalarAsync<string?>(ConnectionString, string.Create(CultureInfo.InvariantCulture, $"SELECT [Status] FROM [core].[Jobs] WHERE [Id] = {jobId};"));
        }

        /// <summary>Runs one batch of the platform's own maintenance work on the tenant: a statement appended to a fresh maintenance batch.</summary>
        /// <typeparam name="T">What the statement returns.</typeparam>
        /// <param name="append">Appends the statement and returns its result.</param>
        /// <param name="untransacted">Whether the batch runs with no transaction of its own, as the sweep's claim does.</param>
        /// <returns>The statement's result.</returns>
        internal async Task<T> MaintenanceAsync<T>(Func<DataBatch, BatchResult<T>> append, bool untransacted = false)
        {
            await using AsyncServiceScope scope = Tenant.CreateScope(WellKnownIds.SystemUserId);
            var batch = (DataBatch)scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Maintenance);
            if (untransacted)
            {
                batch.TransactionMode = TransactionMode.None;
            }

            BatchResult<T> result = append(batch);
            _ = await batch.ExecuteAsync(Token);
            return result.Value;
        }
    }

    /// <summary>A seeded blob row.</summary>
    /// <param name="Id">The row's id.</param>
    /// <param name="Names">Its objects' names: the primary, then the thumbnail when it lists one.</param>
    public sealed record SeededBlob(int Id, IReadOnlyList<string> Names);
}
