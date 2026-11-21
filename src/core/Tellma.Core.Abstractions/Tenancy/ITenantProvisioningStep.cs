// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     One versioned piece of a tenant's provisioning: reference data, a storage container, an
    ///     external integration. The migrator runs every step whose version is ahead of the one the
    ///     tenant database records, in <see cref="Order" />, each in a system scope of its own, and
    ///     records each completion.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A feature registers a step with <see cref="FeatureContribution.ProvisioningStep{TStep}" />;
    ///         the step is created from the tenant scope it runs in, so it injects scoped services.
    ///         Its constructor must not reach the database: the startup gate creates every step once,
    ///         outside any tenant, to read its name, order and version.
    ///     </para>
    ///     <para>
    ///         A step re-runs whenever its <see cref="Version" /> moves past the recorded one, and a
    ///         failed run is retried by the next migration, so a step is idempotent: running it twice
    ///         leaves what running it once does.
    ///     </para>
    /// </remarks>
    public interface ITenantProvisioningStep
    {
        /// <summary>
        ///     The step's key in the tenant database's record of completed steps: <c>core.&lt;step&gt;</c>
        ///     for the platform's, <c>&lt;module&gt;.&lt;step&gt;</c> for a pack's and
        ///     <c>&lt;slug&gt;.&lt;step&gt;</c> for a distribution's. At most 128 characters, unique
        ///     across the composition, and never changed once shipped.
        /// </summary>
        string Name { get; }

        /// <summary>
        ///     Where the step runs among the others, ascending: 0 to 99 and 900 and above for the
        ///     platform's, 100 to 199 for packs', 200 to 899 for distributions'.
        /// </summary>
        int Order { get; }

        /// <summary>The step's version, from 1; the step re-runs on every tenant whose recorded version is lower.</summary>
        int Version { get; }

        /// <summary>Runs the step for one tenant.</summary>
        /// <param name="context">The tenant, the scope's services and the run's administrator.</param>
        /// <returns>A task that completes when the step's work is committed.</returns>
        Task RunAsync(TenantProvisioningContext context);
    }
}
