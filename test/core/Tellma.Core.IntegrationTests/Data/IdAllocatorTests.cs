// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using System.Data.Common;
using System.Diagnostics.Metrics;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Data.Ids;
using Tellma.Core.Data.Queries;
using Tellma.Core.Data.Saves;
using Tellma.Core.Data.TableTypes;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Data
{
    /// <summary>
    ///     The id allocator against a fixture database, with the server's own round-trip count as the
    ///     oracle: a warm buffer adds no round trip and a cold one adds one statement; ids return on a
    ///     validation failure and never after a persist; a sequence behind its table heals once; a
    ///     duplicate primary key is not retried and drops the buffer, another unique index does not;
    ///     <c>TakeAsync</c>; and the create, update and import budgets.
    /// </summary>
    /// <param name="fixture">The class's database, reset before each test.</param>
    [Trait("Category", "Integration")]
    public class IdAllocatorTests(SaveDatabase fixture) : IClassFixture<SaveDatabase>, IAsyncLifetime
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            await fixture.ResetAsync();
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }

        [Fact]
        public async Task A_create_on_a_warm_buffer_is_one_round_trip()
        {
            _ = await CreateAsync(NewWidget("warm-up"));
            Assert.Equal(64, fixture.Allocator.Buffered(FixtureTenant.TenantId, typeof(Widget)));
            await using AsyncServiceScope scope = fixture.CreateScope();
            CountingConnection connection = new(fixture.ConnectionString);

            // --- The reservation is served from the buffer: nothing rides the validation batch, which never runs ---
            Widget widget = NewWidget("w1");
            DataBatch validate = Batch(scope, connection, BatchPurpose.Validate);
            using IdReservation reservation = Allocator(scope).Reserve(validate, typeof(Widget), [widget]);
            Assert.True(reservation.IsImmediate);
            Assert.Empty(validate.Statements);
            reservation.Assign([widget]);

            DataBatch persist = Batch(scope, connection, BatchPurpose.Persist);
            _ = persist.Save([widget]);
            BatchOutcome outcome = await persist.ExecuteAsync(Token);

            Assert.Equal((1, 1L), (outcome.RoundTrips, connection.ServerRoundtrips));
            Assert.Equal(1, await CountAsync(widget.Id));
        }

        [Fact]
        public async Task A_cold_reservation_is_one_statement_on_the_batch_it_is_given()
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            CountingConnection connection = new(fixture.ConnectionString);
            Widget[] widgets = [NewWidget("w1"), NewWidget("w2")];

            DataBatch validate = Batch(scope, connection, BatchPurpose.Validate);
            using IdReservation reservation = Allocator(scope).Reserve(validate, typeof(Widget), widgets);
            Assert.False(reservation.IsImmediate);

            // The version-tag prelude rides the prologue; the reservation is the one statement after it.
            _ = Assert.Single(validate.Statements, static statement => statement.Section == BatchSection.Body);
            Assert.Contains("sp_sequence_get_range", validate.Statements[^1].Text, StringComparison.Ordinal);
            BatchOutcome reserved = await validate.ExecuteAsync(Token);
            reservation.Assign(widgets);

            DataBatch persist = Batch(scope, connection, BatchPurpose.Persist);
            _ = persist.Save(widgets);
            BatchOutcome persisted = await persist.ExecuteAsync(Token);

            // One round trip each; the buffer kept the low-water mark beyond the two ids.
            Assert.Equal((1, 1, 2L), (reserved.RoundTrips, persisted.RoundTrips, connection.ServerRoundtrips));
            Assert.Equal(widgets[0].Id + 1, widgets[1].Id);
            Assert.Equal(64, fixture.Allocator.Buffered(FixtureTenant.TenantId, typeof(Widget)));
        }

        [Fact]
        public async Task A_validation_failure_returns_the_ids_to_the_next_create()
        {
            _ = await CreateAsync(NewWidget("warm-up"));
            Widget[] rejected = [NewWidget("w1"), NewWidget("w2")];
            await using (AsyncServiceScope scope = fixture.CreateScope())
            {
                using IdReservation reservation = Allocator(scope).Reserve(Database(scope).CreateBatch(BatchPurpose.Validate), typeof(Widget), rejected);
                reservation.Assign(rejected);
                Assert.Equal(62, fixture.Allocator.Buffered(FixtureTenant.TenantId, typeof(Widget)));
            }

            // The reservation was disposed with no persist attempted: its ids are the next ones handed out.
            Assert.Equal(64, fixture.Allocator.Buffered(FixtureTenant.TenantId, typeof(Widget)));
            Widget[] accepted = [NewWidget("w3"), NewWidget("w4")];
            _ = await CreateAsync(accepted);
            Assert.Equal(rejected.Select(static widget => widget.Id), accepted.Select(static widget => widget.Id));
        }

        [Fact]
        public async Task A_persist_failure_keeps_the_ids_and_a_duplicate_on_another_unique_index_does_not_drop_the_buffer()
        {
            _ = await CreateAsync(NewWidget("taken"));
            Widget duplicate = NewWidget("taken");

            UniqueConstraintViolationException violation = await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => CreateAsync(duplicate));

            // The id may have reached the table, so it is never handed out again; the buffer stands.
            Assert.Equal("UX_Widgets_Subject", violation.IndexName);
            Assert.Equal(63, fixture.Allocator.Buffered(FixtureTenant.TenantId, typeof(Widget)));
            Widget next = NewWidget("next");
            _ = await CreateAsync(next);
            Assert.Equal(duplicate.Id + 1, next.Id);
            Assert.Empty(Logged("IdAllocator.BufferDropped"));
            Assert.Empty(Logged("IdAllocator.Healed"));
        }

        [Fact]
        public async Task A_sequence_behind_its_table_heals_once_by_consuming_the_gap()
        {
            int behind = await InsertAheadOfSequenceAsync(10);
            using MetricCollector<long> healed = Collector(DataTelemetryNames.IdHealed);
            await using AsyncServiceScope scope = fixture.CreateScope();
            CountingConnection connection = new(fixture.ConnectionString);

            // --- The reservation reads a range under the table's largest id: one more round trip consumes the gap ---
            Widget widget = NewWidget("w1");
            DataBatch validate = Batch(scope, connection, BatchPurpose.Validate);
            using IdReservation reservation = Allocator(scope).Reserve(validate, typeof(Widget), [widget]);
            BatchOutcome reserved = await validate.ExecuteAsync(Token);
            reservation.Assign([widget]);
            DataBatch persist = Batch(scope, connection, BatchPurpose.Persist);
            _ = persist.Save([widget]);
            _ = await persist.ExecuteAsync(Token);

            // The first range (1 + 64 ids) is discarded whole; the heal's range starts past it, above the table.
            Assert.Equal((2, 3L), (reserved.RoundTrips, connection.ServerRoundtrips));
            Assert.Equal(behind - 10 + 65 + 1, widget.Id);
            FakeLogRecord record = Assert.Single(Logged("IdAllocator.Healed"));
            Assert.Equal(LogLevel.Warning, record.Level);
            Assert.Equal(1, Assert.Single(healed.GetMeasurementSnapshot()).Value);

            // --- The buffer holds only ids above the table's: the next create neither heals nor collides ---
            Widget next = NewWidget("w2");
            _ = await CreateAsync(next);
            Assert.Equal(widget.Id + 1, next.Id);
            _ = Assert.Single(Logged("IdAllocator.Healed"));
        }

        [Fact]
        public async Task A_heal_replaces_the_ids_the_reservation_took_from_the_buffer_at_or_below_the_table()
        {
            // --- The buffer holds ids the table also holds: one inside it, and the table runs ahead of the sequence ---
            Widget warmUp = NewWidget("warm-up");
            _ = await CreateAsync(warmUp);
            await InsertWidgetAsync(warmUp.Id + 5);
            int ahead = await InsertAheadOfSequenceAsync(10);

            // --- A create that empties the buffer and reads the rest: the heal replaces every taken id ---
            Widget[] widgets = [.. Enumerable.Range(0, 70).Select(static index => NewWidget(string.Create(CultureInfo.InvariantCulture, $"w{index}")))];
            SaveReceipt receipt = await CreateAsync(widgets);

            Assert.Equal(70, receipt.Inserted);
            Assert.All(widgets, widget => Assert.True(widget.Id > ahead));
            _ = Assert.Single(Logged("IdAllocator.Healed"));
        }

        [Fact]
        public async Task A_refill_that_finds_the_sequence_behind_reports_it_and_drops_the_buffered_ids_below_the_table()
        {
            Widget warmUp = NewWidget("warm-up");
            _ = await CreateAsync(warmUp);
            await InsertWidgetAsync(warmUp.Id + 3);
            int ahead = await InsertAheadOfSequenceAsync(10);
            using MetricCollector<long> healed = Collector(DataTelemetryNames.IdHealed);

            // --- A create served from the buffer carries the refill, whose range starts under the table ---
            Widget served = NewWidget("served");
            _ = await CreateAsync(served);

            Assert.Equal(warmUp.Id + 1, served.Id);
            FakeLogRecord record = Assert.Single(Logged("IdAllocator.BehindOnRefill"));
            Assert.Equal(LogLevel.Warning, record.Level);
            Assert.Equal(1, Assert.Single(healed.GetMeasurementSnapshot()).Value);
            Assert.Equal(0, fixture.Allocator.Buffered(FixtureTenant.TenantId, typeof(Widget)));

            // --- The next create reuses no dropped id, collides with nothing and heals ---
            Widget next = NewWidget("next");
            _ = await CreateAsync(next);
            Assert.True(next.Id > ahead);
            _ = Assert.Single(Logged("IdAllocator.Healed"));
        }

        [Fact]
        public async Task A_heals_newer_version_tags_are_not_replaced_by_the_older_tags_of_the_batch_that_healed()
        {
            _ = await InsertAheadOfSequenceAsync(10);
            await using AsyncServiceScope scope = fixture.CreateScope();
            RecordingSnapshots snapshots = new();
            TagReadingContributor tags = new();
            DataExecutor executor = ExecutorWith(snapshots);
            ContributorScope services = new(scope.ServiceProvider, tags);

            // --- The reservation's batch reads its tags first; the heal it triggers reads newer ones ---
            Widget widget = NewWidget("w1");
            DataBatch validate = new(executor, new CountingConnection(fixture.ConnectionString), services, BatchPurpose.Validate);
            using IdReservation reservation = Allocator(scope).Reserve(validate, typeof(Widget), [widget]);
            BatchOutcome outcome = await validate.ExecuteAsync(Token);
            reservation.Assign([widget]);

            // The heal's snapshot replaced the reservation batch's, never the other way round.
            Assert.Equal(2, outcome.RoundTrips);
            Assert.Equal(2, tags.Read.Count);
            Assert.Equal(tags.Read, snapshots.Replaced);
            Assert.Same(tags.Read[1], snapshots.Current(FixtureTenant.TenantId));
            _ = Assert.Single(Logged("IdAllocator.Healed"));
        }

        [Fact]
        public async Task A_duplicate_primary_key_is_not_retried_and_empties_the_buffer_so_the_next_create_heals()
        {
            // --- An id the buffer will hand out next is taken out of band, and the table runs ahead of the sequence ---
            _ = await CreateAsync(NewWidget("warm-up"));
            Widget probe = NewWidget("probe");
            await using (AsyncServiceScope scope = fixture.CreateScope())
            {
                using IdReservation reservation = Allocator(scope).Reserve(Database(scope).CreateBatch(BatchPurpose.Validate), typeof(Widget), [probe]);
                reservation.Assign([probe]);
            }

            await InsertWidgetAsync(probe.Id);
            int ahead = await InsertAheadOfSequenceAsync(5);

            // --- The create that receives the taken id fails once, unretried, and drops the buffer ---
            await using (AsyncServiceScope scope = fixture.CreateScope())
            {
                DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
                Widget colliding = NewWidget("colliding");
                UniqueConstraintViolationException violation = await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => CreateAsync(colliding, scope));
                Assert.Equal(probe.Id, colliding.Id);
                Assert.Equal("PK_Widgets", violation.IndexName);
                Assert.Equal(0, access.Retries);
            }

            Assert.Equal(0, fixture.Allocator.Buffered(FixtureTenant.TenantId, typeof(Widget)));
            _ = Assert.Single(Logged("IdAllocator.BufferDropped"));

            // --- The next create reads the table afresh and heals ---
            Widget next = NewWidget("next");
            _ = await CreateAsync(next);
            Assert.True(next.Id > ahead);
            _ = Assert.Single(Logged("IdAllocator.Healed"));
        }

        [Fact]
        public async Task Take_pays_one_round_trip_on_an_empty_buffer_and_none_on_a_warm_one()
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            IIdAllocator allocator = Allocator(scope);
            ITenantDatabase database = Database(scope);

            IReadOnlyList<object> cold = await allocator.TakeAsync(database, typeof(Widget), 3, Token);
            Assert.Equal(1, access.RoundTrips);
            IReadOnlyList<object> warm = await allocator.TakeAsync(database, typeof(Widget), 3, Token);
            Assert.Equal(1, access.RoundTrips);

            int first = Assert.IsType<int>(cold[0]);
            Assert.Equal(Enumerable.Range(first, 6).Cast<object>(), cold.Concat(warm));

            // A long sequence hands out longs.
            IReadOnlyList<object> shipments = await allocator.TakeAsync(database, typeof(Shipment), 2, Token);
            _ = Assert.IsType<long>(shipments[0]);
            Assert.Equal(2, access.RoundTrips);
        }

        [Fact]
        public async Task An_update_with_children_and_an_import_chunk_are_two_round_trips_each_and_a_validation_round_adds_one()
        {
            Widget existing = NewWidget("existing", new WidgetPart { Ordinal = 1, Notes = "a" });
            _ = await CreateAsync(existing);

            // --- Update: before images, the grant count's stand-in and the reservation of a new child ride round trip 1; the persist is 2 ---
            existing.Name = "edited";
            existing.Parts!.Add(new WidgetPart { Ordinal = 2, Notes = "b" });
            Assert.Equal((2, 2L), await SaveInTwoAsync([existing], validationRounds: 1));

            // --- Import chunk: new and existing roots alike ---
            existing.Name = "imported";
            Widget[] chunk = [existing, NewWidget("i1", new WidgetPart { Ordinal = 1 }), NewWidget("i2")];
            Assert.Equal((2, 2L), await SaveInTwoAsync(chunk, validationRounds: 1));

            // --- A second, dependent validation round is one more ---
            existing.Name = "revalidated";
            Assert.Equal((3, 3L), await SaveInTwoAsync([existing], validationRounds: 2));
            Assert.Equal(3, await QueryAsync<int>("SELECT COUNT(*) FROM [fixture].[Widgets];"));
        }

        /// <summary>A new widget.</summary>
        /// <param name="subject">The write-once subject, unique.</param>
        /// <param name="parts">The parts.</param>
        /// <returns>The widget.</returns>
        private static Widget NewWidget(string subject, params WidgetPart[] parts)
        {
            return new Widget { Subject = subject, Name = subject, Parts = [.. parts] };
        }

        /// <summary>The scope's allocator.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The allocator.</returns>
        private static IIdAllocator Allocator(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<IIdAllocator>();
        }

        /// <summary>The scope's tenant database.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The database.</returns>
        private static ITenantDatabase Database(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<ITenantDatabase>();
        }

        /// <summary>A batch of the scope on a connection whose server round trips the test counts.</summary>
        /// <param name="scope">The scope, which supplies the contributors, the counters and the audit.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="purpose">The purpose.</param>
        /// <returns>The batch.</returns>
        private DataBatch Batch(AsyncServiceScope scope, CountingConnection connection, BatchPurpose purpose)
        {
            return new DataBatch(fixture.Tenant.Services.GetRequiredService<DataExecutor>(), connection, scope.ServiceProvider, purpose);
        }

        /// <summary>Reserves ids for new widgets on a validation batch, runs it when it carries the reservation, and saves them.</summary>
        /// <param name="widgets">The widgets.</param>
        /// <returns>The receipt.</returns>
        private async Task<SaveReceipt> CreateAsync(params Widget[] widgets)
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            return await CreateAsync(widgets, scope);
        }

        /// <summary>Reserves ids for new widgets in a scope and saves them.</summary>
        /// <param name="widget">The widget.</param>
        /// <param name="scope">The scope.</param>
        /// <returns>The receipt.</returns>
        private static Task<SaveReceipt> CreateAsync(Widget widget, AsyncServiceScope scope)
        {
            return CreateAsync([widget], scope);
        }

        /// <summary>Reserves ids for new widgets in a scope and saves them.</summary>
        /// <param name="widgets">The widgets.</param>
        /// <param name="scope">The scope.</param>
        /// <returns>The receipt.</returns>
        private static async Task<SaveReceipt> CreateAsync(Widget[] widgets, AsyncServiceScope scope)
        {
            ITenantDatabase database = Database(scope);
            IDataBatch validate = database.CreateBatch(BatchPurpose.Validate);
            using IdReservation reservation = Allocator(scope).Reserve(validate, typeof(Widget), widgets);
            if (!reservation.IsImmediate)
            {
                _ = await validate.ExecuteAsync(Token);
            }

            reservation.Assign(widgets);
            IDataBatch persist = database.CreateBatch(BatchPurpose.Persist);
            SaveHandle handle = persist.Save(widgets);
            _ = await persist.ExecuteAsync(Token);
            return handle.Receipt.Value;
        }

        /// <summary>
        ///     Saves widgets as the pipeline would: validation rounds that read the before images and
        ///     count, the first carrying the reservation of the new rows, then the persist.
        /// </summary>
        /// <param name="widgets">The widgets, new and existing.</param>
        /// <param name="validationRounds">The validation rounds.</param>
        /// <returns>The round trips the outcomes report, and the server's own count.</returns>
        private async Task<(int RoundTrips, long ServerRoundtrips)> SaveInTwoAsync(Widget[] widgets, int validationRounds)
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            CountingConnection connection = new(fixture.ConnectionString);
            int roundTrips = 0;
            IdReservation? reservation = null;
            try
            {
                for (int round = 1; round <= validationRounds; round++)
                {
                    // The before images of the existing rows, and a count standing in for the grant check.
                    DataBatch validate = Batch(scope, connection, BatchPurpose.Validate);
                    _ = validate.Sql($"SELECT [Id], [ModifiedAt] FROM [fixture].[Widgets] WHERE [Id] IN (SELECT [Id] FROM {validate.Tvp([.. widgets.Where(static widget => widget.Id > 0).Select(static widget => new IdList { Id = widget.Id })])});", new SqlOptions(resultSets: 1));
                    _ = validate.Sql($"SELECT COUNT(*) AS [Granted] FROM [core].[Users] WHERE [Id] = {WellKnownIds.SystemUserId};", new SqlOptions(resultSets: 1));
                    if (round == 1)
                    {
                        reservation = Allocator(scope).Reserve(validate, typeof(Widget), widgets);
                    }

                    roundTrips += (await validate.ExecuteAsync(Token)).RoundTrips;
                }

                reservation!.Assign(widgets);
                DataBatch persist = Batch(scope, connection, BatchPurpose.Persist);
                _ = persist.Save(widgets);
                roundTrips += (await persist.ExecuteAsync(Token)).RoundTrips;
            }
            finally
            {
                reservation?.Dispose();
            }

            return (roundTrips, connection.ServerRoundtrips);
        }

        /// <summary>Inserts a widget out of band, as a restore or a manual insert would.</summary>
        /// <param name="id">The widget's id.</param>
        /// <returns>A task that completes when it is inserted.</returns>
        private Task InsertWidgetAsync(int id)
        {
            return SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"INSERT INTO [fixture].[Widgets] ([Id], [Kind], [Name], [Subject], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]) VALUES ({id}, 'Plain', N'out-of-band', N'oob-{id}', SYSDATETIMEOFFSET(), 1, SYSDATETIMEOFFSET(), 1);"));
        }

        /// <summary>Inserts a widget out of band some ids beyond the sequence's position, so the sequence falls behind its table.</summary>
        /// <param name="gap">How far beyond the sequence's position.</param>
        /// <returns>The widget's id: the table's largest.</returns>
        private async Task<int> InsertAheadOfSequenceAsync(int gap)
        {
            int current = await QueryAsync<int>("SELECT CAST([current_value] AS int) FROM sys.sequences WHERE [object_id] = OBJECT_ID(N'fixture.sq_Widgets');");
            await InsertWidgetAsync(current + gap);
            return current + gap;
        }

        /// <summary>The rows of a widget.</summary>
        /// <param name="id">The widget.</param>
        /// <returns>The count.</returns>
        private Task<int> CountAsync(int id)
        {
            return QueryAsync<int>(string.Create(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Id] = {id};"));
        }

        /// <summary>Runs a scalar query against the fixture database.</summary>
        /// <typeparam name="T">The result's type.</typeparam>
        /// <param name="sql">The query.</param>
        /// <returns>The result.</returns>
        private async Task<T> QueryAsync<T>(string sql)
        {
            return (await SqlServerFixture.ScalarAsync<T>(fixture.ConnectionString, sql))!;
        }

        /// <summary>The log records of an event since the reset.</summary>
        /// <param name="eventName">The event's name.</param>
        /// <returns>The records.</returns>
        private FakeLogRecord[] Logged(string eventName)
        {
            return [.. fixture.Logs.GetSnapshot().Where(record => record.Id.Name == eventName)];
        }

        /// <summary>A collector of one of the host's counters, from now on.</summary>
        /// <param name="name">The counter.</param>
        /// <returns>The collector.</returns>
        private MetricCollector<long> Collector(string name)
        {
            return new MetricCollector<long>(fixture.Tenant.Services.GetRequiredService<IMeterFactory>(), DataTelemetryNames.MeterName, name);
        }

        /// <summary>An executor over the host's services whose process snapshots are the test's.</summary>
        /// <param name="snapshots">The snapshots.</param>
        /// <returns>The executor.</returns>
        private DataExecutor ExecutorWith(IVersionTagSnapshots snapshots)
        {
            IServiceProvider root = fixture.Tenant.Services;
            return new DataExecutor(
                root.GetRequiredService<IOptions<DataOptions>>(),
                root.GetRequiredService<IEntityMetadataProvider>(),
                root.GetRequiredService<TableTypeBinder>(),
                root.GetRequiredService<IStackTableOwners>(),
                TimeProvider.System,
                root.GetRequiredService<ILogger<DataBatch>>(),
                root.GetRequiredService<DataMetrics>(),
                root.GetRequiredService<QueryServices>(),
                root.GetRequiredService<IdAllocator>(),
                root.GetRequiredService<SaveEmitter>(),
                new ContributorScope(root, snapshots: snapshots));
        }

        /// <summary>The process snapshots, recording every replacement in order.</summary>
        private sealed class RecordingSnapshots : IVersionTagSnapshots
        {
            /// <summary>The snapshots replaced, in order.</summary>
            public List<VersionTagSnapshot> Replaced { get; } = [];

            /// <inheritdoc />
            public VersionTagSnapshot Current(int tenantId)
            {
                return Replaced.LastOrDefault() ?? new VersionTagSnapshot(tenantId, new Dictionary<string, VersionTag>(StringComparer.Ordinal), DateTimeOffset.MinValue);
            }

            /// <inheritdoc />
            public void Replace(VersionTagSnapshot snapshot)
            {
                Replaced.Add(snapshot);
            }
        }

        /// <summary>
        ///     Stands in for the tag-reading prelude: every batch's prologue carries a statement whose
        ///     reader reports a snapshot read later than the one before.
        /// </summary>
        private sealed class TagReadingContributor : IDataBatchContributor
        {
            /// <summary>The snapshots reported, in the order the batches read them.</summary>
            public List<VersionTagSnapshot> Read { get; } = [];

            /// <inheritdoc />
            /// <remarks>Last, so its report follows the real prelude's and is the one the batch keeps.</remarks>
            public int Order => int.MaxValue;

            /// <inheritdoc />
            public void Contribute(IDataBatch batch, DataBatchStage stage)
            {
                if (stage != DataBatchStage.Prologue)
                {
                    return;
                }

                var data = (DataBatch)batch;
                StatementDraft draft = data.Begin();
                int ordinal = draft.Ordinal;
                data.Append(draft, "SELECT 1 AS [Read];", reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken);
                    _ = await set.ReadAsync(cancellationToken);
                    cursor.CountRows(1);
                    VersionTagSnapshot snapshot = new(
                        FixtureTenant.TenantId,
                        new Dictionary<string, VersionTag>(StringComparer.Ordinal) { ["fx"] = new VersionTag(Guid.NewGuid()) },
                        DateTimeOffset.UtcNow);
                    Read.Add(snapshot);
                    data.ReportVersionTags(snapshot);
                });
            }
        }

        /// <summary>A scope's services with a contributor or the process snapshots added.</summary>
        /// <param name="inner">The scope's services.</param>
        /// <param name="contributor">The contributor added to the scope's, if any.</param>
        /// <param name="snapshots">The process snapshots, if any.</param>
        private sealed class ContributorScope(IServiceProvider inner, IDataBatchContributor? contributor = null, IVersionTagSnapshots? snapshots = null) : IServiceProvider
        {
            /// <inheritdoc />
            public object? GetService(Type serviceType)
            {
                // The contributor joins the scope's own; the snapshots stand in for the process's.
                return contributor is not null && serviceType == typeof(IEnumerable<IDataBatchContributor>)
                    ? (IDataBatchContributor[])[contributor, .. inner.GetServices<IDataBatchContributor>()]
                    : snapshots is not null && serviceType == typeof(IVersionTagSnapshots) ? snapshots : inner.GetService(serviceType);
            }
        }

        /// <summary>
        ///     A batch connection that opens a fresh connection per round trip with statistics on, and adds
        ///     up the server round trips each reports: the oracle the budgets are asserted against.
        /// </summary>
        /// <param name="connectionString">The fixture database.</param>
        private sealed class CountingConnection(string connectionString) : IBatchConnection
        {
            /// <summary>The server round trips of every connection released so far.</summary>
            public long ServerRoundtrips { get; private set; }

            /// <inheritdoc />
            public int TenantId => FixtureTenant.TenantId;

            /// <inheritdoc />
            public MultilingualShape Shape => throw new InvalidOperationException("The counted batches compose saves and raw statements only, so they read no tenant settings.");

            /// <inheritdoc />
            public QueryexContextValues Context => throw new InvalidOperationException("The counted batches compose saves and raw statements only, so they bind no context values.");

            /// <inheritdoc />
            public int? UserId => WellKnownIds.SystemUserId;

            /// <inheritdoc />
            public async ValueTask<SqlConnection> AcquireAsync(CancellationToken cancellationToken)
            {
                // Counting starts once the login is over: only the batch's own requests count.
                SqlConnection connection = new(connectionString);
                await connection.OpenAsync(cancellationToken);
                connection.StatisticsEnabled = true;
                connection.ResetStatistics();
                return connection;
            }

            /// <inheritdoc />
            public async ValueTask ReleaseAsync(SqlConnection connection)
            {
                ServerRoundtrips += (long)connection.RetrieveStatistics()["ServerRoundtrips"]!;
                await connection.DisposeAsync();
            }
        }
    }
}
