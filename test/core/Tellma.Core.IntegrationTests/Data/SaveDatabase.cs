// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Data.Ids;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Data
{
    /// <summary>
    ///     One fixture database for the save and allocator tests of a class, created once and reset
    ///     before each test: every fixture row deleted, the allocator's buffers dropped, the log cleared.
    ///     A class's tests run one at a time, so the reset and the change-tracking audit see only the
    ///     running test's writes.
    /// </summary>
    /// <param name="sql">The suite's server.</param>
    public sealed class SaveDatabase(SqlServerFixture sql) : IAsyncLifetime
    {
        /// <summary>The statements that delete every row a save or a test's own statement may have written.</summary>
        private const string ResetText = """
            DELETE FROM [fixture].[WidgetPartNotes];
            DELETE FROM [fixture].[WidgetParts];
            DELETE FROM [fixture].[WidgetExtras];
            DELETE FROM [fixture].[Widgets];
            DELETE FROM [fixture].[Links];
            DELETE FROM [fixture].[Nodes];
            DELETE FROM [fixture].[Shipments];
            DELETE FROM [core].[Blobs];
            DELETE FROM [core].[Jobs];
            DELETE FROM [core].[VersionTags];
            DELETE FROM [dbo].[__TellmaSchema];
            """;

        /// <summary>The primary keys of the sequenced fixture tables, whose buffers each reset drops.</summary>
        private static readonly string[] PrimaryKeys = ["PK_Widgets", "PK_WidgetParts", "PK_WidgetPartNotes", "PK_Nodes", "PK_Links", "PK_Shipments"];

        /// <summary>The host, once created.</summary>
        private FixtureTenant? _tenant;

        /// <summary>The fixture host over the database.</summary>
        public FixtureTenant Tenant => _tenant ?? throw new InvalidOperationException("The database is not created yet.");

        /// <summary>The fixture database's connection string.</summary>
        public string ConnectionString => Tenant.ConnectionString;

        /// <summary>The host's id allocator, a singleton whose buffers outlive a test unless the reset drops them.</summary>
        internal IdAllocator Allocator => Tenant.Services.GetRequiredService<IdAllocator>();

        /// <summary>The host's log records.</summary>
        public FakeLogCollector Logs => Tenant.Services.GetFakeLogCollector();

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            _tenant = await FixtureTenant.CreateAsync(
                sql.MasterConnectionString,
                sql.NewDatabaseName("fx_save"),
                static services => services.AddFakeLogging(),
                cancellationToken: TestContext.Current.CancellationToken);
        }

        /// <summary>
        ///     Deletes every fixture row, lists the running fingerprint again, drops the allocator's
        ///     buffers of the fixture's sequences and clears the log.
        /// </summary>
        /// <returns>A task that completes when the database is reset.</returns>
        public async Task ResetAsync()
        {
            await SqlServerFixture.ExecuteAsync(ConnectionString, ResetText);
            await FixtureDatabase.RecordFingerprintAsync(ConnectionString, Tenant.Fingerprint, TestContext.Current.CancellationToken);

            // A duplicate primary key is what drops a sequence's buffer in production; a reset drops them the same way.
            foreach (string primaryKey in PrimaryKeys)
            {
                Allocator.OnDuplicateKey(FixtureTenant.TenantId, primaryKey);
            }

            Logs.Clear();
        }

        /// <summary>Creates a scope bound to the fixture tenant whose writes are stamped with the system user.</summary>
        /// <returns>The scope; the caller disposes it.</returns>
        public AsyncServiceScope CreateScope()
        {
            return Tenant.CreateScope(WellKnownIds.SystemUserId);
        }

        /// <summary>Stores committed blob rows, which blob-reference columns may point at.</summary>
        /// <param name="ids">The blobs' ids.</param>
        /// <returns>A task that completes when they are stored.</returns>
        public Task SeedBlobsAsync(params int[] ids)
        {
            return SqlServerFixture.ExecuteAsync(
                ConnectionString,
                [.. ids.Select(static id => string.Create(
                    CultureInfo.InvariantCulture,
                    $"INSERT INTO [core].[Blobs] ([Id], [Kind], [State], [ContentType], [Size], [Variants], [CreatedAt], [CreatedById], [CommittedAt], [Sha256], [StorageKey]) VALUES ({id}, N'fixture-image', 'Committed', N'image/png', 1, N'', SYSDATETIMEOFFSET(), {WellKnownIds.SystemUserId}, SYSDATETIMEOFFSET(), 0x{new string('0', 64)}, N'blob-{id}');"))]);
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _tenant?.DisposeAsync() ?? ValueTask.CompletedTask;
        }
    }
}
