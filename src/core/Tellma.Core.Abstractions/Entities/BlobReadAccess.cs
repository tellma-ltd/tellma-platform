// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>Who may download a committed blob of a kind.</summary>
    /// <remarks>
    ///     A staged blob is served to its uploader alone, whatever the kind's read access, so a page
    ///     can preview a picked file before the record is saved.
    /// </remarks>
    public enum BlobReadAccess
    {
        /// <summary>
        ///     A caller who can read the owner row: the owner resource's <c>Read</c> grant, with its
        ///     row-level filter, is evaluated on every download. The default.
        /// </summary>
        OwnerRead,

        /// <summary>Any active member of the tenant, for blobs that render wherever their owner is mentioned.</summary>
        AnyMember,
    }
}
