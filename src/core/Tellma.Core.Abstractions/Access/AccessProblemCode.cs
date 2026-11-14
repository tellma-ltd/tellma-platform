// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     Why a stored permission grants nothing. A permission with a problem drifts: it is left out
    ///     of the caller's grants and never affects their other permissions, so an administrator whose
    ///     one broken permission would otherwise lock them out can still fix it.
    /// </summary>
    public enum AccessProblemCode
    {
        /// <summary>The resource is not registered, under its name or a former name's alias.</summary>
        UnknownResource,

        /// <summary>
        ///     The action is not registered for the resource; for the wildcard resource, the action is
        ///     registered for no resource at all.
        /// </summary>
        UnknownAction,

        /// <summary>
        ///     The permission carries a filter, but its securable takes none (it has no filter root, or
        ///     the resource's securables share none), or its resource is the wildcard.
        /// </summary>
        FilterUnsupported,

        /// <summary>The filter does not validate against the tenant's current query schema.</summary>
        FilterInvalid,

        /// <summary>The filter's Queryex version is missing or outside the versions this engine compiles.</summary>
        VersionUnsupported,

        /// <summary>
        ///     The permission belongs to a public role and names the wildcard resource, which would hand
        ///     every user of the tenant every resource. Role saves refuse it; a row that exists anyway
        ///     grants nothing.
        /// </summary>
        WildcardResourceOnPublicRole,
    }
}
