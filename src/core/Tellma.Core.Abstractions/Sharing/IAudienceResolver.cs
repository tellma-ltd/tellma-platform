// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Abstractions.Sharing
{
    /// <summary>
    ///     Answers who can see a shareable row, and why: every audience user it reaches through its
    ///     owner, its grants, the stored permissions on its resource and the rows whose audience also
    ///     sees it, with each one's highest access, and, per grant, how many of them cannot read the
    ///     data the row shows. One per scope; generic over every shareable entity.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An audience user is an active user other than the system user, of any kind and any
    ///         state, and a role reaches only its current members; a public role, like the Everyone
    ///         grant, reaches every audience user. A stored permission of an active role reaches the
    ///         row when it grants any action on the row's resource or on the wildcard resource and,
    ///         when it carries a filter, the row satisfies that filter.
    ///     </para>
    ///     <para>
    ///         The caller must be allowed to share the row: it is read under the caller's
    ///         <c>Share</c> decision on its resource, and a row the decision does not admit is not
    ///         found. The members are listed one by one only to a caller who may read the roles;
    ///         every count stays exact whatever the listing shows.
    ///     </para>
    /// </remarks>
    public interface IAudienceResolver
    {
        /// <summary>Resolves the audience of one shareable row.</summary>
        /// <param name="query">The row, the data its audience needs, and the rows whose audience also sees it.</param>
        /// <param name="cancellationToken">Cancels the round trips.</param>
        /// <returns>The audience.</returns>
        /// <exception cref="ArgumentException">The query names a resource that is not shareable, or rows of no resource.</exception>
        /// <exception cref="NotFoundException">The row does not exist, or the caller's <c>Share</c> decision does not admit it.</exception>
        /// <exception cref="TenantNotFoundException">The caller is not, or no longer, an active user of the tenant.</exception>
        /// <exception cref="StaleContextException">The caller's permissions moved again after a batch was recomposed once.</exception>
        Task<ShareAudience> ResolveAsync(AudienceQuery query, CancellationToken cancellationToken = default);
    }
}
