// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>
    ///     A blob an outgoing email attaches by reference, which the caller must be able to download: a
    ///     staged <c>email-attachment</c> blob the caller uploaded, attached as it is, or a committed blob
    ///     of any kind (an invoice's own PDF) under that kind's read access, copied into a new staged
    ///     blob so the email keeps what it sent when the record's file is later replaced.
    /// </summary>
    /// <param name="Kind">The blob's kind, which the blob must have.</param>
    /// <param name="BlobId">The blob's id.</param>
    /// <param name="FileName">The file name the recipient sees; null for the blob's own.</param>
    /// <param name="ContentId">The content id an HTML body references the file by as an inline <c>cid:</c> image; null for an ordinary attachment.</param>
    public sealed record EmailBlobAttachment(string Kind, int BlobId, string? FileName = null, string? ContentId = null);
}
