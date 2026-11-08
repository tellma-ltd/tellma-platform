// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Reflection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Realizers;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition
{
    /// <summary>Realisation: items realised in topological order, services overridden by dependents, platform registrations.</summary>
    public class RealisationTests
    {
        [Fact]
        public void A_dependent_feature_replaces_the_service_of_a_feature_it_requires_whatever_the_order_added()
        {
            // The distribution's feature is added before the pack it requires, so only the
            // topological order can put the replacement last.
            IServiceCollection services = TestComposition.Compose(tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("acme", d => d.Requires("gl"), c => c.Singleton<ISampleService, ReplacementService>()))
                .AddFeature(new TestFeature<Tag.B>("gl", contribute: c => c.Singleton<ISampleService, SampleService>())));

            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.Equal("distribution", provider.GetRequiredService<ISampleService>().Who);
            Assert.Single(provider.GetServices<ISampleService>());
        }

        [Fact]
        public void Collection_services_are_never_replaced()
        {
            IServiceCollection services = TestComposition.Compose(tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("acme", d => d.Requires("gl"), c => c.Singleton<ISampleExtension, SecondExtension>()))
                .AddFeature(new TestFeature<Tag.B>("gl", contribute: c => c.Singleton<ISampleExtension, FirstExtension>())));

            using ServiceProvider provider = services.BuildServiceProvider();
            Type[] expected = [typeof(FirstExtension), typeof(SecondExtension)];
            Assert.Equal(expected, provider.GetServices<ISampleExtension>().Select(static e => e.GetType()));
        }

        [Fact]
        public void Startup_checks_contributed_by_features_add_to_the_built_in_checks()
        {
            IServiceCollection services = TestComposition.Compose(tellma =>
                tellma.AddFeature(new TestFeature<Tag.A>("acme", contribute: c => c.Singleton<IStartupCheck, FailingCheck>())));

            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.Contains(provider.GetServices<IStartupCheck>(), static check => check is FailingCheck);
            Assert.Contains(provider.GetServices<IStartupCheck>(), static check => check.Name == "core.options");
        }

        [Fact]
        public void Features_are_realised_core_first_then_each_after_what_it_requires_and_nests_else_in_the_order_added()
        {
            IServiceCollection services = TestComposition.Compose(tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("acme", d => d.Requires("gl"), c => c.Feature(new TestFeature<Tag.D>("acme.sales"))))
                .AddFeature(new TestFeature<Tag.B>("other"))
                .AddFeature(new TestFeature<Tag.C>("gl")));

            string[] expected = ["core", "other", "gl", "acme.sales", "acme"];
            Assert.Equal(expected, Record(services).Features.Select(static f => f.Name));
        }

        [Fact]
        public void A_service_with_both_or_neither_an_implementation_and_a_factory_is_reported()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(tellma => tellma.AddFeature(new TestFeature<Tag.A>(
                "acme",
                contribute: c => c
                    .Add(new ServiceContributionItem(typeof(ISampleService), null, null, ServiceLifetimeKind.Singleton))
                    .Add(new ServiceContributionItem(typeof(ISampleService), typeof(SampleService), static _ => new SampleService(), ServiceLifetimeKind.Singleton)))));

            Assert.Equal(2, problems.Count);
            Assert.Contains("neither an implementation type nor a factory", problems[0].Problem, StringComparison.Ordinal);
            Assert.Contains("both an implementation type and a factory", problems[1].Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void An_implementation_that_is_abstract_or_does_not_implement_its_service_is_reported()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(tellma => tellma.AddFeature(new TestFeature<Tag.A>(
                "acme",
                contribute: c => c
                    .Add(new ServiceContributionItem(typeof(ISampleService), typeof(FirstExtension), null, ServiceLifetimeKind.Scoped))
                    .Add(new ServiceContributionItem(typeof(ISampleService), typeof(ISampleService), null, ServiceLifetimeKind.Scoped))
                    .Add(new ServiceContributionItem(typeof(ISampleService), typeof(SampleService), null, (ServiceLifetimeKind)7)))));

            Assert.Equal(3, problems.Count);
            Assert.Contains("which it does not implement", problems[0].Problem, StringComparison.Ordinal);
            Assert.Contains("is not a concrete class", problems[1].Problem, StringComparison.Ordinal);
            Assert.Contains("undefined lifetime 7", problems[2].Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void Factory_and_open_generic_registrations_are_realised_with_their_lifetimes()
        {
            IServiceCollection services = TestComposition.Compose(tellma => tellma.AddFeature(new TestFeature<Tag.A>(
                "acme",
                contribute: c => c
                    .Add(new ServiceContributionItem(typeof(ISampleService), null, static _ => new ReplacementService(), ServiceLifetimeKind.Transient))
                    .Add(new ServiceContributionItem(typeof(IRepository<>), typeof(Repository<>), null, ServiceLifetimeKind.Scoped)))));

            ServiceDescriptor factory = services.Single(static d => d.ServiceType == typeof(ISampleService));
            Assert.Equal(ServiceLifetime.Transient, factory.Lifetime);

            using ServiceProvider provider = services.BuildServiceProvider();
            using IServiceScope scope = provider.CreateScope();
            Assert.IsType<Repository<int>>(scope.ServiceProvider.GetRequiredService<IRepository<int>>());
            Assert.Equal("distribution", provider.GetRequiredService<ISampleService>().Who);
        }

        [Fact]
        public void A_realizer_for_a_base_item_does_not_handle_a_derived_one()
        {
            ContributionRealizerSet realizers = ContributionRealizerSet.CreateBuiltIn().Add(new BaseItemRealizer());

            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(
                tellma => tellma.AddFeature(new TestFeature<Tag.A>("acme", contribute: c => c.Add(new BaseItem()).Add(new DerivedItem()))),
                realizers: realizers);

            Assert.Contains(nameof(DerivedItem), Assert.Single(problems).Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void Table_types_model_configurations_and_shape_requirements_are_recorded_once_in_feature_order()
        {
            IServiceCollection services = TestComposition.Compose(tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("acme", d => d.Requires("gl"), c => c
                    .TableType<SampleRow>()
                    .Model<SampleRowConfiguration>()))
                .AddFeature(new TestFeature<Tag.B>("gl", contribute: c => c
                    .TableType<SampleRow>()
                    .Model<SampleRowConfiguration>()
                    .Model(typeof(OpenConfiguration<>))
                    .RequiresShape<SampleEntity, IEInvoiceTracked>()
                    .RequiresShape<SampleEntity, IEInvoiceTracked>()))
                .UseEntity<SampleEntity, TrackedLeaf>());

            TellmaCompositionRecord record = Record(services);
            Assert.Equal("gl", Assert.Single(record.TableTypes).Owner.Name);
            Type[] configurations = [typeof(SampleRowConfiguration), typeof(OpenConfiguration<>)];
            Assert.Equal(configurations, record.ModelConfigurations.Select(static c => c.Type));
            Assert.Single(record.ShapeRequirements);
            Assert.Equal(typeof(TrackedLeaf), record.LeafOf(typeof(SampleEntity)));
            Assert.Equal(typeof(SampleRow), record.LeafOf(typeof(SampleRow)));
        }

        [Fact]
        public void A_shape_that_is_not_an_interface_and_an_open_generic_table_type_are_reported()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(tellma => tellma.AddFeature(new TestFeature<Tag.A>(
                "acme",
                contribute: c => c
                    .RequiresShape<SampleEntity, SampleLeaf>()
                    .TableType<SampleEntity>()
                    .Add(new TableTypeContributionItem(typeof(Repository<>))))));

            Assert.Equal(2, problems.Count);
            Assert.Contains("a shape must be a closed interface", problems[0].Problem, StringComparison.Ordinal);
            Assert.Contains("Repository<T>", problems[1].Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void Declared_options_are_bound_from_their_configuration_path()
        {
            IServiceCollection services = TestComposition.Compose(
                tellma => tellma.AddFeature(new TestFeature<Tag.A>("acme", d => d.Options<SampleOptions>("Tellma:Acme"))),
                TestComposition.Configuration(new Dictionary<string, string?>
                {
                    ["Tellma:PublicOrigin"] = "https://acme.app.tellma.com",
                    ["Tellma:Acme:Name"] = "Acme",
                    ["Tellma:Acme:Count"] = "4",
                }));

            using ServiceProvider provider = services.BuildServiceProvider();
            SampleOptions options = provider.GetRequiredService<IOptions<SampleOptions>>().Value;
            Assert.Equal(("Acme", 4), (options.Name, options.Count));
        }

        [Fact]
        public void The_deployment_identity_and_versions_are_registered()
        {
            IServiceCollection services = TestComposition.Compose(static _ => { }, environment: new TestEnvironment("Staging"));

            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.Equal("acme-staging", provider.GetRequiredService<DeploymentIdentity>().DeploymentId);

            DeploymentVersions versions = provider.GetRequiredService<DeploymentVersions>();
            string platform = typeof(CoreFeature).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
            Assert.Equal(platform, versions.PlatformVersion);
            Assert.False(string.IsNullOrWhiteSpace(versions.DistributionVersion));
        }

        [Fact]
        public void A_deployment_identity_the_host_registered_first_is_kept()
        {
            ServiceCollection services = new();
            services.AddSingleton(new DeploymentIdentity("identity", "Production"));

            _ = TestComposition.Compose(static _ => { }, services: services);

            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.Equal("identity", provider.GetRequiredService<DeploymentIdentity>().Application);
        }

        [Fact]
        public void The_startup_gate_is_inserted_ahead_of_every_hosted_service()
        {
            ServiceCollection services = new();
            services.AddHostedService<ProbeHostedService>();

            _ = TestComposition.Compose(static _ => { }, services: services);

            ServiceDescriptor[] hosted = [.. services.Where(static d => d.ServiceType == typeof(IHostedService))];
            Assert.Equal(typeof(TellmaStartupGate), hosted[0].ImplementationType);
            Assert.Equal(2, hosted.Length);
        }

        private static TellmaCompositionRecord Record(IServiceCollection services)
        {
            return (TellmaCompositionRecord)services.Single(static d => d.ServiceType == typeof(TellmaCompositionRecord)).ImplementationInstance!;
        }

        /// <summary>A base item kind with a realizer.</summary>
        private record BaseItem : FeatureContributionItem;

        /// <summary>A derived item kind without one.</summary>
        private sealed record DerivedItem : BaseItem;

        /// <summary>Realises <see cref="BaseItem" /> and nothing else.</summary>
        private sealed class BaseItemRealizer : IContributionRealizer<BaseItem>
        {
            /// <inheritdoc />
            public void Realize(BaseItem item, ITellmaFeature owner, RealizationContext context)
            {
            }
        }

        /// <summary>Stands in for an EF entity type configuration.</summary>
        private sealed class SampleRowConfiguration;

        /// <summary>Stands in for an open EF entity type configuration.</summary>
        /// <typeparam name="T">The leaf.</typeparam>
        private sealed class OpenConfiguration<T>;
    }
}
