// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

[assembly: AssemblyFixture(typeof(Tellma.Identity.E2E.Infrastructure.InProcIdentityServerFixture))]

namespace Tellma.Identity.E2E.Infrastructure
{
    /// <summary>
    ///     The in-proc-shaped E2E host: the authority mounted at the reserved <c>/id</c> path base
    ///     inside a host that serves its own root route — the composition a distribution runs, so
    ///     browser ceremonies are proven under the route prefix too.
    /// </summary>
    public sealed class InProcIdentityServerFixture : IdentityServerFixtureBase
    {
        /// <inheritdoc />
        protected override string Mode => "InProc";

        /// <inheritdoc />
        protected override string PathPrefix => "/id";
    }
}
