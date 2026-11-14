// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Jobs;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Jobs
{
    /// <summary>
    ///     One fixture database for a job test class, created once and reset before each test. Its
    ///     host audits every batch's writes against change tracking, so an enqueue that wrote a table
    ///     it did not declare fails its batch. A class's tests run one at a time, so the wake count
    ///     and the audit see only the running test's batches.
    /// </summary>
    /// <param name="sql">The suite's server.</param>
    public sealed class JobsDatabase(SqlServerFixture sql) : IAsyncLifetime
    {
        /// <summary>The statements that return the database to its state after creation, the seeded rows kept.</summary>
        /// <remarks>The fixture's built-in schedule and its state are seeded by the migrations, so they stay.</remarks>
        private static readonly string ResetText = string.Create(CultureInfo.InvariantCulture, $"""
            DELETE FROM [fixture].[Shipments];
            DELETE FROM [fixture].[Exports];
            DELETE FROM [core].[ScheduleStates] WHERE [ScheduleId] <> {FixtureFeature.ProbeScheduleId};
            DELETE FROM [core].[Jobs];
            DELETE FROM [core].[Schedules] WHERE [Id] <> {FixtureFeature.ProbeScheduleId};
            DELETE FROM [dbo].[__TellmaSchema];
            """);

        /// <summary>The host, once created.</summary>
        private FixtureTenant? _tenant;

        /// <summary>The subscription that counts the host's wakes of the fixture tenant.</summary>
        private IDisposable? _subscription;

        /// <summary>The wakes since the last reset.</summary>
        private int _wakes;

        /// <summary>The fixture host over the database.</summary>
        public FixtureTenant Tenant => _tenant ?? throw new InvalidOperationException("The database is not created yet.");

        /// <summary>The database's connection string.</summary>
        public string ConnectionString => Tenant.ConnectionString;

        /// <summary>How many times the host woke the fixture tenant's pollers since the last reset.</summary>
        public int Wakes => Volatile.Read(ref _wakes);

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            _tenant = await FixtureTenant.CreateAsync(
                sql.MasterConnectionString,
                sql.NewDatabaseName("fx_jobs"),
                cancellationToken: TestContext.Current.CancellationToken);

            // The wakes stand in for the poller a later host runs: each one counts.
            _subscription = _tenant.Services.GetRequiredService<TenantPollerWake>()
                .Subscribe(FixtureTenant.TenantId, () => Interlocked.Increment(ref _wakes));
        }

        /// <summary>Deletes every row a test may have written, lists the running fingerprint again, and zeroes the wakes.</summary>
        /// <returns>A task that completes when the database is reset.</returns>
        public async Task ResetAsync()
        {
            await SqlServerFixture.ExecuteAsync(ConnectionString, ResetText);
            await FixtureDatabase.RecordFingerprintAsync(ConnectionString, Tenant.Fingerprint, TestContext.Current.CancellationToken);
            _ = Interlocked.Exchange(ref _wakes, 0);
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            _subscription?.Dispose();
            return _tenant?.DisposeAsync() ?? ValueTask.CompletedTask;
        }
    }
}
