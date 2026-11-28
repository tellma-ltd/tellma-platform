// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Azure.Core;

namespace Tellma.Connector.AzureBlobs
{
    /// <summary>
    ///     The identity the store reaches its account's endpoint with: the credential the
    ///     composition passed, or else the host's own registered <see cref="TokenCredential" />.
    ///     The adapter never constructs one.
    /// </summary>
    /// <param name="Credential">The credential; null when neither was supplied, which suffices for a connection string alone.</param>
    internal sealed record AzureBlobStoreCredential(TokenCredential? Credential);
}
