// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Tests.Infrastructure;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.AspNetCore.Tests.Tenancy
{
    /// <summary>
    ///     The tenant verdicts on the web surface and the hub: every tenant state against a read, a
    ///     write and a write a read-only tenant allows, for an anonymous caller, a member and a stranger.
    /// </summary>
    public sealed class TenantVerdictTests(TenantVerdictTests.MatrixHost matrix) : IClassFixture<TenantVerdictTests.MatrixHost>
    {
        /// <summary>The tenant ids of the matrix, by state; 99 is unknown.</summary>
        private static readonly Dictionary<string, int> Tenants = new(StringComparer.Ordinal)
        {
            ["Active"] = 1,
            ["ReadOnly"] = 2,
            ["Frozen"] = 3,
            ["Suspended"] = 4,
            ["Provisioning"] = 5,
            ["Retired"] = 6,
            ["Unknown"] = 99,
        };

        /// <summary>
        ///     The matrix: state, operation, caller, then the status and problem code expected. The
        ///     operations are <c>read</c> (GET), <c>write</c> (POST) and <c>tolerant</c> (a POST a
        ///     read-only tenant allows).
        /// </summary>
        public static TheoryData<string, string, string, int, string?> Matrix()
        {
            TheoryData<string, string, string, int, string?> data = [];
            string[] operations = ["read", "write", "tolerant"];
            foreach (string state in Tenants.Keys)
            {
                foreach (string operation in operations)
                {
                    // Authorization runs before the tenant is resolved: an anonymous caller learns nothing.
                    data.Add(state, operation, "anonymous", 401, "unauthenticated");
                    foreach (string caller in new[] { "member", "stranger" })
                    {
                        (int status, string? code) = Expected(state, operation, caller);
                        data.Add(state, operation, caller, status, code);
                    }
                }
            }

            return data;
        }

        [Theory]
        [MemberData(nameof(Matrix))]
        public async Task The_web_surface_answers_each_tenant_state_operation_and_caller(
            string state, string operation, string caller, int status, string? code)
        {
            TellmaTestHost host = matrix.Host;
            string? cookie = caller switch
            {
                "member" => host.Cookie(),
                "stranger" => host.Cookie(TellmaTestHost.Stranger),
                _ => null,
            };
            (HttpMethod method, string path) = operation switch
            {
                "read" => (HttpMethod.Get, "probe"),
                "write" => (HttpMethod.Post, "probe"),
                _ => (HttpMethod.Post, "tolerant"),
            };

            using HttpRequestMessage request = TellmaTestHost.SpaRequest(method, $"/{Tenants[state]}/api/web/{path}", cookie, method == HttpMethod.Post ? "{}" : null);
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(status, (int)response.StatusCode);
            if (code is null)
            {
                ProbeResult probe = await ProbeResult.ReadAsync(response);
                Assert.Equal(Tenants[state], probe.TenantId);
                Assert.Equal(TellmaTestHost.UserId, probe.UserId);
            }
            else
            {
                Assert.Equal(code, (await Problem.ReadAsync(response)).Code);
            }
        }

        [Theory]
        [InlineData("Active", "member", 200, null)]
        [InlineData("Active", "stranger", 404, TenantProblemCodes.NotFound)]
        [InlineData("ReadOnly", "member", 200, null)]
        [InlineData("Frozen", "member", 200, null)]
        [InlineData("Suspended", "member", 403, TenantProblemCodes.Suspended)]
        [InlineData("Provisioning", "member", 404, TenantProblemCodes.NotFound)]
        [InlineData("Retired", "member", 404, TenantProblemCodes.NotFound)]
        [InlineData("Unknown", "member", 404, TenantProblemCodes.NotFound)]
        [InlineData("Active", "anonymous", 401, "unauthenticated")]
        [InlineData("Suspended", "stranger", 403, TenantProblemCodes.Suspended)]
        [InlineData("ReadOnly", "stranger", 404, TenantProblemCodes.NotFound)]
        [InlineData("Frozen", "stranger", 404, TenantProblemCodes.NotFound)]
        public async Task The_hub_front_door_answers_as_the_web_surface_does_for_a_read(string state, string caller, int status, string? code)
        {
            TellmaTestHost host = matrix.Host;
            string? cookie = caller switch
            {
                "member" => host.Cookie(),
                "stranger" => host.Cookie(TellmaTestHost.Stranger),
                _ => null,
            };

            // The negotiate is a POST, yet the hub's front door asks for a read: no hub connection writes.
            using HttpRequestMessage request = new(HttpMethod.Post, $"/{Tenants[state]}/hub/negotiate");
            if (cookie is not null)
            {
                request.Headers.Add("Cookie", cookie);
            }

            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(status, (int)response.StatusCode);
            if (code is null)
            {
                Assert.Equal(TellmaTestHost.UserId, (await ProbeResult.ReadAsync(response)).UserId);
            }
            else
            {
                Assert.Equal(code, (await Problem.ReadAsync(response)).Code);
            }
        }

        /// <summary>
        ///     The guarded surfaces besides the web's: the blobs and the public API, for a cookie
        ///     member and stranger, a user token's member and stranger, and a service account.
        /// </summary>
        public static TheoryData<string, string, string, string, int, string?> Surfaces()
        {
            TheoryData<string, string, string, string, int, string?> data = [];
            foreach (string state in Tenants.Keys)
            {
                foreach ((string surface, string operation, string caller) in new[]
                {
                    ("blobs", "read", "member"),
                    ("blobs", "read", "stranger"),
                    ("api", "read", "member"),
                    ("api", "read", "stranger"),
                    ("api", "read", "service"),
                    ("api", "write", "member"),
                    ("api", "write", "stranger"),
                })
                {
                    // A service account is a member as far as the scripted connection goes.
                    (int status, string? code) = Expected(state, operation, caller == "service" ? "member" : caller);
                    data.Add(state, surface, operation, caller, status, code);
                }
            }

            return data;
        }

        [Theory]
        [MemberData(nameof(Surfaces))]
        public async Task The_blobs_and_api_surfaces_answer_each_tenant_state_as_the_web_surface_does(
            string state, string surface, string operation, string caller, int status, string? code)
        {
            TellmaTestHost host = matrix.Host;
            int tenantId = Tenants[state];
            HttpMethod method = operation == "write" ? HttpMethod.Post : HttpMethod.Get;
            using HttpRequestMessage request = surface == "blobs"
                ? TellmaTestHost.SpaRequest(method, $"/{tenantId}/blobs/attachments/7", host.Cookie(caller == "stranger" ? TellmaTestHost.Stranger : "sub-1"))
                : TellmaTestHost.BearerRequest(method, $"/{tenantId}/api/v1/probe", caller switch
                {
                    "service" => TellmaTestHost.ServiceToken(),
                    "stranger" => TellmaTestHost.UserToken(TellmaTestHost.Stranger),
                    _ => TellmaTestHost.UserToken(),
                });

            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(status, (int)response.StatusCode);
            if (code is null)
            {
                // Served: the guard ran, so the connection bound the user.
                ProbeResult probe = await ProbeResult.ReadAsync(response);
                Assert.Equal(tenantId, probe.TenantId);
                Assert.Equal(TellmaTestHost.UserId, probe.UserId);
            }
            else
            {
                Assert.Equal(code, (await Problem.ReadAsync(response)).Code);
            }
        }

        [Fact]
        public async Task A_member_request_binds_the_tenant_principal_session_and_client()
        {
            TellmaTestHost host = matrix.Host;
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/probe", host.Cookie(acr: "urn:tellma:acr:pwd", handle: "handle-1"));

            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            ProbeResult probe = await ProbeResult.ReadAsync(response);
            Assert.Equal(1, probe.TenantId);
            Assert.Equal(nameof(PrincipalKind.User), probe.Kind);
            Assert.Equal("sub-1", probe.Subject);
            Assert.Equal(TellmaTestHost.Slug, probe.ClientId);
            Assert.Equal("sid-1", probe.SessionId);
            Assert.Equal("handle-1", probe.SessionHandle);
            Assert.Equal("web", probe.Client);
            Assert.Equal("urn:tellma:acr:pwd", probe.Acr);
        }

        [Fact]
        public async Task A_stale_snapshot_answers_catalog_unavailable_with_a_retry_after()
        {
            await using TellmaTestHost host = await StartAsync();
            host.Catalog.Failure = new InvalidOperationException("The catalog is down.");
            host.Time.Advance(TimeSpan.FromMinutes(6));

            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/probe", host.Cookie());
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal(TenantProblemCodes.CatalogUnavailable, (await Problem.ReadAsync(response)).Code);
            Assert.NotNull(response.Headers.RetryAfter);
        }

        [Fact]
        public async Task A_tenant_route_with_no_tenant_metadata_resolves_no_tenant()
        {
            // The protected-resource document names a tenant id in its route and is anonymous; it
            // must answer alike for a tenant that exists and one that does not.
            TellmaTestHost host = matrix.Host;

            using HttpResponseMessage known = await host.Client.GetAsync("/.well-known/oauth-protected-resource/1/mcp", TestContext.Current.CancellationToken);
            using HttpResponseMessage unknown = await host.Client.GetAsync("/.well-known/oauth-protected-resource/99/mcp", TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, known.StatusCode);
            Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        }

        /// <summary>Starts a host whose catalog holds the matrix's tenants.</summary>
        /// <returns>The host.</returns>
        internal static Task<TellmaTestHost> StartAsync()
        {
            return TellmaTestHost.StartAsync(static options =>
            {
                options.Rows.Clear();
                foreach ((string state, int id) in Tenants)
                {
                    if (state != "Unknown")
                    {
                        options.Rows.Add(FakeCatalogReader.Row(id, state));
                    }
                }
            });
        }

        /// <summary>The verdict for an authenticated caller.</summary>
        /// <param name="state">The tenant's state.</param>
        /// <param name="operation">The operation.</param>
        /// <param name="caller">The caller.</param>
        /// <returns>The status and problem code; no code for a served request.</returns>
        private static (int Status, string? Code) Expected(string state, string operation, string caller)
        {
            // Absent, retired and provisioning tenants answer alike, member or not.
            if (state is "Unknown" or "Retired" or "Provisioning")
            {
                return (404, TenantProblemCodes.NotFound);
            }

            // The state verdict comes before the membership check.
            (int Status, string? Code)? refused = (state, operation) switch
            {
                ("Suspended", _) => (403, TenantProblemCodes.Suspended),
                ("ReadOnly", "write") => (403, TenantProblemCodes.ReadOnly),
                ("Frozen", "write" or "tolerant") => (403, TenantProblemCodes.Frozen),
                _ => null,
            };
            return refused ?? (caller == "stranger" ? (404, TenantProblemCodes.NotFound) : (200, null));
        }

        /// <summary>One host for the read-only rows of the matrix, which change nothing.</summary>
        public sealed class MatrixHost : IAsyncLifetime
        {
            private TellmaTestHost? _host;

            /// <summary>The host.</summary>
            internal TellmaTestHost Host => _host ?? throw new InvalidOperationException("The host has not started.");

            /// <inheritdoc />
            public async ValueTask InitializeAsync()
            {
                _host = await StartAsync();
            }

            /// <inheritdoc />
            public async ValueTask DisposeAsync()
            {
                if (_host is not null)
                {
                    await _host.DisposeAsync();
                }
            }
        }
    }
}
