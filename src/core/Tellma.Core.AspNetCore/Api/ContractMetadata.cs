// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.AspNetCore.Api
{
    /// <summary>
    ///     The contract fingerprint of a projected web endpoint: a hash of its request and response
    ///     JSON contracts and their revisions. A caller that sends a <c>Tellma-Contract</c> header
    ///     other than this value is refused as outdated before its body is read. Hand-mapped
    ///     endpoints carry none and are never checked.
    /// </summary>
    /// <param name="Fingerprint">The fingerprint: twelve base64url characters.</param>
    public sealed record ContractMetadata(string Fingerprint)
    {
        /// <summary>The fingerprint: twelve base64url characters.</summary>
        public string Fingerprint { get; init; } = !string.IsNullOrWhiteSpace(Fingerprint)
            ? Fingerprint
            : throw new ArgumentException("A contract fingerprint is not empty.", nameof(Fingerprint));
    }
}
