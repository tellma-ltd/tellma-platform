// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using System.Net;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Tests.Infrastructure;
using Tellma.Core.Tenancy;

namespace Tellma.Core.AspNetCore.Tests.Hosting
{
    /// <summary>
    ///     What the host checks at startup and reports when ready: the persisted key ring, the
    ///     framework's forwarded-headers switch, the identity clients' secrets, and the readiness of
    ///     the catalog.
    /// </summary>
    public sealed class HostCompositionTests
    {
        [Fact]
        public async Task Outside_Development_the_key_ring_must_be_persisted()
        {
            (WebApplication app, _, _) = TellmaTestHost.Build(static options => options.Settings.Remove("Tellma:DataProtection:KeyRingPath"));
            await using (app)
            {
                TellmaCompositionException exception = await Assert.ThrowsAsync<TellmaCompositionException>(
                    () => app.StartAsync(TestContext.Current.CancellationToken));
                _ = Assert.Single(exception.Problems, static problem => problem.Source == DataProtectionCheck.CheckName);
            }
        }

        [Fact]
        public async Task A_standalone_host_without_its_client_secret_fails_startup()
        {
            (WebApplication app, _, _) = TellmaTestHost.Build(static options => options.Settings.Remove("Tellma:Identity:ClientSecret"));
            await using (app)
            {
                TellmaCompositionException exception = await Assert.ThrowsAsync<TellmaCompositionException>(
                    () => app.StartAsync(TestContext.Current.CancellationToken));
                CompositionProblem problem = Assert.Single(exception.Problems, static problem => problem.Source == IdentityCheck.CheckName);
                Assert.Contains("Tellma:Identity:ClientSecret", problem.Problem, StringComparison.Ordinal);
            }
        }

        /// <summary>The identity check's verdicts: the mode, the environment, the secrets set, whether mail is composed, and the settings it names.</summary>
        /// <returns>The cases.</returns>
        public static TheoryData<string, string, bool, bool, bool, string[]> IdentityCases()
        {
            return new TheoryData<string, string, bool, bool, bool, string[]>
            {
                { "Standalone", "Production", true, false, false, [] },
                { "Standalone", "Production", false, false, false, ["ClientSecret"] },
                { "InProc", "Production", true, true, true, [] },
                { "InProc", "Production", false, false, true, ["ClientSecret", "ServiceClientSecret"] },
                { "InProc", "Production", true, true, false, ["IEmailSender"] },
                { "InProc", "Development", true, false, true, [] },
            };
        }

        [Theory]
        [MemberData(nameof(IdentityCases))]
        public async Task The_identity_check_requires_stable_secrets_and_in_proc_mail(
            string mode, string environment, bool clientSecret, bool serviceSecret, bool mail, string[] named)
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Tellma:Identity:Mode"] = mode })
                .Build();
            ServiceCollection services = new();
            if (mail)
            {
                _ = services.AddSingleton<IEmailSender>(static _ => throw new InvalidOperationException("Never resolved."));
            }

            await using ServiceProvider provider = services.BuildServiceProvider();
            IdentityCheck check = new(
                new IdentityClientSecrets(clientSecret ? "secret" : null, serviceSecret ? "service-secret" : null),
                configuration,
                new HostingEnvironment { EnvironmentName = environment });

            IReadOnlyList<CompositionProblem> problems = await check.CheckAsync(provider, TestContext.Current.CancellationToken);

            Assert.Equal(named.Length, problems.Count);
            foreach (string name in named)
            {
                _ = Assert.Single(problems, problem => problem.Problem.StartsWith("Tellma:Identity:" + name + " is not set", StringComparison.Ordinal) || problem.Problem.Contains("no " + name, StringComparison.Ordinal));
            }
        }

        [Fact]
        public async Task Development_runs_on_the_default_key_ring()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options =>
            {
                options.Environment = "Development";
                options.Settings.Remove("Tellma:DataProtection:KeyRingPath");
            });
            using HttpResponseMessage response = await host.Client.GetAsync("/health/live", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task The_framework_forwarded_headers_switch_fails_startup()
        {
            (WebApplication app, _, _) = TellmaTestHost.Build(static options => options.Settings["ForwardedHeaders_Enabled"] = "true");
            await using (app)
            {
                TellmaCompositionException exception = await Assert.ThrowsAsync<TellmaCompositionException>(
                    () => app.StartAsync(TestContext.Current.CancellationToken));
                _ = Assert.Single(exception.Problems, static problem => problem.Problem.Contains("ASPNETCORE_FORWARDEDHEADERS_ENABLED", StringComparison.Ordinal));
            }
        }

        [Fact]
        public async Task Readiness_follows_the_catalog()
        {
            // The first load fails: the host starts, alive but not ready.
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options => options.CatalogFailure = new TimeoutException("The catalog is unreachable."));
            using HttpResponseMessage down = await host.Client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, down.StatusCode);
            using HttpResponseMessage alive = await host.Client.GetAsync("/health/live", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, alive.StatusCode);

            // The catalog comes back: ready.
            host.Catalog.Failure = null;
            await host.Services.GetRequiredService<ITenantRegistry>().RefreshAsync(force: true, TestContext.Current.CancellationToken);
            using HttpResponseMessage up = await host.Client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, up.StatusCode);
        }

        [Fact]
        public async Task The_readiness_answer_names_no_check()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options => options.CatalogFailure = new TimeoutException("The catalog is unreachable."));
            using HttpResponseMessage response = await host.Client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.DoesNotContain("catalog", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("unreachable", body, StringComparison.OrdinalIgnoreCase);
        }
    }
}
