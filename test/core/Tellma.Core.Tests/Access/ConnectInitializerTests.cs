// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     The connect initializer's cases that need no database: tenantless work connects no one,
    ///     and an anonymous caller finds no tenant; neither is witnessed as a member. The connecting
    ///     cases run against a database.
    /// </summary>
    public sealed class ConnectInitializerTests : IAsyncDisposable
    {
        /// <summary>The composed root.</summary>
        private readonly ServiceProvider _services = TestComposition.Compose(static _ => { }).BuildServiceProvider(validateScopes: true);

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _services.DisposeAsync();
        }

        [Fact]
        public async Task Runs_first_of_the_platforms_initializers_before_the_locale_negotiation()
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();

            Assert.Equal(100, Initializer(scope).Order);
            Assert.True(Initializer(scope).Order < scope.ServiceProvider.GetServices<IRequestContextInitializer>().Max(static initializer => initializer.Order));
        }

        [Fact]
        public async Task Tenantless_work_is_returned_unchanged()
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            RequestContext tenantless = scope.ServiceProvider.GetRequiredService<IRequestContextAccessor>().Current;

            RequestContext initialized = await Initializer(scope).InitializeAsync(tenantless, RequestContextInputs.Empty, TestContext.Current.CancellationToken);

            Assert.Same(tenantless, initialized);
            Assert.False(scope.ServiceProvider.GetRequiredService<AccessWitness>().IsMemberWitnessed);
        }

        [Fact]
        public async Task An_anonymous_caller_of_a_tenant_finds_no_tenant()
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            RequestContext anonymous = scope.ServiceProvider.GetRequiredService<IRequestContextAccessor>().Current with
            {
                Tenant = new TenantDescriptor(3, "Acme", null, null, ["en"], TenantCategory.Live, TenantState.Active, null),
                Kind = PrincipalKind.Anonymous,
            };

            TenantNotFoundException refused = await Assert.ThrowsAsync<TenantNotFoundException>(
                () => Initializer(scope).InitializeAsync(anonymous, RequestContextInputs.Empty, TestContext.Current.CancellationToken));

            Assert.Equal(3, refused.TenantId);
            Assert.False(scope.ServiceProvider.GetRequiredService<AccessWitness>().IsMemberWitnessed);
        }

        /// <summary>The scope's connect initializer.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The initializer.</returns>
        private static ConnectInitializer Initializer(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetServices<IRequestContextInitializer>().OfType<ConnectInitializer>().Single();
        }
    }
}
