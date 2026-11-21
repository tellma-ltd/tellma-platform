// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Crud
{
    // Spec 0015 §5.1: the pipeline repeats spec 0011's tenant-state verdict before it composes a
    // Persist batch, so a job or provisioning scope meets it as a request does. Frozen refuses every
    // persist; ReadOnly refuses a mutation without AllowWhenReadOnly, unless a frame it is nested in
    // admits it, an [ApiAction] frame replacing the operation's own descriptor. The other states are
    // the tenant middleware's and the prologue's to refuse.
    /// <summary>The tenant-state verdict on a persist, taken before its batch is composed.</summary>
    internal static class PersistVerdict
    {
        /// <summary>Refuses a persist the tenant's state does not admit.</summary>
        /// <param name="context">The scope's request context.</param>
        /// <param name="mutation">Whether the operation writes tenant data.</param>
        /// <param name="allowWhenReadOnly">Whether the operation runs on a read-only tenant.</param>
        /// <param name="frames">The write frames the persist is nested in, outermost first.</param>
        /// <exception cref="TenantUnavailableException">The tenant is frozen, or read-only and nothing admits the persist.</exception>
        public static void Ensure(RequestContext context, bool mutation, bool allowWhenReadOnly, IReadOnlyList<WriteFrame> frames)
        {
            if (Refusal(context, mutation, allowWhenReadOnly, frames) is TenantUnavailableException refused)
            {
                throw refused;
            }
        }

        /// <summary>The refusal <see cref="Ensure" /> raises, answered rather than thrown, so a caller can ask ahead of the persist.</summary>
        /// <param name="context">The scope's request context.</param>
        /// <param name="mutation">Whether the operation writes tenant data.</param>
        /// <param name="allowWhenReadOnly">Whether the operation runs on a read-only tenant.</param>
        /// <param name="frames">The write frames the persist is nested in, outermost first.</param>
        /// <returns>The refusal; null when the tenant's state admits the persist.</returns>
        public static TenantUnavailableException? Refusal(RequestContext context, bool mutation, bool allowWhenReadOnly, IReadOnlyList<WriteFrame> frames)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(frames);
            if (context.Tenant is not TenantDescriptor tenant)
            {
                return null;
            }

            switch (tenant.State)
            {
                case TenantState.Frozen:
                    // The database may be READ_ONLY: no persist runs, whatever frame it is nested in.
                    return Refused(tenant, TenantProblemCodes.Frozen);

                case TenantState.ReadOnly:
                    // --- A frame that admits it: a read-only-safe API action, a RunsWhenReadOnly job, a provisioning step ---
                    if (frames.Any(static frame => frame.AdmitsReadOnly))
                    {
                        return null;
                    }

                    // --- An API action frame that does not: its descriptor, not the nested operation's, judges ---
                    return frames.Any(static frame => frame.Kind == WriteHostKind.ApiAction) || (mutation && !allowWhenReadOnly)
                        ? Refused(tenant, TenantProblemCodes.ReadOnly)
                        : null;

                // The tenant middleware and the prologue refuse a tenant in any other state.
                case TenantState.Active:
                case TenantState.Suspended:
                case TenantState.Provisioning:
                case TenantState.Retired:
                default:
                    return null;
            }
        }

        /// <summary>The refusal of a persist the tenant's state does not admit.</summary>
        /// <param name="tenant">The tenant.</param>
        /// <param name="code">The refusal's problem code.</param>
        /// <returns>The exception.</returns>
        private static TenantUnavailableException Refused(TenantDescriptor tenant, string code)
        {
            // No retry-after: waiting does not change a state an operator set.
            return new TenantUnavailableException(tenant.Id, tenant.State, code, retryAfter: null);
        }
    }
}
