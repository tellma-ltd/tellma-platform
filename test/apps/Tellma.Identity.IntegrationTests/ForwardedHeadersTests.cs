// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Identity.Data;
using Tellma.Identity.IntegrationTests.Infrastructure;

namespace Tellma.Identity.IntegrationTests
{
    /// <summary>
    ///     The standalone host's forwarded-headers wiring. The gate fails closed — enabling the
    ///     middleware without naming a known proxy or network would let any direct caller spoof
    ///     <c>X-Forwarded-For</c>, so that combination must refuse to boot — and, once configured,
    ///     the header is honored from the named proxy and ignored from anyone else.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class ForwardedHeadersTests(SqlServerFixture fixture)
    {
        [Fact]
        public void Enabling_forwarded_headers_without_a_known_proxy_fails_startup()
        {
            // The gate throws before the host builds, so no database is needed.
            using StandaloneFactory factory = new();
            factory.ConfigurationOverrides["ForwardedHeaders:Enabled"] = "true";

            Exception? exception = Record.Exception(factory.CreateClient);

            Assert.NotNull(exception);
            Assert.Contains("ForwardedHeaders:KnownProxies", MessageChain(exception), StringComparison.Ordinal);
        }

        [Fact]
        public void The_framework_forwarded_headers_switch_is_refused()
        {
            // ASP.NET Core's own ForwardedHeaders_Enabled key (ASPNETCORE_FORWARDEDHEADERS_ENABLED)
            // registers the middleware with both known lists empty — trusting every caller — and
            // never passes through the gate above, so the host must refuse to start on it.
            using StandaloneFactory factory = new();
            factory.ConfigurationOverrides["ForwardedHeaders_Enabled"] = "true";

            Exception? exception = Record.Exception(factory.CreateClient);

            Assert.NotNull(exception);
            Assert.Contains("ASPNETCORE_FORWARDEDHEADERS_ENABLED", MessageChain(exception), StringComparison.Ordinal);
        }

        /// <summary>
        ///     The messages of an exception and its inner chain — the host wraps startup failures,
        ///     and asserting on messages alone keeps the assertion from passing on a coincidental
        ///     match in a stack frame.
        /// </summary>
        private static string MessageChain(Exception exception)
        {
            List<string> messages = [];
            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                messages.Add(current.Message);
            }

            return string.Join(" | ", messages);
        }

        [Fact]
        public async Task Enabling_forwarded_headers_with_a_known_proxy_boots()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(
                fixture,
                "idfwd",
                new Dictionary<string, string?>
                {
                    ["ForwardedHeaders:Enabled"] = "true",
                    ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.4",
                    ["ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/8",
                });

            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.GetAsync(
                new Uri("/.well-known/openid-configuration", UriKind.Relative),
                TestContext.Current.CancellationToken);

            response.EnsureSuccessStatusCode();
        }

        [Fact]
        public async Task A_known_proxys_forwarded_client_address_is_the_one_the_engine_uses()
        {
            // Every test request arrives from the harness's address, so naming that as the known
            // proxy is what makes this request "one that came through the deployment's proxy".
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(
                fixture,
                "idfwdon",
                new Dictionary<string, string?>
                {
                    ["ForwardedHeaders:Enabled"] = "true",
                    ["ForwardedHeaders:KnownProxies:0"] = StandaloneFactory.RemoteIpAddress,
                });

            // The header wins: per-IP limits and audit forensics see the real client, not the
            // gateway every request would otherwise appear to come from.
            Assert.Equal(ForwardedClientAddress, await AuditedClientAddressAsync(factory));
        }

        [Fact]
        public async Task A_forwarded_client_address_from_an_unknown_caller_is_ignored()
        {
            // The same request, but the deployment's proxy is somewhere else — so this caller is a
            // direct one, and trusting its header would let anyone forge the client address.
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(
                fixture,
                "idfwdspoof",
                new Dictionary<string, string?>
                {
                    ["ForwardedHeaders:Enabled"] = "true",
                    ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.4",
                });

            Assert.Equal(StandaloneFactory.RemoteIpAddress, await AuditedClientAddressAsync(factory));
        }

        /// <summary>The address a caller claims for itself in <c>X-Forwarded-For</c>.</summary>
        private const string ForwardedClientAddress = "198.51.100.9";

        /// <summary>
        ///     Makes one rejected token request carrying <c>X-Forwarded-For</c> and returns the
        ///     client address the engine stamped on the resulting audit row. The audit row is the
        ///     observable end of this wiring: it is written from inside the pipeline, after the
        ///     middleware has had its say, and it is one of the two things (with per-IP rate
        ///     limits) that the whole configuration section exists to feed.
        /// </summary>
        /// <param name="factory">The configured host.</param>
        /// <returns>The audited client address.</returns>
        private static async Task<string> AuditedClientAddressAsync(StandaloneFactory factory)
        {
            using HttpClient client = factory.CreateClient();
            using HttpRequestMessage request = new(HttpMethod.Post, new Uri("/connect/token", UriKind.Relative))
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = "no-such-client",
                    ["client_secret"] = "wrong",
                }),
            };
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", ForwardedClientAddress);

            using (HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken))
            {
                Assert.False(response.IsSuccessStatusCode);
            }

            using IServiceScope scope = factory.Services.CreateScope();
            TellmaIdentityDbContext db = scope.ServiceProvider.GetRequiredService<TellmaIdentityDbContext>();
            Data.Entities.AuditEvent rejected = Assert.Single(
                await db.Set<Data.Entities.AuditEvent>()
                    .Where(static e => e.Action == "TokenRequestRejected")
                    .ToListAsync(TestContext.Current.CancellationToken));

            return rejected.IpAddress!;
        }
    }
}
