// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Caching;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Tests.Caching;
using Tellma.Core.Tests.Data.Metadata;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition.Checks
{
    /// <summary>
    ///     <c>core.cacheable-entities</c>: a cacheable entity is a top-level entity with an int key and
    ///     no child collection, its resolved row limit is within the ceiling, and its read securable
    ///     has no row filter; the store itself is created at startup, so one that cannot be is reported
    ///     there.
    /// </summary>
    public sealed class CacheableEntitiesCheckTests
    {
        private const string Check = "core.cacheable-entities";

        [Fact]
        public async Task A_sound_model_passes()
        {
            Assert.Empty((await TestComposition.CheckAsync(CacheModel.Compose)).From(Check));
        }

        [Fact]
        public async Task A_cacheable_entity_with_a_bigint_key_is_reported()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static tellma => tellma
                .AddFeature(new TestFeature<Tag.C>("cache", contribute: static contribution => contribution.Entity<CmLedgerLine>())));

            CompositionProblem problem = Assert.Single(problems.From(Check));
            Assert.Equal(
                "The cacheable entity cm.CmLedgerLine (CmLedgerLine) is not a top-level entity with an int key; a cached list is keyed by an int id.",
                problem.Problem);
        }

        [Fact]
        public async Task A_cacheable_entity_with_a_child_collection_is_reported()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("fixture", contribute: static contribution => contribution
                    .Entity<Account>()
                    .Entity<Region>()
                    .Entity<Shipment>()
                    .Entity<ShipmentExtra>()
                    .Entity<Carrier>()
                    .Model<AccountConfiguration>()))
                .UseEntity<User, MdMember>()
                .UseEntity<Region, MyRegion>());

            CompositionProblem problem = Assert.Single(problems.From(Check));
            Assert.StartsWith("The cacheable entity fixture.Account declares the child collection ", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_row_limit_above_the_ceiling_is_reported_and_an_unspecified_one_takes_the_default()
        {
            // The currencies ask for 3 rows; the units leave it to the default of 1.
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(
                CacheModel.Compose,
                TestComposition.Configuration(TestComposition.Sound(("Tellma:Cache:EntityMaxRowsCeiling", "2"), ("Tellma:Cache:EntityMaxRowsDefault", "1"))));

            CompositionProblem problem = Assert.Single(problems.From(Check));
            Assert.Equal(
                "The cacheable entity cm.CmCurrency has a row limit of 3, outside 1 to 2; a list that large is not reference data.",
                problem.Problem);
        }

        [Fact]
        public async Task Limits_that_do_not_validate_are_left_to_the_options_check()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(
                CacheModel.Compose,
                TestComposition.Configuration(TestComposition.Sound(("Tellma:Cache:EntityMaxRowsCeiling", "2"))));

            Assert.Empty(problems.From(Check));
            Assert.NotEmpty(problems.From("core.options"));
        }

        [Fact]
        public async Task A_store_that_cannot_be_created_is_reported_at_startup_and_one_that_can_is_created_there()
        {
            await using ServiceProvider services = TestComposition.Compose(CacheModel.Compose, TestComposition.Configuration()).BuildServiceProvider();
            CacheableEntitiesCheck check = new();

            // --- Another live entities cache on the host's meter: the store cannot be created ---
            CacheableEntityStore other = new(
                services.GetRequiredService<IEntityMetadataProvider>(),
                services.GetRequiredService<IQueryexSchemaProvider>(),
                Options.Create(new CacheOptions()),
                services.GetRequiredService<IMeterFactory>(),
                TimeProvider.System,
                NullLogger<CacheableEntityStore>.Instance);
            CompositionProblem problem = Assert.Single(await check.CheckAsync(services, TestContext.Current.CancellationToken));
            Assert.StartsWith("The cacheable entities' cache cannot be created: A cache of the kind 'entities' already reports on this meter.", problem.Problem, StringComparison.Ordinal);

            // --- Once it is gone, the check creates the host's store, which every later resolution finds ---
            other.Dispose();
            Assert.Empty(await check.CheckAsync(services, TestContext.Current.CancellationToken));
            Assert.Same(services.GetRequiredService<CacheableEntityStore>(), services.GetRequiredService<CacheableEntityStore>());
        }

        [Fact]
        public async Task A_read_securable_with_a_row_filter_is_reported()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static tellma => tellma
                .AddFeature(new TestFeature<Tag.C>("cache", contribute: static contribution => contribution
                    .Entity<CmCurrency>()
                    .Securables(static securables => securables.Add(CacheModel.Currencies, AccessActions.Read, CacheModel.Currencies)))));

            CompositionProblem problem = Assert.Single(problems.From(Check));
            Assert.Equal(
                "The cacheable entity cm.CmCurrency has a Read securable filtered through cm.CmCurrency; a cached list is shared by every reader, so a row filter on it would be ignored.",
                problem.Problem);
        }

        [Fact]
        public async Task A_read_securable_without_a_row_filter_passes()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static tellma => tellma
                .AddFeature(new TestFeature<Tag.C>("cache", contribute: static contribution => contribution
                    .Entity<CmCurrency>()
                    .Securables(static securables => securables.Add(CacheModel.Currencies, AccessActions.Read, filterRoot: null)))));

            Assert.Empty(problems.From(Check));
        }
    }
}
