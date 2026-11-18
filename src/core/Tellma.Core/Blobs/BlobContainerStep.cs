// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Blobs
{
    // spec 0017 §6.4: the tenant's container exists before its first upload; the store's lazy
    // creation on a write stays the fallback for a store registered after provisioning ran.
    /// <summary>
    ///     <c>core.blob-container</c>: creates the tenant's container or directory in the process's
    ///     blob store, so that its first upload finds it. Idempotent, as the store's call is.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The store is resolved when the step runs, not when it is created, so a composition
    ///         without a store (one that emits client assets, say) still builds: the startup gate
    ///         creates every step to read its name, and the deployment's blobs check names the missing store.
    ///     </para>
    ///     <para>Removing a retired tenant's container is an operator's action; no step does it.</para>
    /// </remarks>
    internal sealed class BlobContainerStep : ITenantProvisioningStep
    {
        /// <summary>The step's name.</summary>
        public const string StepName = "core.blob-container";

        /// <summary>The step's order: after the tenant's settings (15).</summary>
        public const int StepOrder = 20;

        /// <inheritdoc />
        public string Name => StepName;

        /// <inheritdoc />
        public int Order => StepOrder;

        /// <inheritdoc />
        public int Version => 1;

        /// <inheritdoc />
        public Task RunAsync(TenantProvisioningContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // The runner passes no cancellation; a step runs to completion or fails.
            IBlobStore store = context.Services.GetRequiredService<IBlobStore>();
            return store.EnsureTenantAsync(context.Tenant.Id, CancellationToken.None);
        }
    }
}
