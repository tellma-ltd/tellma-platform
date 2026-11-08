// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     The lifetime of a contributed service. Mirrors the container's lifetimes without naming the
    ///     container, which this contract does not reference.
    /// </summary>
    public enum ServiceLifetimeKind
    {
        /// <summary>One instance for the life of the host.</summary>
        Singleton,

        /// <summary>One instance per scope: per request, or per background tenant scope.</summary>
        Scoped,

        /// <summary>A new instance every time the service is resolved.</summary>
        Transient,
    }
}
