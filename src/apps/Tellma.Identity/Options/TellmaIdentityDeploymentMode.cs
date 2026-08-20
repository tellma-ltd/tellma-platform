// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Options
{
    /// <summary>The hosting shape the identity engine is deployed in.</summary>
    public enum TellmaIdentityDeploymentMode
    {
        /// <summary>
        ///     The engine runs as its own app (the shared authority, or an isolated authority for
        ///     data-residency deployments), hosted by <c>Tellma.Identity.Web</c>.
        /// </summary>
        Standalone = 0,

        /// <summary>
        ///     The engine runs inside a distribution's own web host, mounted at a reserved path
        ///     base (for example <c>/id</c>) on the distribution's origin.
        /// </summary>
        InProc = 1,
    }
}
