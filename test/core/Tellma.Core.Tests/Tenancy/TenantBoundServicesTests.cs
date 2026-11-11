// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Tenancy;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Tenancy
{
    /// <summary>
    ///     The scoped services that read the bound tenant: the sandbox flag, which throws when no
    ///     tenant is bound and answers the tenant's category otherwise, and the connection provider,
    ///     which refuses a tenantless scope and reaches the bound tenant alone.
    /// </summary>
    public class TenantBoundServicesTests
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        // --- The sandbox flag ---

        [Fact]
        public async Task The_sandbox_flag_throws_in_a_scope_bound_to_no_tenant()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync();
            await using AsyncServiceScope scope = harness.Services.CreateAsyncScope();
            ISandboxContext sandbox = scope.ServiceProvider.GetRequiredService<ISandboxContext>();

            // Resolving it is harmless; asking it outside a tenant is the bug that must surface.
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => sandbox.IsSandbox);

            Assert.IsType<TenantSandboxContext>(sandbox);
            Assert.Contains("outside a tenant", failure.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(TenantCategory.Live, false)]
        [InlineData(TenantCategory.Sandbox, true)]
        public async Task The_sandbox_flag_of_a_request_is_its_tenants_category(TenantCategory category, bool expected)
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync();
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant(category: category));

            Assert.Equal(expected, scope.ServiceProvider.GetRequiredService<ISandboxContext>().IsSandbox);
        }

        [Fact]
        public async Task The_sandbox_flag_follows_the_scope_once_a_tenant_is_bound()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync();
            await using AsyncServiceScope scope = harness.Services.CreateAsyncScope();
            ISandboxContext sandbox = scope.ServiceProvider.GetRequiredService<ISandboxContext>();
            IRequestContextHolder holder = scope.ServiceProvider.GetRequiredService<IRequestContextHolder>();
            _ = Assert.Throws<InvalidOperationException>(() => sandbox.IsSandbox);

            // The same instance, resolved before the tenant was bound, reads the scope's context afresh.
            holder.Set(holder.Current with { Tenant = TenantContextHarness.Tenant(category: TenantCategory.Sandbox) });

            Assert.True(sandbox.IsSandbox);
        }

        [Fact]
        public async Task The_composition_replaces_a_never_sandboxed_context_registered_before_it()
        {
            // A host that registered the fixed implementation first must not keep it: in a tenant host
            // it would treat every sandbox tenant's side effects as live.
            ServiceCollection services = [];
            _ = services.AddSingleton(SandboxContext.Never);
            IServiceCollection composed = TestComposition.Compose(static _ => { }, services: services);
            await using ServiceProvider provider = composed.BuildServiceProvider(validateScopes: true);
            await using AsyncServiceScope scope = provider.CreateAsyncScope();

            Assert.IsType<TenantSandboxContext>(scope.ServiceProvider.GetRequiredService<ISandboxContext>());
        }

        // --- The connection provider ---

        [Fact]
        public async Task The_connection_provider_refuses_a_scope_bound_to_no_tenant()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync([FakeCatalogReader.Row(17)]);
            await using AsyncServiceScope scope = harness.Services.CreateAsyncScope();
            ITenantConnectionProvider provider = scope.ServiceProvider.GetRequiredService<ITenantConnectionProvider>();

            InvalidOperationException tenant = Assert.Throws<InvalidOperationException>(() => provider.Tenant);
            InvalidOperationException open = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.OpenAsync(Token));

            Assert.Contains("not bound to a tenant", tenant.Message, StringComparison.Ordinal);
            Assert.Contains("not bound to a tenant", open.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task The_connection_provider_answers_the_bound_tenant_from_the_snapshot()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(
                [FakeCatalogReader.Row(17, database: "tenant-seventeen"), FakeCatalogReader.Row(18, database: "tenant-eighteen")]);
            await using AsyncServiceScope scope = harness.BindRequest(harness.Registry.Get(18).Descriptor);
            ITenantConnectionProvider provider = scope.ServiceProvider.GetRequiredService<ITenantConnectionProvider>();

            Assert.Same(harness.Registry.Get(18), provider.Tenant);
            Assert.Contains("Initial Catalog=tenant-eighteen", provider.Tenant.ConnectionString, StringComparison.Ordinal);
            Assert.DoesNotContain("tenant-seventeen", provider.Tenant.ConnectionString, StringComparison.Ordinal);
        }

        [Fact]
        public async Task The_connection_provider_follows_the_snapshot_and_never_serves_a_tenant_that_left_it()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync([FakeCatalogReader.Row(17, database: "tenant-old")]);
            await using AsyncServiceScope scope = harness.BindRequest(harness.Registry.Get(17).Descriptor);
            ITenantConnectionProvider provider = scope.ServiceProvider.GetRequiredService<ITenantConnectionProvider>();

            // --- A relocation is picked up: the provider keeps no string of its own ---
            harness.Catalog.Update(FakeCatalogReader.Row(17, database: "tenant-new"));
            await harness.Registry.RefreshAsync(force: false, Token);
            Assert.Contains("Initial Catalog=tenant-new", provider.Tenant.ConnectionString, StringComparison.Ordinal);

            // --- A tenant that left the snapshot is not found, for the property and the open alike ---
            harness.Catalog.Remove(17);
            await harness.Registry.RefreshAsync(force: false, Token);
            _ = Assert.Throws<TenantNotFoundException>(() => provider.Tenant);
            _ = await Assert.ThrowsAsync<TenantNotFoundException>(() => provider.OpenAsync(Token));
        }

        [Fact]
        public async Task The_connection_provider_fails_closed_on_a_stale_snapshot()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync([FakeCatalogReader.Row(17)]);
            await using AsyncServiceScope scope = harness.BindRequest(harness.Registry.Get(17).Descriptor);
            ITenantConnectionProvider provider = scope.ServiceProvider.GetRequiredService<ITenantConnectionProvider>();

            harness.Time.Advance(TimeSpan.FromHours(1));

            Assert.Equal("catalog-unavailable", Assert.Throws<TenantUnavailableException>(() => provider.Tenant).Code);
            Assert.Equal("catalog-unavailable", (await Assert.ThrowsAsync<TenantUnavailableException>(() => provider.OpenAsync(Token))).Code);
        }
    }
}
