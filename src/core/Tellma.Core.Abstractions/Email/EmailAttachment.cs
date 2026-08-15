// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>A file attached to an email.</summary>
    /// <remarks>
    ///     The generated value equality compares <see cref="Content" /> by buffer identity, not by
    ///     content, because <see cref="ReadOnlyMemory{T}" /> equality is reference-shaped. Compare
    ///     attachment bytes explicitly rather than relying on record equality.
    /// </remarks>
    /// <param name="FileName">The file name presented to the recipient.</param>
    /// <param name="ContentType">The MIME content type.</param>
    /// <param name="Content">The file content.</param>
    /// <param name="ContentId">A content id for inline use (e.g. images referenced by
    ///     <c>cid:</c> from the HTML body); null for ordinary attachments.</param>
    public sealed record EmailAttachment(
        string FileName,
        string ContentType,
        ReadOnlyMemory<byte> Content,
        string? ContentId = null);
}
