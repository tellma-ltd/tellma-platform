// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.AspNetCore.Routing;
using Tellma.Core.AspNetCore.Tests.Infrastructure;
using Tellma.Core.Composition;

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
                host.Services.GetRequiredService<IAuthorizationPolicyProvider>());
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

        [Fact]
        public async Task A_bundle_s_platform_route_is_mapped_anonymous_and_tenantless_and_passes_the_audit()
        {
            // A route under a reserved platform segment, which the audit refuses from a distribution.
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options =>
                options.ConfigureServices = static services => services.AddSingleton<IPlatformEndpointSource>(new ProbePlatformSource()));

            RouteEndpoint endpoint = Assert.Single(
                host.Services.GetRequiredService<MappedEndpoints>().Endpoints().OfType<RouteEndpoint>(),
                static endpoint => endpoint.RoutePattern.RawText == ProbePlatformSource.Route);
            Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
            Assert.Equal(ProbePlatformSource.TenantlessReason, endpoint.Metadata.GetMetadata<TenantlessEndpointMetadata>()?.Reason);
            Assert.NotNull(endpoint.Metadata.GetMetadata<PlatformEndpointMetadata>());

            using HttpResponseMessage response = await host.Client.GetAsync(new Uri(ProbePlatformSource.Route, UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
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
                { "default-policy", "GET /api/whoami", "names no authentication scheme" },
                { "authorize-without-scheme", "GET /api/me", "names no authentication scheme" },
                { "spa-first-segment", "GET /tenants/list", "begins with 'tenants'" },
                { "server-first-segment", "GET /bff/extra", "begins with 'bff'" },
                { "tenantless-group-under-platform-route", "GET /api/admin/extra", "sits under /api/admin" },
                { "tenantless-group-without-metadata", "GET /api/report", "sits on the Tenantless group without TenantlessEndpointMetadata" },
                { "tenant-second-segment", "GET /{tenantId:int:min(1)}/settings", "takes 'settings' as its second segment" },
                { "tenant-metadata-off-tenant-route", "GET /custom", "does not begin with the tenantId parameter" },
                { "unregistered-policy", "GET /unknown-policy", "names an authorization policy that is not registered" },
                { "tenant-endpoint-off-group", "GET /{tenantId:int:min(1)}/reports", "is a Web tenant endpoint outside the platform's tenant groups" },
                { "web-surface-on-hub-group", "GET /{tenantId:int:min(1)}/hub/state", "is stamped the Web surface on the Hub group" },
                { "hub-surface-on-web-group", "POST /{tenantId:int:min(1)}/api/web/stream", "is stamped the Hub surface on the Web group" },
                { "mcp-without-front-door", "GET /{tenantId:int:min(1)}/assistant", "is an MCP endpoint that MCP's front door did not install" },
                { "unstated-mutation", "POST /{tenantId:int:min(1)}/api/web/save", "does not state whether it changes data" },
                { "hub-plain-endpoint", "POST /{tenantId:int:min(1)}/hub/write", "is on the Hub group but is not a SignalR hub's connection endpoint" },
                { "hub-stated-mutation", "POST /{tenantId:int:min(1)}/hub/write", "states whether it changes data on the Hub group" },
                { "hub-stamped-mutation", "POST /{tenantId:int:min(1)}/hub/write", "states whether it changes data on the Hub group" },
                { "tenantless-short-circuit", "GET /api/short", "short-circuits routing without allowing anonymous callers" },
                { "tenant-short-circuit", "GET /{tenantId:int:min(1)}/api/web/short", "short-circuits routing without allowing anonymous callers" },
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
        public async Task An_endpoint_whose_guarded_delegate_was_replaced_is_refused()
        {
            // A later convention swaps the delegate the group's guard wrapped for an unguarded one.
            (WebApplication app, _, _) = TellmaTestHost.Build(static options =>
            {
                options.MapProbes = false;
                options.Map = static (application, endpoints) =>
                {
                    _ = endpoints.Blobs.MapGet("/raw", Nothing).WithMutation(false).AllowMember();
                    ((IEndpointConventionBuilder)endpoints.Blobs).Finally(static endpoint => endpoint.RequestDelegate = Nothing);
                };
            });
            await using (app)
            {
                TellmaCompositionException exception = await Assert.ThrowsAsync<TellmaCompositionException>(
                    () => app.StartAsync(TestContext.Current.CancellationToken));
                CompositionProblem problem = Assert.Single(exception.Problems);
                Assert.StartsWith("GET /{tenantId:int:min(1)}/blobs/raw runs a request delegate the tenant access guard does not wrap", problem.Problem, StringComparison.Ordinal);
            }
        }

        [Fact]
        public async Task A_guard_marker_that_is_not_the_delegate_the_endpoint_runs_is_refused()
        {
            // The walk itself: a Web-group endpoint whose marker names another delegate.
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options => options.MapProbes = false);
            RouteEndpoint endpoint = new(
                Nothing,
                RoutePatternFactory.Parse("/{tenantId:int:min(1)}/api/web/raw"),
                order: 0,
                new EndpointMetadataCollection(
                    new HttpMethodMetadata(["GET"]),
                    new TellmaGroupMetadata(TellmaGroup.Web),
                    new TenantEndpointMetadata(TenantSurface.Web, IsMutation: false, AllowWhenReadOnly: false),
                    new TenantGuardMetadata(Guarded),
                    new AuthorizeAttribute(TellmaPolicies.Web),
                    new MemberEndpointMetadata()),
                "raw");

            IReadOnlyList<CompositionProblem> problems = await TellmaEndpointAudit.AuditAsync(
                [endpoint], host.Services.GetRequiredService<IAuthorizationPolicyProvider>());
            CompositionProblem problem = Assert.Single(problems);
            Assert.Contains("runs a request delegate the tenant access guard does not wrap", problem.Problem, StringComparison.Ordinal);

            // A delegate the endpoint does not run, which the marker names.
            static Task Guarded(HttpContext _)
            {
                return Task.CompletedTask;
            }
        }

        [Fact]
        public async Task A_hub_on_the_hub_group_passes_as_a_read()
        {
            // SignalR maps a negotiate endpoint and a connection endpoint, the latter with no methods.
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options =>
            {
                options.ConfigureServices = static services => services.AddSignalR();
                options.Map = static (_, endpoints) => endpoints.Hub.MapHub<ProbeHub>("/realtime").AllowMember();
            });

            RouteEndpoint[] hub = [.. host.Services.GetRequiredService<MappedEndpoints>().Endpoints()
                .OfType<RouteEndpoint>()
                .Where(static endpoint => endpoint.RoutePattern.RawText!.StartsWith("/{tenantId:int:min(1)}/hub/realtime", StringComparison.Ordinal))];
            Assert.Equal(2, hub.Length);
            Assert.All(hub, static endpoint => Assert.Equal(
                new TenantEndpointMetadata(TenantSurface.Hub, IsMutation: false, AllowWhenReadOnly: false),
                endpoint.Metadata.GetRequiredMetadata<TenantEndpointMetadata>()));
        }

        [Fact]
        public async Task An_anonymous_tenantless_endpoint_may_short_circuit()
        {
            // Startup runs the audit: the host starting is the assertion.
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options =>
                options.Map = static (_, endpoints) => endpoints.Tenantless.MapGet("/ping", Nothing)
                    .AllowAnonymous()
                    .AsTenantlessEndpoint("test")
                    .ShortCircuit());

            Assert.Contains(
                host.Services.GetRequiredService<MappedEndpoints>().Endpoints().OfType<RouteEndpoint>(),
                static endpoint => endpoint.RoutePattern.RawText == "/api/ping");
        }

        [Fact]
        public async Task WithMutation_clears_a_read_only_allowance()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options =>
                options.Map = static (_, endpoints) => endpoints.Web.MapPost("/amend", Nothing)
                    .WithMetadata(new TenantEndpointMetadata(TenantSurface.Web, IsMutation: false, AllowWhenReadOnly: true))
                    .WithMutation(true)
                    .AllowMember());

            TenantEndpointMetadata metadata = Assert.Single(
                host.Services.GetRequiredService<MappedEndpoints>().Endpoints().OfType<RouteEndpoint>(),
                static endpoint => endpoint.RoutePattern.RawText == "/{tenantId:int:min(1)}/api/web/amend").Metadata.GetRequiredMetadata<TenantEndpointMetadata>();
            Assert.Equal(new TenantEndpointMetadata(TenantSurface.Web, IsMutation: true, AllowWhenReadOnly: false), metadata);
        }

        [Theory]
        [InlineData("settings")]
        [InlineData("blobs")]
        [InlineData("Hub")]
        public void A_stack_whose_resource_segment_is_a_reserved_word_fails_startup(string segment)
        {
            TellmaCompositionException exception = Assert.Throws<TellmaCompositionException>(
                () => TellmaTestHost.Build(options => options.ConfigureServices = services =>
                    services.Replace(ServiceDescriptor.Singleton<IStackRouteSource>(new StackRoutes(new StackRoute("sales-invoices", StackOperations.All), new StackRoute(segment, StackOperations.All))))));
            CompositionProblem problem = Assert.Single(exception.Problems);
            Assert.StartsWith($"The stack at /{{tenantId}}/{segment} takes '{segment}'", problem.Problem, StringComparison.Ordinal);
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
                "tenant-endpoint-with-another-policy" => endpoints.Web.MapGet("/mixed", Nothing).RequireAuthorization(TellmaPolicies.Api).WithMutation(false).AllowMember(),
                "tenant-endpoint-anonymous" => endpoints.Web.MapGet("/open", Nothing).AllowAnonymous().AsTenantlessEndpoint("x").WithMutation(false).AllowMember(),
                "tenant-endpoint-without-grant" => endpoints.Web.MapGet("/ungranted", Nothing).WithMutation(false),
                "tenant-endpoint-with-two-grants" => endpoints.Web.MapGet("/twice", Nothing).WithMutation(false).AllowMember().RequireSecurable("acme.Thing", "Read"),
                "cookie-get-mutation" => endpoints.Web.MapGet("/delete", Nothing).WithMutation(true).AllowMember(),
                "cookie-cors" => endpoints.Web.MapPost("/shared", Nothing).RequireCors("anywhere").WithMutation(true).AllowMember(),
                "anonymous-without-tenantless" => app.MapGet("/public", Nothing).AllowAnonymous(),
                "no-authorization" => app.MapGet("/plain", Nothing),
                "default-policy" => endpoints.Tenantless.MapGet("/whoami", Nothing).RequireAuthorization().AsTenantlessEndpoint("x"),
                "authorize-without-scheme" => endpoints.Tenantless.MapGet("/me", Nothing).RequireAuthorization(static policy => policy.RequireAuthenticatedUser()).AsTenantlessEndpoint("x"),
                "spa-first-segment" => app.MapGet("/tenants/list", Nothing).AllowAnonymous().AsTenantlessEndpoint("x"),
                "server-first-segment" => app.MapGet("/bff/extra", Nothing).RequireAuthorization(TellmaPolicies.Web),
                "tenantless-group-under-platform-route" => endpoints.Tenantless.MapGet("/admin/extra", Nothing).AsTenantlessEndpoint("x"),
                "tenantless-group-without-metadata" => endpoints.Tenantless.MapGet("/report", Nothing).RequireAuthorization(TellmaPolicies.Web),
                "tenant-second-segment" => app.MapGet("/{tenantId:int:min(1)}/settings", Nothing).RequireAuthorization(TellmaPolicies.Web).WithMetadata(web).AllowMember(),
                "tenant-metadata-off-tenant-route" => app.MapGet("/custom", Nothing).RequireAuthorization(TellmaPolicies.Web).WithMetadata(web),
                "unregistered-policy" => app.MapGet("/unknown-policy", Nothing).RequireAuthorization("Nope"),
                "tenant-endpoint-off-group" => app.MapGet("/{tenantId:int:min(1)}/reports", Nothing).RequireAuthorization(TellmaPolicies.Web).WithMetadata(web).AllowMember(),
                "web-surface-on-hub-group" => endpoints.Hub.MapGet("/state", Nothing).WithMetadata(web).AllowMember(),
                "hub-surface-on-web-group" => endpoints.Web.MapPost("/stream", Nothing)
                    .WithMetadata(new TenantEndpointMetadata(TenantSurface.Hub, IsMutation: false, AllowWhenReadOnly: false))
                    .AllowMember(),
                "mcp-without-front-door" => app.MapGet("/{tenantId:int:min(1)}/assistant", Nothing)
                    .RequireAuthorization(TellmaPolicies.Mcp)
                    .WithMetadata(new TenantEndpointMetadata(TenantSurface.Mcp, IsMutation: false, AllowWhenReadOnly: false))
                    .AllowMember(),
                "unstated-mutation" => endpoints.Web.MapPost("/save", Nothing).AllowMember(),
                "hub-plain-endpoint" => endpoints.Hub.MapPost("/write", Nothing).AllowMember(),
                "hub-stated-mutation" => endpoints.Hub.MapPost("/write", Nothing)
                    .WithMetadata(new HubMetadata(typeof(ProbeHub)))
                    .WithMutation(true)
                    .AllowMember(),
                "hub-stamped-mutation" => endpoints.Hub.MapPost("/write", Nothing)
                    .WithMetadata(new HubMetadata(typeof(ProbeHub)))
                    .WithMetadata(new TenantEndpointMetadata(TenantSurface.Hub, IsMutation: true, AllowWhenReadOnly: false))
                    .AllowMember(),
                "tenantless-short-circuit" => endpoints.Tenantless.MapGet("/short", Nothing).AsTenantlessEndpoint("test").ShortCircuit(),
                "tenant-short-circuit" => endpoints.Web.MapGet("/short", Nothing).WithMutation(false).AllowMember().ShortCircuit(),
                _ => throw new ArgumentOutOfRangeException(nameof(violation), violation, "Not a violation of the suite."),
            };
        }

        /// <summary>A bundle's platform route: under a reserved segment, answering no content.</summary>
        private sealed class ProbePlatformSource : IPlatformEndpointSource
        {
            /// <summary>The route.</summary>
            public const string Route = "/api/webhooks/probe";

            /// <summary>The tenantless reason.</summary>
            public const string TenantlessReason = "probe-bundle";

            /// <inheritdoc />
            public string Reason => TenantlessReason;

            /// <inheritdoc />
            public IReadOnlyList<IEndpointConventionBuilder> Map(IEndpointRouteBuilder endpoints)
            {
                return [endpoints.MapGet(Route, static () => Results.NoContent())];
            }
        }

        /// <summary>A stack registry reduced to its routes.</summary>
        /// <param name="routes">The stacks' routes.</param>
        private sealed class StackRoutes(params StackRoute[] routes) : IStackRouteSource
        {
            /// <inheritdoc />
            public IReadOnlyCollection<StackRoute> Routes => routes;

            /// <inheritdoc />
            public StackRoute? Find(string resource)
            {
                return null;
            }
        }
    }
}
