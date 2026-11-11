// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using System.Data.Common;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.IntegrationTests.Infrastructure;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Data
{
    /// <summary>
    ///     One fixture database for a test class, created once and reset before each test, so a
    ///     class pays for one database however many tests it holds. A class's tests run one at a
    ///     time, so the reset and the change-tracking audit see only the running test's writes.
    /// </summary>
    /// <param name="sql">The suite's server.</param>
    public sealed class DataBatchDatabase(SqlServerFixture sql) : IAsyncLifetime
    {
        /// <summary>The statements that return the database to its state after creation.</summary>
        private const string ResetText = """
            DELETE FROM [core].[Jobs];
            DELETE FROM [core].[VersionTags];
            DELETE FROM [fixture].[WidgetExtras];
            DELETE FROM [dbo].[__TellmaSchema];
            """;

        /// <summary>The host, once created.</summary>
        private FixtureTenant? _tenant;

        /// <summary>The connect-prologue stand-in every batch of the host runs.</summary>
        public ConnectPrologue Prologue { get; } = new();

        /// <summary>The fixture host over the database.</summary>
        public FixtureTenant Tenant => _tenant ?? throw new InvalidOperationException("The database is not created yet.");

        /// <summary>The host's log records.</summary>
        public FakeLogCollector Logs => Tenant.Services.GetFakeLogCollector();

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            // Every round trip is slow, so each logs its text and a test can read the record.
            _tenant = await FixtureTenant.CreateAsync(
                sql.MasterConnectionString,
                sql.NewDatabaseName("fx_batch"),
                services =>
                {
                    _ = services.AddSingleton<IDataBatchContributor>(Prologue);
                    _ = services.AddFakeLogging();
                    _ = services.Configure<DataOptions>(static options => options.SlowRoundTripThreshold = TimeSpan.FromTicks(1));
                },
                cancellationToken: TestContext.Current.CancellationToken);
        }

        /// <summary>
        ///     Deletes every row a test may have written, lists the running fingerprint again, and
        ///     clears the log and the prologue's verdict.
        /// </summary>
        /// <returns>A task that completes when the database is reset.</returns>
        public async Task ResetAsync()
        {
            await SqlServerFixture.ExecuteAsync(Tenant.ConnectionString, ResetText);
            await FixtureDatabase.RecordFingerprintAsync(Tenant.ConnectionString, Tenant.Fingerprint, TestContext.Current.CancellationToken);
            Prologue.Passes = true;
            Logs.Clear();
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _tenant?.DisposeAsync() ?? ValueTask.CompletedTask;
        }

        /// <summary>
        ///     A stand-in for the connect prologue: on a marked batch it declares the guard variable with
        ///     the verdict the test chose, returns it, and reports it from its reader.
        /// </summary>
        public sealed class ConnectPrologue : IDataBatchContributor
        {
            /// <summary>The verdict the next batch's guard reports.</summary>
            public bool Passes { get; set; } = true;

            /// <inheritdoc />
            public int Order => 0;

            /// <inheritdoc />
            public void Contribute(IDataBatch batch, DataBatchStage stage)
            {
                if (stage != DataBatchStage.Prologue || batch is not DataBatch { CarriesConnectPrologue: true } marked)
                {
                    return;
                }

                StatementDraft draft = marked.Begin();
                int ordinal = draft.Ordinal;
                marked.Append(
                    draft,
                    $"DECLARE {ReservedSqlNames.Guard} bit = {(Passes ? 1 : 0)};\nSELECT {ReservedSqlNames.Guard} AS [Guard];",
                    reader: async (cursor, cancellationToken) =>
                    {
                        DbDataReader reader = await cursor.NextAsync(ordinal, cancellationToken);
                        _ = await reader.ReadAsync(cancellationToken);
                        marked.ReportGuard(reader.GetBoolean(0));
                        cursor.CountRows(1);
                    });
            }
        }
    }
}
