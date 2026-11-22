// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Tellma.Distro.Acme.IntegrationTests.Infrastructure;
using Tellma.Distro.Acme.TestSupport;
using Tellma.Identity.TestSupport;

namespace Tellma.Distro.Acme.IntegrationTests
{
    /// <summary>
    ///     Acme's web host over a migrated deployment with its limits lowered through configuration:
    ///     a signed-in user past the per-user rate is refused 429 <c>rate-limited</c> with
    ///     <c>Retry-After</c>, and a read past the request timeout answers 504 <c>timeout</c> with its
    ///     batch cancelled on the server rather than run to its end. In Development the host also
    ///     serves the web surface's OpenAPI document over Acme's own composition, from one cached copy.
    /// </summary>
    [Trait("Category", "Integration")]
    public sealed class LimitsTests(SqlServerFixture sql)
    {
        /// <summary>Why a read the tenant serves to the seeded administrator cannot be asserted yet.</summary>
        private const string NeedsUserConnectionAndBootstrap =
            "Needs the user connection initializer and the administrator bootstrap provisioning step, so a tenant request "
            + "reaches its endpoint for the seeded administrator.";

        /// <summary>The tenant every test reads: Acme's live seed tenant.</summary>
        private const int Tenant = 1;

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_user_past_the_rate_is_refused_with_retry_after()
        {
            AcmeMigrator acme = new(sql, "acme_rate");
            _ = await acme.MigrateAsync();
            Dictionary<string, string?> values = new(acme.Values) { ["Tellma:Api:RequestsPerMinutePerUser"] = "1" };
            await using AcmeHost host = await AcmeHost.StartAsync(values, new AcmeHostOptions { MapsProbes = true });
            using OidcFlowClient flow = host.CreateFlowClient();
            await AcmeSignIn.SignInAsync(flow);

            // --- The first read spends the minute's one request, whatever it answers; the second is refused ---
            using HttpResponseMessage first = await TenantProbes.ReadAsync(flow.Browser, Tenant);
            using HttpResponseMessage second = await TenantProbes.ReadAsync(flow.Browser, Tenant);

            Assert.NotEqual(HttpStatusCode.TooManyRequests, first.StatusCode);
            Assert.True(second.StatusCode == HttpStatusCode.TooManyRequests, host.Warnings.ToString());
            Assert.Equal("rate-limited", await TenantProbes.ProblemCodeAsync(second));
            TimeSpan retryAfter = Assert.IsType<TimeSpan>(second.Headers.RetryAfter?.Delta);
            Assert.InRange(retryAfter, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1));
        }

        [Fact]
        public async Task The_development_host_serves_the_web_surfaces_openapi_document_from_one_copy()
        {
            AcmeMigrator acme = new(sql, "acme_openapi");
            _ = await acme.MigrateAsync();
            await using AcmeHost host = await AcmeHost.StartAsync(acme.Values);
            using HttpClient client = host.Server.CreateClient();

            using HttpResponseMessage first = await client.GetAsync(new Uri("/openapi/web.json", UriKind.Relative), Token);
            using HttpResponseMessage second = await client.GetAsync(new Uri("/openapi/web.json", UriKind.Relative), Token);

            // --- A 3.1 document of the web surface's paths alone, the second answer the cached copy ---
            string body = await first.Content.ReadAsStringAsync(Token);
            Assert.True(first.StatusCode == HttpStatusCode.OK, body);
            using var document = JsonDocument.Parse(body);
            Assert.StartsWith("3.1", document.RootElement.GetProperty("openapi").GetString(), StringComparison.Ordinal);
            Assert.All(
                document.RootElement.GetProperty("paths").EnumerateObject(),
                static path => Assert.StartsWith("/{tenantId}/api/web/", path.Name, StringComparison.Ordinal));
            Assert.NotNull(second.Headers.Age);
        }

        [Fact(Skip = NeedsUserConnectionAndBootstrap, SkipUnless = nameof(PlatformSeams.ServesTheAdministrator), SkipType = typeof(PlatformSeams))]
        public async Task A_read_past_the_timeout_answers_504_and_is_cancelled_on_the_server()
        {
            AcmeMigrator acme = new(sql, "acme_timeout");
            _ = await acme.MigrateAsync();
            Dictionary<string, string?> values = new(acme.Values) { ["Tellma:Api:RequestTimeout"] = "00:00:02" };
            await using AcmeHost host = await AcmeHost.StartAsync(values, new AcmeHostOptions { MapsProbes = true });
            using OidcFlowClient flow = host.CreateFlowClient();
            await AcmeSignIn.SignInAsync(flow);

            // --- A batch that would wait a minute on the server ---
            var elapsed = Stopwatch.StartNew();
            using HttpResponseMessage response = await TenantProbes.SlowReadAsync(flow.Browser, Tenant);
            elapsed.Stop();

            // --- Answered at the timeout, the server's wait cancelled long before its minute ---
            Assert.True(response.StatusCode == HttpStatusCode.GatewayTimeout, host.Warnings.ToString());
            Assert.Equal("timeout", await TenantProbes.ProblemCodeAsync(response));
            Assert.InRange(elapsed.Elapsed, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30));
        }
    }
}
