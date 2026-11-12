// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>
    ///     Where a blob is in its life, stored as the member's name, <c>varchar(9)</c>. A blob is staged
    ///     by an upload, committed by the save that attaches it, released when its owner row lets go of
    ///     it, and claimed for deletion by the sweep once released or once its staging deadline passes.
    /// </summary>
    public enum BlobState
    {
        /// <summary>Uploaded and not yet attached; the uploader may attach it until its deadline.</summary>
        Staged,

        /// <summary>Attached to exactly one owner row.</summary>
        Committed,

        /// <summary>No longer attached, and waiting for the sweep.</summary>
        Released,

        /// <summary>Claimed by the sweep, which deletes its objects and then the row.</summary>
        Deleting,
    }
}
