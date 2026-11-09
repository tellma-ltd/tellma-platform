// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     Told when a tenant's descriptor changes (its state, its names or languages, a new tenant)
    ///     and when a tenant leaves the registry's snapshot. The realtime hub closes a suspended or
    ///     removed tenant's connections through it, and the job worker stops polling a tenant that
    ///     leaves the states it serves.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The tenant registry tells every listener after a snapshot reload, once per changed or
    ///         removed tenant in tenant order, and reloads after every catalog write of this instance.
    ///         A change made by another instance reaches this one's listeners within one refresh
    ///         interval. Listeners are not called for the snapshot a process loads first.
    ///     </para>
    ///     <para>
    ///         Calls are delivered from an ordered queue after the refresh has completed, so a
    ///         listener never holds up the registry and may itself refresh the registry or write the
    ///         catalog. Each call runs inside a service scope of its own and is bounded: a call that
    ///         throws or outlasts the bound is logged and the queue moves on.
    ///     </para>
    ///     <para>
    ///         A collection service: every registration is called, in registration order.
    ///     </para>
    /// </remarks>
    [CollectionService]
    public interface ITenantStateListener
    {
        /// <summary>Reacts to a changed tenant descriptor.</summary>
        /// <param name="tenant">The tenant as it is now.</param>
        /// <param name="previous">The state the tenant had before; null for a tenant the snapshot did not hold before.</param>
        /// <returns>A task that completes when the listener has reacted.</returns>
        Task OnStateChangedAsync(TenantDescriptor tenant, TenantState? previous);

        /// <summary>
        ///     Reacts to a tenant that left the snapshot: its row was deleted, or no longer passes the
        ///     deployment's rules (its server, database or profile), so the tenant now answers as if
        ///     it did not exist.
        /// </summary>
        /// <param name="tenant">The tenant as the snapshot last held it.</param>
        /// <returns>A task that completes when the listener has reacted.</returns>
        Task OnRemovedAsync(TenantDescriptor tenant);
    }
}
