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
using Microsoft.Extensions.Time.Testing;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Caching;
using Tellma.Core.Calendars;
using Tellma.Core.Data.Batches;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Data.Batches;

namespace Tellma.Core.Tests.Caching
{
    /// <summary>
    ///     The entities cache: which entities it holds and at what row limit, how a probe's rows settle
    ///     into it (stored in rows, oversized and warned about once, or uncached), the views by a base
    ///     class, a list per multilingual shape, the scope's façade, and how a batch reads a list
    ///     through <c>FromCache</c>, its guard locking a probed list's tag on a writing persist batch.
    /// </summary>
    public sealed class CacheableEntityStoreTests : IDisposable
    {
        private static readonly VersionTag First = new(new Guid("00000001-0000-0000-0000-000000000000"));

        private static readonly VersionTag Second = new(new Guid("00000002-0000-0000-0000-000000000000"));

        private static readonly DateTimeOffset Now = new(2026, 3, 14, 9, 30, 0, TimeSpan.Zero);

        /// <summary>The cache model over a database that refuses to connect.</summary>
        private readonly BatchHarness _harness = new(compose: CacheModel.Compose);

        /// <summary>The meter factory and the logs of the stores the tests create on their own.</summary>
        private readonly ServiceProvider _services = new ServiceCollection().AddMetrics().AddFakeLogging().BuildServiceProvider();

        /// <inheritdoc />
        public void Dispose()
        {
            _services.Dispose();
            _harness.Dispose();
        }

        // ===================== The entities =====================

        [Fact]
        public void The_store_holds_each_cacheable_entity_at_its_own_row_limit_or_the_configured_default()
        {
            CacheableEntityStore store = Composed;

            Assert.Equal(
                [(CacheModel.Currencies, 3, "entity:cm.CmCurrency", typeof(CmCurrency)), (CacheModel.Units, 1000, "entity:cm.CmUnit", typeof(CmMyUnit))],
                store.Types.Select(static type => (type.Name, type.MaxRows, type.TagName, type.Metadata.ClrType)).OrderBy(static type => type.Name, StringComparer.Ordinal));
            Assert.Equal((CacheKinds.Entities, 2000000L), (store.Kind, store.SizeLimit));
        }

        [Fact]
        public void A_class_resolves_to_its_cacheable_entity_through_its_leaf_and_any_other_class_is_refused()
        {
            CacheableEntityStore store = Composed;

            Assert.Equal(CacheModel.Units, store.TypeOf(typeof(CmUnit)).Name);
            Assert.Same(store.TypeOf(typeof(CmUnit)), store.TypeOf(typeof(CmMyUnit)));
            Assert.Contains("not cacheable", Assert.Throws<ArgumentException>(() => store.TypeOf(typeof(CmCountry))).Message, StringComparison.Ordinal);
            Assert.Contains("no entity", Assert.Throws<ArgumentException>(() => store.TypeOf(typeof(IdList))).Message, StringComparison.Ordinal);
        }

        [Fact]
        public void The_probe_selects_every_scalar_property_the_tenants_schema_declares()
        {
            CacheableEntityType currencies = Composed.TypeOf(typeof(CmCurrency));
            IQueryexSchemaProvider schemas = _harness.Services.GetRequiredService<IQueryexSchemaProvider>();

            // A tenant with one language reads no twin of the name.
            Assert.Equal(
                ["Code", "CreatedAt", "CreatedById", "Id", "IsActive", "ModifiedAt", "ModifiedById", "Name"],
                currencies.Select(schemas.GetSchema(MultilingualShape.Primary)).Split(", ").Order(StringComparer.Ordinal));
            Assert.Equal(
                ["Code", "CreatedAt", "CreatedById", "Id", "IsActive", "ModifiedAt", "ModifiedById", "Name", "Name2", "Name3"],
                currencies.Select(schemas.GetSchema(MultilingualShape.All)).Split(", ").Order(StringComparer.Ordinal));
        }

        // ===================== Settling a probe's rows =====================

        [Fact]
        public void Rows_under_the_preludes_tag_are_stored_in_key_order_with_their_ids_and_the_load_time()
        {
            using CacheableEntityStore store = Standalone(out _);
            using MetricCollector<long> requests = Requests();
            CacheableEntityType currencies = store.TypeOf(typeof(CmCurrency));

            CacheResult<CachedEntitySet<CmCurrency>> result = store.Settle<CmCurrency>(currencies, 1, MultilingualShape.Primary, CacheOutcome.Miss, First, Currencies(2));

            CachedEntitySet<CmCurrency> set = Assert.IsType<CachedEntitySet<CmCurrency>>(result.Value);
            Assert.Equal((First, CacheOutcome.Miss, First, Now), (result.Tag, result.Outcome, set.Tag, set.LoadedAt));
            Assert.Equal([1, 2], set.Items.Select(static currency => currency.Id));
            Assert.Equal("C2", set.ById[2].Code);
            Assert.Same(set, store.Peek<CmCurrency>(1, MultilingualShape.Primary, First));
            Assert.Null(store.Peek<CmCurrency>(2, MultilingualShape.Primary, First));
            Assert.Equal(["miss", "hit", "miss"], Outcomes(requests));
        }

        [Fact]
        public void The_store_is_sized_in_rows_and_an_empty_list_takes_one()
        {
            using CacheableEntityStore store = Standalone(out _);
            using MetricCollector<long> size = new(MeterFactory, CacheTelemetryNames.MeterName, CacheTelemetryNames.Size);
            using MetricCollector<long> entries = new(MeterFactory, CacheTelemetryNames.MeterName, CacheTelemetryNames.Entries);
            CacheableEntityType currencies = store.TypeOf(typeof(CmCurrency));

            _ = store.Settle<CmCurrency>(currencies, 1, MultilingualShape.Primary, CacheOutcome.Miss, First, Currencies(2));
            _ = store.Settle<CmCurrency>(currencies, 2, MultilingualShape.Primary, CacheOutcome.Miss, First, []);

            // Two rows and an empty list: three units over two entries, where one per entry would be two.
            size.RecordObservableInstruments();
            entries.RecordObservableInstruments();
            Assert.Equal(3, Gauge(size));
            Assert.Equal(2, Gauge(entries));
        }

        [Fact]
        public void More_rows_than_the_limit_are_oversized_store_nothing_and_warn_once_per_entity()
        {
            using CacheableEntityStore store = Standalone(out FakeLogCollector logs);
            using MetricCollector<long> requests = Requests();
            CacheableEntityType currencies = store.TypeOf(typeof(CmCurrency));

            CacheResult<CachedEntitySet<CmCurrency>> first = store.Settle<CmCurrency>(currencies, 1, MultilingualShape.Primary, CacheOutcome.Miss, First, Currencies(4));
            CacheResult<CachedEntitySet<CmCurrency>> second = store.Settle<CmCurrency>(currencies, 2, MultilingualShape.Primary, CacheOutcome.Stale, First, Currencies(4));

            Assert.Equal((null, First, CacheOutcome.Oversized), (first.Value, first.Tag, first.Outcome));
            Assert.Equal(CacheOutcome.Oversized, second.Outcome);
            Assert.Null(store.Peek<CmCurrency>(1, MultilingualShape.Primary, First));
            FakeLogRecord warning = Assert.Single(logs.GetSnapshot());
            Assert.Equal((LogLevel.Warning, "CacheableEntityOverflow"), (warning.Level, warning.Id.Name));
            Assert.Equal(
                [("Entity", CacheModel.Currencies), ("Rows", "4"), ("MaxRows", "3")],
                warning.StructuredState!.Where(static pair => pair.Key != "{OriginalFormat}").Select(static pair => (pair.Key, pair.Value)));
            Assert.Equal(["oversized", "oversized", "miss"], Outcomes(requests));
        }

        [Fact]
        public void Rows_whose_tag_the_prelude_did_not_find_are_served_uncached()
        {
            using CacheableEntityStore store = Standalone(out _);
            using MetricCollector<long> requests = Requests();

            CacheResult<CachedEntitySet<CmCurrency>> result = store.Settle<CmCurrency>(store.TypeOf(typeof(CmCurrency)), 1, MultilingualShape.Primary, CacheOutcome.Miss, VersionTag.None, Currencies(1));

            Assert.Equal((CacheOutcome.Uncached, 1), (result.Outcome, result.Value!.Items.Count));
            Assert.Equal(CacheOutcome.Miss, store.Find(store.TypeOf(typeof(CmCurrency)), 1, MultilingualShape.Primary, VersionTag.None, out _));
            Assert.Equal(["uncached"], Outcomes(requests));
        }

        [Fact]
        public void A_base_class_reads_one_view_of_the_leafs_rows_and_the_leaf_reads_the_list_itself()
        {
            using CacheableEntityStore store = Standalone(out _);
            CacheableEntityType units = store.TypeOf(typeof(CmUnit));
            CmMyUnit kilogram = new() { Id = 5, Name = "Kilogram", Symbol = "kg" };

            CacheResult<CachedEntitySet<CmUnit>> settled = store.Settle<CmUnit>(units, 1, MultilingualShape.Primary, CacheOutcome.Miss, First, [kilogram]);

            CachedEntitySet<CmUnit> view = settled.Value!;
            Assert.Same(kilogram, Assert.Single(view.Items));
            Assert.Same(kilogram, view.ById[5]);
            Assert.Same(view, store.Peek<CmUnit>(1, MultilingualShape.Primary, First));
            Assert.Same(kilogram, store.Peek<CmMyUnit>(1, MultilingualShape.Primary, First)!.ById[5]);
            Assert.Throws<NotSupportedException>(() => ((IList<CmUnit>)view.Items).Add(kilogram));
        }

        // ===================== A list per multilingual shape =====================

        [Fact]
        public void A_list_read_under_one_shape_is_a_miss_under_another_in_either_direction()
        {
            using CacheableEntityStore store = Standalone(out _);
            CacheableEntityType currencies = store.TypeOf(typeof(CmCurrency));

            // --- A language added: the one-language list has no twins, so the wider shape misses ---
            _ = store.Settle<CmCurrency>(currencies, 1, MultilingualShape.Primary, CacheOutcome.Miss, First, Currencies(2));
            Assert.Equal(CacheOutcome.Miss, store.Find(currencies, 1, MultilingualShape.All, First, out _));
            Assert.Null(store.Peek<CmCurrency>(1, MultilingualShape.All, First));

            // --- A language removed: the wider list carries twins the narrower shape no longer has ---
            _ = store.Settle<CmCurrency>(currencies, 2, MultilingualShape.All, CacheOutcome.Miss, First, Currencies(2));
            Assert.Equal(CacheOutcome.Miss, store.Find(currencies, 2, MultilingualShape.Primary, First, out _));
            Assert.Null(store.Peek<CmCurrency>(2, MultilingualShape.PrimaryAndSecondary, First));

            // Each list still answers under its own shape.
            Assert.NotNull(store.Peek<CmCurrency>(1, MultilingualShape.Primary, First));
            Assert.NotNull(store.Peek<CmCurrency>(2, MultilingualShape.All, First));
        }

        [Fact]
        public async Task A_batch_under_a_new_shape_probes_its_own_columns_instead_of_reading_the_old_shapes_list()
        {
            _ = Warm(First);
            _harness.Shape = MultilingualShape.All;
            DataBatch batch = _harness.Batch(BatchPurpose.Read);

            BatchResult<CacheResult<CachedEntitySet<CmCurrency>>> result = batch.FromCache<CmCurrency>();

            Assert.False(result.IsCompleted);
            Assert.Empty(batch.Dependencies);
            Assert.Contains("[Name3]", (await BatchHarness.SealAsync(batch)).Text, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Callers_under_different_shapes_never_share_a_load_and_each_reads_its_own_columns()
        {
            using CacheableEntityStore store = Standalone(out _);
            TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            GatedDatabase primary = new(_harness, MultilingualShape.Primary, gate.Task);
            GatedDatabase all = new(_harness, MultilingualShape.All, gate.Task);

            // --- Same tenant, entity and tag: the two shapes start a load each; a second caller of one shape joins it ---
            Task<CacheResult<CachedEntitySet<CmCurrency>>> one = store.GetAsync<CmCurrency>(1, MultilingualShape.Primary, First, primary, Token);
            Task<CacheResult<CachedEntitySet<CmCurrency>>> other = store.GetAsync<CmCurrency>(1, MultilingualShape.All, First, all, Token);
            Task<CacheResult<CachedEntitySet<CmCurrency>>> joined = store.GetAsync<CmCurrency>(1, MultilingualShape.Primary, First, primary, Token);
            Assert.Equal((1, 1), (primary.Batches.Count, all.Batches.Count));

            // --- Each load fails on its own handle, and each read only its own shape's columns ---
            gate.SetResult();
            _ = await Assert.ThrowsAsync<BatchHarness.RefusedConnection.RefusedException>(() => one.WaitAsync(Bound, Token));
            _ = await Assert.ThrowsAsync<BatchHarness.RefusedConnection.RefusedException>(() => other.WaitAsync(Bound, Token));
            _ = await Assert.ThrowsAsync<BatchHarness.RefusedConnection.RefusedException>(() => joined.WaitAsync(Bound, Token));
            Assert.DoesNotContain("[Name3]", primary.Batches[0].Assemble().Text, StringComparison.Ordinal);
            Assert.Contains("[Name3]", all.Batches[0].Assemble().Text, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_load_asked_of_a_handle_under_another_shape_is_refused()
        {
            using CacheableEntityStore store = Standalone(out _);
            GatedDatabase primary = new(_harness, MultilingualShape.Primary, Task.CompletedTask);

            InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.GetAsync<CmCurrency>(1, MultilingualShape.All, First, primary, Token));

            Assert.Contains("under the Primary shape", refused.Message, StringComparison.Ordinal);
            Assert.Empty(primary.Batches);
        }

        // ===================== The scope's façade =====================

        [Fact]
        public async Task The_facade_reads_the_scopes_tenant_under_the_snapshots_tag_and_reports_every_entitys_wire_tag()
        {
            using CacheableEntityStore store = Standalone(out _);
            using VersionTagSnapshots snapshots = new(Options.Create(new CacheOptions()));
            snapshots.Replace(new VersionTagSnapshot(17, new Dictionary<string, VersionTag> { ["entity:cm.CmCurrency"] = First }, Now));
            _ = store.Settle<CmCurrency>(store.TypeOf(typeof(CmCurrency)), 17, MultilingualShape.Primary, CacheOutcome.Miss, First, Currencies(1));
            CacheableEntities facade = new(store, snapshots, new FixedContext(17), new RefusedDatabase());

            Assert.Equal("C1", Assert.Single(facade.Peek<CmCurrency>()!.Items).Code);
            Assert.Null(facade.Peek<CmUnit>());
            Assert.Equal(
                new Dictionary<string, string>
                {
                    [CacheModel.Currencies] = First.ToWire(CachedEntitySet<CmCurrency>.FormatVersion),
                    [CacheModel.Units] = VersionTag.None.ToWire(CachedEntitySet<CmUnit>.FormatVersion),
                },
                await facade.GetWireTagsAsync());

            // A hit loads nothing, so the refusing handle is never asked for a batch.
            CacheResult<CachedEntitySet<CmCurrency>> hit = await facade.GetAsync<CmCurrency>();
            Assert.Equal(CacheOutcome.Hit, hit.Outcome);
        }

        // ===================== FromCache =====================

        [Fact]
        public async Task A_cold_read_appends_the_capped_probe_after_the_prologue_and_declares_nothing()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Validate);

            BatchResult<CacheResult<CachedEntitySet<CmCurrency>>> result = batch.FromCache<CmCurrency>();

            Assert.False(result.IsCompleted);
            Assert.Empty(batch.Dependencies);
            BatchAssembly assembly = await BatchHarness.SealAsync(batch);
            Assert.Contains("[cm].[Currencies]", assembly.Text, StringComparison.Ordinal);
            Assert.Contains(4, assembly.Parameters.OfType<ScalarParameter>().Select(static parameter => parameter.Value));
        }

        [Theory]
        [InlineData(BatchPurpose.Read, VersionTagMismatchPolicy.Refresh)]
        [InlineData(BatchPurpose.Validate, VersionTagMismatchPolicy.Rerun)]
        [InlineData(BatchPurpose.Persist, VersionTagMismatchPolicy.Rerun)]
        public async Task A_warm_read_completes_at_once_reads_nothing_and_declares_the_lists_tag(BatchPurpose purpose, VersionTagMismatchPolicy policy)
        {
            CachedEntitySet<CmCurrency> warm = Warm(First);
            DataBatch batch = _harness.Batch(purpose);

            BatchResult<CacheResult<CachedEntitySet<CmCurrency>>> result = batch.FromCache<CmCurrency>();

            Assert.True(result.IsCompleted);
            Assert.Equal((warm, First, CacheOutcome.Hit), (result.Value.Value, result.Value.Tag, result.Value.Outcome));
            Assert.Equal(new VersionTagDependency("entity:cm.CmCurrency", First, policy), Assert.Single(batch.Dependencies));
            Assert.DoesNotContain("[cm].[Currencies]", (await BatchHarness.SealAsync(batch)).Text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_list_cached_under_a_tag_the_snapshot_moved_past_is_probed_again()
        {
            _ = Warm(First);
            ReplaceSnapshot(Second);
            DataBatch batch = _harness.Batch(BatchPurpose.Read);

            Assert.False(batch.FromCache<CmCurrency>().IsCompleted);
            Assert.Empty(batch.Dependencies);
        }

        [Fact]
        public async Task A_cold_read_on_a_writing_persist_batch_has_its_guard_lock_the_lists_tag_without_comparing_it()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist, new TextContributor(0, "DECLARE @tm_Guard bit = 1;", null));
            batch.MarkConnectPrologue(GuardPremises.ForUser(First, Second));

            _ = batch.FromCache<CmCurrency>();
            _ = batch.Sql($"DELETE FROM [core].[Jobs] WHERE 1 = 0;", new SqlOptions(writes: new HashSet<TableName> { new("core", "Jobs") }));

            // No dependency, so nothing compared; the tag row is still read under the guard's shared lock to the commit.
            Assert.Empty(batch.Dependencies);
            PersistChecks checks = Assert.IsType<PersistChecks>((await BatchHarness.SealAsync(batch)).Checks);
            Assert.Contains(("entity:cm.CmCurrency", PersistChecks.NoExpectation, false), checks.ExpectedTags.Select(static row => (row.Name, row.Tag, row.Bumped)));
        }

        [Theory]
        [InlineData(BatchPurpose.Read)]
        [InlineData(BatchPurpose.Validate)]
        public void A_cold_read_on_a_batch_with_no_guard_locks_nothing(BatchPurpose purpose)
        {
            DataBatch batch = _harness.Batch(purpose);

            _ = batch.FromCache<CmCurrency>();

            Assert.Empty(batch.LockedTags);
        }

        [Fact]
        public void A_warm_read_on_a_persist_batch_compares_the_lists_tag_instead_of_only_locking_it()
        {
            _ = Warm(First);
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = batch.FromCache<CmCurrency>();

            Assert.Empty(batch.LockedTags);
            Assert.Equal(new VersionTagDependency("entity:cm.CmCurrency", First, VersionTagMismatchPolicy.Rerun), Assert.Single(batch.Dependencies));
        }

        [Fact]
        public void A_second_read_of_one_list_answers_with_the_first_handle()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Read);

            Assert.Same(batch.FromCache<CmCurrency>(), batch.FromCache<CmCurrency>());
            Assert.NotSame(batch.FromCache<CmCurrency>(), batch.FromCache<CmUnit>());
        }

        [Fact]
        public void A_batch_that_already_writes_the_table_or_names_no_cacheable_entity_is_refused()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);
            batch.Append(batch.Begin(), "UPDATE [cm].[Currencies] SET [IsActive] = 0;", writes: new HashSet<TableName> { new("cm", "Currencies") });

            Assert.Contains("already writes", Assert.Throws<InvalidOperationException>(batch.FromCache<CmCurrency>).Message, StringComparison.Ordinal);
            Assert.False(batch.FromCache<CmUnit>().IsCompleted);
            _ = Assert.Throws<ArgumentException>(batch.FromCache<CmCountry>);
        }

        // ===================== Helpers =====================

        /// <summary>The bound on every wait for a load to finish.</summary>
        private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

        /// <summary>The test's cancellation token.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <summary>The composition's store.</summary>
        private CacheableEntityStore Composed => _harness.Services.GetRequiredService<CacheableEntityStore>();

        /// <summary>The meter factory of the standalone stores.</summary>
        private IMeterFactory MeterFactory => _services.GetRequiredService<IMeterFactory>();

        /// <summary>Currencies with ids from 1, as a probe reads them.</summary>
        /// <param name="count">How many.</param>
        /// <returns>The rows.</returns>
        private static List<object> Currencies(int count)
        {
            return [.. Enumerable.Range(1, count).Select(static id => new CmCurrency { Id = id, Code = $"C{id}", Name = $"Currency {id}", IsActive = true })];
        }

        /// <summary>The last value a gauge reported for the entities kind.</summary>
        /// <param name="gauge">The gauge's collector.</param>
        /// <returns>The value.</returns>
        private static long Gauge(MetricCollector<long> gauge)
        {
            return gauge.GetMeasurementSnapshot().Last(static measurement => Equals(measurement.Tags[CacheTelemetryNames.KindTag], CacheKinds.Entities)).Value;
        }

        /// <summary>The outcomes a request collector recorded, in order.</summary>
        /// <param name="requests">The collector.</param>
        /// <returns>The outcomes.</returns>
        private static List<string> Outcomes(MetricCollector<long> requests)
        {
            return [.. requests.GetMeasurementSnapshot().Select(static measurement => (string)measurement.Tags[CacheTelemetryNames.OutcomeTag]!)];
        }

        /// <summary>A store of the cache model on its own meter, its clock fixed and its logs collected.</summary>
        /// <param name="logs">The store's logs.</param>
        /// <returns>The store.</returns>
        private CacheableEntityStore Standalone(out FakeLogCollector logs)
        {
            FakeLogger<CacheableEntityStore> logger = new();
            logs = logger.Collector;
            return new CacheableEntityStore(
                _harness.Services.GetRequiredService<IEntityMetadataProvider>(),
                _harness.Services.GetRequiredService<IQueryexSchemaProvider>(),
                Options.Create(new CacheOptions()),
                MeterFactory,
                new FakeTimeProvider(Now),
                logger);
        }

        /// <summary>A collector of the standalone stores' requests.</summary>
        /// <returns>The collector.</returns>
        private MetricCollector<long> Requests()
        {
            return new MetricCollector<long>(MeterFactory, CacheTelemetryNames.MeterName, CacheTelemetryNames.Requests);
        }

        /// <summary>Makes the harness's tenant snapshot hold a tag for the currencies.</summary>
        /// <param name="tag">The tag.</param>
        private void ReplaceSnapshot(VersionTag tag)
        {
            _harness.Services.GetRequiredService<IVersionTagSnapshots>().Replace(
                new VersionTagSnapshot(1, new Dictionary<string, VersionTag> { ["entity:cm.CmCurrency"] = tag }, Now));
        }

        /// <summary>Warms the composition's store with the harness tenant's currencies under a tag the snapshot holds.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The cached list.</returns>
        private CachedEntitySet<CmCurrency> Warm(VersionTag tag)
        {
            ReplaceSnapshot(tag);
            CacheableEntityType currencies = Composed.TypeOf(typeof(CmCurrency));
            return Composed.Settle<CmCurrency>(currencies, 1, MultilingualShape.Primary, CacheOutcome.Miss, tag, Currencies(2)).Value!;
        }

        /// <summary>A request context accessor whose context names one tenant, its settings of one language loaded.</summary>
        /// <param name="tenantId">The tenant.</param>
        private sealed class FixedContext(int tenantId) : IRequestContextAccessor
        {
            /// <inheritdoc />
            public RequestContext Current { get; } = RequestContext.Tenantless(new GregorianCalendarSystem(), Now) with
            {
                Tenant = new TenantDescriptor(tenantId, "Acme", null, null, ["en"], TenantCategory.Live, TenantState.Active, null),
                TenantSettings = new TenantSettings
                {
                    TenantId = tenantId,
                    Tag = First,
                    Names = ["Acme", null, null],
                    Languages = [new LanguageInfo("en", "English", "E", false)],
                    Shape = MultilingualShape.Primary,
                    Calendars = [],
                    TimeZone = TimeZoneInfo.Utc,
                    SqlServerTimeZoneName = "UTC",
                    ModifiedAt = Now,
                    Entries = new Dictionary<string, JsonElement>(),
                },
            };
        }

        /// <summary>
        ///     A handle of one shape whose batches wait at a gate, then refuse to connect; it keeps the
        ///     batches it created.
        /// </summary>
        /// <param name="harness">The harness whose executor the batches run on.</param>
        /// <param name="shape">The shape the handle reports.</param>
        /// <param name="gate">What each batch waits for before it refuses.</param>
        private sealed class GatedDatabase(BatchHarness harness, MultilingualShape shape, Task gate) : ITenantDatabase, IBatchConnection
        {
            /// <summary>The batches created, in order.</summary>
            public List<DataBatch> Batches { get; } = [];

            /// <inheritdoc />
            public QueryexSchema Schema => harness.Services.GetRequiredService<IQueryexSchemaProvider>().GetSchema(shape);

            /// <inheritdoc />
            public QueryexContextValues Context => harness.Context;

            /// <inheritdoc />
            public MultilingualShape Shape => shape;

            /// <inheritdoc />
            public int TenantId => 1;

            /// <inheritdoc />
            public int? UserId => 7;

            /// <inheritdoc />
            public IDataBatch CreateBatch(BatchPurpose purpose)
            {
                DataBatch batch = new(harness.Executor, this, new ServiceCollection().BuildServiceProvider(), purpose);
                Batches.Add(batch);
                return batch;
            }

            /// <inheritdoc />
            public async ValueTask<SqlConnection> AcquireAsync(CancellationToken cancellationToken)
            {
                await gate.WaitAsync(cancellationToken);
                throw new BatchHarness.RefusedConnection.RefusedException();
            }

            /// <inheritdoc />
            public ValueTask ReleaseAsync(SqlConnection connection)
            {
                return ValueTask.CompletedTask;
            }
        }

        /// <summary>A database handle no batch is created on.</summary>
        private sealed class RefusedDatabase : ITenantDatabase
        {
            /// <inheritdoc />
            public QueryexSchema Schema => throw new NotSupportedException();

            /// <inheritdoc />
            public QueryexContextValues Context => throw new NotSupportedException();

            /// <inheritdoc />
            public IDataBatch CreateBatch(BatchPurpose purpose)
            {
                throw new NotSupportedException();
            }
        }
    }
}
