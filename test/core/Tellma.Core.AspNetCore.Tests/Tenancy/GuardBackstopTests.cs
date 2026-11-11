// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using System.Net;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Tests.Infrastructure;
using Tellma.Core.Tenancy;

namespace Tellma.Core.AspNetCore.Tests.Tenancy
{
    /// <summary>
    ///     The tenant guard reaches every endpoint of a guarded group, including one from a data source
    ///     that ignores endpoint filters, as static files and hand-built endpoints do.
    /// </summary>
    public sealed class GuardBackstopTests
    {
        [Fact]
        public async Task An_endpoint_that_ignores_filters_is_still_guarded()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options => options.Map = static (_, endpoints) =>
                ((IEndpointRouteBuilder)endpoints.Web).DataSources.Add(new RawDataSource()));

            // A member is bound: the guard ran and initialized the context.
            using HttpRequestMessage member = TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/raw", host.Cookie());
            using HttpResponseMessage bound = await host.Client.SendAsync(member, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, bound.StatusCode);
            Assert.Equal(TellmaTestHost.UserId, (await ProbeResult.ReadAsync(bound)).UserId);

            // A stranger is refused, as on every other endpoint of the group.
            using HttpRequestMessage stranger = TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/raw", host.Cookie(TellmaTestHost.Stranger));
            using HttpResponseMessage refused = await host.Client.SendAsync(stranger, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        }

        [Fact]
        public async Task A_route_handler_is_guarded_once()
        {
            // The filter and the backstop both wrap a route handler; the guard's counter sees one access.
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options =>
                options.ConfigureServices = static services => services.Decorate());
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/probe", host.Cookie());
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(1, host.Services.GetRequiredService<CountingGuard.Counter>().Count);
        }

        /// <summary>A data source of one hand-built endpoint, which carries no filter of its own.</summary>
        private sealed class RawDataSource : EndpointDataSource
        {
            /// <inheritdoc />
            public override IReadOnlyList<Endpoint> Endpoints { get; } =
            [
                new RouteEndpoint(
                    static async context =>
                    {
                        RequestContext current = context.RequestServices.GetRequiredService<IRequestContextAccessor>().Current;
                        await context.Response.WriteAsJsonAsync(
                            new ProbeResult(current.Tenant?.Id, current.UserId, current.Kind.ToString(), current.Subject, null, null, null, current.Client, null, string.Empty),
                            context.RequestAborted);
                    },
                    RoutePatternFactory.Parse("/raw"),
                    order: 0,
                    new EndpointMetadataCollection(new HttpMethodMetadata(["GET"]), new MemberEndpointMetadata()),
                    "raw"),
            ];

            /// <inheritdoc />
            public override IChangeToken GetChangeToken()
            {
                return NullChangeToken.Singleton;
            }
        }
    }

    /// <summary>A guard that counts the accesses it checks, then defers to the platform's.</summary>
    /// <param name="inner">The platform's guard.</param>
    /// <param name="counter">The counter.</param>
    internal sealed class CountingGuard(ITenantAccessGuard inner, CountingGuard.Counter counter) : ITenantAccessGuard
    {
        /// <inheritdoc />
        public Task EnsureAccessAsync(TenantAccessRequirement requirement, RequestContextInputs inputs, CancellationToken cancellationToken)
        {
            _ = Interlocked.Increment(ref counter.Value);
            return inner.EnsureAccessAsync(requirement, inputs, cancellationToken);
        }

        /// <summary>The number of accesses checked.</summary>
        internal sealed class Counter
        {
            /// <summary>The count, as a field so it can be incremented atomically.</summary>
            public int Value;

            /// <summary>The count.</summary>
            public int Count => Volatile.Read(ref Value);
        }
    }

    /// <summary>Registers <see cref="CountingGuard" /> over the platform's guard.</summary>
    internal static class CountingGuardRegistration
    {
        /// <summary>Wraps the registered guard in a counting one.</summary>
        /// <param name="services">The services.</param>
        public static void Decorate(this IServiceCollection services)
        {
            ServiceDescriptor platform = services.Last(static descriptor => descriptor.ServiceType == typeof(ITenantAccessGuard));
            _ = services.AddSingleton<CountingGuard.Counter>();
            _ = services.AddScoped(platform.ImplementationType ?? throw new InvalidOperationException("The guard is not registered by type."));
            _ = services.AddScoped<ITenantAccessGuard>(provider => new CountingGuard(
                (ITenantAccessGuard)provider.GetRequiredService(platform.ImplementationType),
                provider.GetRequiredService<CountingGuard.Counter>()));
        }
    }
}
