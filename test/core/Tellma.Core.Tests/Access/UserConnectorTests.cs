// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Tenancy;
using Tellma.Core.Tests.Data.Batches;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     The connector's refusals that need no database: the system's connection and the system's
    ///     premises are the platform's own, and no caller's scope reaches them.
    /// </summary>
    public sealed class UserConnectorTests : IDisposable
    {
        /// <summary>A harness whose batches are of tenant 1.</summary>
        private readonly BatchHarness _harness = new();

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
        }

        [Theory]
        [InlineData(PrincipalKind.User)]
        [InlineData(PrincipalKind.ServiceAccount)]
        public async Task A_callers_scope_never_connects_as_the_system(PrincipalKind kind)
        {
            await using AsyncServiceScope scope = Scope(kind);

            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => Connector(scope).ConnectAsSystemAsync(TestContext.Current.CancellationToken));
        }

        [Theory]
        [InlineData(PrincipalKind.User)]
        [InlineData(PrincipalKind.ServiceAccount)]
        public async Task A_callers_scope_never_composes_a_batch_under_the_systems_premises(PrincipalKind kind)
        {
            await using AsyncServiceScope scope = Scope(kind);

            _ = Assert.Throws<InvalidOperationException>(() => Connector(scope).Contribute(_harness.Batch(), new ConnectPremises.ForSystem(Guid.Empty)));
        }

        /// <summary>The scope's connector, behind its contract.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The connector.</returns>
        private static IUserConnector Connector(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<IUserConnector>();
        }

        /// <summary>A scope of tenant 1 bound to a caller of a kind.</summary>
        /// <param name="kind">The caller's kind.</param>
        /// <returns>The scope.</returns>
        private AsyncServiceScope Scope(PrincipalKind kind)
        {
            AsyncServiceScope scope = _harness.Services.CreateAsyncScope();
            IRequestContextHolder holder = scope.ServiceProvider.GetRequiredService<IRequestContextHolder>();
            holder.Set(holder.Current with
            {
                Tenant = new TenantDescriptor(1, "Acme", null, null, ["en"], TenantCategory.Live, TenantState.Active, null),
                Kind = kind,
                Subject = "sub-1",
                UserId = 7,
            });
            return scope;
        }
    }
}
