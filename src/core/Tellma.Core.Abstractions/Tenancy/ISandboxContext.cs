// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     Reports whether the ambient unit of work executes on behalf of a sandbox tenant — a tenant
    ///     whose actions must produce no external side effects. Consulted by connector pipelines
    ///     (email today; every side-effecting connector eventually) to route between live and
    ///     sandbox channels.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A host composed with <c>AddTellma</c> gets the platform's implementation over the
    ///         scope's request context: it answers the bound tenant's category in request scopes and
    ///         background tenant scopes alike, and throws <see cref="InvalidOperationException" />
    ///         when no tenant is bound, so a connector called outside a tenant fails on its first
    ///         call instead of treating the work as live or as sandbox by guess.
    ///     </para>
    ///     <para>
    ///         Hosts without tenants (identity, landing) register <see cref="SandboxContext.Never" />.
    ///         No other package registers a default: a composition that forgot to decide fails at
    ///         startup rather than silently treating sandbox tenants as live.
    ///     </para>
    /// </remarks>
    public interface ISandboxContext
    {
        /// <summary>True when the ambient work belongs to a sandbox tenant.</summary>
        bool IsSandbox { get; }
    }
}
