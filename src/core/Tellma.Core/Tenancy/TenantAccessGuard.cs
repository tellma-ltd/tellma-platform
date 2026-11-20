// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Data;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     Decides whether a request's principal may proceed against its tenant: the state verdict,
    ///     the initializers, then the assurance bar. One of the two writers of the scope's context.
    /// </summary>
    /// <param name="holder">The scope's context, which the tenant front door already bound.</param>
    /// <param name="initializers">The scope's initializers, in registration order.</param>
    /// <param name="metrics">Counts each verdict.</param>
    internal sealed class TenantAccessGuard(
        IRequestContextHolder holder,
        IEnumerable<IRequestContextInitializer> initializers,
        TenancyMetrics metrics) : ITenantAccessGuard
    {
        /// <inheritdoc />
        public async Task EnsureAccessAsync(
            TenantAccessRequirement requirement, RequestContextInputs inputs, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(requirement);
            ArgumentNullException.ThrowIfNull(inputs);

            // --- What the front door bound: a tenant and an authenticated caller ---
            RequestContext context = holder.Current;
            TenantDescriptor tenant = context.Tenant ?? throw new InvalidOperationException(
                "The access guard was called in a scope that is not bound to a tenant. The tenant front door binds the tenant and the principal before it calls the guard.");

            if (context.Kind == PrincipalKind.System)
            {
                // The platform's own principal skips the membership check, so no request may carry it.
                throw new InvalidOperationException(
                    "The access guard was called for the platform's own principal. A request runs for a user or a service account; the platform's work enters a tenant through the tenant scope factory.");
            }

            if (context.Kind == PrincipalKind.Anonymous)
            {
                // Before the state verdict, so a caller nobody authenticated learns nothing about the
                // tenant, not even that it is suspended.
                metrics.RecordResolution(TenantResolutionOutcome.NotMember);
                throw new TenantNotFoundException(tenant.Id);
            }

            // --- Step 1: the state verdict (spec 0011 §3.3) ---
            EnsureStateServes(tenant, requirement);

            // --- Step 2: the initializers, the user connection first ---
            try
            {
                context = await RequestContextInitialization
                    .RunAsync(holder, initializers, context, inputs, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TenantNotFoundException)
            {
                metrics.RecordResolution(TenantResolutionOutcome.NotMember);
                throw;
            }
            catch (TenantUnavailableException unavailable) when (unavailable.Code == TenantProblemCodes.CatalogUnavailable)
            {
                metrics.RecordResolution(TenantResolutionOutcome.CatalogUnavailable);
                throw;
            }
            catch (DataAccessException failure) when (failure is DataAccessAmbiguousException || DataAccessTranslation.Translate(failure) is not null)
            {
                // Spec 0015 §14.2: a cold connect's round trip runs before any operation that would
                // translate it, so an outage it outlasted is answered here as a retryable 503. Its one
                // write is the activity stamp, so a stamp that committed unseen is retried harmlessly.
                throw DataAccessTranslation.Unavailable(failure);
            }

            if (context.UserId is null)
            {
                // Fail closed: everything after the guard relies on the user being bound.
                throw new InvalidOperationException(
                    "No request-context initializer bound the caller's user in the tenant. The user connection initializer is missing from the composition.");
            }

            // The tenant serves the caller. The assurance bar below is a verdict on the session, not
            // on the tenant, and is counted by the host that writes its challenge.
            metrics.RecordResolution(TenantResolutionOutcome.Served);

            // --- Step 3: the assurance bar ---
            if (requirement.Assurance is { } bar)
            {
                AssuranceCheck.Ensure(context, bar, context.Now);
            }
        }

        /// <summary>Refuses a request the tenant's state does not serve, and counts the refusal.</summary>
        /// <param name="tenant">The bound tenant.</param>
        /// <param name="requirement">What the endpoint asks.</param>
        /// <exception cref="TenantUnavailableException">The tenant is suspended, or the request is a mutation the state refuses.</exception>
        /// <exception cref="TenantNotFoundException">The tenant is in a state a request sees as absent.</exception>
        private void EnsureStateServes(TenantDescriptor tenant, TenantAccessRequirement requirement)
        {
            switch (tenant.State)
            {
                case TenantState.Active:
                    return;

                case TenantState.ReadOnly:
                    if (requirement.IsMutation && !requirement.AllowWhenReadOnly)
                    {
                        throw Refuse(tenant, TenantResolutionOutcome.ReadOnly, TenantProblemCodes.ReadOnly);
                    }

                    return;

                case TenantState.Frozen:
                    // The database may be set READ_ONLY, so no mutation runs, whatever the endpoint allows.
                    if (requirement.IsMutation)
                    {
                        throw Refuse(tenant, TenantResolutionOutcome.Frozen, TenantProblemCodes.Frozen);
                    }

                    return;

                case TenantState.Suspended:
                    throw Refuse(tenant, TenantResolutionOutcome.Suspended, TenantProblemCodes.Suspended);

                case TenantState.Provisioning:
                    // The tenant middleware refuses these two before binding; a front door that let one
                    // through still answers as if the tenant were absent.
                    metrics.RecordResolution(TenantResolutionOutcome.Provisioning);
                    throw new TenantNotFoundException(tenant.Id);

                case TenantState.Retired:
                default:
                    metrics.RecordResolution(TenantResolutionOutcome.NotFound);
                    throw new TenantNotFoundException(tenant.Id);
            }
        }

        /// <summary>Counts a state's refusal and builds its exception.</summary>
        /// <param name="tenant">The bound tenant.</param>
        /// <param name="outcome">The outcome counted.</param>
        /// <param name="code">The refusal's problem code.</param>
        /// <returns>The exception to throw.</returns>
        private TenantUnavailableException Refuse(TenantDescriptor tenant, TenantResolutionOutcome outcome, string code)
        {
            metrics.RecordResolution(outcome);

            // No retry-after: waiting does not change a state an operator set.
            return new TenantUnavailableException(tenant.Id, tenant.State, code, retryAfter: null);
        }
    }
}
