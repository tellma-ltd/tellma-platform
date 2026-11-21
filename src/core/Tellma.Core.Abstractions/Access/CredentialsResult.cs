// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     The answer of the users' <c>issue-credentials</c> action: the service account's new client
    ///     id and its secret, which crosses the wire once, in this answer, and is stored nowhere.
    /// </summary>
    /// <remarks>
    ///     The secret never appears in the record's text form, so a log line or an assertion message
    ///     that prints the record never carries it.
    /// </remarks>
    /// <param name="Id">The service account's user id.</param>
    /// <param name="ClientId">The client id the account authenticates with.</param>
    /// <param name="ClientSecret">The client secret, answered once.</param>
    public sealed record CredentialsResult(int Id, string ClientId, string ClientSecret)
    {
        /// <summary>The record's text form, with the secret redacted.</summary>
        /// <returns>The id, the client id and a placeholder for the secret.</returns>
        public override string ToString()
        {
            return $"{nameof(CredentialsResult)} {{ {nameof(Id)} = {Id}, {nameof(ClientId)} = {ClientId}, {nameof(ClientSecret)} = *** }}";
        }
    }
}
