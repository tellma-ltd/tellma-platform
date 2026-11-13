// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition
{
    /// <summary>The realised gate, run by a real generic host.</summary>
    public class StartupGateTests
    {
        [Fact]
        public async Task The_gate_throws_every_check_problem_at_once_before_any_other_hosted_service_starts()
        {
            ProbeHostedService probe = new();
            ProbeLifecycleService lifecycle = new();

            using IHost host = BuildHost(
                services =>
                {
                    // Registered before the composition, as a host's own services are.
                    services.AddHostedService(_ => lifecycle);
                    services.AddHostedService(_ => probe);
                },
                tellma => tellma.AddFeature(new TestFeature<Tag.A>("acme", contribute: c => c
                    .Singleton<IStartupCheck, FailingCheck>()
                    .Singleton<IStartupCheck, FailingInfrastructureCheck>())));

            TellmaCompositionException exception = await Assert.ThrowsAsync<TellmaCompositionException>(
                () => host.StartAsync(TestContext.Current.CancellationToken));

            string[] expected = ["test.failing", "test.infrastructure"];
            Assert.Equal(expected, exception.Problems.Select(static p => p.Source));
            Assert.False(lifecycle.Starting);
            Assert.False(probe.Started);
        }

        [Fact]
        public async Task A_check_that_throws_is_reported_beside_the_problems_of_the_others()
        {
            using IHost host = BuildHost(
                static _ => { },
                tellma => tellma.AddFeature(new TestFeature<Tag.A>("acme", contribute: c => c
                    .Singleton<IStartupCheck, ThrowingCheck>()
                    .Singleton<IStartupCheck, FailingCheck>())));

            TellmaCompositionException exception = await Assert.ThrowsAsync<TellmaCompositionException>(
                () => host.StartAsync(TestContext.Current.CancellationToken));

            Assert.Equal(2, exception.Problems.Count);
            Assert.Equal("test.throwing", exception.Problems[0].Source);
            Assert.Contains("FormatException: The check crashed.", exception.Problems[0].Problem, StringComparison.Ordinal);
            Assert.Equal("test.failing", exception.Problems[1].Source);
        }

        [Fact]
        public async Task A_problem_text_spanning_lines_renders_as_one_message_line()
        {
            using IHost host = BuildHost(
                static _ => { },
                tellma => tellma.AddFeature(new TestFeature<Tag.A>("acme", contribute: c => c
                    .Singleton<IStartupCheck, MultiLineThrowingCheck>()
                    .Singleton<IStartupCheck, FailingCheck>())));

            TellmaCompositionException exception = await Assert.ThrowsAsync<TellmaCompositionException>(
                () => host.StartAsync(TestContext.Current.CancellationToken));

            Assert.Equal(exception.Problems.Count + 1, exception.Message.Split('\n').Length);
            Assert.Contains("first error second error", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_contributed_check_that_cannot_be_created_still_lets_the_built_in_checks_report()
        {
            using IHost host = BuildHost(
                static _ => { },
                tellma => tellma.AddFeature(new TestFeature<Tag.A>("acme", contribute: c => c.Singleton<IStartupCheck, UnconstructibleCheck>())),
                new Dictionary<string, string?>());

            TellmaCompositionException exception = await Assert.ThrowsAsync<TellmaCompositionException>(
                () => host.StartAsync(TestContext.Current.CancellationToken));

            Assert.Contains("could not be created, so only the built-in ones ran", exception.Problems[0].Problem, StringComparison.Ordinal);
            Assert.Contains("ArgumentException", exception.Problems[0].Problem, StringComparison.Ordinal);

            // The empty configuration then fails the built-in checks that read configuration.
            string[] builtIn = ["core.options", "core.sql-profiles", "core.sql-profiles", "core.public-origin"];
            Assert.Equal(builtIn, exception.Problems.Skip(1).Select(static p => p.Source));
        }

        [Fact]
        public async Task Built_in_check_problems_aggregate_with_contributed_ones()
        {
            using IHost host = BuildHost(
                static services => services.AddSingleton(new DeploymentIdentity("identity", "Production")),
                tellma => tellma.AddFeature(new TestFeature<Tag.A>("acme", contribute: c => c.Singleton<IStartupCheck, FailingCheck>())),
                new Dictionary<string, string?>());

            TellmaCompositionException exception = await Assert.ThrowsAsync<TellmaCompositionException>(
                () => host.StartAsync(TestContext.Current.CancellationToken));

            string[] expected = ["core.options", "core.deployment-identity", "core.sql-profiles", "core.sql-profiles", "core.public-origin", "test.failing"];
            Assert.Equal(expected, exception.Problems.Select(static p => p.Source));
        }

        [Fact]
        public async Task A_sound_composition_starts_and_the_host_runs_its_hosted_services()
        {
            ProbeHostedService probe = new();
            using IHost host = BuildHost(services => services.AddHostedService(_ => probe), static _ => { });

            await host.StartAsync(TestContext.Current.CancellationToken);
            await host.StopAsync(TestContext.Current.CancellationToken);

            Assert.True(probe.Started);
        }

        [Fact]
        public async Task Every_check_runs_inside_one_shared_scope()
        {
            List<Guid> seen = [];
            ServiceCollection services = [];
            services.AddScoped<ScopedProbe>();
            services.AddSingleton(seen);

            _ = TestComposition.Compose(
                tellma => tellma.AddFeature(new TestFeature<Tag.A>("acme", contribute: c => c
                    .Add(new ServiceContributionItem(typeof(IStartupCheck), null, static sp => new ScopeRecordingCheck(sp.GetRequiredService<List<Guid>>()), ServiceLifetimeKind.Singleton))
                    .Add(new ServiceContributionItem(typeof(IStartupCheck), null, static sp => new ScopeRecordingCheck(sp.GetRequiredService<List<Guid>>()), ServiceLifetimeKind.Singleton)))),
                services: services);

            await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
            Assert.Empty(await TellmaStartupGate.RunChecksAsync(provider, includeInfrastructure: true, TestContext.Current.CancellationToken));
            Assert.Empty(await TellmaStartupGate.RunChecksAsync(provider, includeInfrastructure: true, TestContext.Current.CancellationToken));

            // Two checks per run share one instance; the second run gets a fresh scope.
            Assert.Equal(4, seen.Count);
            Assert.Equal(seen[0], seen[1]);
            Assert.Equal(seen[2], seen[3]);
            Assert.NotEqual(seen[0], seen[2]);
        }

        [Fact]
        public async Task A_run_without_infrastructure_skips_the_checks_that_need_it()
        {
            IServiceCollection services = TestComposition.Compose(
                tellma => tellma.AddFeature(new TestFeature<Tag.A>("acme", contribute: c => c
                    .Singleton<IStartupCheck, FailingCheck>()
                    .Singleton<IStartupCheck, FailingInfrastructureCheck>())),
                TestComposition.Configuration(new Dictionary<string, string?>()));

            await using ServiceProvider provider = services.BuildServiceProvider();
            IReadOnlyList<CompositionProblem> problems = await TellmaStartupGate.RunChecksAsync(
                provider, includeInfrastructure: false, TestContext.Current.CancellationToken);

            // The missing public origin and the infrastructure check are both skipped.
            Assert.Equal("test.failing", Assert.Single(problems).Source);
        }

        [Fact]
        public void The_built_in_checks_declare_which_of_them_need_infrastructure()
        {
            IServiceCollection services = TestComposition.Compose(static _ => { });
            using ServiceProvider provider = services.BuildServiceProvider();

            var checks = provider.GetServices<IStartupCheck>().ToDictionary(static c => c.Name, static c => c.RequiresInfrastructure);
            Dictionary<string, bool> expected = new()
            {
                ["core.options"] = true,
                ["core.deployment-identity"] = false,
                ["core.globalization"] = false,
                ["core.key-vault-literals"] = true,
                ["core.sql-profiles"] = true,
                ["core.public-origin"] = true,
                ["core.taxonomy"] = false,
                ["core.entity-shapes"] = false,
                ["core.model-contributors"] = false,
                ["core.entity-metadata"] = false,
                ["core.version-tags"] = false,
                ["core.languages"] = false,
                ["core.calendars"] = false,
                ["core.value-sets"] = false,
                ["core.notification-types"] = false,
                ["core.notification-channels"] = false,
                ["core.client-events"] = false,
                ["core.securables"] = false,
                ["core.time-zones"] = true,
                ["core.provisioning-steps"] = false,
            };

            Assert.Equal(expected, checks);
        }

        private static IHost BuildHost(
            Action<IServiceCollection> configureServices,
            Action<TellmaBuilder> compose,
            IReadOnlyDictionary<string, string?>? configuration = null)
        {
            HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
            {
                EnvironmentName = Environments.Production,
                ContentRootPath = AppContext.BaseDirectory,
            });

            builder.Configuration.AddInMemoryCollection(configuration ?? TestComposition.SoundConfiguration);
            configureServices(builder.Services);
            builder.Services.AddTellma(TestComposition.Slug, builder.Configuration, builder.Environment, compose).UseFakeCatalog();
            return builder.Build();
        }
    }
}
