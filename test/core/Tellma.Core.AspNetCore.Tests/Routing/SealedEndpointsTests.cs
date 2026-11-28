// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using System.Net;
using Tellma.Core.AspNetCore.Routing;
using Tellma.Core.AspNetCore.Tests.Infrastructure;
using Tellma.Testing.WebHost;

namespace Tellma.Core.AspNetCore.Tests.Routing
{
    /// <summary>
    ///     The endpoints the startup audit reads are sealed: every later reader (the authorization
    ///     policy cache, the router, the route-miss index) is answered with the endpoints the audit
    ///     built, so each endpoint is built once as the host starts, and an endpoint mapped after the
    ///     audit is never served.
    /// </summary>
    public sealed class SealedEndpointsTests
    {
        /// <summary>The counted endpoint's route, below the web group.</summary>
        private const string CountedRoute = "/counted";

        [Fact]
        public async Task Each_endpoint_is_built_once_for_the_audit_the_router_and_the_route_misses()
        {
            // --- An endpoint whose builds a convention counts; MapTellma's own audit runs before it is mapped ---
            int builds = 0;
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options => options.Map = (application, endpoints) =>
            {
                _ = endpoints.Web.MapGet(CountedRoute, static () => "counted").WithMutation(false).AllowMember();
                ((IEndpointConventionBuilder)endpoints.Web).Finally(builder =>
                {
                    if (builder is RouteEndpointBuilder { RoutePattern.RawText: string pattern } && pattern.EndsWith(CountedRoute, StringComparison.Ordinal))
                    {
                        _ = Interlocked.Increment(ref builds);
                    }
                });
            });
            string cookie = host.Cookie();

            // --- The router serves it, a miss is classified against the table, and the table is read again ---
            using HttpResponseMessage first = await GetAsync(host, CountedRoute, cookie);
            using HttpResponseMessage second = await GetAsync(host, CountedRoute, cookie);
            using HttpResponseMessage miss = await GetAsync(host, "/nothing-here", cookie);
            int mapped = host.Services.GetRequiredService<MappedEndpoints>().Endpoints().Count(static endpoint => endpoint is RouteEndpoint { RoutePattern.RawText: string pattern } && pattern.EndsWith(CountedRoute, StringComparison.Ordinal));

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.Equal("route-not-found", (await Problem.ReadAsync(miss)).Code);
            Assert.Equal(1, mapped);
            Assert.Equal(1, Volatile.Read(ref builds));
        }

        [Fact]
        public async Task An_endpoint_mapped_after_the_host_started_is_neither_audited_nor_served()
        {
            RouteGroupBuilder? web = null;
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options => options.Map = (_, endpoints) => web = endpoints.Web);

            // --- Mapped once the audit ran, before the router's first request ---
            _ = web!.MapGet("/late", static () => "late").WithMutation(false).AllowMember();
            using HttpResponseMessage response = await GetAsync(host, "/late", host.Cookie());

            Assert.Equal("route-not-found", (await Problem.ReadAsync(response)).Code);
            Assert.DoesNotContain(
                host.Services.GetRequiredService<MappedEndpoints>().Endpoints(),
                static endpoint => endpoint is RouteEndpoint { RoutePattern.RawText: string pattern } && pattern.EndsWith("/late", StringComparison.Ordinal));
        }

        [Fact]
        public async Task A_top_level_endpoint_mapped_after_the_host_started_is_neither_served_nor_offered_to_another_verb()
        {
            WebApplication? application = null;
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options => options.Map = (app, _) => application = app);

            // --- Mapped once the audit ran, in a source of its own, before the first route miss ---
            IEndpointRouteBuilder routes = application!;
            int before = routes.DataSources.Count;
            _ = application!.MapPost("/late-top", static () => "late");
            using HttpResponseMessage post = await host.Client.PostAsync(new Uri("/late-top", UriKind.Relative), null, TestContext.Current.CancellationToken);
            using HttpResponseMessage get = await host.Client.GetAsync(new Uri("/late-top", UriKind.Relative), TestContext.Current.CancellationToken);

            // --- The late source is on the application, yet neither verb finds a route: a tenantless miss ---
            Assert.Equal(before + 1, routes.DataSources.Count);
            Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
            Assert.Equal("not-found", (await Problem.ReadAsync(post)).Code);
            Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
            Assert.Equal("not-found", (await Problem.ReadAsync(get)).Code);
            Assert.Empty(get.Content.Headers.Allow);
            Assert.DoesNotContain(
                host.Services.GetRequiredService<MappedEndpoints>().Endpoints(),
                static endpoint => endpoint is RouteEndpoint { RoutePattern.RawText: "/late-top" });
        }

        [Fact]
        public void A_sealed_source_keeps_its_endpoints_until_its_inner_source_signals_a_change()
        {
            using ChangingSource inner = new();
            SealedEndpointDataSource sealedSource = new(inner);

            // --- Built on the first read, kept for the next ---
            IReadOnlyList<Endpoint> first = sealedSource.Endpoints;
            Assert.Same(first, sealedSource.Endpoints);
            Assert.Equal(1, inner.Reads);

            // --- A change is read again, once ---
            inner.Change();
            IReadOnlyList<Endpoint> changed = sealedSource.Endpoints;
            Assert.NotSame(first, changed);
            Assert.Same(changed, sealedSource.Endpoints);
            Assert.Equal(2, inner.Reads);
        }

        [Fact]
        public void A_sealed_source_answers_its_inner_source_s_change_token_and_grouped_endpoints()
        {
            using ChangingSource inner = new();
            SealedEndpointDataSource sealedSource = new(inner);

            Assert.Same(inner.GetChangeToken(), sealedSource.GetChangeToken());
            RouteGroupContext group = new() { Prefix = RoutePatternFactory.Parse("/group"), ApplicationServices = new ServiceCollection().BuildServiceProvider() };
            _ = sealedSource.GetGroupedEndpoints(group);
            _ = sealedSource.GetGroupedEndpoints(group);
            Assert.Equal(2, inner.GroupedReads);
        }

        [Fact]
        public void Sealing_wraps_every_source_in_place_and_in_order_once()
        {
            using ChangingSource first = new();
            using ChangingSource second = new();
            List<EndpointDataSource> sources = [first, second];
            MappedEndpoints mapped = new();
            mapped.Capture(sources);

            mapped.Seal();
            EndpointDataSource[] once = [.. sources];
            mapped.Seal();

            Assert.Collection(
                sources,
                source => Assert.Same(first, Assert.IsType<SealedEndpointDataSource>(source).Inner),
                source => Assert.Same(second, Assert.IsType<SealedEndpointDataSource>(source).Inner));
            Assert.Equal(once, sources);
        }

        [Fact]
        public void Once_sealed_the_endpoints_are_read_from_the_sealed_sources_alone()
        {
            using ChangingSource first = new();
            using ChangingSource late = new();
            List<EndpointDataSource> sources = [first];
            MappedEndpoints mapped = new();
            mapped.Capture(sources);

            mapped.Seal();
            sources.Add(late);

            _ = Assert.Single(mapped.Endpoints());
            Assert.Equal(0, late.Reads);
        }

        /// <summary>Calls a route of the web group as the SPA does.</summary>
        /// <param name="host">The host.</param>
        /// <param name="route">The route below the web group.</param>
        /// <param name="cookie">The session cookie.</param>
        /// <returns>The response.</returns>
        private static async Task<HttpResponseMessage> GetAsync(TellmaTestHost host, string route, string cookie)
        {
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web" + route, cookie);
            return await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        /// <summary>A source that builds new endpoints on every read and signals a change when told to.</summary>
        private sealed class ChangingSource : EndpointDataSource, IDisposable
        {
            /// <summary>Fires the current change token.</summary>
            private CancellationTokenSource _changed = new();

            /// <summary>The current change token.</summary>
            private CancellationChangeToken _token;

            /// <summary>Creates the source.</summary>
            public ChangingSource()
            {
                _token = new CancellationChangeToken(_changed.Token);
            }

            /// <summary>How many times its endpoints were built.</summary>
            public int Reads { get; private set; }

            /// <summary>How many times its grouped endpoints were built.</summary>
            public int GroupedReads { get; private set; }

            /// <inheritdoc />
            public override IReadOnlyList<Endpoint> Endpoints
            {
                get
                {
                    Reads++;
                    return [new Endpoint(static _ => Task.CompletedTask, EndpointMetadataCollection.Empty, "changing")];
                }
            }

            /// <inheritdoc />
            public override IChangeToken GetChangeToken()
            {
                return _token;
            }

            /// <inheritdoc />
            public override IReadOnlyList<Endpoint> GetGroupedEndpoints(RouteGroupContext context)
            {
                GroupedReads++;
                return Endpoints;
            }

            /// <inheritdoc />
            public void Dispose()
            {
                _changed.Dispose();
            }

            /// <summary>Signals a change: fires the current token and arms a new one.</summary>
            public void Change()
            {
                CancellationTokenSource fired = _changed;
                _changed = new CancellationTokenSource();
                _token = new CancellationChangeToken(_changed.Token);
                fired.Cancel();
                fired.Dispose();
            }
        }
    }
}
