// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Composition;

namespace Tellma.Distro.Acme.IntegrationTests.Infrastructure
{
    /// <summary>What a test adds to Acme's web host beyond its configuration files and a deployment's settings.</summary>
    internal sealed record AcmeHostOptions
    {
        /// <summary>The host environment; Development, as a developer's checkout runs it, by default.</summary>
        public string Environment { get; init; } = "Development";

        /// <summary>
        ///     A further composition the host adds to Acme's, as <see cref="AcmeMigrator.Compose" /> does
        ///     for the migrator; null for none. It must change neither the tenant model nor the catalog's.
        /// </summary>
        public Action<TellmaBuilder>? Compose { get; init; }

        /// <summary>Registers what stands in for the rest of a deployment, after everything else; null for nothing.</summary>
        public Action<IServiceCollection>? Services { get; init; }

        /// <summary>Whether the host maps the tenant probes onto its web group, as a distribution maps its own endpoints.</summary>
        public bool MapsProbes { get; init; }

        /// <summary>
        ///     Whether starting the host waits until its tenant snapshot is loaded, as a load balancer
        ///     sends an instance nothing before it is ready; on by default, off for a host that must
        ///     start not ready.
        /// </summary>
        public bool AwaitsCatalog { get; init; } = true;
    }
}
