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
    ///     Distributions implement this over their tenant context and must ensure it is resolvable
    ///     wherever sends happen — request scopes and background-worker scopes alike. Hosts without
    ///     tenants (identity, landing) register <see cref="SandboxContext.Never" />. There is no
    ///     default registration: a composition that forgot to decide fails at startup rather than
    ///     silently treating sandbox tenants as live.
    /// </remarks>
    public interface ISandboxContext
    {
        /// <summary>True when the ambient work belongs to a sandbox tenant.</summary>
        bool IsSandbox { get; }
    }
}
