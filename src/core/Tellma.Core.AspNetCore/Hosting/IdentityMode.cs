// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.AspNetCore.Hosting
{
    /// <summary>Where the distribution's identity server runs.</summary>
    public enum IdentityMode
    {
        /// <summary>A standalone identity server at <c>Tellma:Identity:Authority</c>.</summary>
        Standalone,

        /// <summary>The identity engine inside the distribution's own host, at <c>{PublicOrigin}/id</c>.</summary>
        InProc,
    }
}
