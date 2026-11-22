// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.AspNetCore;
using Tellma.Core.Composition;

namespace Tellma.Distro.Acme.TestSupport
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

        /// <summary>Maps a test's own endpoints onto the host's groups, as a distribution maps its own; null for none.</summary>
        public Action<TellmaEndpoints>? Map { get; init; }

        /// <summary>
        ///     Whether starting the host waits until its tenant snapshot is loaded, as a load balancer
        ///     sends an instance nothing before it is ready; on by default, off for a host that must
        ///     start not ready.
        /// </summary>
        public bool AwaitsCatalog { get; init; } = true;

        /// <summary>
        ///     Whether the host keeps the blob store and image processor Acme's composition chose (in
        ///     Development the file-system store under <c>Tellma:Blobs:FileSystem:RootPath</c>, and
        ///     Skia) instead of the in-memory stand-ins; off by default, so a suite never writes under
        ///     the developer's application data.
        /// </summary>
        public bool KeepsComposedBlobs { get; init; }

        /// <summary>
        ///     Whether the host listens with Kestrel on a loopback port instead of the in-memory test
        ///     server, for a case that needs the real server's behavior, such as a stop that waits for
        ///     the requests in flight; off by default. Such a host's browser and protocol drivers are
        ///     unavailable, and it reaches nothing over HTTP at its public origin.
        /// </summary>
        public bool UsesKestrel { get; init; }
    }
}
