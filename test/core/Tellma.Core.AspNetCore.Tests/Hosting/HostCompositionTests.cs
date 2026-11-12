// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Tests.Infrastructure;
using Tellma.Core.Tenancy;

namespace Tellma.Core.AspNetCore.Tests.Hosting
{
    /// <summary>
    ///     What the host checks at startup and reports when ready: the persisted key ring, the
    ///     framework's forwarded-headers switch, and the readiness of the catalog.
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
