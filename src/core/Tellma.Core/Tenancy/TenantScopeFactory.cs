// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     Opens tenant-bound service scopes for background work: the second of the two writers of a
    ///     scope's context. A singleton.
    /// </summary>
    /// <remarks>
    ///     It starts no activity and opens no logger scope: both belong to the caller, whose flow
    ///     outlives this call, so the scope's spans and log lines nest under the caller's.
    /// </remarks>
    /// <param name="registry">The tenant snapshot the tenant is looked up in afresh.</param>
    /// <param name="scopes">Creates the service scope.</param>
    /// <param name="metrics">Counts each scope.</param>
    internal sealed class TenantScopeFactory(ITenantRegistry registry, IServiceScopeFactory scopes, TenancyMetrics metrics)
        : ITenantScopeFactory
    {
        /// <inheritdoc />
        public async Task<TenantScope> CreateScopeAsync(
            RequestContextSnapshot snapshot, bool allowNonActive = false, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            // --- Refusals that need no tenant, before any scope exists ---
            if (snapshot.Kind == PrincipalKind.Anonymous)
            {
                throw new ArgumentException("A background scope always runs for a principal; the snapshot is anonymous.", nameof(snapshot));
            }

            if (allowNonActive && snapshot.Kind != PrincipalKind.System)
            {
                throw new InvalidOperationException(
                    $"Only the platform's own principal may enter a tenant that is not active; the snapshot's kind is {snapshot.Kind}.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            // --- The tenant as it is now: its state may have changed since the work was enqueued ---
            TenantDescriptor tenant = registry.Get(snapshot.TenantId).Descriptor;
            EnsureStateServes(tenant, allowNonActive);

            // --- The scope, bound before any initializer runs and disposed if one refuses ---
            AsyncServiceScope scope = scopes.CreateAsyncScope();
            try
            {
                IServiceProvider services = scope.ServiceProvider;
                IRequestContextHolder holder = services.GetRequiredService<IRequestContextHolder>();

                // A fresh scope's context is the tenantless one, stamped with the clock just now.
                RequestContext context = holder.Current with
                {
                    Tenant = tenant,
                    Kind = snapshot.Kind,
                    Subject = snapshot.Subject,
                    UserId = snapshot.UserId,
                    Client = snapshot.Client,
                    OriginTraceParent = snapshot.TraceParent,
                };
                holder.Set(context);

                // Empty inputs: no request asked for a locale, so the negotiation resolves it from the
                // run-as user's stored preferences, then the tenant's settings (spec 0011 §4.4).
                context = await RequestContextInitialization
                    .RunAsync(holder, services.GetServices<IRequestContextInitializer>(), context, RequestContextInputs.Empty, cancellationToken)
                    .ConfigureAwait(false);

                if (context.UserId is null)
                {
                    // Fail closed, as the access guard does: tenant-scoped services rely on the user.
                    throw new InvalidOperationException(
                        "The snapshot names no user and no request-context initializer bound one. The user connection initializer is missing from the composition.");
                }

                metrics.RecordScope(TenantScopeKind.Background);
                return new TenantScope(services, scope);
            }
            catch
            {
                await scope.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>Refuses a tenant whose state serves no background work.</summary>
        /// <param name="tenant">The tenant as the snapshot holds it now.</param>
        /// <param name="allowNonActive">Whether the caller is the platform's own maintenance work.</param>
        /// <exception cref="TenantUnavailableException">The state refuses the work.</exception>
        private static void EnsureStateServes(TenantDescriptor tenant, bool allowNonActive)
        {
            // The refusal's code, or null when the state serves the work (spec 0011 §3.3). A
            // provisioning or retired tenant is one a request sees as absent, so its code says
            // not-found while the exception's state tells the caller the truth. A state this build
            // does not know serves nothing.
            string? refusal = tenant.State switch
            {
                TenantState.Active or TenantState.ReadOnly => null,
                TenantState.Provisioning => allowNonActive ? null : TenantProblemCodes.NotFound,
                TenantState.Frozen => allowNonActive ? null : TenantProblemCodes.Frozen,
                TenantState.Suspended => TenantProblemCodes.Suspended,
                TenantState.Retired => TenantProblemCodes.NotFound,
                _ => TenantProblemCodes.NotFound,
            };

            if (refusal is not null)
            {
                // No retry-after: waiting does not change a state an operator set.
                throw new TenantUnavailableException(tenant.Id, tenant.State, refusal, retryAfter: null);
            }
        }
    }
}
