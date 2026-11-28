// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     Reads the request context of the service scope it was resolved from. Scoped: every service
    ///     that needs the tenant, the principal or the locale takes this, never the HTTP context.
    /// </summary>
    /// <remarks>
    ///     The context lives in the scope and nowhere else: no ambient, static or singleton copy
    ///     exists, so a singleton that needs a tenant opens a tenant scope
    ///     (<see cref="ITenantScopeFactory" />) and resolves its work from it.
    /// </remarks>
    public interface IRequestContextAccessor
    {
        /// <summary>
        ///     The scope's context: the one the platform bound for the request or the background
        ///     scope, or the tenantless context (<see cref="RequestContext.Tenantless" />) when nothing
        ///     bound one.
        /// </summary>
        RequestContext Current { get; }
    }
}
