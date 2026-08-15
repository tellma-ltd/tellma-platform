// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Webhooks
{
    /// <summary>
    ///     A transport-neutral inbound webhook call: the raw request an external system made,
    ///     captured before any deserialization so receivers can verify payload signatures over
    ///     the exact wire bytes.
    /// </summary>
    /// <remarks>
    ///     The host constructing this guarantees that <paramref name="Headers" /> and
    ///     <paramref name="QueryParams" /> use ordinal-case-insensitive key comparers, so receivers
    ///     index them without defensive re-wrapping. <paramref name="Body" /> is valid only for the
    ///     duration of the receiver's call — a receiver that needs the bytes later must copy them.
    /// </remarks>
    /// <param name="Method">The HTTP method, uppercase. Usually "POST"; "GET" for the verification
    ///     handshakes some providers perform at endpoint registration.</param>
    /// <param name="Headers">Request headers. Keys compare case-insensitively; repeated headers
    ///     carry multiple values.</param>
    /// <param name="QueryParams">Query-string parameters; repeated keys carry multiple values.
    ///     Some providers deliver verification challenges and tokens here.</param>
    /// <param name="Body">The raw, unmodified request body.</param>
    public sealed record WebhookRequest(
        string Method,
        IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
        IReadOnlyDictionary<string, IReadOnlyList<string>> QueryParams,
        ReadOnlyMemory<byte> Body);
}
