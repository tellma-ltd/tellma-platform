// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Services
{
    /// <summary>
    ///     The rule that decides whether a requested resource indicator (RFC 8707) is covered by a
    ///     set of granted resources. It is the only place that rule is written: every endpoint that
    ///     accepts a <c>resource</c> parameter asks it, through <see cref="ResourceGrantValidator" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A requested resource is granted in two cases: it equals a granted resource exactly
    ///         (ordinal, no normalization), or it is exactly <c>&lt;origin&gt;/&lt;id&gt;/mcp</c>, where
    ///         <c>&lt;origin&gt;</c> is a granted resource that is itself an origin (scheme, host and
    ///         port; no path) and <c>&lt;id&gt;</c> is a positive decimal tenant id without leading
    ///         zeros. Nothing else matches: no other path, no trailing slash, no query, no wildcard.
    ///     </para>
    ///     <para>
    ///         Membership is always compared ordinally, whatever comparer the caller's set carries,
    ///         so a case-insensitive set can never widen what is granted.
    ///     </para>
    /// </remarks>
    public interface IResourceGrantEvaluator
    {
        /// <summary>
        ///     Whether a requested resource is granted: an exact match, or one tenant's resource
        ///     under a granted origin.
        /// </summary>
        /// <param name="grantedResources">The resources the requester holds.</param>
        /// <param name="requestedResource">The resource indicator the request names.</param>
        /// <returns>True when the requested resource is granted.</returns>
        bool IsGranted(IReadOnlySet<string> grantedResources, string requestedResource);

        /// <summary>
        ///     Whether a requested resource equals one of the granted resources exactly. Used where
        ///     the per-tenant pattern must not apply, such as a control-plane client's grants.
        /// </summary>
        /// <param name="grantedResources">The resources the requester holds.</param>
        /// <param name="requestedResource">The resource indicator the request names.</param>
        /// <returns>True when the requested resource is one of the granted resources.</returns>
        bool IsGrantedExactly(IReadOnlySet<string> grantedResources, string requestedResource);
    }
}
