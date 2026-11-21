// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>How <c>queryex/discover</c> checks the text it is sent.</summary>
    public enum QueryexProfile
    {
        /// <summary>As a query compiles its clauses: under the pipeline's ceilings, with each path held to the caller's read access.</summary>
        Query,

        /// <summary>As a role save validates a permission's filter: one filter, no parameters, under the stored filter's ceilings.</summary>
        Permission,
    }
}
