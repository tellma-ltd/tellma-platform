// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json.Serialization;
using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>
    ///     A blob kind's upload policy as the client checks a file against it before uploading: what
    ///     the file picker offers, how large a file may be, how images are re-encoded, and whether a
    ///     thumbnail renders. The server stays the authority on every rule.
    /// </summary>
    /// <param name="Kind">The blob kind, such as <c>customer-image</c>.</param>
    /// <param name="Accept">The content types the kind accepts, for the file picker's <c>accept</c>.</param>
    /// <param name="MaxSize">The largest file accepted, in bytes: the kind's own limit, which every deployment's upload limit admits.</param>
    /// <param name="Image">How images are re-encoded; null, written as such, when the kind keeps files as they are.</param>
    /// <param name="Thumbnail">Whether the kind renders a thumbnail variant.</param>
    /// <param name="ReadAccess">Who may read a blob of the kind.</param>
    /// <seealso cref="BlobKindPolicy" />
    public sealed record ClientBlobKindPolicy(
        string Kind,
        IReadOnlyList<string> Accept,
        [property: JsonConverter(typeof(SafeIntegerJsonConverter))] long MaxSize,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] ClientImagePolicy? Image,
        bool Thumbnail,
        ClientBlobReadAccess ReadAccess);
}
