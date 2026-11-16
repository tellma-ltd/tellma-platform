// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Tenancy;
using Tellma.Core.Tests.Data.Batches;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     The connector's refusals that need no database: the system's connection and the system's
    ///     premises are the platform's own, and no caller's scope reaches them; the subject premises
    ///     are the scope's own caller's, and one the subject column can hold.
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

        [Theory]
        [InlineData("sub-2")]
        [InlineData("SUB-1")]
        public async Task A_subject_other_than_the_callers_is_never_composed(string subject)
        {
            await using AsyncServiceScope scope = Scope(PrincipalKind.User);

            _ = Assert.Throws<ArgumentException>(() => Connector(scope).Contribute(_harness.Batch(), ConnectPremises.Cold(subject, Guid.Empty, false, false)));
        }

        [Theory]
        [InlineData(256)]
        [InlineData(0)]
        public async Task A_callers_subject_the_column_cannot_hold_byte_for_byte_is_never_composed(int length)
        {
            // A subject longer than the column, or one with a character outside printable ASCII.
            string subject = length > 0 ? new string('a', length) : "s\u00FCb-1";
            await using AsyncServiceScope scope = Scope(PrincipalKind.User, subject);

            _ = Assert.Throws<ArgumentException>(() => Connector(scope).Contribute(_harness.Batch(), ConnectPremises.Cold(subject, Guid.Empty, false, false)));
        }

        [Fact]
        public async Task The_callers_own_subject_is_composed()
        {
            await using AsyncServiceScope scope = Scope(PrincipalKind.User);

            BatchResult<ConnectResult> handle = Connector(scope).Contribute(_harness.Batch(), ConnectPremises.Cold("sub-1", Guid.Empty, false, false));

            Assert.False(handle.IsCompleted);
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
        /// <param name="subject">The caller's subject.</param>
        /// <returns>The scope.</returns>
        private AsyncServiceScope Scope(PrincipalKind kind, string subject = "sub-1")
        {
            AsyncServiceScope scope = _harness.Services.CreateAsyncScope();
            IRequestContextHolder holder = scope.ServiceProvider.GetRequiredService<IRequestContextHolder>();
            holder.Set(holder.Current with
            {
                Tenant = new TenantDescriptor(1, "Acme", null, null, ["en"], TenantCategory.Live, TenantState.Active, null),
                Kind = kind,
                Subject = subject,
                UserId = 7,
            });
            return scope;
        }
    }
}
