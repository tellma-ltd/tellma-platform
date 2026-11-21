// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>
    ///     Writes part of the client metadata a distribution's build emits: the deployment facts the
    ///     client needs before its first request. Each section owner implements one and registers it
    ///     as a singleton; the emit runs every registered contributor in feature order.
    /// </summary>
    /// <remarks>
    ///     Every registration adds one contributor and none replaces another. A contributor reads the
    ///     realised composition only, and performs no I/O, so the emit runs at build time with no
    ///     database reachable.
    /// </remarks>
    [CollectionService]
    public interface IClientAssetContributor
    {
        /// <summary>Writes this contributor's sections and files.</summary>
        /// <param name="context">The emit's context.</param>
        void Contribute(ClientAssetContext context);
    }
}
