// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Testing;
using System.Diagnostics.Metrics;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Blobs;
using Tellma.Core.IntegrationTests.Data;
using Tellma.Core.Jobs;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Blobs
{
    /// <summary>
    ///     The sweep and the reconcile against a fixture database: the claim takes only rows past their
    ///     deadline and takes a lapsed claim again, the row delete removes only rows still deleting, the
    ///     sweep job erases released and expired blobs from storage and the database through the
    ///     worker's poll on a read-only tenant, a storage failure, a still-referenced row and a row
    ///     whose names fall outside the grammar each stay claimed and are metered, a failed purge is
    ///     metered without failing the run, a requested cancel finishes the round under way and claims
    ///     nothing more, the reconcile job releases old orphans of every kind, and both
    ///     built-in schedules are seeded rows. Every instant is relative to the database's clock, the
    ///     one the claim reads.
    /// </summary>
    /// <param name="fixture">The class's database, reset before each test.</param>
    [Trait("Category", "Integration")]
    public sealed class BlobSweepTests(BlobSweepDatabase fixture) : IAsyncLifetime, IClassFixture<BlobSweepDatabase>
    {
        /// <summary>The cancellation token of the running test.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            await fixture.ResetAsync();
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        // --- The claim and the row delete ---

        [Fact]
        public async Task The_claim_takes_only_rows_past_their_deadline_earliest_first_and_takes_a_lapsed_claim_again()
        {
            SeededBlob expired = await fixture.SeedAsync(BlobState.Staged, "DATEADD(minute, -3, SYSUTCDATETIME())");
            SeededBlob released = await fixture.SeedAsync(BlobState.Released, "DATEADD(minute, -2, SYSUTCDATETIME())");
            SeededBlob lapsed = await fixture.SeedAsync(BlobState.Deleting, "DATEADD(minute, -1, SYSUTCDATETIME())");
            SeededBlob fresh = await fixture.SeedAsync(BlobState.Staged, "DATEADD(hour, 1, SYSUTCDATETIME())");
            SeededBlob claimedElsewhere = await fixture.SeedAsync(BlobState.Deleting, "DATEADD(minute, 30, SYSUTCDATETIME())");
            SeededBlob committed = await fixture.SeedAsync(BlobState.Committed, "NULL", committedAt: "SYSUTCDATETIME()");

            // --- A claim of two takes the two earliest due rows, then the next claim the third ---
            IReadOnlyList<ClaimedBlob> first = await ClaimAsync(2);
            IReadOnlyList<ClaimedBlob> second = await ClaimAsync(10);

            Assert.Equal([(expired.Id, BlobState.Staged), (released.Id, BlobState.Released)], first.Select(static row => (row.Id, row.PreviousState)));
            Assert.Equal([(lapsed.Id, BlobState.Deleting)], second.Select(static row => (row.Id, row.PreviousState)));
            Assert.Equal((expired.Names[0], BlobReferenceDatabase.FileKind, string.Empty), (first[0].StorageKey, first[0].Kind, first[0].Variants));

            // --- Each claimed row is deleting until the reclaim span from now; the others are untouched ---
            foreach (SeededBlob row in new[] { expired, released, lapsed })
            {
                Assert.Equal(nameof(BlobState.Deleting), await fixture.StateAsync(row.Id));
                Assert.InRange(await fixture.MinutesToExpiryAsync(row.Id) ?? -1, 59, 60);
            }

            Assert.Equal(
                (nameof(BlobState.Staged), nameof(BlobState.Deleting), nameof(BlobState.Committed)),
                (await fixture.StateAsync(fresh.Id), await fixture.StateAsync(claimedElsewhere.Id), await fixture.StateAsync(committed.Id)));
            Assert.InRange(await fixture.MinutesToExpiryAsync(claimedElsewhere.Id) ?? -1, 29, 30);

            // --- Nothing more is due until a claim lapses; once its reclaim instant passed, it is taken again ---
            Assert.Empty(await ClaimAsync(10));
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Blobs] SET [ExpiresAt] = DATEADD(second, -1, SYSUTCDATETIME()) WHERE [Id] = {released.Id};"));
            Assert.Equal([(released.Id, BlobState.Deleting)], (await ClaimAsync(10)).Select(static row => (row.Id, row.PreviousState)));
        }

        [Fact]
        public async Task The_row_delete_removes_only_the_rows_still_deleting_and_a_second_run_removes_nothing()
        {
            SeededBlob deleting = await fixture.SeedAsync(BlobState.Deleting, "DATEADD(hour, 1, SYSUTCDATETIME())");
            SeededBlob staged = await fixture.SeedAsync(BlobState.Staged, "DATEADD(hour, 1, SYSUTCDATETIME())");
            SeededBlob committed = await fixture.SeedAsync(BlobState.Committed, "NULL", committedAt: "SYSUTCDATETIME()");
            int[] ids = [deleting.Id, staged.Id, committed.Id];

            Assert.Equal([deleting.Id], await fixture.MaintenanceAsync(batch => BlobStatements.DeleteSwept(batch, ids)));
            Assert.Empty(await fixture.MaintenanceAsync(batch => BlobStatements.DeleteSwept(batch, ids)));
            Assert.Equal([staged.Id, committed.Id], await fixture.BlobIdsAsync());
        }

        // --- The sweep job ---

        [Fact]
        public async Task The_sweep_erases_released_and_expired_blobs_from_storage_and_the_database_on_a_read_only_tenant()
        {
            // --- Four due rows over two rounds of three, and two rows the sweep must leave ---
            // The last row's objects were never written: a missing object is deleted all the same.
            _ = await fixture.SeedAsync(BlobState.Staged, "DATEADD(minute, -4, SYSUTCDATETIME())", thumbnail: true);
            _ = await fixture.SeedAsync(BlobState.Released, "DATEADD(minute, -3, SYSUTCDATETIME())");
            _ = await fixture.SeedAsync(BlobState.Released, "DATEADD(minute, -2, SYSUTCDATETIME())", thumbnail: true);
            _ = await fixture.SeedAsync(BlobState.Staged, "DATEADD(minute, -1, SYSUTCDATETIME())", stored: false);
            SeededBlob committed = await fixture.SeedAsync(BlobState.Committed, "NULL", committedAt: "SYSUTCDATETIME()", thumbnail: true);
            SeededBlob fresh = await fixture.SeedAsync(BlobState.Staged, "DATEADD(hour, 1, SYSUTCDATETIME())");

            // --- The tenant is read-only: the sweep runs, a job of a handler that does not is left pending ---
            fixture.Tenant.State = TenantState.ReadOnly;
            int sweep = await fixture.JobAsync(BlobSweepHandler.Key);
            int probe = await fixture.JobAsync(FixtureJobKeys.Probe);
            using MetricCollector<long> deleted = Collector(BlobTelemetryNames.SweepDeleted);
            using MetricCollector<long> failures = Collector(BlobTelemetryNames.SweepFailures);
            DateTimeOffset before = DateTimeOffset.UtcNow;

            PolledJob polled = Assert.Single(await JobPollDriver.PollOnceAsync(fixture.Tenant, keys: null, Token, Token));

            Assert.Equal((sweep, BlobSweepHandler.Key, PolledJobEnd.Completed, JobStatus.Succeeded), (polled.JobId, polled.HandlerKey, polled.End, polled.Status));
            Assert.Equal(nameof(JobStatus.Pending), await fixture.JobStatusAsync(probe));

            // --- Storage and the table keep only the committed and the fresh blobs ---
            Assert.Equal([.. committed.Names.Concat(fresh.Names).Order(StringComparer.Ordinal)], fixture.Store.Names(FixtureTenant.TenantId));
            Assert.Equal([committed.Id, fresh.Id], await fixture.BlobIdsAsync());

            // --- Deletions by the state each row was claimed from, no failure, one purge a day back ---
            Assert.Equal(
                [(BlobTelemetryTagValues.StateReleased, 2L), (BlobTelemetryTagValues.StateStaged, 2L)],
                deleted.GetMeasurementSnapshot()
                    .GroupBy(static measurement => (string)measurement.Tags[BlobTelemetryNames.StateTag]!)
                    .Select(static group => (group.Key, group.Sum(static measurement => measurement.Value)))
                    .Order());
            Assert.Empty(failures.GetMeasurementSnapshot());
            (int tenantId, DateTimeOffset olderThan) = Assert.Single(fixture.Store.Purges);
            Assert.Equal(FixtureTenant.TenantId, tenantId);
            Assert.InRange(olderThan, before - BlobSweepHandler.PurgeAge, DateTimeOffset.UtcNow - BlobSweepHandler.PurgeAge);

            // --- One progress report and one batch event per round: rows claimed, objects deleted ---
            Assert.Equal(["3 files collected, 5 stored objects deleted.", "4 files collected, 6 stored objects deleted."], Progress());
            Assert.Equal(
                ["3|3|0", "1|1|0"],
                Records("BlobSweepBatch").Select(static record => $"{Property(record, "Claimed")}|{Property(record, "Deleted")}|{Property(record, "Failed")}"));
        }

        [Fact]
        public async Task An_object_the_store_fails_to_delete_keeps_its_row_claimed_and_is_metered_while_the_others_are_swept()
        {
            SeededBlob failing = await fixture.SeedAsync(BlobState.Released, "DATEADD(minute, -2, SYSUTCDATETIME())", thumbnail: true);
            SeededBlob swept = await fixture.SeedAsync(BlobState.Released, "DATEADD(minute, -1, SYSUTCDATETIME())");
            fixture.Store.FailDeletes = name => name == failing.Names[1];
            int sweep = await fixture.JobAsync(BlobSweepHandler.Key);
            using MetricCollector<long> failures = Collector(BlobTelemetryNames.SweepFailures);

            PolledJob polled = Assert.Single(await JobPollDriver.PollOnceAsync(fixture.Tenant, [BlobSweepHandler.Key], Token, Token));

            // --- The job succeeds; the failing row stays claimed, its thumbnail stored, for the sweep after its reclaim instant ---
            Assert.Equal((sweep, JobStatus.Succeeded), (polled.JobId, polled.Status));
            Assert.Equal([failing.Id], await fixture.BlobIdsAsync());
            Assert.Equal(nameof(BlobState.Deleting), await fixture.StateAsync(failing.Id));
            Assert.InRange(await fixture.MinutesToExpiryAsync(failing.Id) ?? -1, 59, 60);
            Assert.Equal([failing.Names[1]], fixture.Store.Names(FixtureTenant.TenantId));
            Assert.Null(fixture.Store.Find(FixtureTenant.TenantId, swept.Names[0]));

            // --- One failure, metered and warned of with the store's exception ---
            Assert.Equal([1L], failures.GetMeasurementSnapshot().Select(static measurement => measurement.Value));
            FakeLogRecord warning = Assert.Single(Records("BlobSweepStoreFailed"));
            Assert.IsType<BlobStoreException>(warning.Exception);
            Assert.Equal((BlobTelemetryTagValues.StoreDelete, "1"), (Property(warning, "Operation"), Property(warning, "Rows")));
            Assert.Equal(["2|1|1"], Records("BlobSweepBatch").Select(static record => $"{Property(record, "Claimed")}|{Property(record, "Deleted")}|{Property(record, "Failed")}"));
        }

        [Fact]
        public async Task A_swept_row_a_row_still_references_is_retained_and_metered_while_the_others_are_deleted()
        {
            SeededBlob referenced = await fixture.SeedAsync(BlobState.Released, "DATEADD(minute, -2, SYSUTCDATETIME())");
            _ = await fixture.SeedAsync(BlobState.Released, "DATEADD(minute, -1, SYSUTCDATETIME())");
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                DataQueryDatabase.Stamped("[fixture].[BlobOwners]", "[Id], [Name], [FileId]", FormattableString.Invariant($"(1, N'owner', {referenced.Id})")));
            _ = await fixture.JobAsync(BlobSweepHandler.Key);
            using MetricCollector<long> failures = Collector(BlobTelemetryNames.SweepFailures);

            PolledJob polled = Assert.Single(await JobPollDriver.PollOnceAsync(fixture.Tenant, [BlobSweepHandler.Key], Token, Token));

            // --- Both rows' objects are gone; the referenced row stays claimed, the other is deleted ---
            Assert.Equal(JobStatus.Succeeded, polled.Status);
            Assert.Empty(fixture.Store.Names(FixtureTenant.TenantId));
            Assert.Equal([referenced.Id], await fixture.BlobIdsAsync());
            Assert.Equal(nameof(BlobState.Deleting), await fixture.StateAsync(referenced.Id));

            // --- The retained row is warned of by id and counted as a failure ---
            FakeLogRecord retained = Assert.Single(Records("BlobSweepRowRetained"));
            Assert.Equal(referenced.Id.ToString(CultureInfo.InvariantCulture), Property(retained, "Id"));
            Assert.Equal([1L], failures.GetMeasurementSnapshot().Select(static measurement => measurement.Value));
        }

        [Fact]
        public async Task A_claimed_row_whose_names_are_outside_the_grammar_stays_claimed_and_is_metered_while_the_others_are_swept()
        {
            // The key's kind and segment are upper case, which no staged row ever carries.
            string outside = BlobName.Primary(BlobReferenceDatabase.FileKind, Guid.NewGuid().ToString("N")).ToUpperInvariant();
            SeededBlob malformed = await fixture.SeedAsync(BlobState.Released, "DATEADD(minute, -2, SYSUTCDATETIME())", stored: false, storageKey: outside);
            SeededBlob swept = await fixture.SeedAsync(BlobState.Released, "DATEADD(minute, -1, SYSUTCDATETIME())");
            _ = await fixture.JobAsync(BlobSweepHandler.Key);
            using MetricCollector<long> failures = Collector(BlobTelemetryNames.SweepFailures);

            PolledJob polled = Assert.Single(await JobPollDriver.PollOnceAsync(fixture.Tenant, [BlobSweepHandler.Key], Token, Token));

            // --- The job succeeds; the malformed row stays claimed until its reclaim instant, the other is gone ---
            Assert.Equal(JobStatus.Succeeded, polled.Status);
            Assert.Equal([malformed.Id], await fixture.BlobIdsAsync());
            Assert.Equal(nameof(BlobState.Deleting), await fixture.StateAsync(malformed.Id));
            Assert.InRange(await fixture.MinutesToExpiryAsync(malformed.Id) ?? -1, 59, 60);
            Assert.Null(fixture.Store.Find(FixtureTenant.TenantId, swept.Names[0]));

            // --- One failure, counted in the round's event; the store was never asked about the malformed names ---
            Assert.Equal([1L], failures.GetMeasurementSnapshot().Select(static measurement => measurement.Value));
            Assert.Equal(["2|1|1"], Records("BlobSweepBatch").Select(static record => $"{Property(record, "Claimed")}|{Property(record, "Deleted")}|{Property(record, "Failed")}"));
            Assert.Empty(Records("BlobSweepStoreFailed"));
        }

        [Fact]
        public async Task A_failed_purge_is_metered_and_warned_of_and_the_sweep_still_succeeds()
        {
            SeededBlob swept = await fixture.SeedAsync(BlobState.Released, "DATEADD(minute, -1, SYSUTCDATETIME())");
            fixture.Store.FailPurges = new BlobStoreException("The purge failed.", null);
            int sweep = await fixture.JobAsync(BlobSweepHandler.Key);
            using MetricCollector<long> failures = Collector(BlobTelemetryNames.SweepFailures);

            PolledJob polled = Assert.Single(await JobPollDriver.PollOnceAsync(fixture.Tenant, [BlobSweepHandler.Key], Token, Token));

            // --- The rows were swept before the purge, and its failure does not fail the run ---
            Assert.Equal((sweep, JobStatus.Succeeded), (polled.JobId, polled.Status));
            Assert.Empty(await fixture.BlobIdsAsync());
            Assert.Null(fixture.Store.Find(FixtureTenant.TenantId, swept.Names[0]));
            _ = Assert.Single(fixture.Store.Purges);

            // --- One failure, metered and warned of with the store's exception, for no row ---
            Assert.Equal([1L], failures.GetMeasurementSnapshot().Select(static measurement => measurement.Value));
            FakeLogRecord warning = Assert.Single(Records("BlobSweepStoreFailed"));
            Assert.Same(fixture.Store.FailPurges, warning.Exception);
            Assert.Equal((BlobTelemetryTagValues.StorePurge, "0"), (Property(warning, "Operation"), Property(warning, "Rows")));
        }

        [Fact]
        public async Task A_cancel_during_a_rounds_store_delete_finishes_that_round_and_claims_nothing_more()
        {
            // --- Five due rows: a full round of three, then two more ---
            List<SeededBlob> rows = [];
            for (int minutes = 5; minutes >= 1; minutes--)
            {
                rows.Add(await fixture.SeedAsync(BlobState.Released, string.Create(CultureInfo.InvariantCulture, $"DATEADD(minute, -{minutes}, SYSUTCDATETIME())")));
            }

            // --- The job's cancel is requested, and seen by a renewal, while the first round's objects are being deleted ---
            int sweep = await fixture.JobAsync(BlobSweepHandler.Key);
            int deletes = 0;
            fixture.Store.Deleting = async _ =>
            {
                if (Interlocked.Increment(ref deletes) == 1)
                {
                    await SqlServerFixture.ExecuteAsync(
                        fixture.ConnectionString,
                        string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Jobs] SET [CancelRequestedAt] = SYSUTCDATETIME() WHERE [Id] = {sweep};"));
                    await fixture.Tenant.Services.GetRequiredService<IJobRenewal>().RenewAsync(FixtureTenant.TenantId, Token);
                }
            };

            PolledJob polled = Assert.Single(await JobPollDriver.PollOnceAsync(fixture.Tenant, [BlobSweepHandler.Key], Token, Token));

            // --- The job ends cancelled after one round ---
            Assert.Equal((sweep, PolledJobEnd.Completed, JobStatus.Cancelled), (polled.JobId, polled.End, polled.Status));
            Assert.Equal(1, deletes);

            // --- The first round's rows went with their objects, though the cancel came first ---
            Assert.Equal([.. rows.Skip(BlobSweepDatabase.SweepBatchSize).Select(static row => row.Id)], await fixture.BlobIdsAsync());
            Assert.All(rows.Take(BlobSweepDatabase.SweepBatchSize), row => Assert.Null(fixture.Store.Find(FixtureTenant.TenantId, row.Names[0])));

            // --- The later rows were never claimed: still released, still past their seeded deadline; no purge ran ---
            foreach (SeededBlob row in rows.Skip(BlobSweepDatabase.SweepBatchSize))
            {
                Assert.Equal(nameof(BlobState.Released), await fixture.StateAsync(row.Id));
                Assert.InRange(await fixture.MinutesToExpiryAsync(row.Id) ?? 1, -5, 0);
                Assert.NotNull(fixture.Store.Find(FixtureTenant.TenantId, row.Names[0]));
            }

            Assert.Empty(fixture.Store.Purges);
            Assert.Equal(["3|3|0"], Records("BlobSweepBatch").Select(static record => $"{Property(record, "Claimed")}|{Property(record, "Deleted")}|{Property(record, "Failed")}"));
        }

        [Fact]
        public async Task A_sweep_with_nothing_due_reports_one_empty_round_and_still_purges()
        {
            _ = await fixture.SeedAsync(BlobState.Staged, "DATEADD(hour, 1, SYSUTCDATETIME())");
            _ = await fixture.JobAsync(BlobSweepHandler.Key);

            PolledJob polled = Assert.Single(await JobPollDriver.PollOnceAsync(fixture.Tenant, [BlobSweepHandler.Key], Token, Token));

            Assert.Equal(JobStatus.Succeeded, polled.Status);
            Assert.Equal(["0 files collected, 0 stored objects deleted."], Progress());
            Assert.Empty(Records("BlobSweepBatch"));
            _ = Assert.Single(fixture.Store.Purges);
        }

        // --- The reconcile job ---

        [Fact]
        public async Task The_reconcile_releases_each_kinds_old_orphans_and_leaves_young_and_referenced_blobs_alone()
        {
            SeededBlob fileOrphan = await fixture.SeedAsync(BlobState.Committed, "NULL", committedAt: "DATEADD(day, -2, SYSUTCDATETIME())");
            SeededBlob photoOrphan = await fixture.SeedAsync(BlobState.Committed, "NULL", committedAt: "DATEADD(day, -2, SYSUTCDATETIME())", kind: BlobReferenceDatabase.PhotoKind);
            SeededBlob young = await fixture.SeedAsync(BlobState.Committed, "NULL", committedAt: "DATEADD(hour, -1, SYSUTCDATETIME())");
            SeededBlob referenced = await fixture.SeedAsync(BlobState.Committed, "NULL", committedAt: "DATEADD(day, -2, SYSUTCDATETIME())");
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                DataQueryDatabase.Stamped("[fixture].[BlobOwners]", "[Id], [Name], [FileId]", FormattableString.Invariant($"(1, N'owner', {referenced.Id})")));
            fixture.Tenant.State = TenantState.ReadOnly;
            int reconcile = await fixture.JobAsync(BlobReconcileHandler.Key);

            // The first kind's percent renews the job in the background while the next kind's round
            // trip runs; the audit attributes every change in a batch's window to that batch, so it is
            // suspended while the two overlap.
            fixture.Audit.Suspended = true;
            using MetricCollector<long> released = Collector(BlobTelemetryNames.ReconcileReleased);

            PolledJob polled = Assert.Single(await JobPollDriver.PollOnceAsync(fixture.Tenant, [BlobReconcileHandler.Key], Token, Token));

            Assert.Equal((reconcile, JobStatus.Succeeded), (polled.JobId, polled.Status));
            Assert.Equal(
                (nameof(BlobState.Released), nameof(BlobState.Released), nameof(BlobState.Committed), nameof(BlobState.Committed)),
                (await fixture.StateAsync(fileOrphan.Id), await fixture.StateAsync(photoOrphan.Id), await fixture.StateAsync(young.Id), await fixture.StateAsync(referenced.Id)));

            // --- Each kind's release is metered and warned of; progress names every kind ---
            Assert.Equal(
                [(BlobReferenceDatabase.FileKind, 1L), (BlobReferenceDatabase.PhotoKind, 1L)],
                released.GetMeasurementSnapshot().Select(static measurement => ((string)measurement.Tags[BlobTelemetryNames.KindTag]!, measurement.Value)).Order());
            Assert.Equal(
                [$"{BlobReferenceDatabase.FileKind}|1", $"{BlobReferenceDatabase.PhotoKind}|1"],
                Records("BlobReconcileReleased").Select(static record => $"{Property(record, "Kind")}|{Property(record, "Count")}").Order(StringComparer.Ordinal));
            int kinds = fixture.Tenant.Services.GetRequiredService<IBlobKindRegistry>().All.Count;
            Assert.Equal(kinds, Progress().Count);
            Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"{kinds} of {kinds} file kinds checked, 2 files released."), Progress()[^1]);
        }

        // --- The built-in schedules ---

        [Fact]
        public async Task Both_built_in_schedules_are_seeded_rows_run_by_the_system_user()
        {
            string? rows = await SqlServerFixture.ScalarAsync<string>(fixture.ConnectionString, """
                SELECT STRING_AGG(CONCAT([Id], N'|', [HandlerKey], N'|', [CronExpression], N'|', [OverlapPolicy], N'|', [MissedPolicy], N'|', [RunAsUserId]), N';')
                    WITHIN GROUP (ORDER BY [Id])
                FROM [core].[Schedules] WHERE [Id] IN (4, 5);
                """);

            Assert.Equal(
                string.Create(CultureInfo.InvariantCulture, $"4|core.blob-sweep|*/15 * * * *|Skip|Coalesce|{WellKnownIds.SystemUserId};5|core.blob-reconcile|0 2 * * 6|Skip|Coalesce|{WellKnownIds.SystemUserId}"),
                rows);
        }

        /// <summary>Runs the sweep's claim on a maintenance batch with no transaction of its own, an hour's reclaim span.</summary>
        /// <param name="size">The most rows to claim.</param>
        /// <returns>The claimed rows.</returns>
        private Task<IReadOnlyList<ClaimedBlob>> ClaimAsync(int size)
        {
            return fixture.MaintenanceAsync(batch => BlobStatements.Claim(batch, size, TimeSpan.FromMinutes(60)), untransacted: true);
        }

        /// <summary>A collector of one of the platform meter's instruments on the host.</summary>
        /// <param name="instrument">The instrument.</param>
        /// <returns>The collector; the caller disposes it.</returns>
        private MetricCollector<long> Collector(string instrument)
        {
            return new MetricCollector<long>(fixture.Tenant.Services.GetRequiredService<IMeterFactory>(), BlobTelemetryNames.MeterName, instrument);
        }

        /// <summary>The progress messages the jobs reported, rendered, in order.</summary>
        /// <returns>The messages.</returns>
        private List<string?> Progress()
        {
            return [.. Records("JobProgress").Select(static record => Property(record, "Message"))];
        }

        /// <summary>The log records of one event, in order.</summary>
        /// <param name="eventName">The event.</param>
        /// <returns>The records.</returns>
        private List<FakeLogRecord> Records(string eventName)
        {
            return [.. fixture.Log.GetSnapshot().Where(record => record.Id.Name == eventName)];
        }

        /// <summary>One structured property of a log record.</summary>
        /// <param name="record">The record.</param>
        /// <param name="name">The property.</param>
        /// <returns>Its value as text.</returns>
        private static string? Property(FakeLogRecord record, string name)
        {
            return record.StructuredState!.Single(pair => pair.Key == name).Value;
        }
    }
}
