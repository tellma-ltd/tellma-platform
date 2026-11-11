// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     The sandbox flag of a tenant host: whether the tenant the scope is bound to is a sandbox.
    ///     Scoped, over the scope's request context, so it answers alike in a request and in a
    ///     background tenant scope.
    /// </summary>
    /// <param name="accessor">The scope's request context.</param>
    internal sealed class TenantSandboxContext(IRequestContextAccessor accessor) : ISandboxContext
    {
        /// <inheritdoc />
        /// <exception cref="InvalidOperationException">The scope is not bound to a tenant.</exception>
        public bool IsSandbox
        {
            get
            {
                // Unbound work is neither live nor sandbox. Answering either would leak a sandbox
                // tenant's side effect or silently drop a live one, so the first call fails instead.
                TenantDescriptor tenant = accessor.Current.Tenant ?? throw new InvalidOperationException(
                    "A side-effecting connector asked whether its work is sandboxed outside a tenant. Run the work inside a tenant request or a scope the tenant scope factory created; a host without tenants registers SandboxContext.Never.");

                return tenant.Category == TenantCategory.Sandbox;
            }
        }
    }
}
