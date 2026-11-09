// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     Told when a tenant's descriptor changes: its state, its names or languages, a new tenant.
    ///     The realtime hub closes a suspended tenant's connections through it, and the job worker
    ///     stops polling a tenant that leaves the states it serves.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The tenant registry calls every listener after a snapshot reload that changed any
    ///         descriptor, once per changed tenant, and after every catalog write of this instance,
    ///         which reloads the snapshot at once. A change made by another instance reaches this
    ///         one's listeners within one refresh interval. Listeners are not called for the snapshot
    ///         a process loads first.
    ///     </para>
    ///     <para>
    ///         A collection service: every registration is called, in registration order, inside a
    ///         service scope of its own. A listener that throws is logged and never fails the
    ///         refresh or the write that caused the call.
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
    }
}
