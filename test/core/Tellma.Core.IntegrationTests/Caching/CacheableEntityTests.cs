// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text.Json;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Caching;
using Tellma.Core.IntegrationTests.Data;
using Tellma.Core.Tenancy;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Caching
{
    /// <summary>
    ///     The fixture's cacheable list against a real database: a cold <c>FromCache</c> riding the
    ///     consumer's one round trip and warming the cache, a warm one declaring the list's tag so a
    ///     validate batch re-runs and a read batch refreshes when it moved, the scope's façade loading
    ///     once and then serving from memory, an overflowing list reported and never cached, and a
    ///     list whose tag row is missing served uncached.
    /// </summary>
    /// <param name="fixture">The class's database, reset before each test.</param>
    [Trait("Category", "Integration")]
    public class CacheableEntityTests(DataBatchDatabase fixture) : IClassFixture<DataBatchDatabase>, IAsyncLifetime
    {
        /// <summary>The list's own tag.</summary>
        private static readonly string LookupTag = VersionTagNames.ForEntity("fixture.Lookup");

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <summary>The process-wide store of the fixture tenant's host.</summary>
        private CacheableEntityStore Store => fixture.Tenant.Services.GetRequiredService<CacheableEntityStore>();

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            await fixture.ResetAsync();
            await fixture.SeedCallerAsync();
            await fixture.SeedTagsAsync();

            // The store outlives the tests of the class; each starts cold.
            Store.Remove(new CacheableEntityKey(FixtureTenant.TenantId, "fixture.Lookup", MultilingualShape.Primary));
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }

        [Fact]
        public async Task A_cold_read_costs_no_round_trip_of_its_own_and_warms_the_cache_under_the_preludes_tag()
        {
            await InsertLookupsAsync(3);
            Guid stored = (await fixture.TagsAsync())[LookupTag];
            await using AsyncServiceScope scope = CreateScope();
            IDataBatch batch = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Read);
            BatchResult<RawResult> other = batch.Sql($"SELECT 1 AS [One];", new SqlOptions(resultSets: 1));
            BatchResult<CacheResult<CachedEntitySet<Lookup>>> cold = batch.FromCache<Lookup>();
            fixture.ResetRoundTrips();

            BatchOutcome outcome = await batch.ExecuteAsync(Token);

            // --- One round trip carried the consumer's statement and the probe ---
            Assert.Equal((1, 1L), (outcome.RoundTrips, fixture.ServerRoundTrips));
            Assert.True(other.IsCompleted);
            CachedEntitySet<Lookup> list = Assert.IsType<CachedEntitySet<Lookup>>(cold.Value.Value);
            Assert.Equal((CacheOutcome.Miss, new VersionTag(stored)), (cold.Value.Outcome, cold.Value.Tag));
            Assert.Equal(["L1", "L2", "L3"], list.Items.Select(static lookup => lookup.Code));
            Assert.Equal((false, "Lookup 2"), (list.ById[2].IsActive, list.ById[2].Name));

            // --- The next batch finds it before it executes, and still reads only its prelude ---
            await using AsyncServiceScope next = CreateScope();
            IDataBatch warm = next.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Read);
            BatchResult<CacheResult<CachedEntitySet<Lookup>>> hit = warm.FromCache<Lookup>();
            Assert.True(hit.IsCompleted);
            Assert.Equal(CacheOutcome.Hit, hit.Value.Outcome);
            Assert.Same(list, hit.Value.Value);
            fixture.ResetRoundTrips();
            Assert.Equal(1, (await warm.ExecuteAsync(Token)).RoundTrips);
        }

        [Fact]
        public async Task A_validate_batch_reading_a_list_whose_tag_moved_under_it_is_discarded_for_a_rerun()
        {
            await InsertLookupsAsync(2);
            await WarmAsync();
            await MoveLookupTagAsync();
            await using AsyncServiceScope scope = CreateScope();
            IDataBatch batch = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Validate);
            BatchResult<CacheResult<CachedEntitySet<Lookup>>> hit = batch.FromCache<Lookup>();
            BatchResult<RawResult> other = batch.Sql($"SELECT 1 AS [One];", new SqlOptions(resultSets: 1));

            StaleContextException stale = await Assert.ThrowsAsync<StaleContextException>(() => batch.ExecuteAsync(Token));

            Assert.Equal(CacheOutcome.Hit, hit.Value.Outcome);
            VersionTagDependency dependency = Assert.Single(stale.Dependencies);
            Assert.Equal((LookupTag, hit.Value.Tag, VersionTagMismatchPolicy.Rerun), (dependency.Name, dependency.ExpectedTag, dependency.OnMismatch));
            Assert.False(other.IsCompleted);
        }

        [Fact]
        public async Task A_read_batch_reading_a_list_whose_tag_moved_is_served_and_the_next_read_reloads_it()
        {
            await InsertLookupsAsync(2);
            await WarmAsync();
            Guid moved = await MoveLookupTagAsync();
            await using AsyncServiceScope scope = CreateScope();
            IDataBatch batch = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Read);
            BatchResult<CacheResult<CachedEntitySet<Lookup>>> served = batch.FromCache<Lookup>();

            _ = await batch.ExecuteAsync(Token);

            // The read is served; the batch's snapshot carries the moved tag, so the next read misses.
            Assert.Equal(CacheOutcome.Hit, served.Value.Outcome);
            await using AsyncServiceScope next = CreateScope();
            IDataBatch reload = next.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Read);
            BatchResult<CacheResult<CachedEntitySet<Lookup>>> reloaded = reload.FromCache<Lookup>();
            _ = await reload.ExecuteAsync(Token);
            Assert.Equal((CacheOutcome.Stale, new VersionTag(moved)), (reloaded.Value.Outcome, reloaded.Value.Tag));
        }

        [Fact]
        public async Task The_facade_loads_a_cold_list_in_one_read_batch_and_then_serves_it_from_memory()
        {
            await InsertLookupsAsync(2);
            using MetricCollector<long> requests = new(
                fixture.Tenant.Services.GetRequiredService<IMeterFactory>(), CacheTelemetryNames.MeterName, CacheTelemetryNames.Requests);
            using MetricCollector<double> loads = new(
                fixture.Tenant.Services.GetRequiredService<IMeterFactory>(), CacheTelemetryNames.MeterName, CacheTelemetryNames.LoadDuration);
            await using AsyncServiceScope scope = CreateScope();
            ICacheableEntities entities = scope.ServiceProvider.GetRequiredService<ICacheableEntities>();
            fixture.ResetRoundTrips();

            CacheResult<CachedEntitySet<Lookup>> cold = await entities.GetAsync<Lookup>();
            long coldTrips = fixture.ServerRoundTrips;
            CacheResult<CachedEntitySet<Lookup>> warm = await entities.GetAsync<Lookup>();

            Assert.Equal((CacheOutcome.Miss, 1L), (cold.Outcome, coldTrips));
            Assert.Equal((CacheOutcome.Hit, 1L), (warm.Outcome, fixture.ServerRoundTrips));
            Assert.Same(cold.Value, warm.Value);
            Assert.Same(cold.Value, entities.Peek<Lookup>());
            Assert.Equal(new VersionTag((await fixture.TagsAsync())[LookupTag]).ToWire(CachedEntitySet<Lookup>.FormatVersion), (await entities.GetWireTagsAsync())["fixture.Lookup"]);
            Assert.Equal(
                ["miss", "hit", "hit"],
                requests.GetMeasurementSnapshot().Where(static measurement => Equals(measurement.Tags[CacheTelemetryNames.KindTag], CacheKinds.Entities)).Select(static measurement => (string)measurement.Tags[CacheTelemetryNames.OutcomeTag]!));
            Assert.Single(loads.GetMeasurementSnapshot());
        }

        [Fact]
        public async Task A_list_past_its_limit_is_reported_oversized_with_no_value_and_never_cached()
        {
            // The fixture's list holds at most 50 rows; the capped read stops at 51.
            await InsertLookupsAsync(60);
            await using AsyncServiceScope scope = CreateScope();
            ICacheableEntities entities = scope.ServiceProvider.GetRequiredService<ICacheableEntities>();

            CacheResult<CachedEntitySet<Lookup>> loaded = await entities.GetAsync<Lookup>();
            IDataBatch batch = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Read);
            BatchResult<CacheResult<CachedEntitySet<Lookup>>> probed = batch.FromCache<Lookup>();
            _ = await batch.ExecuteAsync(Token);

            Assert.Equal((null, CacheOutcome.Oversized), (loaded.Value, loaded.Outcome));
            Assert.Equal((null, CacheOutcome.Oversized), (probed.Value.Value, probed.Value.Outcome));
            Assert.Null(entities.Peek<Lookup>());

            // Warned once per entity per process, though both reads overflowed.
            Assert.Single(fixture.Logs.GetSnapshot(), static record => record.Id.Name == "CacheableEntityOverflow");
        }

        [Fact]
        public async Task A_list_whose_tag_row_is_missing_is_served_uncached()
        {
            await InsertLookupsAsync(1);
            await SqlServerFixture.ExecuteAsync(fixture.Tenant.ConnectionString, $"DELETE FROM [core].[VersionTags] WHERE [Name] = N'{LookupTag}';");
            await using AsyncServiceScope scope = CreateScope();
            ICacheableEntities entities = scope.ServiceProvider.GetRequiredService<ICacheableEntities>();

            CacheResult<CachedEntitySet<Lookup>> first = await entities.GetAsync<Lookup>();
            CacheResult<CachedEntitySet<Lookup>> second = await entities.GetAsync<Lookup>();

            Assert.Equal((CacheOutcome.Uncached, VersionTag.None, 1), (first.Outcome, first.Tag, first.Value!.Items.Count));
            Assert.Equal(CacheOutcome.Uncached, second.Outcome);
            Assert.Equal(CacheOutcome.Miss, Store.Find(Store.TypeOf(typeof(Lookup)), FixtureTenant.TenantId, MultilingualShape.Primary, VersionTag.None, out _));
        }

        /// <summary>Inserts lookups <c>L1</c>... with ids from 1; every even one inactive.</summary>
        /// <param name="count">How many.</param>
        /// <returns>A task that completes when they are stored.</returns>
        private async Task InsertLookupsAsync(int count)
        {
            string values = string.Join(
                ",\n",
                Enumerable.Range(1, count).Select(static id => string.Create(
                    CultureInfo.InvariantCulture,
                    $"({id}, 'L{id}', N'Lookup {id}', {(id % 2 == 0 ? 0 : 1)}, SYSDATETIMEOFFSET(), 1, SYSDATETIMEOFFSET(), 1)")));
            await SqlServerFixture.ExecuteAsync(
                fixture.Tenant.ConnectionString,
                $"INSERT INTO [fixture].[Lookups] ([Id], [Code], [Name], [IsActive], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]) VALUES\n{values};");
        }

        /// <summary>Warms the cache through a read batch.</summary>
        /// <returns>A task that completes when the list is cached.</returns>
        private async Task WarmAsync()
        {
            await using AsyncServiceScope scope = CreateScope();
            IDataBatch batch = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Read);
            BatchResult<CacheResult<CachedEntitySet<Lookup>>> cold = batch.FromCache<Lookup>();
            _ = await batch.ExecuteAsync(Token);
            Assert.Equal(CacheOutcome.Miss, cold.Value.Outcome);
        }

        /// <summary>Moves the list's tag in the database behind the process's back, as another instance's write would.</summary>
        /// <returns>The new tag.</returns>
        private async Task<Guid> MoveLookupTagAsync()
        {
            var moved = Guid.NewGuid();
            await SqlServerFixture.ExecuteAsync(
                fixture.Tenant.ConnectionString,
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[VersionTags] SET [Tag] = '{moved}' WHERE [Name] = N'{LookupTag}';"));
            return moved;
        }

        /// <summary>A scope of the fixture tenant whose context carries settings of one language, as the settings load leaves it.</summary>
        /// <returns>The scope.</returns>
        private AsyncServiceScope CreateScope()
        {
            AsyncServiceScope scope = fixture.Tenant.CreateScope();
            IRequestContextHolder holder = scope.ServiceProvider.GetRequiredService<IRequestContextHolder>();
            TenantSettings settings = new()
            {
                TenantId = FixtureTenant.TenantId,
                Tag = VersionTag.New(),
                Names = ["Fixture", null, null],
                Languages = [new LanguageInfo("en", "English", "E", false)],
                Shape = MultilingualShape.Primary,
                Calendars = [],
                TimeZone = TimeZoneInfo.Utc,
                SqlServerTimeZoneName = "UTC",
                ModifiedAt = DateTimeOffset.UnixEpoch,
                Entries = new Dictionary<string, JsonElement>(),
            };
            holder.Set(holder.Current with { TenantSettings = settings });
            return scope;
        }
    }
}
