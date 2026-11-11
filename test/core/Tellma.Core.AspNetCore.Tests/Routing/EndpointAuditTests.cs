// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.AspNetCore.Routing;
using Tellma.Core.AspNetCore.Tests.Infrastructure;

namespace Tellma.Core.AspNetCore.Tests.Routing
{
    /// <summary>
    ///     The endpoint audit: the platform's own endpoints pass it, and each violation a distribution
    ///     could map fails startup, naming the route and the method.
    /// </summary>
    public sealed class EndpointAuditTests
    {
        /// <summary>A handler that answers nothing.</summary>
        private static readonly RequestDelegate Nothing = static _ => Task.CompletedTask;

        [Fact]
        public async Task The_platform_endpoints_and_the_probes_pass_the_audit()
        {
            // Startup runs the audit over every endpoint: the host starting is the assertion.
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();

            IReadOnlyList<CompositionProblem> problems = await TellmaEndpointAudit.AuditAsync(
                host.Services.GetRequiredService<MappedEndpoints>().Endpoints(),
                host.Services.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider>());
            Assert.Empty(problems);
        }

        [Fact]
        public async Task MapTellma_maps_every_platform_route_and_group()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            string[] routes = [.. host.Services.GetRequiredService<MappedEndpoints>().Endpoints()
                .OfType<RouteEndpoint>()
                .Select(static endpoint => "/" + endpoint.RoutePattern.RawText!.TrimStart('/'))];

            Assert.Contains("/api/distribution-info", routes);
            Assert.Contains("/health/live", routes);
            Assert.Contains("/health/ready", routes);
            Assert.Contains("/.well-known/oauth-protected-resource/{tenantId:int:min(1)}/mcp", routes);
            Assert.Contains("/api/admin/v1/info", routes);
            Assert.Contains("/{*path}", routes);
            Assert.Contains("/{tenantId:int:min(1)}/api/web/probe", routes);
            Assert.Contains("/{tenantId:int:min(1)}/hub/negotiate", routes);
            Assert.Contains("/{tenantId:int:min(1)}/blobs/{kind}/{id:int}", routes);
            Assert.Contains("/{tenantId:int:min(1)}/api/v{version:apiVersion}/probe", routes);
        }

        /// <summary>Each violation: the endpoint to map, the route and method the problem names, and what it says.</summary>
        public static TheoryData<string, string, string> Violations()
        {
            return new TheoryData<string, string, string>
            {
                { "tenant-route-without-metadata", "GET /{tenantId:int}/custom", "lacks TenantEndpointMetadata" },
                { "tenant-endpoint-with-another-policy", "GET /{tenantId:int:min(1)}/api/web/mixed", "authenticates another scheme" },
                { "tenant-endpoint-anonymous", "GET /{tenantId:int:min(1)}/api/web/open", "is a tenant endpoint that allows anonymous callers" },
                { "tenant-endpoint-without-grant", "GET /{tenantId:int:min(1)}/api/web/ungranted", "carries neither SecurableEndpointMetadata nor MemberEndpointMetadata" },
                { "tenant-endpoint-with-two-grants", "GET /{tenantId:int:min(1)}/api/web/twice", "carries 2 of SecurableEndpointMetadata and MemberEndpointMetadata" },
                { "cookie-get-mutation", "GET /{tenantId:int:min(1)}/api/web/delete", "is a cookie-authenticated GET stamped as a mutation" },
                { "cookie-cors", "POST /{tenantId:int:min(1)}/api/web/shared", "attaches a CORS policy" },
                { "anonymous-without-tenantless", "GET /public", "allows anonymous callers without TenantlessEndpointMetadata" },
                { "no-authorization", "GET /plain", "has neither an authorization policy nor TenantlessEndpointMetadata" },
                { "spa-first-segment", "GET /tenants/list", "begins with 'tenants'" },
                { "server-first-segment", "GET /bff/extra", "begins with 'bff'" },
                { "tenantless-group-under-platform-route", "GET /api/admin/extra", "sits under /api/admin" },
                { "tenantless-group-without-metadata", "GET /api/report", "sits on the Tenantless group without TenantlessEndpointMetadata" },
                { "tenant-second-segment", "GET /{tenantId:int:min(1)}/settings", "takes 'settings' as its second segment" },
                { "tenant-metadata-off-tenant-route", "GET /custom", "does not begin with the tenantId parameter" },
                { "unregistered-policy", "GET /unknown-policy", "names an authorization policy that is not registered" },
            };
        }

        [Theory]
        [MemberData(nameof(Violations))]
        public async Task Each_violation_fails_startup_naming_the_route_and_method(string violation, string where, string says)
        {
            (WebApplication app, _, _) = TellmaTestHost.Build(options => options.Map = (app, endpoints) => MapViolation(app, endpoints, violation));
            await using (app)
            {
                TellmaCompositionException exception = await Assert.ThrowsAsync<TellmaCompositionException>(
                    () => app.StartAsync(TestContext.Current.CancellationToken));

                CompositionProblem problem = Assert.Single(exception.Problems, problem => problem.Problem.Contains(says, StringComparison.Ordinal));
                Assert.Equal(TellmaEndpointAudit.CheckName, problem.Source);
                Assert.StartsWith(where + " ", problem.Problem, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void MapTellma_audits_what_is_mapped_when_it_runs()
        {
            // An endpoint mapped ahead of MapTellma fails it there, before the host is built.
            TellmaCompositionException exception = Assert.Throws<TellmaCompositionException>(
                () => TellmaTestHost.Build(static options => options.BeforeMapTellma = static app => app.MapGet("/plain", Nothing)));

            CompositionProblem problem = Assert.Single(exception.Problems);
            Assert.StartsWith("GET /plain ", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void MapTellma_refuses_a_pipeline_UseTellma_did_not_install()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => TellmaTestHost.Build(static options => options.UseTellma = false));
            Assert.Contains("UseTellma", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task MapTellma_maps_once()
        {
            (WebApplication app, _, _) = TellmaTestHost.Build(static options => options.MapProbes = false);
            await using (app)
            {
                _ = Assert.Throws<InvalidOperationException>(app.MapTellma);
            }
        }

        /// <summary>Maps one violation.</summary>
        /// <param name="app">The application.</param>
        /// <param name="endpoints">The platform's groups.</param>
        /// <param name="violation">The violation.</param>
        private static void MapViolation(WebApplication app, TellmaEndpoints endpoints, string violation)
        {
            TenantEndpointMetadata web = new(TenantSurface.Web, IsMutation: false, AllowWhenReadOnly: false);
            _ = violation switch
            {
                "tenant-route-without-metadata" => app.MapGet("/{tenantId:int}/custom", Nothing).RequireAuthorization(TellmaPolicies.Web).AllowMember(),
                "tenant-endpoint-with-another-policy" => endpoints.Web.MapGet("/mixed", Nothing).RequireAuthorization(TellmaPolicies.Api).AllowMember(),
                "tenant-endpoint-anonymous" => endpoints.Web.MapGet("/open", Nothing).AllowAnonymous().AsTenantlessEndpoint("x").AllowMember(),
                "tenant-endpoint-without-grant" => endpoints.Web.MapGet("/ungranted", Nothing),
                "tenant-endpoint-with-two-grants" => endpoints.Web.MapGet("/twice", Nothing).AllowMember().RequireSecurable("acme.Thing", "Read"),
                "cookie-get-mutation" => endpoints.Web.MapGet("/delete", Nothing).WithMutation(true).AllowMember(),
                "cookie-cors" => endpoints.Web.MapPost("/shared", Nothing).RequireCors("anywhere").AllowMember(),
                "anonymous-without-tenantless" => app.MapGet("/public", Nothing).AllowAnonymous(),
                "no-authorization" => app.MapGet("/plain", Nothing),
                "spa-first-segment" => app.MapGet("/tenants/list", Nothing).AllowAnonymous().AsTenantlessEndpoint("x"),
                "server-first-segment" => app.MapGet("/bff/extra", Nothing).RequireAuthorization(TellmaPolicies.Web),
                "tenantless-group-under-platform-route" => endpoints.Tenantless.MapGet("/admin/extra", Nothing).AsTenantlessEndpoint("x"),
                "tenantless-group-without-metadata" => endpoints.Tenantless.MapGet("/report", Nothing).RequireAuthorization(TellmaPolicies.Web),
                "tenant-second-segment" => app.MapGet("/{tenantId:int:min(1)}/settings", Nothing).RequireAuthorization(TellmaPolicies.Web).WithMetadata(web).AllowMember(),
                "tenant-metadata-off-tenant-route" => app.MapGet("/custom", Nothing).RequireAuthorization(TellmaPolicies.Web).WithMetadata(web),
                "unregistered-policy" => app.MapGet("/unknown-policy", Nothing).RequireAuthorization("Nope"),
                _ => throw new ArgumentOutOfRangeException(nameof(violation), violation, "Not a violation of the suite."),
            };
        }
    }
}
