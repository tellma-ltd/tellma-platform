// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Composition;
using Tellma.Core.Data.Metadata;
using Tellma.Core.Tests.Data.Metadata;
using Tellma.Core.Tests.Data.Metadata.Malformed;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition.Checks
{
    /// <summary>
    ///     <c>core.entity-metadata</c>: the realised gate builds the tenant model and its entity metadata,
    ///     fails startup on every problem, logs every warning, and the host's provider is the one it built.
    /// </summary>
    public class EntityMetadataCheckTests
    {
        private const string Check = "core.entity-metadata";

        [Fact]
        public async Task A_sound_composition_passes_and_its_provider_resolves_a_leaf_from_its_default()
        {
            IServiceCollection services = TestComposition.Compose(SoundComposition);
            await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

            Assert.Empty((await TellmaStartupGate.RunChecksAsync(provider, includeInfrastructure: true, TestContext.Current.CancellationToken)).From(Check));

            IEntityMetadataProvider metadata = provider.GetRequiredService<IEntityMetadataProvider>();
            Assert.Same(provider.GetRequiredService<EntityMetadataSource>().Provider, metadata);
            Assert.Equal(typeof(MyRegion), metadata.Get(typeof(Region)).ClrType);
            Assert.Equal("geo.Region", metadata.Get(typeof(MyRegion)).Name);
        }

        [Fact]
        public async Task A_warning_is_logged_and_never_fails_startup()
        {
            FakeLoggerProvider logs = new();
            IServiceCollection services = new ServiceCollection()
                .AddSingleton(SandboxContext.Never)
                .AddSingleton<ILoggerFactory>(new LoggerFactory([logs]));

            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(SoundComposition, services: services);

            Assert.Empty(problems.From(Check));
            FakeLogRecord warning = Assert.Single(logs.Collector.GetSnapshot(), static record => record.Category == "Tellma.Core.Composition.Checks.EntityMetadataCheck");
            Assert.Equal(LogLevel.Warning, warning.Level);
            Assert.Contains("fixture.Carrier", warning.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_malformed_composition_fails_the_gate_and_its_provider_never_resolves()
        {
            static void Compose(TellmaBuilder tellma)
            {
                _ = tellma.AddFeature(new TestFeature<Tag.A>("fixture", contribute: static contribution => contribution
                    .Entity<MdUser>()
                    .Entity<MdJob>()
                    .Entity<MdBlob>()
                    .Entity<Untabled>()
                    .Entity<TwinLess>()));
            }

            Assert.Equal(2, (await TestComposition.CheckAsync(Compose)).From(Check).Count);

            await using ServiceProvider provider = TestComposition.Compose(Compose).BuildServiceProvider(validateScopes: true);
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(provider.GetRequiredService<IEntityMetadataProvider>);
            Assert.Contains("failed entity metadata validation", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Untabled declares no [Table]", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_model_that_cannot_be_built_is_one_problem_naming_the_declaration()
        {
            CompositionProblem dangling = await CannotBuildAsync(static contribution => contribution.Entity<Dangling>());

            Assert.Contains("names the entity Tellma.Core.Tests.Data.Metadata.Malformed.Tabled, which is not in the model", dangling.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_configuration_of_a_replaced_default_that_cannot_share_its_leafs_table_is_refused()
        {
            static void Compose(TellmaBuilder tellma)
            {
                _ = tellma
                    .AddFeature(new TestFeature<Tag.A>("fixture", contribute: static contribution => contribution
                        .Entity<MdUser>()
                        .Entity<MdJob>()
                        .Entity<MdBlob>()
                        .Model<DefaultUserConfiguration>()))
                    .UseEntity<MdUser, MyMdUser>();
            }

            CompositionProblem problem = Assert.Single((await TestComposition.CheckAsync(Compose)).From(Check));

            Assert.Contains("table-per-hierarchy", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public async Task The_web_host_and_the_migrator_compositions_fingerprint_one_storage()
        {
            // The two hosts compose one distribution under their own environments and deployments.
            IServiceCollection web = TestComposition.Compose(SoundComposition, environment: new TestEnvironment("Production"));
            IServiceCollection migrator = TestComposition.Compose(SoundComposition, environment: new TestEnvironment("Staging"));
            await using ServiceProvider webProvider = web.BuildServiceProvider(validateScopes: true);
            await using ServiceProvider migratorProvider = migrator.BuildServiceProvider(validateScopes: true);

            string webFingerprint = webProvider.GetRequiredService<IEntityMetadataProvider>().StorageFingerprint;

            Assert.Matches("^[0-9a-f]{64}$", webFingerprint);
            Assert.Equal(webFingerprint, migratorProvider.GetRequiredService<IEntityMetadataProvider>().StorageFingerprint);
            Assert.Equal(
                webProvider.GetRequiredService<IEntityMetadataProvider>().Get(typeof(Account)).SchemaFingerprint,
                migratorProvider.GetRequiredService<IEntityMetadataProvider>().Get(typeof(Account)).SchemaFingerprint);
        }

        /// <summary>The one problem of a composition whose model cannot be built.</summary>
        /// <param name="contribute">The entity that breaks the model, beside the platform stand-ins.</param>
        /// <returns>The problem.</returns>
        private static async Task<CompositionProblem> CannotBuildAsync(Action<FeatureContribution> contribute)
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("fixture", contribute: contribution =>
                {
                    _ = contribution.Entity<MdUser>().Entity<MdJob>().Entity<MdBlob>();
                    contribute(contribution);
                })));

            CompositionProblem problem = Assert.Single(problems.From(Check));
            Assert.StartsWith("The tenant model cannot be built: ", problem.Problem, StringComparison.Ordinal);
            return problem;
        }

        /// <summary>The sound metadata model as a distribution composes it.</summary>
        /// <param name="tellma">The builder.</param>
        private static void SoundComposition(TellmaBuilder tellma)
        {
            _ = tellma
                .AddFeature(new TestFeature<Tag.A>("fixture", contribute: static contribution => contribution
                    .Entity<MdUser>()
                    .Entity<MdJob>()
                    .Entity<MdBlob>()
                    .Entity<Account>()
                    .Entity<Region>()
                    .Entity<Shipment>()
                    .Entity<ShipmentExtra>()
                    .Entity<Carrier>()
                    .Model<AccountConfiguration>()))
                .UseEntity<MdUser, MyMdUser>()
                .UseEntity<Region, MyRegion>();
        }
    }
}
