// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json.Serialization;

namespace Tellma.Core.Abstractions.Errors
{
    /// <summary>
    ///     Why a <see cref="ForbiddenException" /> refused its securable. The raise site sets it,
    ///     because only the raise site knows the cause. On the wire each reason is kebab-case
    ///     (<c>no-grant</c>, <c>outside-filter</c>, <c>restricted-path</c>).
    /// </summary>
    public enum ForbiddenReason
    {
        /// <summary>The caller holds no grant and no criterion on the pair.</summary>
        [JsonStringEnumMemberName("no-grant")]
        NoGrant,

        /// <summary>
        ///     The caller holds the pair, but the grant's row-level filter excludes a row the operation
        ///     targets: raised by the save pre-check, the delete and action pre-checks and the
        ///     post-check.
        /// </summary>
        [JsonStringEnumMemberName("outside-filter")]
        OutsideFilter,

        /// <summary>
        ///     A query reads a column of a related entity beyond that entity's related projection,
        ///     which only an unrestricted read of the entity reaches. The refused pair is the related
        ///     entity's <c>Read</c>, and the exception names the path read.
        /// </summary>
        [JsonStringEnumMemberName("restricted-path")]
        RestrictedPath,
    }
}
