// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore;
using Tellma.Core.Tenancy;
using Tellma.Distro.Acme.IntegrationTests.Infrastructure;
using Tellma.Identity.TestSupport;

namespace Tellma.Distro.Acme.IntegrationTests
{
    /// <summary>
    ///     Acme's web host serving its tenants' data by tenant state: a request bound to the signed-in
    ///     user, a frozen tenant whose database is set read-only, a read-only tenant whose
    ///     bookkeeping goes on while user changes stop, and a suspension reaching the instance that
    ///     received it at once and every other instance within one refresh.
    /// </summary>
    [Trait("Category", "Integration")]
    public sealed class TenantStateTests(SqlServerFixture sql)
    {
        /// <summary>Why a request a tenant serves to the seeded administrator cannot be asserted yet.</summary>
        private const string NeedsUserConnectionAndBootstrap =
            "Needs the user connection initializer, which binds a tenant request to its tenant user and stamps the user's activity, "
            + "and the administrator bootstrap provisioning step, which makes the seeded administrator a user of the seed tenants; "
            + "until the initializer is composed, every request a tenant serves fails closed.";

        /// <summary>Why a tenant request cannot reach the tenant's database yet.</summary>
        private const string NeedsUserConnection =
            "Needs the user connection initializer, whose round trip is a tenant request's first against the tenant's database.";

        /// <summary>The tenant every test moves: Acme's live seed tenant.</summary>
        private const int Tenant = 1;

        /// <summary>The refresh interval of the host that must see another's catalog write.</summary>
        private static readonly TimeSpan ShortRefresh = TimeSpan.FromSeconds(1);

        /// <summary>
        ///     The refresh interval of the host that receives the write: the option's maximum, so no
        ///     refresh of its own lands within the test.
        /// </summary>
        private static readonly TimeSpan LongRefresh = TellmaCatalogOptions.MaxRefreshInterval;

        /// <summary>
        ///     How long the host on the short interval may take to see another's catalog write, and
        ///     the most its refreshes may lie apart: a few of its intervals, with room for a loaded
        ///     machine to run the timer late, and a third of the default interval, so a host that
        ///     ignored the configured interval cannot meet it.
        /// </summary>
        private static readonly TimeSpan PropagationBound = TimeSpan.FromSeconds(5);

        /// <summary>How long to watch the host on the short interval for two refreshes.</summary>
        private static readonly TimeSpan CadenceWindow = TimeSpan.FromSeconds(10);

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact(Skip = NeedsUserConnectionAndBootstrap, SkipUnless = nameof(PlatformSeams.ServesTheAdministrator), SkipType = typeof(PlatformSeams))]
        public async Task The_first_tenant_request_binds_the_signed_in_user()
        {
            AcmeMigrator acme = new(sql, "acme_userid");
            _ = await acme.MigrateAsync();
            await using AcmeHost host = await AcmeHost.StartAsync(acme.Values, new AcmeHostOptions { MapsProbes = true });
            using OidcFlowClient flow = host.CreateFlowClient();
            await AcmeSignIn.SignInAsync(flow);

            using HttpResponseMessage read = await TenantProbes.ReadAsync(flow.Browser, Tenant);

            Assert.True(read.StatusCode == HttpStatusCode.OK, await Describe(read, host));
            TenantProbeResult bound = await TenantProbes.ResultAsync(read);
            Assert.Equal(Tenant, bound.TenantId);
            Assert.Equal(await AdministratorIdAsync(acme), bound.UserId);
        }

        [Fact]
        public async Task A_frozen_tenant_refuses_a_mutation_as_tenant_frozen_against_a_database_set_read_only()
        {
            AcmeMigrator acme = new(sql, "acme_frozen_write");
            _ = await acme.MigrateAsync();

            // --- The relocation runbook's first two steps: freeze, then set the database read-only ---
            await acme.SetStateAsync(Tenant, TenantState.Frozen, "relocation");
            await acme.SetDatabaseReadOnlyAsync(Tenant);
            await using AcmeHost host = await AcmeHost.StartAsync(acme.Values, new AcmeHostOptions { MapsProbes = true });
            using OidcFlowClient flow = host.CreateFlowClient();
            await AcmeSignIn.SignInAsync(flow);

            using HttpResponseMessage write = await TenantProbes.WriteAsync(flow.Browser, Tenant);

            // Refused before the endpoint ran, so nothing attempted a write the database would refuse.
            await AssertRefusedAsync(write, TenantProblemCodes.Frozen, host);
            Assert.Equal(0, host.Probes!.Writes);
        }

        [Fact(Skip = NeedsUserConnectionAndBootstrap, SkipUnless = nameof(PlatformSeams.ServesTheAdministrator), SkipType = typeof(PlatformSeams))]
        public async Task A_frozen_tenant_serves_a_read_against_a_database_set_read_only()
        {
            AcmeMigrator acme = new(sql, "acme_frozen_read");
            _ = await acme.MigrateAsync();
            await acme.SetStateAsync(Tenant, TenantState.Frozen, "relocation");
            await acme.SetDatabaseReadOnlyAsync(Tenant);
            await using AcmeHost host = await AcmeHost.StartAsync(acme.Values, new AcmeHostOptions { MapsProbes = true });
            using OidcFlowClient flow = host.CreateFlowClient();
            await AcmeSignIn.SignInAsync(flow);

            // The connection stamps no activity and flips no first sign-in, so the read runs whole
            // against a database that refuses every write.
            using HttpResponseMessage read = await TenantProbes.ReadAsync(flow.Browser, Tenant);

            Assert.True(read.StatusCode == HttpStatusCode.OK, await Describe(read, host));
            TenantProbeResult served = await TenantProbes.ResultAsync(read);
            Assert.Equal(Tenant, served.TenantId);
            Assert.NotNull(served.UserId);
            Assert.True(served.Users > 0);
        }

        [Fact(Skip = NeedsUserConnection, SkipUnless = nameof(PlatformSeams.ConnectsTheUser), SkipType = typeof(PlatformSeams))]
        public async Task A_tenant_whose_database_does_not_list_the_running_schema_answers_503_schema_behind_with_retry_after_30()
        {
            AcmeMigrator acme = new(sql, "acme_schema_behind");
            _ = await acme.MigrateAsync();

            // --- The tenant's database as a migrator that has not reached this release leaves it ---
            await SqlServerFixture.ExecuteAsync(acme.TenantConnectionString(Tenant), "UPDATE [dbo].[__TellmaSchema] SET [Fingerprint] = REPLICATE('0', 64);");
            await using AcmeHost host = await AcmeHost.StartAsync(acme.Values, new AcmeHostOptions { MapsProbes = true });
            using OidcFlowClient flow = host.CreateFlowClient();
            await AcmeSignIn.SignInAsync(flow);

            using HttpResponseMessage read = await TenantProbes.ReadAsync(flow.Browser, Tenant);

            // A transient verdict the client waits out, never a server error.
            string answered = await TenantProbes.ProblemCodeAsync(read);
            Assert.True(
                read.StatusCode == HttpStatusCode.ServiceUnavailable && answered == TenantProblemCodes.SchemaBehind,
                $"{(int)read.StatusCode} {answered}\n{host.Warnings}");
            Assert.Equal(TimeSpan.FromSeconds(30), read.Headers.RetryAfter?.Delta);
        }

        [Fact]
        public async Task A_read_only_tenant_refuses_a_mutation_as_tenant_read_only()
        {
            AcmeMigrator acme = new(sql, "acme_readonly_write");
            _ = await acme.MigrateAsync();
            await acme.SetStateAsync(Tenant, TenantState.ReadOnly, "unpaid");
            await using AcmeHost host = await AcmeHost.StartAsync(acme.Values, new AcmeHostOptions { MapsProbes = true });
            using OidcFlowClient flow = host.CreateFlowClient();
            await AcmeSignIn.SignInAsync(flow);

            using HttpResponseMessage write = await TenantProbes.WriteAsync(flow.Browser, Tenant);

            await AssertRefusedAsync(write, TenantProblemCodes.ReadOnly, host);
            Assert.Equal(0, host.Probes!.Writes);
        }

        [Fact(Skip = NeedsUserConnectionAndBootstrap, SkipUnless = nameof(PlatformSeams.ServesTheAdministrator), SkipType = typeof(PlatformSeams))]
        public async Task A_read_on_a_read_only_tenant_stamps_the_user_s_activity()
        {
            AcmeMigrator acme = new(sql, "acme_readonly_stamp");
            _ = await acme.MigrateAsync();
            await acme.SetStateAsync(Tenant, TenantState.ReadOnly, "unpaid");
            await using AcmeHost host = await AcmeHost.StartAsync(acme.Values, new AcmeHostOptions { MapsProbes = true });
            using OidcFlowClient flow = host.CreateFlowClient();
            await AcmeSignIn.SignInAsync(flow);
            int administrator = await AdministratorIdAsync(acme);
            DateTimeOffset? before = await LastActiveAtAsync(acme, administrator);

            using HttpResponseMessage read = await TenantProbes.ReadAsync(flow.Browser, Tenant);

            // A soft lock stops the user's changes, never the platform's bookkeeping of them.
            Assert.True(read.StatusCode == HttpStatusCode.OK, await Describe(read, host));
            DateTimeOffset? after = await LastActiveAtAsync(acme, administrator);
            Assert.NotNull(after);
            Assert.True(before is null || after > before, $"The activity stamp stayed at {before:O}.");
        }

        [Fact]
        public async Task An_action_allowed_when_read_only_succeeds_on_a_read_only_tenant()
        {
            AcmeMigrator acme = new(sql, "acme_readonly_action");
            _ = await acme.MigrateAsync();
            await acme.SetStateAsync(Tenant, TenantState.ReadOnly, "unpaid");
            await using AcmeHost host = await AcmeHost.StartAsync(acme.Values);
            using OidcFlowClient flow = host.CreateFlowClient();
            await AcmeSignIn.SignInAsync(flow);

            // Setting one's own locale changes only the caller's presentation, so a soft lock allows it.
            using HttpRequestMessage request = new(HttpMethod.Post, new Uri(string.Create(CultureInfo.InvariantCulture, $"/{Tenant}/api/web/users/me/locale"), UriKind.Relative))
            {
                Content = JsonContent.Create(new { language = "ar" }),
            };
            _ = request.Headers.TryAddWithoutValidation(TellmaAuthentication.CsrfHeaderName, "web/1.0");
            using HttpResponseMessage locale = await flow.Browser.SendAsync(request, Token);

            Assert.True(locale.StatusCode == HttpStatusCode.OK, await Describe(locale, host));
            int administrator = await AdministratorIdAsync(acme);
            Assert.Equal("ar", await SqlServerFixture.ScalarAsync<string>(
                acme.TenantConnectionString(Tenant),
                string.Create(CultureInfo.InvariantCulture, $"SELECT [PreferredLanguage] FROM [core].[Users] WHERE [Id] = {administrator};")));
        }

        [Fact]
        public async Task A_job_whose_handler_runs_when_read_only_succeeds_on_a_read_only_tenant()
        {
            AcmeMigrator acme = new(sql, "acme_readonly_job");
            _ = await acme.MigrateAsync();
            await acme.SetStateAsync(Tenant, TenantState.ReadOnly, "unpaid");
            await using AcmeHost host = await AcmeHost.StartAsync(acme.Values, new AcmeHostOptions
            {
                Compose = static tellma => tellma.AddFeature(new ProbeJobFeature()),
            });

            // --- The job, enqueued in the platform's own scope of the read-only tenant ---
            int job;
            RequestContextSnapshot system = new(Tenant, PrincipalKind.System, "system", WellKnownIds.SystemUserId, "worker", null);
            await using (TenantScope scope = await host.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(system, cancellationToken: Token))
            {
                IReadOnlyList<int> ids = await scope.Services.GetRequiredService<IJobQueue>()
                    .EnqueueAsync([new JobRequest(ProbeJobHandler.Key, null, null, null, null, null)], Token);
                job = Assert.Single(ids);
            }

            // --- The worker runs it: its handler is one a soft lock keeps running ---
            string? status = null;
            var elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < TimeSpan.FromSeconds(30) && status != nameof(JobStatus.Succeeded))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), Token);
                status = await SqlServerFixture.ScalarAsync<string>(
                    acme.TenantConnectionString(Tenant), string.Create(CultureInfo.InvariantCulture, $"SELECT [Status] FROM [core].[Jobs] WHERE [Id] = {job};"));
            }

            Assert.True(status == nameof(JobStatus.Succeeded), $"The job ended {status ?? "missing"}.\n{host.Warnings}");
        }

        [Fact]
        public async Task A_suspension_takes_effect_on_the_next_request_and_within_one_refresh_on_a_second_host()
        {
            AcmeMigrator acme = new(sql, "acme_suspend");
            string keyRing = Path.Combine(Path.GetTempPath(), "tellma-acme-keys-" + Guid.NewGuid().ToString("N"));

            // Two instances of one deployment: one key ring, so a browser's session cookie reads on
            // both; and the client secrets the deployment configures, since each in-process engine
            // registers its host's secrets in the one store and a per-boot secret of the later host
            // would refuse the earlier one's.
            acme.Values["Tellma:DataProtection:KeyRingPath"] = keyRing;
            acme.Values["Tellma:Identity:ClientSecret"] = "test-client-secret";
            acme.Values["Tellma:Identity:ServiceClientSecret"] = "test-service-client-secret";
            _ = await acme.MigrateAsync();

            // The first host never refreshes on its own within the test, so only the write's own
            // refresh can turn it; the second refreshes every second, well under the default.
            Dictionary<string, string?> firstValues = new(acme.Values, StringComparer.OrdinalIgnoreCase)
            {
                ["Tellma:Catalog:RefreshInterval"] = LongRefresh.ToString("c", CultureInfo.InvariantCulture),
                ["Tellma:Catalog:MaxStaleness"] = LongRefresh.ToString("c", CultureInfo.InvariantCulture),
            };
            Dictionary<string, string?> secondValues = new(acme.Values, StringComparer.OrdinalIgnoreCase)
            {
                ["Tellma:Catalog:RefreshInterval"] = ShortRefresh.ToString("c", CultureInfo.InvariantCulture),
            };

            // A read-only tenant refuses this session's mutation for its own reason, so each host's
            // answer shows which state its snapshot holds before and after the suspension.
            await acme.SetStateAsync(Tenant, TenantState.ReadOnly, "unpaid");
            try
            {
                await using AcmeHost first = await AcmeHost.StartAsync(firstValues, new AcmeHostOptions { MapsProbes = true });
                await using AcmeHost second = await AcmeHost.StartAsync(secondValues, new AcmeHostOptions { MapsProbes = true });
                Assert.Equal(LongRefresh, RefreshIntervalOf(first));
                Assert.Equal(ShortRefresh, RefreshIntervalOf(second));
                CookieContainer cookies = new();
                using OidcFlowClient flow = first.CreateFlowClient(cookies);
                await AcmeSignIn.SignInAsync(flow);
                using HttpClient secondBrowser = second.CreateBrowser(cookies);

                // --- The second host's snapshot is refreshed on its configured interval, not the default ---
                TimeSpan cadence = await RefreshCadenceAsync(second);
                Assert.True(cadence < PropagationBound, $"The second host's refreshes lay {cadence} apart.\n{second.Warnings}");

                // --- Before: both hosts hold the tenant read-only ---
                using (HttpResponseMessage write = await TenantProbes.WriteAsync(flow.Browser, Tenant))
                {
                    await AssertRefusedAsync(write, TenantProblemCodes.ReadOnly, first);
                }

                using (HttpResponseMessage write = await TenantProbes.WriteAsync(secondBrowser, Tenant))
                {
                    await AssertRefusedAsync(write, TenantProblemCodes.ReadOnly, second);
                }

                // --- The suspension, received by the first host ---
                var elapsed = Stopwatch.StartNew();
                _ = await first.Services.GetRequiredService<ITenantCatalog>()
                    .SetStateAsync(Tenant, TenantState.Suspended, "unpaid", "control-plane", Token);

                // --- The first host refuses the very next request, by the write's refresh alone ---
                using (HttpResponseMessage write = await TenantProbes.WriteAsync(flow.Browser, Tenant))
                {
                    await AssertRefusedAsync(write, TenantProblemCodes.Suspended, first);
                }

                // --- The second host refuses within a few of its refreshes of the write ---
                string code = TenantProblemCodes.ReadOnly;
                while (code != TenantProblemCodes.Suspended && elapsed.Elapsed < PropagationBound)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), Token);
                    using HttpResponseMessage write = await TenantProbes.WriteAsync(secondBrowser, Tenant);
                    Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
                    code = await TenantProbes.ProblemCodeAsync(write);
                }

                Assert.True(code == TenantProblemCodes.Suspended, $"The second host still answered {code} after {elapsed.Elapsed}.\n{second.Warnings}");
                Assert.Equal(0, first.Probes!.Writes + second.Probes!.Writes);
            }
            finally
            {
                if (Directory.Exists(keyRing))
                {
                    Directory.Delete(keyRing, recursive: true);
                }
            }
        }

        /// <summary>Asserts that a tenant request was refused with a state's code.</summary>
        /// <param name="response">The response.</param>
        /// <param name="code">The state's problem code.</param>
        /// <param name="host">The host that answered, whose warnings a failure reports.</param>
        /// <returns>A task that completes when the assertion ran.</returns>
        private static async Task AssertRefusedAsync(HttpResponseMessage response, string code, AcmeHost host)
        {
            string answered = await TenantProbes.ProblemCodeAsync(response);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden && answered == code, $"{(int)response.StatusCode} {answered}\n{host.Warnings}");
        }

        /// <summary>The refresh interval a host resolved from its settings.</summary>
        /// <param name="host">The host.</param>
        /// <returns>The interval.</returns>
        private static TimeSpan RefreshIntervalOf(AcmeHost host)
        {
            return host.Services.GetRequiredService<IOptions<TellmaCatalogOptions>>().Value.RefreshInterval;
        }

        /// <summary>
        ///     Watches a host's snapshot until it is confirmed twice more, and answers how far apart the
        ///     two confirmations lie: the interval its refresh loop actually runs on.
        /// </summary>
        /// <param name="host">The host, serving no request meanwhile, so only its loop confirms the snapshot.</param>
        /// <returns>The time between the two confirmations.</returns>
        private static async Task<TimeSpan> RefreshCadenceAsync(AcmeHost host)
        {
            ITenantRegistry registry = host.Services.GetRequiredService<ITenantRegistry>();
            DateTimeOffset? seen = registry.SnapshotLoadedAt;
            List<DateTimeOffset> confirmations = [];
            var elapsed = Stopwatch.StartNew();
            while (confirmations.Count < 2 && elapsed.Elapsed < CadenceWindow)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), Token);
                DateTimeOffset? loaded = registry.SnapshotLoadedAt;
                if (loaded is DateTimeOffset confirmed && loaded != seen)
                {
                    confirmations.Add(confirmed);
                    seen = loaded;
                }
            }

            Assert.True(confirmations.Count == 2, $"The host's snapshot was confirmed {confirmations.Count} time(s) in {CadenceWindow}.\n{host.Warnings}");
            return confirmations[1] - confirmations[0];
        }

        /// <summary>A response's status and body, with the host's warnings, for a failed assertion.</summary>
        /// <param name="response">The response.</param>
        /// <param name="host">The host that answered.</param>
        /// <returns>The text.</returns>
        private static async Task<string> Describe(HttpResponseMessage response, AcmeHost host)
        {
            return $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Token)}\n{host.Warnings}";
        }

        /// <summary>The seeded administrator's user id in the tenant.</summary>
        /// <param name="acme">The deployment.</param>
        /// <returns>The id.</returns>
        private static async Task<int> AdministratorIdAsync(AcmeMigrator acme)
        {
            return await SqlServerFixture.ScalarAsync<int?>(
                acme.TenantConnectionString(Tenant), $"SELECT [Id] FROM [core].[Users] WHERE [Email] = N'{AcmeSignIn.Administrator}';")
                ?? throw new InvalidOperationException($"Tenant {Tenant} has no user {AcmeSignIn.Administrator}.");
        }

        /// <summary>When a user was last active in the tenant, as the user's stamp row records it.</summary>
        /// <param name="acme">The deployment.</param>
        /// <param name="userId">The user.</param>
        /// <returns>The stamp, or null when the user has never been active.</returns>
        private static Task<DateTimeOffset?> LastActiveAtAsync(AcmeMigrator acme, int userId)
        {
            return SqlServerFixture.ScalarAsync<DateTimeOffset?>(
                acme.TenantConnectionString(Tenant), string.Create(CultureInfo.InvariantCulture, $"SELECT [LastActiveAt] FROM [core].[UserStamps] WHERE [UserId] = {userId};"));
        }
    }
}
